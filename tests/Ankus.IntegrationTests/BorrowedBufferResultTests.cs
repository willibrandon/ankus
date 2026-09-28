using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class BorrowedBufferTests
{
    /// <summary>
    /// Fixed text and bytea return declarations reject a different original SQL identity without corrupting the backend.
    /// </summary>
    /// <param name="sql">The mismatched native return.</param>
    /// <param name="expected">The exact native type diagnostic.</param>
    [TestMethod]
    [DataRow("SELECT borrowed_buffers.raw_text_return('café'::varchar)", "Returned PostgreSQL type character varying does not match expected type text")]
    [DataRow("SELECT borrowed_buffers.raw_bytea_return('\\x00ff'::pg_temp.buffer_bytes)", "Returned PostgreSQL type buffer_bytes does not match expected type bytea")]
    public async Task BorrowedBufferReturnsRequireTheDeclaredSqlType(string sql, string expected)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        _ = await Scalar<object>(connection, "CREATE DOMAIN pg_temp.buffer_bytes AS bytea; SELECT 1");
        await using var command = new NpgsqlCommand(sql, connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(context.CancellationToken));
        Assert.AreEqual(PostgresErrorCodes.DatatypeMismatch, error.SqlState);
        Assert.AreEqual(expected, error.MessageText);
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT 42"));
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident = 'Ankus borrowed buffer'"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// TABLE results preserve both native columns in materialized and early-exit executor paths, including NULL cells.
    /// </summary>
    /// <param name="expression">The table expression and optional executor limit.</param>
    /// <param name="expected">The exact observed text and binary pairs.</param>
    [TestMethod]
    [DataRow("SELECT * FROM borrowed_buffers.buffer_table('café','\\x00ff'::bytea,false)", new[] { "café:00ff", "café:00ff" })]
    [DataRow("SELECT (borrowed_buffers.buffer_table('café','\\x00ff'::bytea,true)).* LIMIT 1", new[] { "café:00ff" })]
    [DataRow("SELECT * FROM borrowed_buffers.buffer_table(NULL,NULL,false)", new[] { "NULL:NULL", "NULL:NULL" })]
    public async Task BorrowedBufferTablesReleaseTheirSnapshots(string expression, string[] expected)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual(expected, await Scalar<string[]>(connection,
            $"SELECT ARRAY(SELECT coalesce(label,'NULL') || ':' || coalesce(encode(payload,'hex'),'NULL') FROM ({expression}) AS rows)"));
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident = 'Ankus borrowed buffer'"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Iterator failure disposes both retained snapshots and preserves same-session recovery.
    /// </summary>
    [TestMethod]
    public async Task BorrowedBufferIteratorErrorsReleaseTheirSnapshots()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT * FROM borrowed_buffers.buffer_table('café','\\x00ff'::bytea,true)", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(context.CancellationToken));
        Assert.AreEqual("Borrowed buffer iterator failed.", error.MessageText);
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident = 'Ankus borrowed buffer'"));
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT 42"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Session and prepared-plan multi-column conversions transfer all views beyond temporary result ownership.
    /// </summary>
    [TestMethod]
    public async Task BorrowedBufferPairsSurviveSessionsAndPlans()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string>(["session café", "00FF", "session café", "00FF", "42"],
            await Scalar<string[]>(connection, "SELECT borrowed_buffers.buffer_spi_pairs()"));
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident = 'Ankus borrowed buffer'"));
    }
}
