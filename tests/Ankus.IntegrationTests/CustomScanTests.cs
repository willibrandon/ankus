using System.Diagnostics;
using System.Text.Json;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes the public trace provider through real PostgreSQL planning, execution, rescan and cleanup.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
[DoNotParallelize] // Real workers share a bounded pool; namespace DDL also invalidates cached plans.
public sealed partial class CustomScanTests(TestContext context)
{
    /// <summary>
    /// Native child rows, SQL NULL, text and projected expressions retain their exact values through a real custom plan.
    /// </summary>
    [TestMethod]
    public async Task TraceScanReturnsExactRows()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        const string Query = "SELECT value::bigint * 2, label || '!' FROM trace_input WHERE value <> 2 ORDER BY value";
        using JsonDocument plan = await ExplainAsync(connection, Query, false, token);
        JsonElement scan = Assert.ContainsSingle(TraceNodes(plan.RootElement[0].GetProperty("Plan")));
        Assert.AreEqual("Seq Scan", scan.GetProperty("Plans")[0].GetProperty("Node Type").GetString());
        Assert.AreEqual("trace_input", scan.GetProperty("Plans")[0].GetProperty("Relation Name").GetString());
        await ResetAsync(connection, token);
        await using var command = new NpgsqlCommand(Query, connection);
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
        {
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(-4294967296L, reader.GetInt64(0));
            Assert.AreEqual("minimum!", reader.GetString(1));
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(2L, reader.GetInt64(0));
            Assert.IsTrue(reader.IsDBNull(1));
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(4294967294L, reader.GetInt64(0));
            Assert.AreEqual("maximum!", reader.GetString(1));
            Assert.IsFalse(await reader.ReadAsync(token));
        }

        Assert.AreSequenceEqual<long>([1, 1, 1, 4, 1, 0, 4, 1], await CountsAsync(connection, token));
    }

    /// <summary>
    /// Empty, singleton and early-stop executions preserve exact output and release query-owned state once.
    /// </summary>
    /// <param name="mode">Empty, singleton or LIMIT execution.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task TraceScanHandlesResultBoundaries(int mode)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        string query = mode switch
        {
            0 => "SELECT value FROM trace_input WHERE value = 9",
            1 => "SELECT value FROM trace_input WHERE value = 2",
            _ => "SELECT value FROM trace_input LIMIT 1",
        };
        await using var command = new NpgsqlCommand(query, connection);
        var actual = new List<int>();
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                actual.Add(reader.GetInt32(0));
            }
        }

        Assert.AreSequenceEqual<int>(mode == 0 ? [] : [mode == 1 ? 2 : int.MinValue], actual);
        long calls = mode == 1 ? 2 : 1;
        Assert.AreSequenceEqual<long>([1, 1, 1, calls, 1, 0, calls, 1], await CountsAsync(connection, token));
    }

    /// <summary>
    /// EXPLAIN-only initializes without scanning, while ANALYZE reports independently known child tuple counts.
    /// </summary>
    [TestMethod]
    public async Task TraceScanExplainsExecution()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        const string Query = "SELECT value, label FROM trace_input WHERE value >= 1";
        using (JsonDocument plan = await ExplainAsync(connection, Query, false, token))
        {
            JsonElement scan = Assert.ContainsSingle(TraceNodes(plan.RootElement[0].GetProperty("Plan")));
            Assert.AreEqual(0, scan.GetProperty("Trace Rows").GetInt64());
            Assert.AreEqual(0, scan.GetProperty("Trace Calls").GetInt64());
            Assert.AreEqual(0, scan.GetProperty("Trace Rescans").GetInt64());
        }

        Assert.AreSequenceEqual<long>([1, 1, 1, 0, 1, 0, 0, 1], await CountsAsync(connection, token));
        await ResetAsync(connection, token);
        using (JsonDocument plan = await ExplainAsync(connection, Query, true, token))
        {
            JsonElement scan = Assert.ContainsSingle(TraceNodes(plan.RootElement[0].GetProperty("Plan")));
            Assert.AreEqual(3, scan.GetProperty("Trace Rows").GetInt64());
            Assert.AreEqual(4, scan.GetProperty("Trace Calls").GetInt64());
            Assert.AreEqual(0, scan.GetProperty("Trace Rescans").GetInt64());
            Assert.AreEqual(3D, scan.GetProperty("Actual Rows").GetDouble());
            Assert.AreEqual(3D, scan.GetProperty("Plans")[0].GetProperty("Actual Rows").GetDouble());
        }

        Assert.AreSequenceEqual<long>([1, 1, 1, 4, 1, 0, 4, 1], await CountsAsync(connection, token));
    }

    /// <summary>
    /// A parameterized nested loop performs actual custom rescans and sees each changed outer parameter.
    /// </summary>
    [TestMethod]
    public async Task TraceScanRescansParameterizedChildren()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await using var command = new NpgsqlCommand("SET enable_material = off; " + DisableMemoizeSql(), connection);
        await command.ExecuteNonQueryAsync(token);
        const string Query = """
            SELECT threshold, value
            FROM (VALUES (0), (1), (2)) AS limits(threshold)
            CROSS JOIN LATERAL
              (SELECT value FROM trace_input WHERE value > threshold OFFSET 0) AS matched
            ORDER BY threshold, value
            """;
        using (JsonDocument plan = await ExplainAsync(connection, Query, true, token))
        {
            JsonElement scan = Assert.ContainsSingle(TraceNodes(plan.RootElement[0].GetProperty("Plan")));
            Assert.AreEqual(3, scan.GetProperty("Actual Loops").GetInt64());
            Assert.AreEqual(3, scan.GetProperty("Trace Rescans").GetInt64());
            Assert.AreEqual(6, scan.GetProperty("Trace Rows").GetInt64());
        }

        command.CommandText = Query;
        var rows = new List<(int Threshold, int Value)>();
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                rows.Add((reader.GetInt32(0), reader.GetInt32(1)));
            }
        }

        Assert.AreSequenceEqual<(int, int)>([(0, 1), (0, 2), (0, int.MaxValue), (1, 2), (1, int.MaxValue), (2, int.MaxValue)], rows);
        long[] counts = await CountsAsync(connection, token);
        Assert.AreEqual(6, counts[5]);
        Assert.AreEqual(counts[3], counts[6]);
        Assert.AreEqual(2, counts[7]);
    }

    /// <summary>
    /// Prepared native plans retain their registered method tables across transactions and disabling future tracing.
    /// </summary>
    [TestMethod]
    public async Task TraceScanPreparedPlansRetainMethods()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await using var command = new NpgsqlCommand("""
            SET plan_cache_mode = force_generic_plan;
            PREPARE traced(int) AS SELECT sum(value::bigint) FROM trace_input WHERE value > $1;
            BEGIN;
            """, connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "EXECUTE traced(0)";
        Assert.AreEqual(2147483650m, await command.ExecuteScalarAsync(token));
        command.CommandText = "COMMIT; SELECT datatype.custom_scan_collect()";
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "EXECUTE traced(1)";
        Assert.AreEqual(2147483649m, await command.ExecuteScalarAsync(token));
        Assert.AreSequenceEqual<long>([1, 2, 2, 7, 2, 0, 7, 2], await CountsAsync(connection, token), "The cached plan must remain traced across COMMIT and collection.");
        command.CommandText = "SET ankus_trace_scan.enabled = off";
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "EXECUTE traced(0)";
        Assert.AreEqual(2147483650m, await command.ExecuteScalarAsync(token));
        command.CommandText = "DEALLOCATE traced";
        await command.ExecuteNonQueryAsync(token);
        Assert.AreSequenceEqual<long>([1, 3, 3, 11, 3, 0, 11, 3], await CountsAsync(connection, token));
    }

    /// <summary>
    /// A scroll cursor delegates direction changes to its real sequential child without changing row identity.
    /// </summary>
    [TestMethod]
    public async Task TraceScanReadsBackward()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await using var command = new NpgsqlCommand("BEGIN; DECLARE traced SCROLL CURSOR FOR SELECT value FROM trace_input", connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "FETCH FORWARD 1 FROM traced";
        Assert.AreEqual(int.MinValue, await command.ExecuteScalarAsync(token));
        command.CommandText = "FETCH FORWARD 1 FROM traced";
        Assert.AreEqual(1, await command.ExecuteScalarAsync(token));
        command.CommandText = "FETCH BACKWARD 1 FROM traced";
        Assert.AreEqual(int.MinValue, await command.ExecuteScalarAsync(token));
        command.CommandText = "FETCH FORWARD 1 FROM traced";
        Assert.AreEqual(1, await command.ExecuteScalarAsync(token));
        command.CommandText = "CLOSE traced; COMMIT";
        await command.ExecuteNonQueryAsync(token);
        Assert.AreSequenceEqual<long>([1, 1, 1, 4, 1, 0, 4, 1], await CountsAsync(connection, token));
    }

    /// <summary>
    /// Child failures unwind the managed executor and reclaim its native query owner before a healthy same-session retry.
    /// </summary>
    /// <param name="managed">Whether a managed child function or native integer division raises the error.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TraceScanErrorsUnwindAndRecover(bool managed)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        int backend = connection.ProcessID;
        string predicate = managed ? "datatype.custom_scan_accept(value)" : "100::bigint / (value::bigint - 2) IS NOT NULL";
        await using var command = new NpgsqlCommand($"SELECT value FROM trace_input WHERE {predicate}", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        Assert.AreEqual(managed ? "P7521" : "22012", error.SqlState);
        Assert.AreEqual(managed ? "managed custom scan child failure" : "division by zero", error.MessageText);
        Assert.AreEqual(managed ? "trace child detail" : null, error.Detail);
        Assert.AreEqual(managed ? "trace child hint" : null, error.Hint);
        Assert.AreSequenceEqual<long>([1, 1, 1, 3, 0, 0, 3, 1], await CountsAsync(connection, token));
        await ResetAsync(connection, token);
        command.CommandText = "SELECT pg_backend_pid(), value FROM trace_input WHERE value = 2";
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
        {
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(backend, reader.GetInt32(0));
            Assert.AreEqual(2, reader.GetInt32(1));
            Assert.IsFalse(await reader.ReadAsync(token));
        }

        Assert.AreSequenceEqual<long>([1, 1, 1, 2, 1, 0, 2, 1], await CountsAsync(connection, token));
    }

    /// <summary>
    /// Parallel workers restore the registered methods and execute actual partial child scans without losing or duplicating rows.
    /// </summary>
    /// <param name="leaderParticipates">Whether the leader may also execute the shared child scan.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TraceScanRunsInParallelWorkers(bool leaderParticipates)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        string table = "tests.trace_parallel_" + Guid.NewGuid().ToString("N");
        await using var command = new NpgsqlCommand($"""
            CREATE TABLE {table} AS SELECT generate_series(1, 30000) AS value;
            ALTER TABLE {table} SET (parallel_workers = 2);
            ANALYZE {table};
            SET LOCAL max_parallel_workers_per_gather = 2;
            SET LOCAL min_parallel_table_scan_size = 0;
            SET LOCAL parallel_setup_cost = 0;
            SET LOCAL parallel_tuple_cost = 0;
            SET LOCAL parallel_leader_participation = {(leaderParticipates ? "on" : "off")};
            """, connection, transaction);
        await command.ExecuteNonQueryAsync(token);
        string query = $"SELECT datatype.custom_scan_process(value), array_agg(value) FROM {table} GROUP BY 1";
        using (JsonDocument plan = await ExplainAsync(connection, query, true, token))
        {
            JsonElement root = plan.RootElement[0].GetProperty("Plan");
            int launched = WorkersLaunched(root);
            Assert.IsGreaterThan(0, launched);
            JsonElement scan = Assert.ContainsSingle(TraceNodes(root));
            Assert.IsTrue(scan.GetProperty("Parallel Aware").GetBoolean());
            Assert.AreEqual("Seq Scan", scan.GetProperty("Plans")[0].GetProperty("Node Type").GetString());
            Assert.IsTrue(scan.GetProperty("Plans")[0].GetProperty("Parallel Aware").GetBoolean());
            Assert.AreEqual(30000L, scan.GetProperty("Shared Trace Rows").GetInt64());
            Assert.AreEqual(30000L + launched + (leaderParticipates ? 1 : 0), scan.GetProperty("Shared Trace Calls").GetInt64());
            Assert.AreEqual(launched, scan.GetProperty("Trace Worker Attachments").GetInt64());
            Assert.AreEqual(launched, scan.GetProperty("Trace Worker Shutdowns").GetInt64());
            Assert.AreEqual(1L, scan.GetProperty("Trace DSM Generation").GetInt64());
        }

        command.CommandText = query;
        var seen = new HashSet<int>();
        var workers = new HashSet<int>();
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                int worker = reader.GetInt32(0);
                if (!leaderParticipates)
                {
                    Assert.AreNotEqual(connection.ProcessID, worker);
                }

                Assert.IsTrue(workers.Add(worker));
                int[] values = reader.GetFieldValue<int[]>(1);
                Assert.IsNotEmpty(values);
                foreach (int value in values)
                {
                    Assert.IsInRange(1, 30000, value);
                    Assert.IsTrue(seen.Add(value), $"Row {value} was returned more than once.");
                }
            }
        }

        Assert.IsGreaterThan(0, workers.Count);
        Assert.HasCount(30000, seen);
        Assert.AreSequenceEqual(Enumerable.Range(1, 30000), seen.Order());
        await transaction.RollbackAsync(token);
        command.Transaction = null;
        command.CommandText = "SELECT value FROM trace_input WHERE value = 2";
        Assert.AreEqual(2, await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Concurrent updates recheck the replacement tuple through the child instead of reusing a stale custom slot.
    /// </summary>
    /// <param name="replacement">A replacement which either still matches the predicate or no longer qualifies.</param>
    [TestMethod]
    [DataRow(5)]
    [DataRow(20)]
    public async Task TraceScanRechecksConcurrentUpdates(int replacement)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await using NpgsqlConnection blocker = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        string table = "tests.trace_update_" + Guid.NewGuid().ToString("N");
        await using var setup = new NpgsqlCommand($"CREATE TABLE {table}(value integer); INSERT INTO {table} VALUES (1)", blocker);
        await setup.ExecuteNonQueryAsync(token);
        try
        {
            await using NpgsqlTransaction held = await blocker.BeginTransactionAsync(token);
            setup.Transaction = held;
            setup.CommandText = $"UPDATE {table} SET value = @replacement";
            setup.Parameters.AddWithValue("replacement", replacement);
            await setup.ExecuteNonQueryAsync(token);
            await using var command = new NpgsqlCommand($"UPDATE {table} SET value = value + 10 WHERE value < 10 RETURNING value", connection);
            Task<NpgsqlDataReader> pending = command.ExecuteReaderAsync(token);
            bool waited = await WaitForLockAsync(blocker, connection.ProcessID, token);
            await held.CommitAsync(token);
            setup.Transaction = null;
            await using (NpgsqlDataReader reader = await pending)
            {
                Assert.IsTrue(waited, "The traced UPDATE must block on the actual concurrent row before rechecking it.");
                if (replacement == 5)
                {
                    Assert.IsTrue(await reader.ReadAsync(token));
                    Assert.AreEqual(15, reader.GetInt32(0));
                }

                Assert.IsFalse(await reader.ReadAsync(token));
            }

            command.CommandText = $"SELECT value FROM {table}";
            Assert.AreEqual(replacement == 5 ? 15 : 20, await command.ExecuteScalarAsync(token));
            long[] counts = await CountsAsync(connection, token);
            Assert.IsGreaterThan(2L, counts[1], "The traced update, EvalPlanQual and verification query must each allocate a state.");
            Assert.AreEqual(counts[1], counts[7], "Every native query owner must reclaim its trace state.");
            Assert.AreEqual(counts[3], counts[6]);
        }
        finally
        {
            setup.Transaction = null;
            setup.Parameters.Clear();
            setup.CommandText = $"DROP TABLE {table}";
            await setup.ExecuteNonQueryAsync(token);
        }
    }

    /// <summary>
    /// Observes the real PostgreSQL lock wait before allowing the concurrent writer to commit.
    /// </summary>
    private static async Task<bool> WaitForLockAsync(NpgsqlConnection connection, int backend, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("""
            SELECT pg_stat_clear_snapshot();
            SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = @backend AND wait_event_type = 'Lock');
            """, connection);
        command.Parameters.AddWithValue("backend", backend);
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
            {
                Assert.IsTrue(await reader.NextResultAsync(token));
                Assert.IsTrue(await reader.ReadAsync(token));
                if (reader.GetBoolean(0))
                {
                    return true;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), token);
        }

        return false;
    }

    /// <summary>
    /// Sums actual worker launches reported by Gather nodes in the executed plan.
    /// </summary>
    private static int WorkersLaunched(JsonElement node)
    {
        int count = node.TryGetProperty("Workers Launched", out JsonElement workers) ? workers.GetInt32() : 0;
        if (node.TryGetProperty("Plans", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                count += WorkersLaunched(child);
            }
        }

        return count;
    }

    /// <summary>
    /// Opens a backend, initializes the actual sample module and supplies representative values before enabling tracing.
    /// </summary>
    private static async Task<NpgsqlConnection> OpenAsync(CancellationToken token)
    {
        NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT customscan.trace_scan_reset();
                CREATE TEMP TABLE trace_input(value integer, label text);
                INSERT INTO trace_input VALUES (-2147483648, 'minimum'), (1, NULL), (2, 'café'), (2147483647, 'maximum');
                ANALYZE trace_input;
                SET ankus_trace_scan.enabled = on;
                """, connection);
            await command.ExecuteNonQueryAsync(token);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Returns an owned EXPLAIN document after PostgreSQL has completed the statement.
    /// </summary>
    private static async Task<JsonDocument> ExplainAsync(NpgsqlConnection connection, string query, bool analyze, CancellationToken token)
    {
        await using var command = new NpgsqlCommand($"EXPLAIN ({(analyze ? "ANALYZE, " : "")}FORMAT JSON) {query}", connection);
        string json = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
        return JsonDocument.Parse(json);
    }

    /// <summary>
    /// Selects actual trace plan nodes from the recursively reported PostgreSQL plan.
    /// </summary>
    private static IEnumerable<JsonElement> TraceNodes(JsonElement node)
    {
        if (node.TryGetProperty("Custom Plan Provider", out JsonElement provider) && provider.GetString() == "Ankus Trace")
        {
            Assert.AreEqual("Custom Scan", node.GetProperty("Node Type").GetString());
            yield return node;
        }

        if (node.TryGetProperty("Plans", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                foreach (JsonElement nested in TraceNodes(child))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>
    /// Reads completed native lifecycle counters from the same backend that executed the query.
    /// </summary>
    private static async Task<long[]> CountsAsync(NpgsqlConnection connection, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT customscan.trace_scan_counts()", connection);
        return Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Clears observations between independent executions without replacing the native provider.
    /// </summary>
    private static async Task ResetAsync(NpgsqlConnection connection, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT customscan.trace_scan_reset()", connection);
        await command.ExecuteNonQueryAsync(token);
    }
}
