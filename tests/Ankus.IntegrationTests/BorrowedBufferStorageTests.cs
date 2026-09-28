using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class BorrowedBufferTests
{
    /// <summary>
    /// Independent native callers witness original packed storage and exact detoast ownership before managed conversion.
    /// </summary>
    /// <param name="binary">Whether bytea or text storage is observed.</param>
    /// <param name="kind">The required physical native representation.</param>
    [TestMethod]
    [DataRow(false, "flat")]
    [DataRow(false, "short")]
    [DataRow(false, "compressed")]
    [DataRow(false, "external")]
    [DataRow(true, "flat")]
    [DataRow(true, "short")]
    [DataRow(true, "compressed")]
    [DataRow(true, "external")]
    public async Task BorrowedBuffersMatchNativeStorageAndCleanup(bool binary, string kind)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(context.CancellationToken);
        bool toasted = kind is "compressed" or "external";
        string type = binary ? "bytea" : "text";
        string method = binary ? "bytea_storage" : "text_storage";
        string expression = binary ? "'\\x007fff'::bytea" : "'café'::text";
        string expected = binary ? "007FFF" : "café";
        int length = binary ? 3 : 5;
        if (toasted)
        {
            expression = binary ? "decode(repeat('00ff',10000),'hex')" : "repeat('z',10000)";
            expected = binary ? string.Concat(Enumerable.Repeat("00FF", 10000)) : new string('z', 10000);
            length = binary ? 20000 : 10000;
        }

        if (kind != "flat")
        {
            string storage = kind == "external" ? "EXTERNAL" : "EXTENDED";
            await using var create = new NpgsqlCommand($"""
                CREATE TEMP TABLE borrowed_buffer_storage(value {type});
                ALTER TABLE borrowed_buffer_storage ALTER COLUMN value SET STORAGE {storage};
                INSERT INTO borrowed_buffer_storage VALUES ({expression})
                """, connection, transaction);
            await create.ExecuteNonQueryAsync(context.CancellationToken);
            expression = "(SELECT value FROM borrowed_buffer_storage)";
        }

        string[] observed = await Scalar<string[]>(connection, $"""
            SELECT tests.buffer_argument('borrowed_buffers.{method}({type},bigint,bigint,text)'::regprocedure,{expression})
            """);
        Assert.AreSequenceEqual<string>([kind, toasted.ToString(), (!toasted).ToString(), toasted ? "Ankus borrowed buffer" : "borrowed",
            length.ToString(System.Globalization.CultureInfo.InvariantCulture), expected], observed);
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident = 'Ankus borrowed buffer'"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }
}
