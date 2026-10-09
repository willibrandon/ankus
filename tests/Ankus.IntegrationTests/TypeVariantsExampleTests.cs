using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes the ported pgrx postgres_type_variants sample, mirroring each variant's <c>#[pg_test]</c> cases.
/// </summary>
/// <param name="context">The current cancellation and diagnostic context.</param>
[TestClass]
public sealed class TypeVariantsExampleTests(TestContext context)
{
    /// <summary>
    /// Variant 1: generated JSON text round-trips and functions translate the stored value.
    /// </summary>
    [TestMethod]
    public Task JsonDefaultRoundTripsAndTranslates() => RunAsync(async (connection, transaction, token) =>
    {
        Assert.AreEqual("{\"x\":3,\"y\":4}", await ScalarAsync<string>(connection, transaction, """SELECT '{"x":3.0,"y":4.0}'::coord::text""", token));
        Assert.AreEqual("{\"x\":11,\"y\":22}", await ScalarAsync<string>(connection, transaction,
            """SELECT coord_translate('{"x":1.0,"y":2.0}'::coord, 10.0, 20.0)::text""", token));
        Assert.AreEqual("{\"x\":0,\"y\":0}", await ScalarAsync<string>(connection, transaction, "SELECT coord_origin()::text", token));
        Assert.AreEqual("{\"x\":0.5,\"y\":-1.25}", await ScalarAsync<string>(connection, transaction,
            """SELECT '{"y":-1.25,"x":0.5}'::coord::text""", token));
        await AssertFailureAsync(connection, transaction, """SELECT '{"x":1}'::coord""", "22P02", token);
    });

    /// <summary>
    /// Variant 2: the custom text form parses, formats and adds, while storage remains the generated contract.
    /// </summary>
    [TestMethod]
    public Task CustomTextParsesFormatsAndAdds() => RunAsync(async (connection, transaction, token) =>
    {
        Assert.AreEqual("3+4i", await ScalarAsync<string>(connection, transaction, "SELECT '3+4i'::complex::text", token));
        Assert.AreEqual("3-4i", await ScalarAsync<string>(connection, transaction, "SELECT '3-4i'::complex::text", token));
        Assert.AreEqual("4+6i", await ScalarAsync<string>(connection, transaction, "SELECT complex_add('1+2i'::complex, '3+4i'::complex)::text", token));
        Assert.AreEqual("-1.5-0.25i", await ScalarAsync<string>(connection, transaction, "SELECT '  -1.5-0.25i  '::complex::text", token));
        Assert.AreEqual("100000+0i", await ScalarAsync<string>(connection, transaction, "SELECT '1e5+0i'::complex::text", token));
        await ExecuteAsync(connection, transaction, "CREATE TEMP TABLE complex_values(value complex); INSERT INTO complex_values VALUES ('3+4i')", token);
        Assert.AreEqual("3+4i", await ScalarAsync<string>(connection, transaction, "SELECT value::text FROM complex_values", token));
        await AssertFailureAsync(connection, transaction, "SELECT '3+4'::complex", "22P02", token, "expected trailing 'i'");
        await AssertFailureAsync(connection, transaction, "SELECT '-4i'::complex", "22P02", token, "expected sign between real and imaginary parts");
        await AssertFailureAsync(connection, transaction, "SELECT 'x+4i'::complex", "22P02", token, "invalid real part");
        await AssertFailureAsync(connection, transaction, "SELECT '3+xi'::complex", "22P02", token, "invalid imaginary part");
    });

    /// <summary>
    /// Variant 3: three stored bytes round-trip as lowercase hexadecimal and are read in place.
    /// </summary>
    [TestMethod]
    public Task PackedNativeStoresThreeBytes() => RunAsync(async (connection, transaction, token) =>
    {
        Assert.AreEqual("#ff8000", await ScalarAsync<string>(connection, transaction, "SELECT '#ff8000'::rgb::text", token));
        Assert.AreEqual("#0a0b0c", await ScalarAsync<string>(connection, transaction, "SELECT '#0A0B0C'::rgb::text", token));
        double luminance = await ScalarAsync<double>(connection, transaction, "SELECT rgb_luminance('#ffffff'::rgb)", token);
        Assert.AreEqual(255.0, luminance, 0.001);
        Assert.AreEqual(0.299 * 255 + 0.587 * 128, await ScalarAsync<double>(connection, transaction, "SELECT rgb_luminance('#ff8000'::rgb)", token));
        // A four-byte variable-length header precedes the three payload bytes.
        Assert.AreEqual(7, await ScalarAsync<int>(connection, transaction, "SELECT pg_column_size('#ff8000'::rgb)", token));
        await AssertFailureAsync(connection, transaction, "SELECT 'ff8000'::rgb", "22P02", token, "expected leading '#'");
        await AssertFailureAsync(connection, transaction, "SELECT '#ff80'::rgb", "22P02", token, "expected 6 hex digits");
        await AssertFailureAsync(connection, transaction, "SELECT '#gg8000'::rgb", "22P02", token, "bad red");
        await AssertFailureAsync(connection, transaction, "SELECT '#ff80zz'::rgb", "22P02", token, "bad blue");
    });

    /// <summary>
    /// Variant 4: the hand-written by-value type parses, formats and reports pgrx's error messages.
    /// </summary>
    [TestMethod]
    public Task HandRolledDatumUsesInt4Representation() => RunAsync(async (connection, transaction, token) =>
    {
        Assert.AreEqual("16777215", await ScalarAsync<string>(connection, transaction, "SELECT '16777215'::u24::text", token));
        Assert.AreEqual("42", await ScalarAsync<string>(connection, transaction, "SELECT '42'::u24::text", token));
        Assert.AreEqual("7", await ScalarAsync<string>(connection, transaction, "SELECT '+007'::u24::text", token));
        Assert.AreEqual("4|true|i", await ScalarAsync<string>(connection, transaction,
            "SELECT typlen || '|' || typbyval || '|' || typalign::text FROM pg_type WHERE oid = 'u24'::regtype", token));
        await AssertFailureAsync(connection, transaction, "SELECT '16777216'::u24", "22000", token, "value exceeds 24 bits");
        await AssertFailureAsync(connection, transaction, "SELECT 'abc'::u24", "22000", token, "invalid digit found in string");
        await AssertFailureAsync(connection, transaction, "SELECT '-1'::u24", "22000", token, "invalid digit found in string");
        await AssertFailureAsync(connection, transaction, "SELECT ''::u24", "22000", token, "cannot parse integer from empty string");
        await AssertFailureAsync(connection, transaction, "SELECT '4294967296'::u24", "22000", token, "number too large to fit in target type");
    });

    /// <summary>
    /// Variant 5: a record-shaped type is returned and accepted as an array, skipping SQL NULL elements.
    /// </summary>
    [TestMethod]
    public Task CompositeAndArrayRoundTrip() => RunAsync(async (connection, transaction, token) =>
    {
        Assert.AreSequenceEqual<string>(["{\"a\":1,\"b\":\"one\"}", "{\"a\":2,\"b\":\"two\"}"],
            await ScalarAsync<string[]>(connection, transaction, "SELECT make_pairs()::text[]", token));
        Assert.AreEqual(42L, await ScalarAsync<long>(connection, transaction,
            """SELECT sum_pair_a(ARRAY['{"a":10,"b":"x"}'::pair, '{"a":32,"b":"y"}'::pair])""", token));
        Assert.AreEqual(7L, await ScalarAsync<long>(connection, transaction,
            """SELECT sum_pair_a(ARRAY['{"a":7,"b":"k"}'::pair, NULL::pair])""", token));
        Assert.AreEqual(3L, await ScalarAsync<long>(connection, transaction, "SELECT sum_pair_a(make_pairs())", token));
        Assert.AreEqual(0L, await ScalarAsync<long>(connection, transaction, "SELECT sum_pair_a(ARRAY[]::pair[])", token));
    });

    private Task RunAsync(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task> test)
        => PostgresFixture.Cluster.RunInTransactionAsync(context.TestName ?? nameof(TypeVariantsExampleTests), async (connection, transaction, token) =>
        {
            await ExecuteAsync(connection, transaction, "CREATE EXTENSION ankus_type_variants", token);
            await test(connection, transaction, token);
        }, context.CancellationToken);

    private static async Task AssertFailureAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, string sqlState,
        CancellationToken token, string? message = null)
    {
        const string Savepoint = "type_variants_failure";
        await transaction.SaveAsync(Savepoint, token);
        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual(sqlState, error.SqlState, error.MessageText);
            if (message is not null)
            {
                Assert.AreEqual(message, error.MessageText);
            }
        }
        finally
        {
            await transaction.RollbackAsync(Savepoint, CancellationToken.None);
        }
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(token);
    }
}
