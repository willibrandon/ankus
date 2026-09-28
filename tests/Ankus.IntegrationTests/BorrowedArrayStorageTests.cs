using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class BorrowedArrayTests
{
    /// <summary>
    /// Physical native forms are observed before managed conversion, flattened in the correct owner and repeatedly reclaimed.
    /// </summary>
    /// <param name="kind">The independently witnessed native storage form.</param>
    [TestMethod]
    [DataRow("flat")]
    [DataRow("short")]
    [DataRow("compressed")]
    [DataRow("external")]
    [DataRow("expanded")]
    public async Task BorrowedArrayToastAndCleanupMatchPostgres(string kind)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(context.CancellationToken);
        string expression = "ARRAY['first',NULL,'last']::text[]";
        string?[] values = ["first", null, "last"];
        if (kind is "short" or "compressed" or "external")
        {
            string storage = kind == "external" ? "EXTERNAL" : "EXTENDED";
            expression = kind == "short" ? "ARRAY['tiny']::text[]" : "ARRAY[repeat('z',10000),NULL,'last']::text[]";
            await using var command = new NpgsqlCommand($"""
                CREATE TEMP TABLE borrowed_native_storage(value text[]);
                ALTER TABLE borrowed_native_storage ALTER COLUMN value SET STORAGE {storage};
                INSERT INTO borrowed_native_storage VALUES ({expression})
                """, connection, transaction);
            await command.ExecuteNonQueryAsync(context.CancellationToken);
            expression = "(SELECT value FROM borrowed_native_storage)";
            values = kind == "short" ? ["tiny"] : [new string('z', 10000), null, "last"];
        }

        int expand = kind == "expanded" ? 1 : 0;
        bool copy = kind != "flat";
        string count = values.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string?[] expected = [kind, copy.ToString(), copy ? "Ankus borrowed array" : "borrowed", "0", "0",
            "1009", "25", "1", count, (kind != "short").ToString(), count, "1", .. values];
        Assert.AreSequenceEqual(expected, await Scalar<string?[]>(connection, $"""
            SELECT tests.array_storage('borrowed_arrays.array_view_native(bigint,oid,text)'::regprocedure,{expression},{expand})
            """));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Native callers compare their original flat argument address with the generated borrowed view and every cell.
    /// </summary>
    /// <param name="expression">The by-value or by-reference input partition.</param>
    [TestMethod]
    [DataRow("ARRAY[]::integer[]")]
    [DataRow("ARRAY[0,NULL,-7]")]
    [DataRow("ARRAY['first',NULL,repeat('large 🐘',10000)]")]
    public async Task GeneratedBorrowedArrayInputsRetainOriginalNativeAddresses(string expression)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.IsTrue(await Scalar<bool>(connection, $"""
            SELECT tests.array_argument('borrowed_arrays.array_view_original(anyarray,bigint)'::regprocedure,{expression})
            """));
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident LIKE 'Ankus borrowed array%'"));
    }

    /// <summary>
    /// Typed SPI and catalog function calls retain exact shape and type while binding views as raw parameters.
    /// </summary>
    [TestMethod]
    public async Task BorrowedArraySpiAndFunctionPathsPreserveTypesAndOwners()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string?>(["1009", "25", "1", "4", "True", "4", "-1",
            "first", null, "last", "tail", "0", null, "7"],
            await Scalar<string?[]>(connection, "SELECT borrowed_arrays.array_view_spi()"));
    }

    /// <summary>
    /// Empty cursors never invent a cell and both immediately adjacent indices are rejected explicitly.
    /// </summary>
    [TestMethod]
    public async Task BorrowedArrayEmptyCursorsAndBoundsRemainExact()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string>(["InvalidOperationException", "False", "False", "InvalidOperationException",
            "ArgumentOutOfRangeException:index", "ArgumentOutOfRangeException:index", "ArgumentException:subscripts",
            "ArgumentException:subscripts", "0"],
            await Scalar<string[]>(connection, "SELECT borrowed_arrays.array_view_empty()"));
    }

    /// <summary>
    /// Unregistered enum elements preserve their real catalog identity and ordered labels, including an empty label and SQL NULL.
    /// </summary>
    [TestMethod]
    public async Task BorrowedArraysRetainUnregisteredEnumIdentity()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(context.CancellationToken);
        await using var command = new NpgsqlCommand("CREATE TYPE pg_temp.borrowed_label AS ENUM ('','ready','odd space')", connection, transaction);
        await command.ExecuteNonQueryAsync(context.CancellationToken);
        string arrayOid = await Scalar<string>(connection, "SELECT 'pg_temp.borrowed_label[]'::regtype::oid::text");
        string elementOid = await Scalar<string>(connection, "SELECT 'pg_temp.borrowed_label'::regtype::oid::text");
        Assert.AreSequenceEqual<string?>([arrayOid, elementOid, "1", "4", "True", "4", "1", "odd space", null, "", "ready"],
            await Scalar<string?[]>(connection,
                "SELECT borrowed_arrays.array_view_snapshot(ARRAY['odd space',NULL,'','ready']::pg_temp.borrowed_label[])"));
    }
}
