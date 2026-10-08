using System.Globalization;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes the ported pgrx subtrans_infos sample through its published Native AOT extension.
/// </summary>
/// <param name="context">The current cancellation and diagnostic context.</param>
[TestClass]
public sealed class SubtransactionsExampleTests(TestContext context)
{
    /// <summary>
    /// The query used by pgrx's README to describe every transaction ID locked by the current backend.
    /// </summary>
    private const string LockedTransactions = """
        SELECT string_agg(concat_ws(',', si.xid, si.status, si.parent_xid, si.top_parent_xid, si.sub_level, si.commit_timestamp), ';'
            ORDER BY si.xid)
        FROM pg_locks pgl
        CROSS JOIN LATERAL subtrans_infos(pgl.transactionid::text::bigint) si
        WHERE pgl.transactionid IS NOT NULL AND pgl.pid = pg_backend_pid()
        """;

    /// <summary>
    /// Mirrors pgrx's current, bootstrap, frozen, consistency and error-recovery tests with exact rows and errors.
    /// </summary>
    [TestMethod]
    public async Task SubtransactionsSampleDescribesSpecialAndInvalidIds()
    {
        CancellationToken token = context.CancellationToken;
        await using PostgresTestCluster cluster = await StartAsync(token);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        await ExecuteAsync(connection, "CREATE EXTENSION ankus_subtransactions", token);
        Assert.AreEqual("1,committed", await RowAsync(connection, "1", token));
        Assert.AreEqual("2,committed", await RowAsync(connection, "2", token));

        await ExecuteAsync(connection, "BEGIN", token);
        long current = await ScalarAsync<long>(connection, "SELECT pg_current_xact_id()::text::bigint", token);
        string expected = current.ToString(CultureInfo.InvariantCulture) + ",in progress";
        for (int iteration = 0; iteration < 3; iteration++)
        {
            Assert.AreEqual(expected, await RowAsync(connection, current.ToString(CultureInfo.InvariantCulture), token));
        }

        Assert.AreEqual(1L, await ScalarAsync<long>(connection,
            $"SELECT count(*) FROM subtrans_infos({current.ToString(CultureInfo.InvariantCulture)})", token));
        await ExecuteAsync(connection, "COMMIT", token);

        long next = await ScalarAsync<long>(connection, "SELECT pg_current_xact_id()::text::bigint", token) + 1000;
        await ErrorTrap.InstallAsync(connection, null, token);
        foreach ((string input, string reason) in new[]
        {
            ("0", "invalid transaction ID"),
            (next.ToString(CultureInfo.InvariantCulture), "transaction ID is in the future"),
            ((current | (1L << 32)).ToString(CultureInfo.InvariantCulture), "transaction ID is in the future"),
            ("-1", "transaction ID is in the future"),
        })
        {
            string reported = input == "-1" ? ulong.MaxValue.ToString(CultureInfo.InvariantCulture) : input;
            Assert.AreEqual($"XX000: Invalid transaction ID {reported}: {reason}",
                await ErrorTrap.RunAsync(connection, null, $"SELECT * FROM subtrans_infos({input})", token));
            Assert.AreEqual(expected.Replace("in progress", "committed", StringComparison.Ordinal),
                await RowAsync(connection, current.ToString(CultureInfo.InvariantCulture), token, timestamp: false));
        }

        Assert.AreEqual(connection.ProcessID, await ScalarAsync<int>(connection, "SELECT pg_backend_pid()", token));
    }

    /// <summary>
    /// Reproduces pgrx's README examples: nested savepoints, aborted subtransactions and commit timestamps.
    /// </summary>
    [TestMethod]
    public async Task SubtransactionsSampleFollowsSavepointsAndCommitTimestamps()
    {
        CancellationToken token = context.CancellationToken;
        await using PostgresTestCluster cluster = await StartAsync(token);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        await ExecuteAsync(connection, "CREATE EXTENSION ankus_subtransactions; CREATE TABLE t1 (id int)", token);

        await ExecuteAsync(connection, "BEGIN; INSERT INTO t1 VALUES (1); SAVEPOINT a; INSERT INTO t1 VALUES (2); SAVEPOINT b; INSERT INTO t1 VALUES (3)", token);
        long[] nested = await LockedAsync(connection, token);
        Assert.HasCount(3, nested);
        Assert.AreEqual(string.Join(';', $"{nested[0]},in progress", $"{nested[1]},in progress,{nested[0]},{nested[0]},1",
            $"{nested[2]},in progress,{nested[1]},{nested[0]},2"), await ScalarAsync<string>(connection, LockedTransactions, token));
        await ExecuteAsync(connection, "COMMIT", token);

        // After commit the IDs precede this transaction's TransactionXmin, so pg_subtrans ancestry is not reported.
        string committed = await ScalarAsync<string>(connection, $"""
            SELECT (pg_xact_commit_timestamp('{nested[0]}'::xid) AT TIME ZONE 'UTC')::text
            """, token);
        foreach (long xid in nested)
        {
            Assert.AreEqual($"{xid},committed,{committed}", await RowAsync(connection, xid.ToString(CultureInfo.InvariantCulture), token));
        }

        await ExecuteAsync(connection, "BEGIN; INSERT INTO t1 VALUES (10); SAVEPOINT sp1; INSERT INTO t1 VALUES (20); SAVEPOINT sp2; INSERT INTO t1 VALUES (30)", token);
        long[] aborted = await LockedAsync(connection, token);
        Assert.HasCount(3, aborted);
        await ExecuteAsync(connection, "ROLLBACK TO SAVEPOINT sp1", token);
        Assert.AreEqual($"{aborted[0]},in progress", await ScalarAsync<string>(connection, LockedTransactions, token));
        Assert.AreEqual($"{aborted[1]},aborted,{aborted[0]},{aborted[0]},1", await RowAsync(connection, aborted[1].ToString(CultureInfo.InvariantCulture), token));
        Assert.AreEqual($"{aborted[2]},aborted,{aborted[1]},{aborted[0]},2", await RowAsync(connection, aborted[2].ToString(CultureInfo.InvariantCulture), token));
        await ExecuteAsync(connection, "COMMIT", token);
        Assert.AreEqual($"{aborted[1]},aborted", await RowAsync(connection, aborted[1].ToString(CultureInfo.InvariantCulture), token));
        Assert.StartsWith($"{aborted[0]},committed,", await RowAsync(connection, aborted[0].ToString(CultureInfo.InvariantCulture), token));
        Assert.AreEqual(connection.ProcessID, await ScalarAsync<int>(connection, "SELECT pg_backend_pid()", token));
    }

    /// <summary>
    /// Reports another backend's running and rolled-back transactions, and omits commit times when tracking is off.
    /// </summary>
    [TestMethod]
    public Task SubtransactionsSampleObservesOtherBackends()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SubtransactionsSampleObservesOtherBackends),
            async (connection, transaction, token) =>
            {
                await using (var install = new NpgsqlCommand("CREATE EXTENSION ankus_subtransactions", connection, transaction))
                {
                    await install.ExecuteNonQueryAsync(token);
                }

                await using NpgsqlConnection other = await PostgresFixture.Cluster.OpenConnectionAsync(token);
                long committed = await ScalarAsync<long>(other, "SELECT pg_current_xact_id()::text::bigint", token);
                await ExecuteAsync(other, "BEGIN", token);
                long running = await ScalarAsync<long>(other, "SELECT pg_current_xact_id()::text::bigint", token);
                Assert.AreEqual($"{committed},committed", await RowAsync(connection, committed.ToString(CultureInfo.InvariantCulture), token, transaction));
                Assert.AreEqual($"{running},in progress", await RowAsync(connection, running.ToString(CultureInfo.InvariantCulture), token, transaction));
                await ExecuteAsync(other, "ROLLBACK", token);
                Assert.AreEqual($"{running},aborted", await RowAsync(connection, running.ToString(CultureInfo.InvariantCulture), token, transaction));
            }, context.CancellationToken);

    /// <summary>
    /// Starts an otherwise idle cluster whose TransactionXmin follows this test's transactions and that records commit times.
    /// </summary>
    private static async Task<PostgresTestCluster> StartAsync(CancellationToken token)
    {
        PostgresTestClusterOptions defaults = await IntegrationEnvironment.CreateOptionsAsync(token);
        PostgresTestClusterOptions options = new()
        {
            Installation = defaults.Installation,
            DataDirectoryBase = defaults.DataDirectoryBase,
            LogDirectory = defaults.LogDirectory,
            PostgreSqlConfiguration = [.. defaults.PostgreSqlConfiguration, "track_commit_timestamp = on", "autovacuum = off"],
        };
        return await PostgresTestCluster.StartAsync(options, token);
    }

    /// <summary>
    /// Reads the current backend's locked transaction IDs in ascending order.
    /// </summary>
    private static Task<long[]> LockedAsync(NpgsqlConnection connection, CancellationToken token) => ScalarAsync<long[]>(connection,
        "SELECT array_agg(transactionid::text::bigint ORDER BY transactionid::text::bigint) FROM pg_locks WHERE transactionid IS NOT NULL AND pid = pg_backend_pid()",
        token);

    /// <summary>
    /// Formats the single row for an input with SQL NULL columns omitted, optionally without its commit time.
    /// </summary>
    private static Task<string> RowAsync(NpgsqlConnection connection, string input, CancellationToken token,
        NpgsqlTransaction? transaction = null, bool timestamp = true)
        => ScalarAsync<string>(connection, $"""
            SELECT concat_ws(',', xid, status, parent_xid, top_parent_xid, sub_level, {(timestamp ? "commit_timestamp" : "NULL")})
            FROM subtrans_infos({input})
            """, token, transaction);

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql, CancellationToken token,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(token);
    }
}
