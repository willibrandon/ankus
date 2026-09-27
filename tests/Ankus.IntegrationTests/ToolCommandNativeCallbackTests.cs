using System.Globalization;
using System.Text.Json;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class ToolCommandTests
{
    /// <summary>
    /// Publishes local SQL and initialization entry points whose native callbacks live exclusively in a referenced provider.
    /// </summary>
    private const string NativeCallbackExportSource = """
        using Ankus;
        public static partial class CallbackExports
        {
            [PgGucBool("ankus_tool_probe.fail_module_load", false, "Fail native registration")]
            public static partial bool FailModuleLoad { get; }

            [PgGucBool("ankus_tool_probe.fail_ready", false, "Fail ready initialization")]
            public static partial bool FailReady { get; }

            [PgModuleLoad]
            public static void Register() => BindingCallbacks.Initialize(FailModuleLoad);

            [PgInitialize]
            public static void Ready() => BindingCallbacks.Ready(FailReady);

            [PgFunction(ParallelSafety = PgParallelSafety.Safe, Volatility = PgVolatility.Stable)]
            public static string[] NativeCallbackLifecycle(int input) => BindingCallbacks.NativeCallbackLifecycle(input);

            [PgFunction]
            public static string NativeCallbackPhaseCounts() => BindingCallbacks.NativeCallbackPhaseCounts();

            [PgFunction]
            public static string NativeManagedCallbackValues() => BindingCallbacks.NativeManagedCallbackValues();

            [PgFunction]
            public static bool NativeHookInstall() => BindingHookCallbacks.NativeHookInstall();

            [PgFunction]
            public static string NativeHookOrder() => BindingHookCallbacks.NativeHookOrder();

            [PgFunction]
            public static void NativeHookRejectOnce() => BindingHookCallbacks.NativeHookRejectOnce();

            [PgFunction]
            public static int NativeHookCount() => BindingHookCallbacks.NativeHookCount();

            [PgFunction]
            public static int NativeHookRestore() => BindingHookCallbacks.NativeHookRestore();
        }
        """;

    /// <summary>
    /// Exercises postmaster registration, independent backend state and worker attachment using the same published library.
    /// </summary>
    private static async Task AssertNativeCallbackPreloadAsync(string published, CancellationToken token)
    {
        await using PostgresTestCluster cluster = await StartPublishedClusterAsync(published, token, sharedPreload: true);
        Assert.Contains("Ankus native callback initialized: FFDCBA9876543210", cluster.ReadServerLog());
        string? inheritedInitializer = null;
        HashSet<int> backends = [];
        for (int index = 0; index < 2; index++)
        {
            await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
            Assert.IsTrue(backends.Add(connection.ProcessID));
            await using var command = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS ankus_tool_probe", connection);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT native_callback_lifecycle(0)";
            string[] snapshot = Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(token));
            if (!OperatingSystem.IsWindows())
            {
                inheritedInitializer ??= snapshot[1];
                Assert.AreNotEqual(connection.ProcessID.ToString(CultureInfo.InvariantCulture), inheritedInitializer);
            }

            AssertNativeCallbackSnapshot(snapshot, connection.ProcessID, inheritedInitializer, "unavailable", "unavailable");
            await AssertNativeCallbackWorkersAsync(connection, inheritedInitializer, token, preloaded: true);
            await AssertNativeHookChainingAsync(command, token);
        }
    }

    /// <summary>
    /// Requires real parallel workers to invoke callbacks after initialization and retain their installed executor hook.
    /// </summary>
    private static async Task AssertNativeCallbackWorkersAsync(NpgsqlConnection connection, string? inheritedInitializer,
        CancellationToken token, bool preloaded = false)
    {
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await using var command = new NpgsqlCommand("""
            CREATE TABLE native_callback_input AS SELECT generate_series(1,30000) AS value;
            ALTER TABLE native_callback_input SET (parallel_workers=2);
            ANALYZE native_callback_input;
            SET LOCAL max_parallel_workers_per_gather=2;
            SET LOCAL min_parallel_table_scan_size=0;
            SET LOCAL parallel_setup_cost=0;
            SET LOCAL parallel_tuple_cost=0;
            SET LOCAL parallel_leader_participation=off;
            """, connection, transaction);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT native_callback_lifecycle(0)";
        string[] leader = Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(token));
        AssertNativeCallbackSnapshot(leader, connection.ProcessID, inheritedInitializer,
            preloaded ? "unavailable" : "42", preloaded ? "unavailable" : "42");
        const string Query = "SELECT native_callback_lifecycle(value % 2), count(*) FROM native_callback_input GROUP BY 1";
        command.CommandText = "EXPLAIN (ANALYZE, FORMAT JSON) " + Query;
        string plan = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
        using JsonDocument document = JsonDocument.Parse(plan);
        Assert.IsGreaterThan(0, NativeCallbackWorkersLaunched(document.RootElement[0].GetProperty("Plan")));
        command.CommandText = Query;
        long rows = 0;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                string[] snapshot = reader.GetFieldValue<string[]>(0);
                int process = int.Parse(snapshot[0], CultureInfo.InvariantCulture);
                Assert.AreNotEqual(connection.ProcessID, process, "Every value must be evaluated in an actual worker.");
                AssertNativeCallbackSnapshot(snapshot, process, inheritedInitializer,
                    preloaded ? "unavailable" : "42",
                    preloaded || OperatingSystem.IsWindows() && s_installation.Version.Major < 18 ? "unavailable" : "42");
                long count = reader.GetInt64(1);
                Assert.IsGreaterThan(0L, count);
                rows += count;
            }
        }

        Assert.AreEqual(30000L, rows);
        command.CommandText = "SELECT native_callback_lifecycle(0)";
        Assert.AreSequenceEqual(leader, Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(token)));
        await transaction.RollbackAsync(token);
    }

    /// <summary>
    /// Checks exact callback values, process ownership, single initialization and preserved native registration.
    /// </summary>
    private static void AssertNativeCallbackSnapshot(string[] snapshot, int process, string? inheritedInitializer, string readySql, string loadSql)
    {
        Assert.HasCount(13, snapshot);
        Assert.IsGreaterThan(0, process);
        string processText = process.ToString(CultureInfo.InvariantCulture);
        Assert.AreEqual(processText, snapshot[0]);
        Assert.AreEqual(inheritedInitializer ?? processText, snapshot[1]);
        Assert.AreEqual("1", snapshot[2]);
        Assert.AreEqual("FFDCBA9876543210", snapshot[3]);
        Assert.Contains(snapshot[6], ["0", "1"]);
        Assert.AreEqual(snapshot[6] == "0" ? "0100000000000000" : "0100000000000001", snapshot[4]);
        Assert.AreEqual("True", snapshot[5]);
        Assert.AreEqual("1,2,3,4", snapshot[7], "The worker's executor hook must run before evaluating its first callback row.");
        Assert.AreEqual(inheritedInitializer ?? processText, snapshot[8]);
        Assert.AreEqual("1", snapshot[9]);
        Assert.AreEqual(readySql, snapshot[10], "Deferred initialization must receive restored transaction state.");
        Assert.AreEqual(loadSql, snapshot[11], "Module registration must not use SQL before worker restoration.");
        Assert.AreEqual(loadSql, snapshot[12], "Nested native callbacks must retain the registration phase's SQL capability.");
    }

    /// <summary>
    /// Failed registration retries independently of the later phase and releases each managed frame before PostgreSQL reports errors.
    /// </summary>
    private static async Task AssertNativeModuleLoadRetryAsync(PostgresTestCluster cluster, CancellationToken token)
    {
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand("SET ankus_tool_probe.fail_module_load = true; SET ankus_tool_probe.fail_ready = true", connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "LOAD 'BindingConsumer'";
        PostgresException load = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        Assert.AreEqual("22023", load.SqlState);
        Assert.AreEqual("module registration failure", load.MessageText);
        Assert.AreEqual("owned registration detail", load.Detail);
        Assert.AreEqual("retry registration", load.Hint);
        command.CommandText = "SELECT 6 * 7";
        Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        command.CommandText = "SET ankus_tool_probe.fail_module_load = false; LOAD 'BindingConsumer'";
        PostgresException ready = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        Assert.AreEqual("22023", ready.SqlState);
        Assert.AreEqual("ready initialization failure", ready.MessageText);
        Assert.AreEqual("owned ready detail", ready.Detail);
        Assert.AreEqual("retry initialization", ready.Hint);
        command.CommandText = "SET ankus_tool_probe.fail_ready = false; LOAD 'BindingConsumer'; SELECT native_callback_phase_counts()";
        Assert.AreEqual("2|2|2|2|42", await command.ExecuteScalarAsync(token));
        command.CommandText = "LOAD 'BindingConsumer'; SELECT native_callback_phase_counts()";
        Assert.AreEqual("2|2|2|2|42", await command.ExecuteScalarAsync(token));
        await AssertNativeHookChainingAsync(command, token);
    }

    /// <summary>
    /// Counts workers that PostgreSQL actually launched in the execution plan.
    /// </summary>
    private static int NativeCallbackWorkersLaunched(JsonElement plan)
    {
        int count = plan.TryGetProperty("Workers Launched", out JsonElement workers) ? workers.GetInt32() : 0;
        if (plan.TryGetProperty("Plans", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                count += NativeCallbackWorkersLaunched(child);
            }
        }

        return count;
    }

    /// <summary>
    /// Proves exact executor hook ordering, the standard fallback, native error recovery and explicit restoration.
    /// </summary>
    private static async Task AssertNativeHookChainingAsync(NpgsqlCommand command, CancellationToken token)
    {
        command.CommandText = "SELECT native_hook_install()";
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
        command.CommandText = "SELECT native_hook_order()";
        Assert.AreEqual("1,2,3,4", await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT 6 * 7";
        Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT native_hook_reject_once()";
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT 71";
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual("22023", error.SqlState);
        Assert.AreEqual("managed executor hook failure", error.MessageText);
        Assert.AreEqual("owned executor detail", error.Detail);
        Assert.AreEqual("retry executor", error.Hint);
        command.CommandText = "SELECT native_hook_order()";
        Assert.AreEqual("1,2,3,4", await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT native_hook_restore()";
        int calls = Assert.IsInstanceOfType<int>(await command.ExecuteScalarAsync(token));
        Assert.IsGreaterThan(0, calls);
        command.CommandText = "SELECT 6 * 7";
        Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT native_hook_count()";
        Assert.AreEqual(calls, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT native_hook_restore()";
        Assert.AreEqual(calls, await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Uses PostgreSQL's selected-header executor signature and the same explicit chaining pattern as pgrx's hooks example.
    /// </summary>
    private const string NativeHookSource = """
        using Ankus;
        using Ankus.Postgres;
        public static partial class BindingHookCallbacks
        {
            private static ExecutorStart_hook_type s_previous;
            private static ExecutorStart_hook_type s_inner;
            private static bool s_installed;
            private static bool s_reject;
            private static int s_calls;
            private static readonly System.Collections.Generic.List<int> s_order = new();

            [PgNativeCallback(nameof(OuterStart))]
            private static partial ExecutorStart_hook_type Outer { get; }

            [PgNativeCallback(nameof(InnerStart))]
            private static partial ExecutorStart_hook_type Inner { get; }

            private static void OuterStart(nint query, int flags)
            {
                s_calls++;
                s_order.Clear();
                s_order.Add(1);
                s_inner.Invoke(query, flags);
                s_order.Add(4);
            }

            private static void InnerStart(nint query, int flags)
            {
                s_order.Add(2);
                if (s_reject)
                {
                    s_reject = false;
                    throw new PgException("22023", "managed executor hook failure", "owned executor detail", "retry executor");
                }

                if (s_previous.IsNull)
                {
                    NativeMethods.standard_ExecutorStart(query, flags);
                }
                else
                {
                    s_previous.Invoke(query, flags);
                }

                s_order.Add(3);
            }

            public static bool NativeHookInstall()
            {
                if (!s_installed)
                {
                    s_previous = NativeGlobals.ExecutorStart_hook;
                    NativeGlobals.ExecutorStart_hook = Inner;
                    s_inner = NativeGlobals.ExecutorStart_hook;
                    NativeGlobals.ExecutorStart_hook = Outer;
                    s_installed = true;
                }

                return IsInstalled();
            }

            public static bool IsInstalled() => s_installed &&
                NativeGlobals.ExecutorStart_hook.DangerousGetAddress() == Outer.DangerousGetAddress() &&
                    s_inner.DangerousGetAddress() == Inner.DangerousGetAddress() &&
                    s_inner.DangerousGetAddress() != Outer.DangerousGetAddress();

            public static string NativeHookOrder() => string.Join(",", s_order);

            public static void NativeHookRejectOnce() => s_reject = true;

            public static int NativeHookCount() => s_calls;

            public static int NativeHookRestore()
            {
                if (s_installed)
                {
                    NativeGlobals.ExecutorStart_hook = s_previous;
                    s_installed = false;
                }

                return s_calls;
            }
        }
        """;

    /// <summary>
    /// Exercises statically selected managed callbacks in the packaged native-binding consumer.
    /// </summary>
    private const string ManagedNativeCallbackSource = """
        using Ankus;
        using Ankus.Postgres;
        public static unsafe partial class BindingCallbacks
        {
            private static PgNativeReference<ulong>? s_borrow;
            private static int s_calls;
            private static int s_initializations;
            private static int s_loadFinally;
            private static int s_initializerPid;
            private static ulong s_initializedValue;
            private static int s_readyPid;
            private static int s_readyCount;
            private static int s_readyFinally;
            private static string s_readySql = "missing";
            private static bool s_loading;
            private static string s_loadSql = "missing";
            private static string s_nestedLoadSql = "missing";

            public static void Initialize(bool fail)
            {
                s_initializations++;
                s_loading = true;
                try
                {
                    s_initializerPid = System.Environment.ProcessId;
                    s_loadSql = TrySql();
                    ulong value = 0xFEDCBA9876543210UL;
                    s_initializedValue = Second.Invoke((nint)(&value));
                    if (fail)
                    {
                        throw new PgException("22023", "module registration failure", "owned registration detail", "retry registration");
                    }

                    if (!BindingHookCallbacks.NativeHookInstall())
                    {
                        throw new System.InvalidOperationException("Native hook registration failed during initialization.");
                    }

                    PgLog.Write(PgLogLevel.Notice, $"Ankus native callback initialized: {s_initializedValue:X16}");
                }
                finally
                {
                    s_loading = false;
                    s_loadFinally++;
                }
            }

            public static void Ready(bool fail)
            {
                s_readyCount++;
                try
                {
                    if (!BindingHookCallbacks.IsInstalled())
                    {
                        throw new System.InvalidOperationException("Ready initialization preceded native registration.");
                    }

                    if (fail)
                    {
                        throw new PgException("22023", "ready initialization failure", "owned ready detail", "retry initialization");
                    }

                    s_readyPid = System.Environment.ProcessId;
                    s_readySql = TrySql();
                }
                finally
                {
                    s_readyFinally++;
                }
            }

            public static string NativeCallbackPhaseCounts() => $"{s_initializations}|{s_loadFinally}|{s_readyCount}|{s_readyFinally}|{s_readySql}";

            public static string[] NativeCallbackLifecycle(int input)
            {
                ulong value = (ulong)input;
                ulong result = Second.Invoke((nint)(&value));
                return [System.Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    s_initializerPid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    s_initializations.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    s_initializedValue.ToString("X16", System.Globalization.CultureInfo.InvariantCulture),
                    result.ToString("X16", System.Globalization.CultureInfo.InvariantCulture),
                    BindingHookCallbacks.IsInstalled().ToString(),
                    input.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    BindingHookCallbacks.NativeHookOrder(),
                    s_readyPid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    s_readyCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    s_readySql,
                    s_loadSql,
                    s_nestedLoadSql];
            }

            [PgNativeCallback(nameof(HandleFirst))]
            private static partial PGFunction First { get; }

            [PgNativeCallback(nameof(HandleSecond))]
            private static partial PGFunction Second { get; }

            [PgNativeCallback(nameof(HandleUnused))]
            private static partial PGFunction Unused { get; }

            [System.Runtime.InteropServices.LibraryImport("Ankus.NativeBodies", EntryPoint = "ankus_callback_must_be_trimmed")]
            private static partial ulong Missing(nint address);

            private static ulong HandleUnused(nint address) => Missing(address);

            private static ulong HandleFirst(nint address)
            {
                s_calls++;
                s_borrow = PgMemoryContext.Current.DangerousBorrow<ulong>((void*)address)!;
                ulong value = s_borrow.Value;
                if (value == 0)
                {
                    throw new PgException("22023", "managed callback café", "owned callback detail", "retry callback");
                }

                if (value == 1)
                {
                    return unchecked((ulong)Spi.ExecuteScalar<int>("SELECT 1 / 0"));
                }

                return value ^ 0x1000000000000000UL;
            }

            private static ulong HandleSecond(nint address)
            {
                if (s_loading)
                {
                    s_nestedLoadSql = TrySql();
                }

                return *(ulong*)address ^ 0x0100000000000000UL;
            }

            private static string TrySql()
            {
                try
                {
                    return Spi.ExecuteScalar<int>("SELECT 6 * 7").ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (System.InvalidOperationException error) when (error.Message == "PostgreSQL APIs can only be used on the active PostgreSQL backend thread.")
                {
                    return "unavailable";
                }
            }

            public static string NativeManagedCallbackValues()
            {
                s_calls = 0;
                using PgMemoryContext owner = PgMemoryContext.Create("managed native callbacks");
                using PgNativeBox<ulong> input = owner.CreateBox(0xFEDCBA9876543210UL);
                nint address = (nint)input.DangerousGetPointer();
                PGFunction first = First;
                PGFunction second = Second;
                ulong left = owner.Run(() => first.Invoke(address));
                PgNativeReference<ulong> borrowed = s_borrow!;
                bool retained = borrowed.Value == 0xFEDCBA9876543210UL;
                ulong right = second.Invoke(address);
                System.GC.Collect();
                bool stable = first.DangerousGetAddress() == First.DangerousGetAddress() &&
                    second.DangerousGetAddress() == Second.DangerousGetAddress() &&
                    first.DangerousGetAddress() != second.DangerousGetAddress();
                string managed = "missing";
                string native = "missing";
                input.Value = 0;
                try
                {
                    PgTransaction.RunInSubtransaction(() => first.Invoke(address));
                }
                catch (PgException error)
                {
                    managed = $"{error.SqlState}|{error.Message}|{error.Detail}|{error.Hint}";
                }

                input.Value = 1;
                try
                {
                    PgTransaction.RunInSubtransaction(() => first.Invoke(address));
                }
                catch (PgException error)
                {
                    native = $"{error.SqlState}|{error.Message}";
                }

                input.Value = 0xFEDCBA9876543210UL;
                ulong recovered = first.Invoke(address);
                owner.Reset();
                bool expired = false;
                try
                {
                    _ = borrowed.Value;
                }
                catch (System.ObjectDisposedException)
                {
                    expired = true;
                }

                return $"{left:X16}|{right:X16}|{retained}|{expired}|{stable}|{managed}|{native}|{recovered:X16}|{s_calls}|{Spi.ExecuteScalar<int>("SELECT 6 * 7")}";
            }
        }
        """;
}
