using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes the ported pgrx range sample as an independently published Native AOT extension.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
[DoNotParallelize]
public sealed class RangeExampleTests(TestContext context)
{
    /// <summary>
    /// Every constructor and detached comparison retains its exact SQL value and signature across extension lifecycle changes.
    /// </summary>
    [TestMethod]
    public Task RangeSampleRelocatesAndReinstalls()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RangeSampleRelocatesAndReinstalls), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, """
                CREATE SCHEMA range_first;
                CREATE SCHEMA range_second;
                CREATE EXTENSION ankus_ranges WITH SCHEMA range_first;
                """, token);
            uint[] original = await VerifyFunctions(connection, transaction, "range_first", token);
            await VerifyValues(connection, transaction, "range_first", token);
            await Execute(connection, transaction, """
                CREATE TEMP TABLE stored_ranges(id integer PRIMARY KEY, value int4range);
                INSERT INTO stored_ranges
                SELECT step, range_first.range(100,100+step) FROM generate_series(0,100) AS steps(step);
                ALTER EXTENSION ankus_ranges SET SCHEMA range_second;
                """, token);
            Assert.AreSequenceEqual(original, await VerifyFunctions(connection, transaction, "range_second", token));
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction,
                "SELECT count(*) FROM pg_proc WHERE pronamespace='range_first'::regnamespace", token));
            await VerifyValues(connection, transaction, "range_second", token);
            await VerifyStoredRanges(connection, transaction, token);
            await Execute(connection, transaction, "DROP EXTENSION ankus_ranges", token);
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction,
                "SELECT count(*) FROM pg_proc WHERE pronamespace='range_second'::regnamespace", token));
            await VerifyStoredRanges(connection, transaction, token);
            await Execute(connection, transaction, "CREATE EXTENSION ankus_ranges WITH SCHEMA range_first", token);
            uint[] reinstalled = await VerifyFunctions(connection, transaction, "range_first", token);
            Assert.HasCount(original.Length, reinstalled);
            for (int index = 0; index < original.Length; index++)
            {
                Assert.AreNotEqual(original[index], reinstalled[index]);
            }

            await VerifyValues(connection, transaction, "range_first", token);
            Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, transaction, "SELECT pg_backend_pid()", token));
        }, context.CancellationToken);

    /// <summary>
    /// PostgreSQL rejects invalid requested bounds after managed frames unwind and remains usable in the same session.
    /// </summary>
    /// <param name="expression">The invalid sample function invocation.</param>
    /// <param name="sqlState">The independent PostgreSQL diagnostic code.</param>
    [TestMethod]
    [DataRow("range(5,1)", "22000")]
    [DataRow("range_inclusive(1,2147483647)", "22003")]
    [DataRow("range_to_inclusive(2147483647)", "22003")]
    public Task RangeSampleRejectsInvalidBoundsAndRecovers(string expression, string sqlState)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RangeSampleRejectsInvalidBoundsAndRecovers), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, "CREATE SCHEMA range_errors; CREATE EXTENSION ankus_ranges WITH SCHEMA range_errors", token);
            await transaction.SaveAsync("range_error", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
                Execute(connection, transaction, "SELECT range_errors." + expression, token));
            Assert.AreEqual(sqlState, error.SqlState);
            await transaction.RollbackAsync("range_error", token);
            Assert.AreEqual("[2,6)", await Scalar<string>(connection, transaction,
                "SELECT range_errors.range_inclusive(2,5)::text", token));
            Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, transaction, "SELECT pg_backend_pid()", token));
        }, context.CancellationToken);

    /// <summary>
    /// Checks every sample's result independently, including strict NULL inputs and discrete boundary canonicalization.
    /// </summary>
    private static async Task VerifyValues(NpgsqlConnection connection, NpgsqlTransaction transaction, string schema, CancellationToken token)
    {
        (string Expression, string Expected)[] cases =
        [
            ("range(10,101)", "[10,101)"),
            ("range_from(10)", "[10,)"),
            ("range_full()", "(,)"),
            ("range_inclusive(10,100)", "[10,101)"),
            ("range_to(101)", "(,101)"),
            ("range_to_inclusive(100)", "(,101)"),
            ("empty()", "empty"),
            ("infinite()", "(,)"),
            ("range(7,7)", "empty"),
            ("range_inclusive(7,7)", "[7,8)"),
            ("range(-7,-2)", "[-7,-2)"),
            ("range('-2147483648'::integer,2147483647)", "[-2147483648,2147483647)"),
            ("range_to('-2147483648'::integer)", "(,-2147483648)"),
            ("range_from(2147483647)", "[2147483647,)"),
        ];
        foreach ((string expression, string expected) in cases)
        {
            Assert.AreEqual(expected, await Scalar<string>(connection, transaction, $"SELECT {schema}.{expression}::text", token), expression);
        }

        Assert.IsTrue(await Scalar<bool>(connection, transaction, $"SELECT {schema}.assert_range('[10,101)',10,101)", token));
        Assert.IsFalse(await Scalar<bool>(connection, transaction, $"SELECT {schema}.assert_range('[10,101]',10,101)", token));
        Assert.IsFalse(await Scalar<bool>(connection, transaction, $"SELECT {schema}.assert_range('empty',7,7)", token));
        Assert.IsTrue(await Scalar<bool>(connection, transaction, $"""
            SELECT {schema}.range(NULL,1) IS NULL AND {schema}.range(1,NULL) IS NULL
                AND {schema}.range_from(NULL) IS NULL AND {schema}.range_to(NULL) IS NULL
                AND {schema}.range_inclusive(NULL,1) IS NULL AND {schema}.range_to_inclusive(NULL) IS NULL
                AND {schema}.assert_range(NULL,1,5) IS NULL
            """, token));
    }

    /// <summary>
    /// Verifies every stored row of pgrx's 101-range sequence after function relocation or removal.
    /// </summary>
    private static async Task VerifyStoredRanges(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        string[] expected = ["empty", .. Enumerable.Range(1, 100).Select(static step => FormattableString.Invariant($"[100,{100 + step})"))];
        Assert.AreSequenceEqual(expected, await Scalar<string[]>(connection, transaction,
            "SELECT array_agg(value::text ORDER BY id) FROM stored_ranges", token));
        Assert.AreEqual("int4range", await Scalar<string>(connection, transaction,
            "SELECT pg_typeof(value)::text FROM stored_ranges WHERE id=100", token));
    }

    /// <summary>
    /// Captures exact argument/result identities, function options and extension ownership rather than counting declarations alone.
    /// </summary>
    private static async Task<uint[]> VerifyFunctions(NpgsqlConnection connection, NpgsqlTransaction transaction, string schema, CancellationToken token)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT p.oid,p.proname||':'||p.proargtypes::text||':'||p.prorettype::text,
                p.provolatile='i' AND p.proparallel='s' AND (p.pronargs=0 OR p.proisstrict),
                EXISTS(SELECT FROM pg_depend d JOIN pg_extension e ON e.oid=d.refobjid
                    WHERE d.classid='pg_proc'::regclass AND d.objid=p.oid AND d.refclassid='pg_extension'::regclass
                        AND d.deptype='e' AND e.extname='ankus_ranges')
            FROM pg_proc p WHERE p.pronamespace='{schema}'::regnamespace ORDER BY p.proname COLLATE "C"
            """, connection, transaction);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        var identities = new List<uint>();
        var signatures = new List<string>();
        while (await reader.ReadAsync(token))
        {
            identities.Add(reader.GetFieldValue<uint>(0));
            signatures.Add(reader.GetString(1));
            Assert.IsTrue(reader.GetBoolean(2), reader.GetString(1));
            Assert.IsTrue(reader.GetBoolean(3), reader.GetString(1));
        }

        Assert.AreSequenceEqual<string>(["assert_range:3904 23 23:16", "empty::3904", "infinite::3904", "range:23 23:3904",
            "range_from:23:3904", "range_full::3904", "range_inclusive:23 23:3904", "range_to:23:3904", "range_to_inclusive:23:3904"], signatures);
        return [.. identities];
    }

    /// <summary>
    /// Returns one typed PostgreSQL observation from the current isolated test transaction.
    /// </summary>
    private static async Task<T> Scalar<T>(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Completes DDL or an error-producing operation before the next assertion.
    /// </summary>
    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(token);
    }
}
