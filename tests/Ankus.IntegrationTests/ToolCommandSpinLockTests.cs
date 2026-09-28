using System.Diagnostics;
using System.Globalization;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class ToolCommandTests
{
    /// <summary>
    /// Published spinlocks preserve values, ownership and recovery across independent PostgreSQL backends.
    /// </summary>
    [TestMethod]
    public async Task SharedSpinLocksPreserveValuesAcrossBackends()
    {
        CancellationToken token = context.CancellationToken;
        string output = await PublishPackageConsumerAsync("SpinLocks", "ankus_spin_probe", SpinLockSource, token);
        await using PostgresTestCluster cluster = await StartPublishedClusterAsync(output, token, sharedPreload: true);
        await using NpgsqlConnection first = await cluster.OpenConnectionAsync(token);
        await using NpgsqlConnection second = await cluster.OpenConnectionAsync(token);
        Assert.AreNotEqual(first.ProcessID, second.ProcessID);
        await ExecutePackageGucAsync(first, "CREATE EXTENSION ankus_spin_probe");
        int version = int.Parse(Assert.IsInstanceOfType<string>(await PackageGucScalarAsync(first, "SHOW server_version_num")), CultureInfo.InvariantCulture);
        Assert.AreEqual(version < 190000 ? "False|True|False|0" : "unsupported|0",
            await PackageGucScalarAsync(first, "SELECT spin_state()"));
        Assert.AreSequenceEqual(Enumerable.Range(1, 10),
            Assert.IsInstanceOfType<int[]>(await PackageGucScalarAsync(first, "SELECT array_agg(spin_increment()) FROM generate_series(1, 10)")));
        Assert.AreEqual(10, await PackageGucScalarAsync(second, "SELECT spin_read()"));
        Assert.AreEqual("-9223372036854775808|0|0", await PackageGucScalarAsync(first, "SELECT spin_nested_read()"));
        Assert.AreEqual("-9223372036854775808|14|14", await PackageGucScalarAsync(first, "SELECT spin_nested_local()"));
        Assert.AreEqual("-9223372036854775808|15|15", await PackageGucScalarAsync(first, "SELECT spin_nested_local()"));
        Assert.AreEqual("-9223372036854775808|14|14", await PackageGucScalarAsync(second, "SELECT spin_nested_local()"));
        Assert.AreEqual(13, await PackageGucScalarAsync(first, "SELECT spin_local(true)"));
        Assert.AreEqual(14, await PackageGucScalarAsync(first, "SELECT spin_local(false)"));
        Assert.AreEqual(13, await PackageGucScalarAsync(second, "SELECT spin_local(true)"));
        Assert.AreEqual(47, await PackageGucScalarAsync(first, "SELECT spin_forget_local()"));
        Assert.AreEqual(47, await PackageGucScalarAsync(first, "SELECT spin_local(false)"));
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(first, "SELECT spin_expired_guard()")));
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(first, "SELECT spin_reject_worker()")));
        Assert.AreEqual(10, await PackageGucScalarAsync(second, "SELECT spin_read()"));

        await ExecutePackageGucAsync(first, "SELECT spin_set(0); SELECT spin_prepare_gate()");
        await Task.WhenAll(ExecutePackageGucAsync(first, "SELECT spin_work(2000)"), ExecutePackageGucAsync(second, "SELECT spin_work(3000)"));
        Assert.AreEqual(5000, await PackageGucScalarAsync(first, "SELECT spin_read()"));
        Assert.AreEqual("-9223372036854775808|5000|5000", await PackageGucScalarAsync(second, "SELECT spin_nested_read()"));
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(first, "SELECT spin_nested_reject_aliases()")));
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(first, "SELECT spin_nested_forget()")));
        PostgresException nestedFailure = await Assert.ThrowsExactlyAsync<PostgresException>(() => ExecutePackageGucAsync(first, "SELECT spin_nested_fail(97)"));
        Assert.AreEqual("P7841", nestedFailure.SqlState);
        Assert.AreEqual("spinlock reader error", nestedFailure.MessageText);
        Assert.AreEqual("owned reader detail", nestedFailure.Detail);
        Assert.AreEqual("-9223372036854775808|97|97", await PackageGucScalarAsync(second, "SELECT spin_nested_read()"));
        await ExecutePackageGucAsync(first, "BEGIN; SELECT spin_nested_add(10); ROLLBACK");
        Assert.AreEqual("-9223372036854775808|107|107", await PackageGucScalarAsync(second, "SELECT spin_nested_read()"));
        Assert.AreEqual("113|127|107", await PackageGucScalarAsync(first, "SELECT spin_nested_mutate(113, 127, false)"));
        Assert.AreEqual("113|127|107", await PackageGucScalarAsync(second, "SELECT spin_nested_read()"));
        PostgresException mutationFailure = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
            ExecutePackageGucAsync(first, "SELECT spin_nested_mutate(131, 137, true)"));
        Assert.AreEqual("P7843", mutationFailure.SqlState);
        Assert.AreEqual("spin mutation error", mutationFailure.MessageText);
        Assert.AreEqual("owned mutation detail", mutationFailure.Detail);
        Assert.AreEqual("131|137|107", await PackageGucScalarAsync(second, "SELECT spin_nested_read()"));
        await ExecutePackageGucAsync(first, "BEGIN; SELECT spin_nested_mutate(139, 149, false); ROLLBACK");
        Assert.AreEqual("139|149|107", await PackageGucScalarAsync(second, "SELECT spin_nested_read()"));
        Assert.AreEqual(42, await PackageGucScalarAsync(first, "SELECT 42"));
        Assert.AreEqual(5000, await PackageGucScalarAsync(second, "SELECT spin_set(73)"));
        Assert.AreEqual(73, await PackageGucScalarAsync(first, "SELECT spin_read()"));
        PostgresException failure = await Assert.ThrowsExactlyAsync<PostgresException>(() => ExecutePackageGucAsync(first, "SELECT spin_fail(97)"));
        Assert.AreEqual("P7832", failure.SqlState);
        Assert.AreEqual("spinlock managed error", failure.MessageText);
        Assert.AreEqual(97, await PackageGucScalarAsync(second, "SELECT spin_read()"));
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(first, "SELECT spin_reject_sql()")));
        Assert.AreEqual(101, await PackageGucScalarAsync(second, "SELECT spin_read()"));
        Assert.AreEqual(42, await PackageGucScalarAsync(first, "SELECT 42"));
        await ExecutePackageGucAsync(first, "BEGIN; SELECT spin_set(107); ROLLBACK");
        Assert.AreEqual(107, await PackageGucScalarAsync(second, "SELECT spin_read()"));

        await ExecutePackageGucAsync(first, "SELECT spin_prepare_gate()");
        Task holding = ExecutePackageGucAsync(first, "SELECT spin_hold_for_crash()");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!Equals(1, await PackageGucScalarAsync(second, "SELECT spin_gate()")))
        {
            await Task.Delay(10, timeout.Token);
        }

        int logLength = cluster.ReadServerLog().Length;
        using (Process backend = Process.GetProcessById(first.ProcessID))
        {
            backend.Kill();
            await backend.WaitForExitAsync(token);
        }

        await Assert.ThrowsAsync<NpgsqlException>(() => holding);
        while (!cluster.ReadServerLog()[logLength..].Contains("database system is ready to accept connections", StringComparison.Ordinal))
        {
            await Task.Delay(10, timeout.Token);
        }

        await using NpgsqlConnection recovered = await cluster.OpenConnectionAsync(timeout.Token);
        Assert.AreEqual(0, await PackageGucScalarAsync(recovered, "SELECT spin_read()"));
        Assert.AreEqual("-9223372036854775808|0|0", await PackageGucScalarAsync(recovered, "SELECT spin_nested_read()"));
        Assert.AreEqual("-9223372036854775808|14|14", await PackageGucScalarAsync(recovered, "SELECT spin_nested_local()"));
        Assert.AreEqual(1, await PackageGucScalarAsync(recovered, "SELECT spin_increment()"));
        Assert.AreEqual(13, await PackageGucScalarAsync(recovered, "SELECT spin_local(true)"));
        Assert.AreEqual(42, await PackageGucScalarAsync(recovered, "SELECT 42"));
    }

    /// <summary>
    /// Exercises the pgrx guard update loop, native contention and owned crash recovery in a packaged consumer.
    /// </summary>
    private const string SpinLockSource = """
        using Ankus;
        using System.Diagnostics;

        public readonly struct SpinState(int initial)
        {
            public readonly PgSpinLockValue<int> Counter = new(initial);
            public readonly PgSpinLockValue<NestedState> Nested = new(new NestedState(initial));
        }

        public struct NestedState(int initial)
        {
            public long Marker = long.MinValue;
            public readonly PgAtomicValue<int> Atomic = new(initial);
            public readonly PgSpinLockValue<int> Counter = new(initial);
        }

        public static class SpinFunctions
        {
            private static readonly PgShared<SpinState> State = new("ankus_spin_probe.state");
            private static readonly PgAtomic<int> Gate = new("ankus_spin_probe.gate");
            private static PgSpinLock<int>? s_local;
            private static PgSpinLock<NestedState>? s_nestedLocal;

            [PgModuleLoad]
            public static void Load()
            {
                PgSharedMemory.Initialize(State, static () => new SpinState(0));
                PgSharedMemory.Initialize(Gate);
                s_local = new PgSpinLock<int>(13);
                s_nestedLocal = new PgSpinLock<NestedState>(new NestedState(13));
            }

            [PgFunction]
            public static string SpinNestedRead() => State.Read(static (in SpinState state) =>
            {
                using PgSpinLockGuard<NestedState> parent = state.Nested.Lock();
                return parent.Read(static (in NestedState value) =>
                {
                    using PgSpinLockGuard<int> child = value.Counter.Lock();
                    return $"{value.Marker}|{value.Atomic.Value}|{child.Value}";
                });
            });

            [PgFunction]
            public static string SpinNestedLocal()
            {
                using PgSpinLockGuard<NestedState> parent = s_nestedLocal!.Lock();
                return parent.Read(static (in NestedState value) =>
                {
                    using PgSpinLockGuard<int> child = value.Counter.Lock();
                    child.Value++;
                    value.Atomic.Exchange(child.Value);
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                    return $"{value.Marker}|{value.Atomic.Value}|{child.Value}";
                });
            }

            [PgFunction]
            public static int SpinNestedAdd(int amount) => State.Read((in SpinState state) =>
            {
                using PgSpinLockGuard<NestedState> parent = state.Nested.Lock();
                return parent.Read((in NestedState value) =>
                {
                    using PgSpinLockGuard<int> child = value.Counter.Lock();
                    child.Value += amount;
                    value.Atomic.Exchange(child.Value);
                    return child.Value;
                });
            });

            [PgFunction]
            public static string SpinNestedMutate(long marker, int atomic, bool fail) => State.Read((in SpinState state) =>
            {
                using PgSpinLockGuard<NestedState> parent = state.Nested.Lock();
                _ = parent.Mutate((ref NestedState value) =>
                {
                    value.Marker = marker;
                    value.Atomic.Exchange(atomic);
                    RequireBlocked(parent.Dispose);
                    RequireBlocked(() => parent.Value = default);
                    RequireBlocked(() => parent.Read(static (in NestedState nested) => nested.Marker));
                    RequireBlocked(() => parent.Mutate(static (ref NestedState nested) => nested.Marker = 99));
                    RequireBlocked(static () => Spi.Execute("SELECT 1/0"));
                    RequireBlocked(static () => _ = PgMemoryContext.Current);
                    RequireBlocked(static () => PgLog.Write(PgLogLevel.Notice, "blocked mutation log"));
                    RequireChildBlocked(in value.Counter);
                    if (parent.Value.Marker != marker)
                    {
                        throw new InvalidOperationException("A mutation snapshot lost its current value.");
                    }

                    if (fail)
                    {
                        PgLog.Write(PgLogLevel.Error, new PgDiagnostic("spin mutation error")
                        {
                            SqlState = "P7843",
                            Detail = "owned mutation detail",
                        });
                    }

                    return value.Marker;
                });
                return parent.Read(static (in NestedState value) =>
                {
                    using PgSpinLockGuard<int> child = value.Counter.Lock();
                    return $"{value.Marker}|{value.Atomic.Value}|{child.Value}";
                });
            });

            private static void RequireBlocked(Action operation)
            {
                try
                {
                    operation();
                }
                catch (InvalidOperationException)
                {
                    return;
                }

                throw new InvalidOperationException("An active mutation permitted conflicting access.");
            }

            private static void RequireChildBlocked(scoped in PgSpinLockValue<int> value)
            {
                int rejected = 0;
                try
                {
                    using PgSpinLockGuard<int> child = value.Lock();
                }
                catch (InvalidOperationException)
                {
                    rejected++;
                }

                try
                {
                    _ = value.IsLocked;
                }
                catch (InvalidOperationException)
                {
                    rejected++;
                }

                if (rejected != 2)
                {
                    throw new InvalidOperationException("A surrounding read admitted a mutable child lock.");
                }
            }

            [PgFunction]
            public static bool SpinNestedRejectAliases() => State.Read(static (in SpinState state) =>
            {
                using PgSpinLockGuard<NestedState> parent = state.Nested.Lock();
                return parent.Read((in NestedState value) =>
                {
                    bool release = false;
                    bool replace = false;
                    try
                    {
                        parent.Dispose();
                    }
                    catch (InvalidOperationException error) when (error.Message.Contains("scoped read", StringComparison.Ordinal))
                    {
                        release = true;
                    }

                    try
                    {
                        parent.Value = default;
                    }
                    catch (InvalidOperationException error) when (error.Message.Contains("scoped read", StringComparison.Ordinal))
                    {
                        replace = true;
                    }

                    using PgSpinLockGuard<int> child = value.Counter.Lock();
                    return release && replace && child.Value == 5000 && value.Atomic.Value == 5000 && value.Marker == long.MinValue;
                });
            });

            [PgFunction]
            public static bool SpinNestedForget() => State.Read(static (in SpinState state) =>
            {
                using PgSpinLockGuard<NestedState> parent = state.Nested.Lock();
                PgSpinLockGuard<int> child = parent.Read(static (in NestedState value) => value.Counter.Lock());
                try
                {
                    _ = child.Value;
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return true;
                }
            });

            [PgFunction]
            public static int SpinNestedFail(int changed) => State.Read((in SpinState state) =>
            {
                using PgSpinLockGuard<NestedState> parent = state.Nested.Lock();
                return parent.Read((in NestedState value) =>
                {
                    PgSpinLockGuard<int> child = value.Counter.Lock();
                    child.Value = changed;
                    value.Atomic.Exchange(changed);
                    PgLog.Write(PgLogLevel.Error, new PgDiagnostic("spinlock reader error")
                    {
                        SqlState = "P7841",
                        Detail = "owned reader detail",
                    });
                    return 0;
                });
            });

            [PgFunction]
            public static int SpinRead() => State.Read(static (in SpinState state) =>
            {
                using PgSpinLockGuard<int> guard = state.Counter.Lock();
                return guard.Value;
            });

            [PgFunction]
            public static int SpinSet(int value) => State.Read((in SpinState state) =>
            {
                using PgSpinLockGuard<int> guard = state.Counter.Lock();
                int previous = guard.Value;
                guard.Value = value;
                return previous;
            });

            [PgFunction]
            public static int SpinIncrement() => State.Read(static (in SpinState state) =>
            {
                using PgSpinLockGuard<int> guard = state.Counter.Lock();
                return guard.Mutate(static (ref int value) => ++value);
            });

            [PgFunction]
            public static string SpinState()
            {
                bool supported = true;
                bool before = false;
                try
                {
                    before = State.Read(static (in SpinState state) => state.Counter.IsLocked);
                }
                catch (NotSupportedException)
                {
                    supported = false;
                }

                bool during = false;
                int value = State.Read((in SpinState state) =>
                {
                    using PgSpinLockGuard<int> guard = state.Counter.Lock();
                    if (supported)
                    {
                        during = state.Counter.IsLocked;
                    }

                    return guard.Value;
                });
                bool after = supported && State.Read(static (in SpinState state) => state.Counter.IsLocked);
                return supported ? $"{before}|{during}|{after}|{value}" : $"unsupported|{value}";
            }

            [PgFunction]
            public static int SpinLocal(bool collect)
            {
                using PgSpinLockGuard<int> guard = s_local!.Lock();
                int previous = guard.Value;
                guard.Value++;
                if (collect)
                {
                    // Deliberately stress stable local storage while its test-owned guard exists.
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                    if (guard.Value != previous + 1)
                    {
                        throw new InvalidOperationException("Local spinlock data changed during GC.");
                    }
                }

                return previous;
            }

            [PgFunction]
            public static int SpinForgetLocal()
            {
                PgSpinLockGuard<int> guard = s_local!.Lock();
                guard.Value = 47;
                return guard.Value;
            }

            [PgFunction]
            public static bool SpinExpiredGuard()
            {
                PgSpinLockGuard<int> expired = State.Read(static (in SpinState state) => state.Counter.Lock());
                try
                {
                    _ = expired.Value;
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return true;
                }
            }

            [PgFunction]
            public static bool SpinRejectWorker() => Task.Run(() =>
            {
                try
                {
                    return State.Read(static (in SpinState state) =>
                    {
                        using PgSpinLockGuard<int> guard = state.Counter.Lock();
                        return false;
                    });
                }
                catch (InvalidOperationException error) when (error.Message.Contains("active backend callback", StringComparison.Ordinal))
                {
                    return true;
                }
            }).GetAwaiter().GetResult();

            [PgFunction]
            public static void SpinPrepareGate() => Gate.Value = 0;

            [PgFunction]
            public static int SpinGate() => Gate.Value;

            [PgFunction]
            public static void SpinWork(int count)
            {
                Gate.Increment();
                long started = Stopwatch.GetTimestamp();
                while (Gate.Value != 2)
                {
                    if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(10))
                    {
                        throw new TimeoutException("The other spinlock backend did not arrive.");
                    }

                    Thread.Sleep(1);
                }

                for (int index = 0; index < count; index++)
                {
                    _ = SpinIncrement();
                    _ = SpinNestedAdd(1);
                }
            }

            [PgFunction]
            public static int SpinFail(int value) => State.Read((in SpinState state) =>
            {
                using PgSpinLockGuard<int> guard = state.Counter.Lock();
                guard.Value = value;
                PgLog.Write(PgLogLevel.Error, new PgDiagnostic("spinlock managed error") { SqlState = "P7832" });
                return 0;
            });

            [PgFunction]
            public static bool SpinRejectSql() => State.Read(static (in SpinState state) =>
            {
                using PgSpinLockGuard<int> guard = state.Counter.Lock();
                guard.Value = 101;
                try
                {
                    Spi.Execute("SELECT 1 / 0");
                    return false;
                }
                catch (InvalidOperationException error) when (error.Message.Contains("spinlock", StringComparison.Ordinal))
                {
                    return true;
                }
            });

            [PgFunction]
            public static int SpinHoldForCrash() => State.Read<int>(static (in SpinState state) =>
            {
                using PgSpinLockGuard<int> guard = state.Counter.Lock();
                guard.Value = 211;
                using PgSpinLockGuard<NestedState> parent = state.Nested.Lock();
                return parent.Read<int>(static (in NestedState value) =>
                {
                    using PgSpinLockGuard<int> child = value.Counter.Lock();
                    child.Value = 223;
                    value.Atomic.Exchange(223);
                    Gate.Value = 1;
                    long started = Stopwatch.GetTimestamp();
                    // The test kills this owned backend while both nested guards remain live.
                    while (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(30))
                    {
                        Thread.Sleep(10);
                    }

                    throw new TimeoutException("The owned spinlock backend was not stopped.");
                });
            });
        }
        """;
}
