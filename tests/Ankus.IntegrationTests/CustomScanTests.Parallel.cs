using System.Text.Json;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies provider-owned DSM through real parallel executor lifecycle transitions.
/// </summary>
public sealed partial class CustomScanTests
{
    /// <summary>
    /// A parameter-dependent outer offset rescans the same Gather and resets its provider-owned shared counters.
    /// </summary>
    /// <param name="stopEarly">Whether each inner execution stops before consuming the complete scan.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TraceScanReinitializesParallelState(bool stopEarly)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        string table = await CreateParallelInputAsync(connection, token);
        await using var command = new NpgsqlCommand("SET LOCAL enable_material = off; " + DisableMemoizeSql(true), connection);
        await command.ExecuteNonQueryAsync(token);
        string query = $"""
            SELECT iteration, array_agg(value ORDER BY value)
            FROM (VALUES (1), (2), (3)) AS rounds(iteration)
            CROSS JOIN LATERAL (SELECT value FROM {table} {(stopEarly ? "LIMIT 97" : string.Empty)} OFFSET iteration * 0) AS rows
            GROUP BY iteration ORDER BY iteration
            """;
        using (JsonDocument plan = await ExplainAsync(connection, query, true, token))
        {
            JsonElement root = plan.RootElement[0].GetProperty("Plan");
            context.WriteLine(plan.RootElement.GetRawText());
            JsonElement gather = Assert.ContainsSingle(PlanNodes(root, "Gather"));
            Assert.AreEqual(3L, gather.GetProperty("Actual Loops").GetInt64());
            int launched = gather.GetProperty("Workers Launched").GetInt32();
            Assert.IsGreaterThan(0, launched);
            JsonElement scan = Assert.ContainsSingle(TraceNodes(root));
            Assert.AreEqual(3L, scan.GetProperty("Trace DSM Generation").GetInt64());
            if (stopEarly)
            {
                Assert.IsInRange(97L, 30000L, scan.GetProperty("Shared Trace Rows").GetInt64());
                Assert.IsInRange(97L, 30000L + launched, scan.GetProperty("Shared Trace Calls").GetInt64());
                Assert.IsInRange(1L, launched, scan.GetProperty("Trace Worker Attachments").GetInt64());
                Assert.IsInRange(0L, launched, scan.GetProperty("Trace Worker Shutdowns").GetInt64());
            }
            else
            {
                Assert.AreEqual(30000L, scan.GetProperty("Shared Trace Rows").GetInt64());
                Assert.AreEqual(30000L + launched, scan.GetProperty("Shared Trace Calls").GetInt64());
                Assert.AreEqual(launched, scan.GetProperty("Trace Worker Attachments").GetInt64());
                Assert.AreEqual(launched, scan.GetProperty("Trace Worker Shutdowns").GetInt64());
            }
        }

        command.CommandText = query;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        for (int iteration = 1; iteration <= 3; iteration++)
        {
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(iteration, reader.GetInt32(0));
            int[] values = reader.GetFieldValue<int[]>(1);
            if (stopEarly)
            {
                Assert.HasCount(97, values);
                Assert.HasCount(97, values.Distinct());
                foreach (int value in values)
                {
                    Assert.IsInRange(1, 30000, value);
                }
            }
            else
            {
                Assert.AreSequenceEqual(Enumerable.Range(1, 30000), values);
            }
        }

        Assert.IsFalse(await reader.ReadAsync(token));
    }

    /// <summary>
    /// A never-started or partially consumed Gather copies bounded observations and permits a complete same-session scan.
    /// </summary>
    /// <param name="limit">Zero, one or several requested rows.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(97)]
    public async Task TraceScanStopsParallelExecutionEarly(int limit)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        string table = await CreateParallelInputAsync(connection, token);
        string query = $"SELECT value FROM {table} LIMIT {limit}";
        using (JsonDocument plan = await ExplainAsync(connection, query, true, token))
        {
            JsonElement root = plan.RootElement[0].GetProperty("Plan");
            context.WriteLine(plan.RootElement.GetRawText());
            Assert.AreEqual(limit, root.GetProperty("Actual Rows").GetDouble());
            JsonElement gather = Assert.ContainsSingle(PlanNodes(root, "Gather"));
            int launched = gather.GetProperty("Workers Launched").GetInt32();
            JsonElement scan = Assert.ContainsSingle(TraceNodes(root));
            Assert.IsTrue(scan.GetProperty("Parallel Aware").GetBoolean());
            if (limit == 0)
            {
                Assert.AreEqual(0, launched);
                Assert.AreEqual(0L, scan.GetProperty("Trace DSM Generation").GetInt64());
                Assert.AreEqual(0L, scan.GetProperty("Shared Trace Rows").GetInt64());
                Assert.AreEqual(0L, scan.GetProperty("Shared Trace Calls").GetInt64());
                Assert.AreEqual(0L, scan.GetProperty("Trace Worker Attachments").GetInt64());
                Assert.AreEqual(0L, scan.GetProperty("Trace Worker Shutdowns").GetInt64());
            }
            else
            {
                Assert.IsGreaterThan(0, launched);
                Assert.AreEqual(1L, scan.GetProperty("Trace DSM Generation").GetInt64());
                Assert.IsInRange(limit, 30000L, scan.GetProperty("Shared Trace Rows").GetInt64());
                Assert.IsInRange(limit, 30000L + launched, scan.GetProperty("Shared Trace Calls").GetInt64());
                Assert.IsInRange(1L, launched, scan.GetProperty("Trace Worker Attachments").GetInt64());
                Assert.IsInRange(0L, launched, scan.GetProperty("Trace Worker Shutdowns").GetInt64());
            }
        }

        await using var command = new NpgsqlCommand(query, connection);
        var seen = new HashSet<int>();
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                int value = reader.GetInt32(0);
                Assert.IsInRange(1, 30000, value);
                Assert.IsTrue(seen.Add(value));
            }
        }

        Assert.HasCount(limit, seen);
        await AssertCompleteParallelScanAsync(connection, table, token);
    }

    /// <summary>
    /// Worker errors retain exact diagnostics and reclaim DSM before healthy replacement workers execute in the same leader session.
    /// </summary>
    /// <param name="managed">Whether the child raises a managed or PostgreSQL-native error.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TraceScanRecoversFromParallelErrors(bool managed)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        int backend = connection.ProcessID;
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        string table = await CreateParallelInputAsync(connection, token);
        await ResetAsync(connection, token);
        await transaction.SaveAsync("before_failure", token);
        string predicate = managed ? "datatype.custom_scan_accept(value)" : "100 / (value - 2) IS NOT NULL";
        await using var command = new NpgsqlCommand($"SELECT value FROM {table} WHERE {predicate}", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        Assert.AreEqual(managed ? "P7521" : "22012", error.SqlState);
        Assert.AreEqual(managed ? "managed custom scan child failure" : "division by zero", error.MessageText);
        Assert.AreEqual(managed ? "trace child detail" : null, error.Detail);
        Assert.AreEqual(managed ? "trace child hint" : null, error.Hint);
        Assert.Contains("parallel worker", error.Where ?? string.Empty);
        await transaction.RollbackAsync("before_failure", token);
        Assert.AreEqual(backend, connection.ProcessID);
        long[] counts = await CountsAsync(connection, token);
        Assert.AreEqual(1L, counts[1]);
        Assert.AreEqual(0L, counts[4]);
        Assert.AreEqual(1L, counts[7]);
        await AssertCompleteParallelScanAsync(connection, table, token);
    }

    /// <summary>
    /// A parallel plan falls back to the leader when no workers are available, even with leader participation disabled.
    /// </summary>
    [TestMethod]
    public async Task TraceScanRunsWithoutAvailableWorkers()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        string table = await CreateParallelInputAsync(connection, token);
        await using var command = new NpgsqlCommand("SET LOCAL max_parallel_workers = 0", connection);
        await command.ExecuteNonQueryAsync(token);
        using (JsonDocument plan = await ExplainAsync(connection, $"SELECT value FROM {table}", true, token))
        {
            JsonElement root = plan.RootElement[0].GetProperty("Plan");
            context.WriteLine(plan.RootElement.GetRawText());
            JsonElement gather = Assert.ContainsSingle(PlanNodes(root, "Gather"));
            Assert.IsGreaterThan(0, gather.GetProperty("Workers Planned").GetInt32());
            Assert.AreEqual(0, gather.GetProperty("Workers Launched").GetInt32());
            JsonElement scan = Assert.ContainsSingle(TraceNodes(root));
            Assert.AreEqual(30000L, scan.GetProperty("Shared Trace Rows").GetInt64());
            Assert.AreEqual(30001L, scan.GetProperty("Shared Trace Calls").GetInt64());
            Assert.AreEqual(0L, scan.GetProperty("Trace Worker Attachments").GetInt64());
            Assert.AreEqual(0L, scan.GetProperty("Trace Worker Shutdowns").GetInt64());
            Assert.AreEqual(1L, scan.GetProperty("Trace DSM Generation").GetInt64());
        }

        command.CommandText = $"SELECT datatype.custom_scan_process(value), value FROM {table}";
        var seen = new HashSet<int>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            Assert.AreEqual(connection.ProcessID, reader.GetInt32(0));
            Assert.IsTrue(seen.Add(reader.GetInt32(1)));
        }

        Assert.AreSequenceEqual(Enumerable.Range(1, 30000), seen.Order());
    }

    /// <summary>
    /// Creates transaction-owned input and enables actual parallel scans without changing cluster-wide settings.
    /// </summary>
    private static async Task<string> CreateParallelInputAsync(NpgsqlConnection connection, CancellationToken token)
    {
        string table = "tests.trace_shared_" + Guid.NewGuid().ToString("N");
        await using var command = new NpgsqlCommand($"""
            CREATE TABLE {table} AS SELECT generate_series(1, 30000) AS value;
            ALTER TABLE {table} SET (parallel_workers = 2);
            ANALYZE {table};
            SET LOCAL max_parallel_workers_per_gather = 2;
            SET LOCAL min_parallel_table_scan_size = 0;
            SET LOCAL parallel_setup_cost = 0;
            SET LOCAL parallel_tuple_cost = 0;
            SET LOCAL parallel_leader_participation = off;
            """, connection);
        await command.ExecuteNonQueryAsync(token);
        return table;
    }

    /// <summary>
    /// Requires fresh shared state, real foreign worker processes and every original row exactly once.
    /// </summary>
    private static async Task AssertCompleteParallelScanAsync(NpgsqlConnection connection, string table, CancellationToken token)
    {
        using (JsonDocument plan = await ExplainAsync(connection, $"SELECT value FROM {table}", true, token))
        {
            JsonElement root = plan.RootElement[0].GetProperty("Plan");
            int launched = WorkersLaunched(root);
            Assert.IsGreaterThan(0, launched);
            JsonElement scan = Assert.ContainsSingle(TraceNodes(root));
            Assert.AreEqual(30000L, scan.GetProperty("Shared Trace Rows").GetInt64());
            Assert.AreEqual(30000L + launched, scan.GetProperty("Shared Trace Calls").GetInt64());
            Assert.AreEqual(launched, scan.GetProperty("Trace Worker Attachments").GetInt64());
            Assert.AreEqual(launched, scan.GetProperty("Trace Worker Shutdowns").GetInt64());
            Assert.AreEqual(1L, scan.GetProperty("Trace DSM Generation").GetInt64());
        }

        await using var command = new NpgsqlCommand($"SELECT datatype.custom_scan_process(value), value FROM {table}", connection);
        var seen = new HashSet<int>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            Assert.AreNotEqual(connection.ProcessID, reader.GetInt32(0));
            Assert.IsTrue(seen.Add(reader.GetInt32(1)));
        }

        Assert.AreSequenceEqual(Enumerable.Range(1, 30000), seen.Order());
    }

    /// <summary>
    /// Selects executor nodes by native plan kind, retaining their actual runtime instrumentation.
    /// </summary>
    private static IEnumerable<JsonElement> PlanNodes(JsonElement node, string kind)
    {
        if (node.GetProperty("Node Type").GetString() == kind)
        {
            yield return node;
        }

        if (node.TryGetProperty("Plans", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                foreach (JsonElement nested in PlanNodes(child, kind))
                {
                    yield return nested;
                }
            }
        }
    }
}
