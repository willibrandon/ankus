using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies raw cursor ownership and PostgreSQL fetch semantics using the published extension.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class SpiRawCursorTests(TestContext context)
{
    /// <summary>
    /// Gets batches across each SPI owner with non-mapped values, NULLs, large text, and empty results.
    /// </summary>
    public static IEnumerable<(int Api, string Sql, int Size, string Expected)> Batches
    {
        get
        {
            for (int api = 0; api < 4; api++)
            {
                yield return (api, "SELECT n AS value FROM generate_series(1, 7) n", 3, "1,2,3/4,5,6/7;value:23");
                yield return (api, "SELECT 42 AS value WHERE false", 1, ";value:23");
                yield return (api, "SELECT CASE WHEN n = 2 THEN NULL ELSE B'101' END AS value FROM generate_series(1, 3) n", 2, "101,<null>/101;value:1560");
                yield return (api, "SELECT repeat('owned', 10000) AS value FROM generate_series(1, 2)", 1,
                    string.Concat(Enumerable.Repeat("owned", 10000)) + "/" + string.Concat(Enumerable.Repeat("owned", 10000)) + ";value:25");
            }
        }
    }

    /// <summary>
    /// Verifies earlier raw batches remain live after later fetches and session, plan, and cursor disposal.
    /// </summary>
    /// <param name="api">The SPI owner selection.</param>
    /// <param name="sql">The cursor query.</param>
    /// <param name="size">The fetch batch size.</param>
    /// <param name="expected">The independent value and metadata expectation.</param>
    [TestMethod]
    [DynamicData(nameof(Batches))]
    public Task BatchesSurviveSubsequentFetchesAndCursorDisposal(int api, string sql, int size, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(BatchesSurviveSubsequentFetchesAndCursorDisposal), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT datatype.raw_cursor_batches($1, $2, $3)", connection, transaction);
            command.Parameters.AddWithValue(api);
            command.Parameters.AddWithValue(sql);
            command.Parameters.AddWithValue(size);
            Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Verifies scrolling, mixed fetch modes, ownership guards, and native error cleanup with same-backend recovery.
    /// </summary>
    /// <param name="function">The backend probe.</param>
    /// <param name="expected">The exact observations.</param>
    [TestMethod]
    [DataRow("raw_cursor_movement", "0:1|1,2|2|1|2|3,4")]
    [DataRow("raw_cursor_guards", "4|42")]
    [DataRow("raw_cursor_recover", "22012|0|42")]
    public Task FetchSemanticsAndRecoveryRemainExact(string function, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(FetchSemanticsAndRecoveryRemainExact), async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand($"SELECT datatype.{function}()", connection, transaction);
            Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }, context.CancellationToken);

    /// <summary>
    /// Verifies raw fetching rejects disposal from a recursively invoked extension callback.
    /// </summary>
    [TestMethod]
    public Task RecursiveDisposalCannotInvalidateActiveFetch()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RecursiveDisposalCannotInvalidateActiveFetch), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT datatype.cursor_cache('SELECT datatype.cursor_reentrant_close(42)')", connection, transaction);
            string name = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT datatype.cursor_cached_raw_rows(1)";
            Assert.AreEqual("42", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT datatype.cursor_dispose_cached()";
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT EXISTS (SELECT FROM pg_cursors WHERE name = $1)";
            command.Parameters.AddWithValue(name);
            Assert.IsFalse(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
        }, context.CancellationToken);
}
