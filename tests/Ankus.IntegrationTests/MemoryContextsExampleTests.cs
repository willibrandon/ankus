using System.Globalization;
using System.Text.RegularExpressions;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes the ported pgrx memory_contexts sample through its published Native AOT extension.
/// </summary>
/// <param name="context">The current cancellation and diagnostic context.</param>
[TestClass]
public sealed partial class MemoryContextsExampleTests(TestContext context)
{
    /// <summary>
    /// Mirrors pgrx's scratch-context tests and checks NULL cells, empty input, wide sums and stale allocations.
    /// </summary>
    [TestMethod]
    public Task MemoryContextsSampleResetsScratchContexts()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(MemoryContextsSampleResetsScratchContexts),
            async (connection, transaction, token) =>
            {
                await ExecuteAsync(connection, transaction, "CREATE EXTENSION ankus_memory_contexts", token);
                Assert.AreEqual(21L, await ScalarAsync<long>(connection, transaction, "SELECT sum_with_scratch(ARRAY[1,2,3,4,5,6])", token));
                Assert.AreEqual(100, await ScalarAsync<int>(connection, transaction, "SELECT scratch_count(100)", token));
                Assert.AreEqual(4L, await ScalarAsync<long>(connection, transaction, "SELECT sum_with_scratch(ARRAY[1,NULL,3])", token));
                Assert.AreEqual(0L, await ScalarAsync<long>(connection, transaction, "SELECT sum_with_scratch('{}'::integer[])", token));
                Assert.AreEqual(0L, await ScalarAsync<long>(connection, transaction, "SELECT sum_with_scratch(ARRAY[NULL,NULL]::integer[])", token));
                Assert.AreEqual(4294967294L, await ScalarAsync<long>(connection, transaction,
                    "SELECT sum_with_scratch(ARRAY[2147483647,2147483647])", token));
                Assert.AreEqual(-4294967296L, await ScalarAsync<long>(connection, transaction,
                    "SELECT sum_with_scratch(ARRAY[-2147483648,-2147483648])", token));
                Assert.IsTrue(await ScalarAsync<bool>(connection, transaction,
                    "SELECT sum_with_scratch(NULL) IS NULL AND scratch_count(NULL) IS NULL", token));
                Assert.AreEqual(0, await ScalarAsync<int>(connection, transaction, "SELECT scratch_count(0)", token));
                Assert.AreEqual(0, await ScalarAsync<int>(connection, transaction, "SELECT scratch_count(-5)", token));
                Assert.IsTrue(await ScalarAsync<bool>(connection, transaction, "SELECT reset_rejects_stale_allocation()", token));
                Assert.AreEqual(210L, await ScalarAsync<long>(connection, transaction,
                    "SELECT sum(sum_with_scratch(ARRAY[value, value]))::bigint FROM generate_series(1, 14) AS value", token));
                Assert.AreSequenceEqual<string>(
                [
                    "iter_count(start bigint, \"end\" bigint) SETOF bigint",
                    "materialized_pairs(n integer) TABLE(idx integer, square bigint)",
                    "reset_rejects_stale_allocation() boolean",
                    "scratch_count(n integer) integer",
                    "sum_with_scratch(arr integer[]) bigint",
                ], await ScalarAsync<string[]>(connection, transaction, """
                    SELECT array_agg(p.proname || '(' || pg_get_function_arguments(p.oid) || ') ' || pg_get_function_result(p.oid)
                        ORDER BY p.proname COLLATE "C")
                    FROM pg_proc p JOIN pg_depend d ON d.classid = 'pg_proc'::regclass AND d.objid = p.oid AND d.deptype = 'e'
                    JOIN pg_extension e ON e.oid = d.refobjid WHERE e.extname = 'ankus_memory_contexts'
                    """, token));
                Assert.AreEqual(connection.ProcessID, await ScalarAsync<int>(connection, transaction, "SELECT pg_backend_pid()", token));
            }, context.CancellationToken);

    /// <summary>
    /// Mirrors pgrx's streaming and precomputed set tests, including empty ranges, early termination and wide squares.
    /// </summary>
    [TestMethod]
    public Task MemoryContextsSampleReturnsSets()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(MemoryContextsSampleReturnsSets),
            async (connection, transaction, token) =>
            {
                await ExecuteAsync(connection, transaction, "CREATE EXTENSION ankus_memory_contexts", token);
                Assert.AreEqual(10L, await ScalarAsync<long>(connection, transaction, "SELECT sum(x)::bigint FROM iter_count(0, 5) AS x", token));
                Assert.AreEqual(10L, await ScalarAsync<long>(connection, transaction, "SELECT count(*) FROM materialized_pairs(10)", token));
                Assert.AreEqual(49L, await ScalarAsync<long>(connection, transaction,
                    "SELECT square FROM materialized_pairs(10) WHERE idx = 7", token));
                Assert.AreSequenceEqual([-2L, -1L, 0L, 1L, 2L], await ScalarAsync<long[]>(connection, transaction,
                    "SELECT array_agg(x ORDER BY ordinal) FROM iter_count(-2, 3) WITH ORDINALITY AS item(x, ordinal)", token));
                Assert.AreSequenceEqual([9223372036854775805L, 9223372036854775806L], await ScalarAsync<long[]>(connection, transaction,
                    "SELECT array_agg(x ORDER BY ordinal) FROM iter_count(9223372036854775805, 9223372036854775807) WITH ORDINALITY AS item(x, ordinal)",
                    token));
                Assert.AreEqual(0L, await ScalarAsync<long>(connection, transaction,
                    "SELECT (SELECT count(*) FROM iter_count(5, 5)) + (SELECT count(*) FROM iter_count(5, 0))", token));
                // A target-list set function streams one row per request, so LIMIT stops an effectively unbounded range.
                Assert.AreSequenceEqual([0L, 1L, 2L], await ScalarAsync<long[]>(connection, transaction,
                    "SELECT array_agg(x) FROM (SELECT iter_count(0, 9223372036854775807) AS x LIMIT 3) AS limited", token));
                Assert.AreEqual("0:0,1:1,2:4,3:9", await ScalarAsync<string>(connection, transaction,
                    "SELECT string_agg(idx || ':' || square, ',' ORDER BY ordinality) FROM materialized_pairs(4) WITH ORDINALITY", token));
                Assert.AreEqual(2499900001L, await ScalarAsync<long>(connection, transaction,
                    "SELECT square FROM materialized_pairs(50000) WHERE idx = 49999", token));
                Assert.AreEqual(0L, await ScalarAsync<long>(connection, transaction,
                    "SELECT (SELECT count(*) FROM materialized_pairs(0)) + (SELECT count(*) FROM materialized_pairs(-1))", token));
                Assert.AreEqual(connection.ProcessID, await ScalarAsync<int>(connection, transaction, "SELECT pg_backend_pid()", token));
            }, context.CancellationToken);

    /// <summary>
    /// Shared preload registers pgrx's worker, whose TopMemoryContext counter survives every wake-up until termination.
    /// </summary>
    [TestMethod]
    public async Task MemoryContextsSampleWorkerKeepsCounterInTopMemoryContext()
    {
        CancellationToken token = context.CancellationToken;
        PostgresTestClusterOptions defaults = await IntegrationEnvironment.CreateOptionsAsync(token);
        PostgresTestClusterOptions options = new()
        {
            Installation = defaults.Installation,
            DataDirectoryBase = defaults.DataDirectoryBase,
            LogDirectory = defaults.LogDirectory,
            PostgreSqlConfiguration = [.. defaults.PostgreSqlConfiguration, "shared_preload_libraries = 'Ankus.Examples.MemoryContexts'"],
        };
        await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(options, token);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        (int Process, string Event)[] events = await WaitForWorkerAsync(cluster, 1, token);
        Assert.AreEqual("starting (arg=123)", events[0].Event);
        int worker = events[0].Process;
        Assert.AreNotEqual(connection.ProcessID, worker);
        for (int tick = 1; tick <= 3; tick++)
        {
            // A reload signal wakes the worker's latch before its five-second timeout expires.
            Assert.IsTrue(await ScalarAsync<bool>(connection, null, "SELECT pg_reload_conf()", token));
            events = await WaitForWorkerAsync(cluster, tick + 1, token);
        }

        await using (var install = new NpgsqlCommand("CREATE EXTENSION ankus_memory_contexts; SELECT sum_with_scratch(ARRAY[20,22])", connection))
        {
            Assert.AreEqual(42L, await install.ExecuteScalarAsync(token));
        }

        // The worker has no database connection, so it is not in the process array that pg_terminate_backend searches.
        await ProcessRunner.RunCheckedAsync(cluster.Installation.PgCtlPath,
            ["kill", "TERM", worker.ToString(CultureInfo.InvariantCulture)], new Dictionary<string, string?>(), token);
        while (events[^1].Event != "exiting")
        {
            events = await WaitForWorkerAsync(cluster, events.Length + 1, token);
        }

        // The worker exits gracefully without disturbing other sessions.
        Assert.AreEqual(42, await ScalarAsync<int>(connection, null, "SELECT 6 * 7", token));

        // Every event comes from one process, and each wake-up advances the same TopMemoryContext counter.
        Assert.IsTrue(events.All(item => item.Process == worker), cluster.ReadServerLog());
        string[] ticks = [.. events[1..^1].Select(static item => item.Event)];
        Assert.IsGreaterThanOrEqualTo(3, ticks.Length);
        Assert.AreSequenceEqual(Enumerable.Range(1, ticks.Length).Select(static tick => "tick " + tick.ToString(CultureInfo.InvariantCulture)), ticks);
    }

    /// <summary>
    /// Matches the worker's log lines, capturing the logging process and the event text.
    /// </summary>
    [GeneratedRegex(@"^\[[^\]]*\] \[(?<process>\d+)\] .*?LOG:  memory_contexts demo worker (?<event>.+)$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex WorkerLines();

    private static async Task<(int Process, string Event)[]> WaitForWorkerAsync(PostgresTestCluster cluster, int count, CancellationToken token)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            (int Process, string Event)[] events = [.. WorkerLines().Matches(cluster.ReadServerLog().ReplaceLineEndings("\n"))
                .Select(static match => (int.Parse(match.Groups["process"].Value, CultureInfo.InvariantCulture), match.Groups["event"].Value))];
            if (events.Length >= count)
            {
                return events;
            }

            TimeSpan duration = elapsed.Elapsed;
            Assert.IsLessThan(TimeSpan.FromSeconds(30), duration, duration >= TimeSpan.FromSeconds(30) ? cluster.ReadServerLog() : null);
            await Task.Delay(25, token);
        }
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(token);
    }
}
