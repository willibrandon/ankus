using System.Text.Json;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies native parameter expressions through partition ancestry, prepared plans and actual backend execution.
/// </summary>
public sealed partial class CustomScanTests
{
    /// <summary>
    /// Partitionwise joins remap provider-owned variables despite different physical column positions at each level.
    /// </summary>
    /// <param name="multipleLevels">Whether both parents contain another partitioned level before their leaf tables.</param>
    /// <param name="indexOnly">Whether the actual inner child uses a covering index without heap fetches.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task TraceScanRemapsPrivateParametersByPartition(bool multipleLevels, bool indexOnly)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await CreateParameterPartitionsAsync(connection, multipleLevels, indexOnly, token);
        using (JsonDocument plan = await ExplainAsync(connection, PartitionParameterQuery, true, token))
        {
            context.WriteLine(plan.RootElement.GetRawText());
            JsonElement[] joins = [.. PlanNodes(plan.RootElement[0].GetProperty("Plan"), "Nested Loop")];
            Assert.HasCount(2, joins);
            foreach (JsonElement join in joins)
            {
                JsonElement outer = Assert.ContainsSingle(TraceNodes(join.GetProperty("Plans")[0]));
                Assert.IsFalse(outer.TryGetProperty("Trace Parameters", out _));
                string alias = outer.GetProperty("Plans")[0].GetProperty("Alias").GetString()!;
                JsonElement inner = Assert.ContainsSingle(TraceNodes(join.GetProperty("Plans")[1]));
                Assert.AreEqual(1, inner.GetProperty("Trace Parameter Remaps").GetInt32());
                string[] parameters = [.. inner.GetProperty("Trace Parameters").EnumerateArray().Select(static value => value.GetString()!)];
                Assert.AreSequenceEqual<string>([alias + ".cutoff", alias + ".key"], parameters.Order(StringComparer.Ordinal));
                JsonElement child = inner.GetProperty("Plans")[0];
                Assert.AreEqual(indexOnly ? "Index Only Scan" : "Index Scan", child.GetProperty("Node Type").GetString());
                Assert.IsGreaterThan(1, child.GetProperty("Actual Loops").GetInt32());
                if (indexOnly)
                {
                    Assert.AreEqual(0, child.GetProperty("Heap Fetches").GetInt32());
                }
            }
        }

        Assert.AreSequenceEqual(s_partitionParameterRows, await ReadParameterRowsAsync(connection, PartitionParameterQuery, token));
        long[] counts = await CountsAsync(connection, token);
        Assert.IsGreaterThan(0L, counts[5]);
        Assert.AreEqual(counts[1], counts[4]);
        Assert.AreEqual(counts[1], counts[7]);
        Assert.AreEqual(counts[3], counts[6]);
    }

    /// <summary>
    /// Copied parameter expressions survive cached-plan ownership, changed bounds and repeated execution.
    /// </summary>
    /// <param name="multipleLevels">Whether parameters cross an intermediate partitioned parent.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TraceScanPreparedParametersSurvivePlanningMemory(bool multipleLevels)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await CreateParameterPartitionsAsync(connection, multipleLevels, true, token);
        string query = PartitionParameterQuery.Replace("ORDER BY", "WHERE o.key >= $1 ORDER BY", StringComparison.Ordinal);
        await using var command = new NpgsqlCommand("SET plan_cache_mode = force_generic_plan; PREPARE traced_parameters(int) AS " + query, connection);
        await command.ExecuteNonQueryAsync(token);
        Assert.AreSequenceEqual(s_partitionParameterRows, await ReadParameterRowsAsync(connection, "EXECUTE traced_parameters(0)", token));
        long planned = (await CountsAsync(connection, token))[0];
        Assert.IsGreaterThan(0L, planned);
        command.CommandText = "SELECT datatype.custom_scan_collect()";
        await command.ExecuteNonQueryAsync(token);
        Assert.AreSequenceEqual(s_partitionParameterRows[4..], await ReadParameterRowsAsync(connection, "EXECUTE traced_parameters(12)", token));
        Assert.IsEmpty(await ReadParameterRowsAsync(connection, "EXECUTE traced_parameters(20)", token));
        Assert.AreSequenceEqual(s_partitionParameterRows, await ReadParameterRowsAsync(connection, "EXECUTE traced_parameters(0)", token));
        using (JsonDocument plan = await ExplainAsync(connection, "EXECUTE traced_parameters(0)", true, token))
        {
            JsonElement[] parameters = [.. TraceNodes(plan.RootElement[0].GetProperty("Plan"))
                .Where(static node => node.TryGetProperty("Trace Parameters", out _))];
            Assert.HasCount(2, parameters);
            foreach (JsonElement scan in parameters)
            {
                Assert.AreEqual(1, scan.GetProperty("Trace Parameter Remaps").GetInt32());
                Assert.AreEqual(2, scan.GetProperty("Trace Parameters").GetArrayLength());
            }
        }

        long[] counts = await CountsAsync(connection, token);
        Assert.AreEqual(planned, counts[0], "Every execution and EXPLAIN must use the original generic plan.");
        Assert.AreEqual(counts[1], counts[4]);
        Assert.AreEqual(counts[1], counts[7]);
        Assert.AreEqual(counts[3], counts[6]);
        command.CommandText = "DEALLOCATE traced_parameters";
        await command.ExecuteNonQueryAsync(token);
    }

    /// <summary>
    /// Diagnostics neither execute volatile clauses during planning nor add evaluations during native execution.
    /// </summary>
    [TestMethod]
    public async Task TraceScanParameterDiagnosticsPreserveVolatileEvaluation()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await CreateParameterPartitionsAsync(connection, true, false, token);
        string query = PartitionParameterQuery.Replace("i.value >= o.cutoff", "i.value >= o.cutoff + nextval('trace_calls') * 0", StringComparison.Ordinal);
        await using var command = new NpgsqlCommand("CREATE TEMP SEQUENCE trace_calls; SET ankus_trace_scan.enabled = off", connection);
        await command.ExecuteNonQueryAsync(token);
        Assert.AreSequenceEqual(s_partitionParameterRows, await ReadParameterRowsAsync(connection, query, token));
        command.CommandText = "SELECT last_value FROM trace_calls";
        long nativeCalls = Assert.IsInstanceOfType<long>(await command.ExecuteScalarAsync(token));
        Assert.IsGreaterThan(0L, nativeCalls);
        command.CommandText = "SELECT setval('trace_calls', 1, false); SET ankus_trace_scan.enabled = on";
        await command.ExecuteNonQueryAsync(token);
        using (JsonDocument plan = await ExplainAsync(connection, query, false, token))
        {
            JsonElement[] parameters = [.. TraceNodes(plan.RootElement[0].GetProperty("Plan"))
                .Where(static node => node.TryGetProperty("Trace Parameters", out _))];
            Assert.HasCount(2, parameters);
            foreach (JsonElement scan in parameters)
            {
                Assert.AreEqual(1, scan.GetProperty("Trace Parameter Remaps").GetInt32());
            }
        }

        command.CommandText = "SELECT is_called FROM trace_calls";
        Assert.IsFalse(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)), "Planning and deparsing must not execute the volatile clause.");
        Assert.AreSequenceEqual(s_partitionParameterRows, await ReadParameterRowsAsync(connection, query, token));
        command.CommandText = "SELECT last_value FROM trace_calls";
        Assert.AreEqual(nativeCalls, Assert.IsInstanceOfType<long>(await command.ExecuteScalarAsync(token)));
    }

    /// <summary>
    /// Failing partitioned children release parameterized states and preserve the backend for an exact healthy retry.
    /// </summary>
    /// <param name="managed">Whether the error crosses a managed child function before returning to PostgreSQL.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TraceScanPartitionParameterErrorsRecover(bool managed)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await CreateParameterPartitionsAsync(connection, true, false, token);
        int backend = connection.ProcessID;
        string predicate = managed ? "datatype.custom_scan_accept(i.value - 118)" : "100 / (i.value - 120) IS NOT NULL";
        string query = PartitionParameterQuery.Replace("ORDER BY", "AND " + predicate + " ORDER BY", StringComparison.Ordinal);
        using (JsonDocument plan = await ExplainAsync(connection, query, false, token))
        {
            JsonElement[] parameters = [.. TraceNodes(plan.RootElement[0].GetProperty("Plan"))
                .Where(static node => node.TryGetProperty("Trace Parameters", out _))];
            Assert.HasCount(2, parameters);
            foreach (JsonElement scan in parameters)
            {
                Assert.AreEqual(1, scan.GetProperty("Trace Parameter Remaps").GetInt32());
                Assert.IsTrue(scan.GetProperty("Plans")[0].TryGetProperty("Filter", out _));
            }
        }

        await ResetAsync(connection, token);
        await using var command = new NpgsqlCommand(query, connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        Assert.AreEqual(managed ? "P7521" : "22012", error.SqlState);
        Assert.AreEqual(managed ? "managed custom scan child failure" : "division by zero", error.MessageText);
        Assert.AreEqual(managed ? "trace child detail" : null, error.Detail);
        Assert.AreEqual(managed ? "trace child hint" : null, error.Hint);
        long[] failed = await CountsAsync(connection, token);
        Assert.IsGreaterThan(0L, failed[1]);
        Assert.AreEqual(failed[1], failed[7]);
        Assert.AreEqual(failed[3], failed[6]);
        Assert.IsGreaterThan(failed[4], failed[1], "Error cleanup must reclaim states whose ordinary End callback did not run.");
        command.CommandText = "SELECT pg_backend_pid()";
        Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
        Assert.AreSequenceEqual(s_partitionParameterRows, await ReadParameterRowsAsync(connection, PartitionParameterQuery, token));
        long[] recovered = await CountsAsync(connection, token);
        Assert.AreEqual(recovered[1], recovered[7]);
        Assert.AreEqual(recovered[3], recovered[6]);
    }

    /// <summary>
    /// Joins two partition trees with independent nullable outer bounds and projected text.
    /// </summary>
    private const string PartitionParameterQuery = """
        SELECT o.key, o.cutoff, o.label, i.value, i.label
        FROM trace_outer o LEFT JOIN trace_inner i ON i.key = o.key AND i.value >= o.cutoff
        ORDER BY o.key,o.cutoff,i.value
        """;

    /// <summary>
    /// Pins every duplicate match, unmatched outer row, nullable cutoff and UTF8 projection independently of EXPLAIN.
    /// </summary>
    private static readonly (int Key, int? Cutoff, string? Outer, int? Value, string? Inner)[] s_partitionParameterRows =
    [
        (1, 10, "first", 10, "low"), (1, 10, "first", 11, null), (1, 11, null, 11, null),
        (2, 21, "empty", null, null), (12, 119, "café", 120, "twelve"),
        (13, null, "null cutoff", null, null), (14, 0, "missing", null, null),
    ];

    /// <summary>
    /// Creates real partition ancestry with reordered and dropped columns, then selects parameterized native index paths.
    /// </summary>
    private static async Task CreateParameterPartitionsAsync(NpgsqlConnection connection, bool multipleLevels, bool indexOnly,
        CancellationToken token)
    {
        await using var command = new NpgsqlCommand("""
            CREATE TEMP TABLE trace_outer(key integer, cutoff integer, label text) PARTITION BY RANGE(key);
            CREATE TEMP TABLE trace_inner(key integer, value integer, label text) PARTITION BY RANGE(key);
            """, connection);
        await command.ExecuteNonQueryAsync(token);
        if (multipleLevels)
        {
            command.CommandText = """
                CREATE TEMP TABLE trace_outer_mid(label text, key integer, cutoff integer) PARTITION BY RANGE(key);
                CREATE TEMP TABLE trace_inner_mid(value integer, label text, key integer) PARTITION BY RANGE(key);
                ALTER TABLE trace_outer ATTACH PARTITION trace_outer_mid FOR VALUES FROM (0) TO (20);
                ALTER TABLE trace_inner ATTACH PARTITION trace_inner_mid FOR VALUES FROM (0) TO (20);
                """;
            await command.ExecuteNonQueryAsync(token);
        }

        string outer = multipleLevels ? "trace_outer_mid" : "trace_outer";
        string inner = multipleLevels ? "trace_inner_mid" : "trace_inner";
        command.CommandText = $"""
            CREATE TEMP TABLE trace_outer_low(cutoff integer, discarded text, label text, key integer);
            ALTER TABLE trace_outer_low DROP COLUMN discarded;
            CREATE TEMP TABLE trace_outer_high(label text, key integer, cutoff integer);
            CREATE TEMP TABLE trace_inner_low(label text, value integer, key integer);
            CREATE TEMP TABLE trace_inner_high(value integer, key integer, label text);
            ALTER TABLE {outer} ATTACH PARTITION trace_outer_low FOR VALUES FROM (0) TO (10);
            ALTER TABLE {outer} ATTACH PARTITION trace_outer_high FOR VALUES FROM (10) TO (20);
            ALTER TABLE {inner} ATTACH PARTITION trace_inner_low FOR VALUES FROM (0) TO (10);
            ALTER TABLE {inner} ATTACH PARTITION trace_inner_high FOR VALUES FROM (10) TO (20);
            CREATE INDEX ON trace_inner(key,value) INCLUDE(label);
            INSERT INTO trace_outer VALUES (1,10,'first'),(1,11,NULL),(2,21,'empty'),
                (12,119,'café'),(13,NULL,'null cutoff'),(14,0,'missing');
            INSERT INTO trace_inner VALUES (1,10,'low'),(1,11,NULL),(2,20,'two'),(12,120,'twelve'),(13,130,'ignored');
            ANALYZE trace_outer; ANALYZE trace_inner;
            SET enable_partitionwise_join = on;
            SET enable_hashjoin = off; SET enable_mergejoin = off;
            SET enable_material = off; SET enable_memoize = off;
            SET max_parallel_workers_per_gather = 0;
            """;
        await command.ExecuteNonQueryAsync(token);
        foreach (string table in new[] { "trace_inner_low", "trace_inner_high" })
        {
            command.CommandText = "VACUUM (ANALYZE) " + table;
            await command.ExecuteNonQueryAsync(token);
        }

        await SelectIndexPathsAsync(connection, indexOnly, token);
    }

    /// <summary>
    /// Reads exact typed and nullable outer/inner projections from the original backend.
    /// </summary>
    private static async Task<(int Key, int? Cutoff, string? Outer, int? Value, string? Inner)[]> ReadParameterRowsAsync(
        NpgsqlConnection connection, string query, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(query, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        var rows = new List<(int, int?, string?, int?, string?)>();
        while (await reader.ReadAsync(token))
        {
            rows.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return [.. rows];
    }
}
