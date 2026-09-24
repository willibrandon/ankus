using System.Globalization;
using System.Text.Json;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies native configuration serialization through launched parallel workers and their error boundaries.
/// </summary>
/// <param name="context">The test cancellation and diagnostic context.</param>
[TestClass]
[DoNotParallelize]
public sealed class GucParallelTests(TestContext context)
{
    /// <summary>
    /// Backend-loaded libraries rebuild all five typed settings and their extras in foreign managed runtimes.
    /// </summary>
    /// <param name="textState">Whether text has its default, an explicit value, or a hook-produced null.</param>
    /// <param name="text">The text expected in both processes except for native nondefault-null serialization.</param>
    /// <param name="mode">The alias, canonical, or hidden enum label supplied by the leader.</param>
    /// <param name="modeValue">The full unsigned value independent of the native ordinal.</param>
    [TestMethod]
    [DataRow("default", null, "rest", "18446744073709551615")]
    [DataRow("value", "", "fast", "9223372036854775808")]
    [DataRow("value", "café 🐘", "secret", "10")]
    [DataRow("normalized-null", null, "rest", "18446744073709551615")]
    public Task BackendLoadedWorkersRestoreTypedValuesAndRegenerateExtras(string textState, string? text, string mode, string modeValue)
        => RunParallel(nameof(BackendLoadedWorkersRestoreTypedValuesAndRegenerateExtras),
            async (connection, transaction, token) =>
            {
                await PrepareInputAsync(connection, transaction, token);
                string local = transaction is null ? string.Empty : "LOCAL ";
                await ExecuteAsync(connection, transaction,
                    $"LOAD 'Ankus.TestExtension'; SET {local}ankus_parallel.boolean = off; " +
                    $"SET {local}ankus_parallel.integer = -41; SET {local}ankus_parallel.real = '-0.125'", token);
                await SetAsync(connection, transaction, "ankus_parallel.mode", mode, token);
                if (textState != "default")
                {
                    await SetAsync(connection, transaction, "ankus_parallel.text", textState == "normalized-null" ? "make-null" : text!, token);
                }

                string?[] leader = await ScalarAsync<string?[]>(connection, transaction, "SELECT datatype.guc_parallel_snapshot(0)", token);
                Assert.AreEqual(text, leader[6]);
                Assert.AreEqual(modeValue, leader[7]);
                Assert.AreEqual(connection.ProcessID.ToString(CultureInfo.InvariantCulture), leader[1]);
                Assert.AreEqual(leader[1], leader[2]);
                string expression = "datatype.guc_parallel_snapshot(value % 2)";
                await AssertWorkerPlanAsync(connection, transaction, expression, token);
                IReadOnlyList<(string?[] Snapshot, long Rows)> groups = await ReadGroupsAsync(connection, transaction, expression, token);
                AssertWorkerRows(connection.ProcessID, groups, processIndex: 1);
                foreach ((string?[] snapshot, _) in groups)
                {
                    Assert.HasCount(14, snapshot);
                    string process = snapshot[1]!;
                    Assert.AreEqual(process, snapshot[2], "Managed static state must originate in this worker.");
                    Assert.AreEqual("False", snapshot[3]);
                    Assert.AreEqual("-41", snapshot[4]);
                    string realBits = BitConverter.DoubleToInt64Bits(-0.125).ToString(CultureInfo.InvariantCulture);
                    Assert.AreEqual(realBits, snapshot[5]);
                    string? workerText = textState == "normalized-null" ? "" : text;
                    Assert.AreEqual(workerText, snapshot[6]);
                    Assert.AreEqual(modeValue, snapshot[7]);
                    Assert.AreEqual($"{process}|Session|False", snapshot[8]);
                    Assert.AreEqual($"{process}|Session|-41", snapshot[9]);
                    Assert.AreEqual($"{process}|Session|{realBits}", snapshot[10]);
                    Assert.AreEqual($"{process}|{(textState == "default" ? "Default" : "Session")}|{workerText ?? "<null>"}", snapshot[11]);
                    Assert.AreEqual($"{process}|Session|{modeValue}", snapshot[12]);
                    Assert.AreEqual("1|1|default|42", snapshot[13], "Library initialization runs once before worker state restoration.");
                }

                Assert.AreSequenceEqual(leader, await ScalarAsync<string?[]>(connection, transaction,
                    "SELECT datatype.guc_parallel_snapshot(0)", token));
            });

    /// <summary>
    /// Both session and local mutations fail in actual workers without changing the leader's state.
    /// </summary>
    /// <param name="local">Whether set_config requests transaction-local mutation.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task WorkerSetRejectsAndLeaderRecovers(bool local)
        => RunParallel(nameof(WorkerSetRejectsAndLeaderRecovers),
            async (connection, transaction, token) =>
            {
                await PrepareInputAsync(connection, transaction, token);
                string localKeyword = transaction is null ? string.Empty : "LOCAL ";
                await ExecuteAsync(connection, transaction, $"LOAD 'Ankus.TestExtension'; SET {localKeyword}ankus_parallel.integer = 37", token);
                await AssertWorkerPlanAsync(connection, transaction, "datatype.guc_parallel_snapshot(value % 2)", token);
                if (transaction is not null)
                {
                    await transaction.SaveAsync("worker_set", token);
                }

                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => ExecuteAsync(connection, transaction,
                    $"SELECT sum(datatype.guc_parallel_set(value, {(local ? "true" : "false")})) FROM guc_parallel_input", token));
                Assert.AreEqual("25000", error.SqlState);
                Assert.AreEqual("parameter \"ankus_parallel.integer\" cannot be set during a parallel operation", error.MessageText);
                Assert.AreEqual("guc.c", error.File);
                Assert.Contains("parallel worker", error.Where ?? string.Empty);
                if (transaction is not null)
                {
                    await transaction.RollbackAsync("worker_set", token);
                }

                Assert.AreEqual("37", await ScalarAsync<string>(connection, transaction, "SHOW ankus_parallel.integer", token));
                IReadOnlyList<(string?[] Snapshot, long Rows)> recovered = await ReadGroupsAsync(connection, transaction,
                    "datatype.guc_parallel_snapshot(value % 2)", token);
                AssertWorkerRows(connection.ProcessID, recovered, processIndex: 1);
                foreach ((string?[] snapshot, _) in recovered)
                {
                    Assert.AreEqual("37", snapshot[4]);
                    Assert.AreEqual($"{snapshot[1]}|Session|37", snapshot[9]);
                }

                Assert.AreEqual(42, await ScalarAsync<int>(connection, transaction, "SELECT 42", token));
            });

    /// <summary>
    /// Function-local SAVE settings are visible only in that function and restore accepted extra in the same worker.
    /// </summary>
    [TestMethod]
    public Task WorkerFunctionSettingsRestoreValueAndExtra()
        => RunParallel(nameof(WorkerFunctionSettingsRestoreValueAndExtra),
            async (connection, transaction, token) =>
            {
                await PrepareInputAsync(connection, transaction, token);
                string local = transaction is null ? string.Empty : "LOCAL ";
                await ExecuteAsync(connection, transaction, $$"""
                    LOAD 'Ankus.TestExtension';
                    SET {{local}}ankus_parallel.integer = 37;
                    CREATE FUNCTION datatype.guc_parallel_scoped(integer) RETURNS integer
                        LANGUAGE SQL STABLE PARALLEL SAFE SET ankus_parallel.integer = '73'
                        AS 'SELECT datatype.guc_parallel_integer($1)';
                    """, token);
                string expression = "datatype.guc_parallel_scope(value % 2)";
                await AssertWorkerPlanAsync(connection, transaction, expression, token);
                IReadOnlyList<(string?[] Snapshot, long Rows)> groups = await ReadGroupsAsync(connection, transaction, expression, token);
                AssertWorkerRows(connection.ProcessID, groups, processIndex: 0);
                foreach ((string?[] snapshot, _) in groups)
                {
                    Assert.HasCount(6, snapshot);
                    Assert.AreSequenceEqual<string?>(["37", "73", "37"], snapshot.Skip(1).Take(3));
                    Assert.AreEqual($"{snapshot[0]}|Session|37", snapshot[4]);
                    Assert.AreEqual(snapshot[4], snapshot[5]);
                }

                Assert.AreEqual("37", await ScalarAsync<string>(connection, transaction, "SHOW ankus_parallel.integer", token));
            });

    /// <summary>
    /// A check rejected during worker startup preserves the leader and can be corrected before launching more workers.
    /// </summary>
    [TestMethod]
    public Task WorkerRestoreFailurePreservesLeaderAndRecovers()
        => RunParallel(nameof(WorkerRestoreFailurePreservesLeaderAndRecovers),
            async (connection, transaction, token) =>
            {
                await PrepareInputAsync(connection, transaction, token);
                await ExecuteAsync(connection, transaction, "LOAD 'Ankus.TestExtension'", token);
                string expression = "datatype.guc_parallel_snapshot(value % 2)";
                await AssertWorkerPlanAsync(connection, transaction, expression, token);
                await SetAsync(connection, transaction, "ankus_parallel.owner", connection.ProcessID.ToString(CultureInfo.InvariantCulture), token);
                if (transaction is not null)
                {
                    await transaction.SaveAsync("worker_restore", token);
                }

                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => ReadGroupsAsync(connection, transaction, expression, token));
                Assert.AreEqual("P7821", error.SqlState);
                Assert.AreEqual("Configuration belongs to another backend.", error.MessageText);
                Assert.StartsWith($"source=Session;owner={connection.ProcessID};worker=", error.Detail ?? string.Empty);
                int worker = int.Parse(error.Detail!.Split("worker=", StringSplitOptions.None)[1], CultureInfo.InvariantCulture);
                Assert.AreNotEqual(connection.ProcessID, worker);
                Assert.Contains("ankus_parallel.owner", error.Where ?? string.Empty);
                Assert.Contains("parallel worker", error.Where ?? string.Empty);
                if (transaction is not null)
                {
                    await transaction.RollbackAsync("worker_restore", token);
                }

                Assert.AreEqual(connection.ProcessID.ToString(CultureInfo.InvariantCulture),
                    await ScalarAsync<string>(connection, transaction, "SHOW ankus_parallel.owner", token));
                string local = transaction is null ? string.Empty : "LOCAL ";
                await ExecuteAsync(connection, transaction, $"SET {local}ankus_parallel.owner = 0", token);
                IReadOnlyList<(string?[] Snapshot, long Rows)> recovered = await ReadGroupsAsync(connection, transaction, expression, token);
                AssertWorkerRows(connection.ProcessID, recovered, processIndex: 1);
                Assert.AreEqual(42, await ScalarAsync<int>(connection, transaction, "SELECT 42", token));
            });

    /// <summary>
    /// Native preloaded declarations propagate all types while managed runtime state starts separately in each worker.
    /// </summary>
    /// <param name="text">A default null or explicit Unicode string.</param>
    [TestMethod]
    [DataRow((string?)null)]
    [DataRow("preloaded café 🐘")]
    public async Task NativePreloadWorkersRestoreValuesWithoutManagedPostmasterState(string? text)
    {
        PostgresTestClusterOptions defaults = await IntegrationEnvironment.CreateOptionsAsync(context.CancellationToken);
        var options = new PostgresTestClusterOptions
        {
            Installation = defaults.Installation,
            DataDirectoryBase = defaults.DataDirectoryBase,
            LogDirectory = defaults.LogDirectory,
            PostgreSqlConfiguration =
            [
                .. defaults.PostgreSqlConfiguration,
                "shared_preload_libraries = 'Ankus.Examples.Configuration'",
                "ankus_configuration.startup = 11",
            ],
        };
        await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(options, context.CancellationToken);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(context.CancellationToken);
        await ExecuteAsync(connection, null, "CREATE EXTENSION ankus_configuration", context.CancellationToken);
        await using NpgsqlTransaction? transaction = RequiresAutocommitParallelWorkers()
            ? null
            : await connection.BeginTransactionAsync(context.CancellationToken);
        await PrepareInputAsync(connection, transaction, context.CancellationToken);
        string local = transaction is null ? string.Empty : "LOCAL ";
        await ExecuteAsync(connection, transaction, $$"""
            SET {{local}}ankus_configuration.enabled = off;
            SET {{local}}"ankus_configuration.user" = 72;
            SET {{local}}ankus_configuration.real_seconds = '-0.125';
            SET {{local}}ankus_configuration.mode = 'turbo';
            """, context.CancellationToken);
        if (text is not null)
        {
            await SetAsync(connection, transaction, "ankus_configuration.text", text, context.CancellationToken);
        }

        string?[] leader = await ScalarAsync<string?[]>(connection, transaction, "SELECT configuration_parallel_values(0)", context.CancellationToken);
        Assert.AreEqual(connection.ProcessID.ToString(CultureInfo.InvariantCulture), leader[1]);
        Assert.AreEqual(leader[1], leader[2]);
        string expression = "configuration_parallel_values(value % 2)";
        await AssertWorkerPlanAsync(connection, transaction, expression, context.CancellationToken);
        IReadOnlyList<(string?[] Snapshot, long Rows)> groups = await ReadGroupsAsync(connection, transaction, expression, context.CancellationToken);
        AssertWorkerRows(connection.ProcessID, groups, processIndex: 1);
        foreach ((string?[] snapshot, _) in groups)
        {
            Assert.HasCount(9, snapshot);
            Assert.AreEqual(snapshot[1], snapshot[2]);
            Assert.AreEqual("False", snapshot[3]);
            Assert.AreEqual("72", snapshot[4]);
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(-0.125).ToString(CultureInfo.InvariantCulture), snapshot[5]);
            Assert.AreEqual(text, snapshot[6]);
            Assert.AreEqual("18446744073709551615", snapshot[7]);
            Assert.AreEqual("11", snapshot[8], "Postmaster-only values are inherited rather than serialized as session settings.");
        }

        Assert.AreSequenceEqual(leader, await ScalarAsync<string?[]>(connection, transaction,
            "SELECT configuration_parallel_values(0)", context.CancellationToken));
    }

    private async Task RunParallel(
        string name,
        Func<NpgsqlConnection, NpgsqlTransaction?, CancellationToken, Task> action)
    {
        if (!RequiresAutocommitParallelWorkers())
        {
            await PostgresFixture.Cluster.RunInTransactionAsync(name,
                (connection, transaction, token) => action(connection, transaction, token),
                context.CancellationToken);
            return;
        }

        try
        {
            await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
            await action(connection, null, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw new PostgresTestException(name, PostgresFixture.Cluster.ReadServerLog(), error);
        }
    }

    private static bool RequiresAutocommitParallelWorkers()
        => OperatingSystem.IsWindows() && PostgresFixture.Cluster.Installation.Version.Major < 18;

    private static Task<int> PrepareInputAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken token)
    {
        string local = transaction is null ? string.Empty : "LOCAL ";
        return ExecuteAsync(connection, transaction, $$"""
            DROP TABLE IF EXISTS guc_parallel_input;
            CREATE TABLE guc_parallel_input AS SELECT generate_series(1,30000) AS value;
            ALTER TABLE guc_parallel_input SET (parallel_workers=2);
            ANALYZE guc_parallel_input;
            SET {{local}}max_parallel_workers_per_gather=2;
            SET {{local}}min_parallel_table_scan_size=0;
            SET {{local}}parallel_setup_cost=0;
            SET {{local}}parallel_tuple_cost=0;
            SET {{local}}parallel_leader_participation=off;
            """, token);
    }

    private static async Task AssertWorkerPlanAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string expression, CancellationToken token)
    {
        string plan = await ScalarAsync<string>(connection, transaction,
            $"EXPLAIN (ANALYZE, FORMAT JSON) SELECT {expression} FROM guc_parallel_input", token);
        using JsonDocument document = JsonDocument.Parse(plan);
        Assert.IsGreaterThan(0, WorkersLaunched(document.RootElement[0].GetProperty("Plan")), "A native execution plan must launch workers.");
    }

    private static int WorkersLaunched(JsonElement plan)
    {
        int count = plan.TryGetProperty("Workers Launched", out JsonElement workers) ? workers.GetInt32() : 0;
        if (plan.TryGetProperty("Plans", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                count += WorkersLaunched(child);
            }
        }

        return count;
    }

    private static void AssertWorkerRows(int leader, IReadOnlyList<(string?[] Snapshot, long Rows)> groups, int processIndex)
    {
        Assert.IsNotEmpty(groups);
        Assert.AreEqual(30000L, groups.Sum(static group => group.Rows));
        foreach ((string?[] snapshot, long rows) in groups)
        {
            Assert.IsGreaterThan(0L, rows);
            int process = int.Parse(snapshot[processIndex]!, CultureInfo.InvariantCulture);
            Assert.IsGreaterThan(0, process);
            Assert.AreNotEqual(leader, process, "All rows must be evaluated by foreign workers with leader participation disabled.");
        }
    }

    private static async Task<IReadOnlyList<(string?[] Snapshot, long Rows)>> ReadGroupsAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, string expression, CancellationToken token)
    {
        await using var command = new NpgsqlCommand($"SELECT {expression}, count(*) FROM guc_parallel_input GROUP BY 1", connection, transaction);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        var groups = new List<(string?[] Snapshot, long Rows)>();
        while (await reader.ReadAsync(token))
        {
            groups.Add((reader.GetFieldValue<string?[]>(0), reader.GetInt64(1)));
        }

        return groups;
    }

    private static async Task SetAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string name, string value, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT set_config($1, $2, $3)", connection, transaction);
        command.Parameters.AddWithValue(name);
        command.Parameters.AddWithValue(value);
        command.Parameters.AddWithValue(transaction is not null);
        await command.ExecuteScalarAsync(token);
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }
}
