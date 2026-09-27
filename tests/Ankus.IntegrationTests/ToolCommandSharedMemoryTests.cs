using System.Diagnostics;
using System.Globalization;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class ToolCommandTests
{
    /// <summary>
    /// Real preloaded backends share exact values and release PostgreSQL locks after managed and native errors.
    /// </summary>
    [TestMethod]
    public async Task SharedMemoryLocksPreserveValuesAcrossBackendsAndFailures()
    {
        CancellationToken token = context.CancellationToken;
        string output = await PublishColdGucConsumerAsync("SharedMemory", "ankus_shared_probe", SharedMemorySource, token);
        await using (PostgresTestCluster ordinary = await StartPublishedClusterAsync(output, token))
        {
            await using NpgsqlConnection connection = await ordinary.OpenConnectionAsync(token);
            PostgresException late = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
                ExecutePackageGucAsync(connection, "LOAD 'SharedMemory'"));
            Assert.AreEqual("55000", late.SqlState);
            Assert.Contains("shared_preload_libraries", late.MessageText);
            Assert.AreEqual(42, await PackageGucScalarAsync(connection, "SELECT 42"));
        }

        foreach ((string setting, string message) in new (string, string)[]
        {
            ("ankus_shared_probe.extra_name = '" + new string('x', 48) + "'", "invalid Ankus shared-memory name"),
            ("ankus_shared_probe.extra_name = 'ankus_shared_probe.state'", "is already registered"),
            ("ankus_shared_probe.fail_startup = on", "Shared initializer deliberately failed."),
            ("ankus_shared_probe.fail_startup_hook = on", "Shared startup hook deliberately failed."),
        })
        {
            InvalidOperationException failed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                StartPublishedClusterAsync(output, token, sharedPreload: true, additionalConfiguration: [setting]));
            Assert.Contains(message, failed.Message);
        }

        await using PostgresTestCluster cluster = await StartPublishedClusterAsync(output, token, sharedPreload: true,
            additionalConfiguration: ["ankus_shared_probe.extra_name = '" + new string('é', 23) + "x'"]);
        Assert.Contains("Ankus shared value initialized once", cluster.ReadServerLog());
        await using NpgsqlConnection first = await cluster.OpenConnectionAsync(token);
        await using NpgsqlConnection second = await cluster.OpenConnectionAsync(token);
        Assert.AreNotEqual(first.ProcessID, second.ProcessID);
        await ExecutePackageGucAsync(first, "CREATE EXTENSION ankus_shared_probe");
        Assert.AreEqual("-9223372036854775808|18446744073709551615|1|2", await PackageGucScalarAsync(first, "SELECT shared_snapshot()"));
        Assert.AreEqual("-9223372036854775808|18446744073709551615|1|2", await PackageGucScalarAsync(second, "SELECT shared_snapshot()"));
        Assert.AreEqual(37L, await PackageGucScalarAsync(first, "SELECT shared_write(37)"));
        Assert.AreEqual("37|18446744073709551615|1|2", await PackageGucScalarAsync(second, "SELECT shared_snapshot()"));

        foreach ((int kind, string state, long expected) in new (int, string, long)[]
        {
            (0, "P7803", 41), (1, "22012", 43), (2, "55006", 47),
        })
        {
            PostgresException failure = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
                PackageGucScalarAsync(first, $"SELECT shared_fail({kind}, {expected})"));
            Assert.AreEqual(state, failure.SqlState);
            Assert.AreEqual($"{expected}|18446744073709551615|1|2", await PackageGucScalarAsync(second, "SELECT shared_snapshot()"));
            Assert.AreEqual(expected + 1, await PackageGucScalarAsync(first, $"SELECT shared_write({expected + 1})"));
        }

        Assert.AreEqual(71L, await PackageGucScalarAsync(first, "SELECT shared_forget(71)"));
        Assert.AreEqual("71|18446744073709551615|1|2", await PackageGucScalarAsync(second, "SELECT shared_snapshot()"));
        Assert.AreEqual("disposed", await PackageGucScalarAsync(first, "SELECT shared_expired()"));
        Assert.AreEqual(83L, await PackageGucScalarAsync(first, "SELECT shared_recover(83)"));
        Assert.AreEqual("83|18446744073709551615|1|2", await PackageGucScalarAsync(second, "SELECT shared_snapshot()"));
        await ExecutePackageGucAsync(first, "BEGIN; SELECT shared_write(97); ROLLBACK");
        Assert.AreEqual("97|18446744073709551615|1|2", await PackageGucScalarAsync(second, "SELECT shared_snapshot()"));
        await ExecutePackageGucAsync(first, "SET statement_timeout = '100ms'");
        PostgresException cancelled = await Assert.ThrowsExactlyAsync<PostgresException>(() => PackageGucScalarAsync(first, "SELECT pg_sleep(1)"));
        Assert.AreEqual("57014", cancelled.SqlState, "Shared lock recovery must not leave interrupts disabled.");
        await ExecutePackageGucAsync(first, "RESET statement_timeout");
        Assert.AreEqual(42, await PackageGucScalarAsync(first, "SELECT 42"));
        await AssertSharedLockContentionAsync(cluster, first, second, token);
        await AssertSharedPostmasterReloadAsync(cluster, first, token);
        await AssertSharedMemoryCrashRecoveryAsync(cluster, first, token);
    }

    /// <summary>
    /// An owned test backend crash makes the surviving postmaster initialize a fresh shared segment.
    /// </summary>
    private async Task AssertSharedMemoryCrashRecoveryAsync(PostgresTestCluster cluster, NpgsqlConnection connection, CancellationToken token)
    {
        int previousLogLength = cluster.ReadServerLog().Length;
        int previousProcess = connection.ProcessID;
        using (Process backend = Process.GetProcessById(previousProcess))
        {
            backend.Kill();
            await backend.WaitForExitAsync(token);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!cluster.ReadServerLog()[previousLogLength..].Contains("database system is ready to accept connections", StringComparison.Ordinal))
        {
            await Task.Delay(10, timeout.Token);
        }

        await using NpgsqlConnection recovered = await cluster.OpenConnectionAsync(timeout.Token);
        Assert.AreEqual("-9223372036854775808|18446744073709551615|2|3",
            await PackageGucScalarAsync(recovered, "SELECT shared_snapshot()"));
        Assert.AreEqual(101L, await PackageGucScalarAsync(recovered, "SELECT shared_write(101)"));
        Assert.AreEqual(42, await PackageGucScalarAsync(recovered, "SELECT 42"));
    }

    /// <summary>
    /// Postmaster configuration errors release owned managed diagnostics before returning to its dormant runtime.
    /// </summary>
    private async Task AssertSharedPostmasterReloadAsync(PostgresTestCluster cluster, NpgsqlConnection connection, CancellationToken token)
    {
        string[] identity = await File.ReadAllLinesAsync(Path.Combine(cluster.DataDirectory, "postmaster.pid"), token);
        int process = int.Parse(identity[0], CultureInfo.InvariantCulture);
        string configurationPath = Path.Combine(cluster.DataDirectory, "postgresql.conf");
        string configuration = await File.ReadAllTextAsync(configurationPath, token);
        await File.WriteAllTextAsync(configurationPath, configuration + "\nankus_shared_probe.checked = 13\n", token);
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(connection, "SELECT pg_reload_conf()")));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        while (!cluster.ReadServerLog().Split('\n').Any(line =>
            line.Contains($"[{process}]", StringComparison.Ordinal) && line.Contains("Shared configuration rejected.", StringComparison.Ordinal)))
        {
            await Task.Delay(10, timeout.Token);
        }

        await using (NpgsqlConnection afterRejection = await cluster.OpenConnectionAsync(timeout.Token))
        {
            Assert.AreEqual("7", await PackageGucScalarAsync(afterRejection, "SHOW ankus_shared_probe.checked"));
        }

        await File.WriteAllTextAsync(configurationPath, configuration + "\nankus_shared_probe.checked = 19\n", token);
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(connection, "SELECT pg_reload_conf()")));
        while (!Equals("19", await PackageGucScalarAsync(connection, "SHOW ankus_shared_probe.checked")))
        {
            await Task.Delay(10, timeout.Token);
        }

        await using NpgsqlConnection recovered = await cluster.OpenConnectionAsync(timeout.Token);
        Assert.AreEqual("19", await PackageGucScalarAsync(recovered, "SHOW ankus_shared_probe.checked"));
        Assert.AreEqual("97|18446744073709551615|1|2", await PackageGucScalarAsync(recovered, "SELECT shared_snapshot()"));
    }

    /// <summary>
    /// Observes PostgreSQL wait events to prove shared readers coexist and an exclusive writer blocks readers.
    /// </summary>
    private async Task AssertSharedLockContentionAsync(PostgresTestCluster cluster, NpgsqlConnection first,
        NpgsqlConnection second, CancellationToken token)
    {
        await using NpgsqlConnection observer = await cluster.OpenConnectionAsync(token);
        foreach (bool exclusive in new[] { false, true })
        {
            await using var heldCommand = new NpgsqlCommand(exclusive ? "SELECT shared_hold(true)" : "SELECT shared_hold(false)", first);
            Task<object?> holding = heldCommand.ExecuteScalarAsync(token);
            Task<object?>? reading = null;
            try
            {
                await WaitForEventAsync(first.ProcessID, "Timeout", "PgSleep");
                reading = PackageGucScalarAsync(second, "SELECT shared_snapshot()");
                if (exclusive)
                {
                    await WaitForEventAsync(second.ProcessID, "LWLock", "ankus_shared_probe.state");
                    Assert.IsFalse(reading.IsCompleted, "An exclusive guard must block the other backend's shared read.");
                }
                else
                {
                    Assert.AreEqual("97|18446744073709551615|1|2", await reading);
                    Assert.IsFalse(holding.IsCompleted, "Both backends must hold shared guards concurrently.");
                }
            }
            finally
            {
                await ExecutePackageGucAsync(observer, "SELECT shared_signal()");
                Assert.AreEqual(97L, await holding);
                if (reading is not null)
                {
                    Assert.AreEqual("97|18446744073709551615|1|2", await reading);
                }
            }
        }

        async Task WaitForEventAsync(int process, string type, string name)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await using var command = new NpgsqlCommand("SELECT wait_event_type = $2 AND wait_event = $3 FROM pg_stat_activity WHERE pid = $1", observer);
            command.Parameters.AddWithValue(process);
            command.Parameters.AddWithValue(type);
            command.Parameters.AddWithValue(name);
            while (!Equals(true, await command.ExecuteScalarAsync(timeout.Token)))
            {
                await Task.Delay(10, timeout.Token);
            }
        }
    }

    /// <summary>
    /// A packaged consumer declares shared values without native binding attributes or handwritten C.
    /// </summary>
    private const string SharedMemorySource = """
        using Ankus;
        using Ankus.Postgres;
        using System.Globalization;
        using StartupHookPointer = Ankus.Postgres.JitProviderResetAfterErrorCB;

        public readonly record struct SharedState(long Count, ulong Bits, int Order);

        public static partial class SharedFunctions
        {
            private static readonly PgLwLock<SharedState> State = new("ankus_shared_probe.state");
            private static readonly PgLwLock<int> Order = new("ankus_shared_probe.order");
            private static readonly PgLwLock<int> Gate = new("ankus_shared_probe.gate");
            private static PgLwLockExclusiveGuard<SharedState>? s_escaped;
            private static int s_initializations;
            private static StartupHookPointer s_previousStartup;
            private static bool s_startupHookSeen;

            [PgNativeCallback(nameof(StartupHook))]
            private static partial StartupHookPointer StartupCallback { get; }

            [PgGucBool("ankus_shared_probe.fail_startup_hook", false, "Fail the prior startup hook")]
            public static partial bool FailStartupHook { get; }

            [PgGucInt("ankus_shared_probe.checked", 7, "Configuration reload witness", Check = nameof(CheckSetting))]
            public static partial int CheckedSetting { get; }

            public static PgGucCheckResult<int> CheckSetting(int proposed, PgGucSource source)
            {
                if (proposed == 13)
                {
                    throw new PgException("P7806", "Shared configuration rejected.", "Owned postmaster detail.", "Use another value.");
                }

                return new(proposed);
            }

            private static void StartupHook()
            {
                if (!s_previousStartup.IsNull)
                {
                    s_previousStartup.Invoke();
                }

                if (FailStartupHook)
                {
                    throw new PgException("P7807", "Shared startup hook deliberately failed.");
                }

                s_startupHookSeen = true;
            }

            [PgGucString("ankus_shared_probe.extra_name", null, "Optional extra shared-memory name")]
            public static partial string? ExtraName { get; }

            [PgGucBool("ankus_shared_probe.fail_startup", false, "Fail the shared-memory initializer")]
            public static partial bool FailStartup { get; }

            [PgModuleLoad]
            public static void Register()
            {
                s_previousStartup = NativeGlobals.shmem_startup_hook;
                NativeGlobals.shmem_startup_hook = StartupCallback;
                PgSharedMemory.Initialize(State, () =>
                {
                    if (!s_startupHookSeen)
                    {
                        throw new InvalidOperationException("The prior startup hook did not run first.");
                    }

                    s_startupHookSeen = false;

                    if (FailStartup)
                    {
                        throw new PgException("P7804", "Shared initializer deliberately failed.");
                    }

                    PgLog.Write(PgLogLevel.Notice, "Ankus shared value initialized once");
                    return new SharedState(long.MinValue, ulong.MaxValue, ++s_initializations);
                });
                PgSharedMemory.Initialize(Order, () =>
                {
                    using PgLwLockShareGuard<SharedState> guard = State.Share();
                    return guard.Value.Order + 1;
                });
                PgSharedMemory.Initialize(Gate);
                PgSharedMemory.Initialize(State, () => throw new InvalidOperationException("Duplicate initialization."));
                if (ExtraName is { } extra)
                {
                    PgSharedMemory.Initialize(new PgLwLock<int>(extra));
                }
            }

            [PgFunction]
            public static long SharedHold(bool exclusive)
            {
                if (exclusive)
                {
                    using PgLwLockExclusiveGuard<SharedState> guard = State.Exclusive();
                    WaitForSignal();
                    return guard.Value.Count;
                }

                using PgLwLockShareGuard<SharedState> shared = State.Share();
                WaitForSignal();
                return shared.Value.Count;
            }

            [PgFunction]
            public static void SharedSignal()
            {
                using PgLwLockExclusiveGuard<int> guard = Gate.Exclusive();
                guard.Value = 0;
            }

            private static void WaitForSignal()
            {
                using (PgLwLockExclusiveGuard<int> guard = Gate.Exclusive())
                {
                    guard.Value = 1;
                }

                System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
                while (elapsed.Elapsed < TimeSpan.FromSeconds(20))
                {
                    using (PgLwLockShareGuard<int> guard = Gate.Share())
                    {
                        if (guard.Value == 0)
                        {
                            return;
                        }
                    }

                    Spi.Execute("SELECT pg_sleep(0.05)");
                }

                throw new TimeoutException("The shared-lock test did not signal release.");
            }

            [PgFunction]
            public static string SharedSnapshot()
            {
                using PgLwLockShareGuard<SharedState> guard = State.Share();
                using PgLwLockShareGuard<int> order = Order.Share();
                SharedState value = guard.Value;
                return string.Create(CultureInfo.InvariantCulture, $"{value.Count}|{value.Bits}|{value.Order}|{order.Value}");
            }

            [PgFunction]
            public static long SharedWrite(long value)
            {
                using PgLwLockExclusiveGuard<SharedState> guard = State.Exclusive();
                guard.Value = guard.Value with { Count = value };
                return guard.Value.Count;
            }

            [PgFunction]
            public static void SharedFail(int kind, long value)
            {
                using PgLwLockExclusiveGuard<SharedState> guard = State.Exclusive();
                guard.Value = guard.Value with { Count = value };
                if (kind == 0)
                {
                    throw new PgException("P7803", "Shared managed failure.");
                }

                if (kind == 1)
                {
                    Spi.Execute("SELECT 1/0");
                }
                else
                {
                    using PgLwLockShareGuard<SharedState> nested = State.Share();
                }
            }

            [PgFunction]
            public static long SharedForget(long value)
            {
                s_escaped = State.Exclusive();
                s_escaped.Value = s_escaped.Value with { Count = value };
                return value;
            }

            [PgFunction]
            public static string SharedExpired()
            {
                try
                {
                    return s_escaped!.Value.Count.ToString(CultureInfo.InvariantCulture);
                }
                catch (ObjectDisposedException)
                {
                    s_escaped!.Dispose();
                    return "disposed";
                }
            }

            [PgFunction]
            public static long SharedRecover(long value)
            {
                using PgLwLockExclusiveGuard<SharedState> original = State.Exclusive();
                try
                {
                    PgTransaction.RunInSubtransaction(() => Spi.Execute("SELECT 1/0"));
                }
                catch (PgException exception) when (exception.SqlState == "22012")
                {
                    using PgLwLockExclusiveGuard<SharedState> replacement = State.Exclusive();
                    try
                    {
                        _ = original.Value;
                        throw new InvalidOperationException("A stale shared guard read a replacement acquisition.");
                    }
                    catch (PgException expired) when (expired.SqlState == "55000")
                    {
                    }

                    original.Dispose();
                    replacement.Value = replacement.Value with { Count = value };
                    return replacement.Value.Count;
                }

                throw new InvalidOperationException("Expected a native error.");
            }
        }
        """;
}
