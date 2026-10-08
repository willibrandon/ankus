using System.Diagnostics;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class ToolCommandTests
{
    /// <summary>
    /// Published shared aggregates retain immutable data and original atomic fields through process and segment lifetimes.
    /// </summary>
    [TestMethod]
    public async Task SharedAggregatesPreserveValuesAcrossProcessesAndThreads()
    {
        CancellationToken token = context.CancellationToken;
        string output = await PublishPackageConsumerAsync("SharedAggregates", "ankus_shared_probe", SharedAggregateSource, token);
        await using (PostgresTestCluster ordinary = await StartPublishedClusterAsync(output, token))
        {
            await using NpgsqlConnection connection = await ordinary.OpenConnectionAsync(token);
            PostgresException late = await Assert.ThrowsExactlyAsync<PostgresException>(() => ExecutePackageGucAsync(connection, "LOAD 'SharedAggregates'"));
            Assert.AreEqual("55000", late.SqlState);
            Assert.AreEqual(42, await PackageGucScalarAsync(connection, "SELECT 42"));
        }

        foreach ((string setting, string message) in new (string, string)[]
        {
            ("ankus_shared_probe.fail_startup = on", "Shared initializer deliberately failed."),
            ("ankus_shared_probe.conflict = on", "is already registered"),
        })
        {
            InvalidOperationException failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                StartPublishedClusterAsync(output, token, sharedPreload: true, additionalConfiguration: [setting]));
            Assert.Contains(message, failure.Message);
        }

        await using PostgresTestCluster cluster = await StartPublishedClusterAsync(output, token, sharedPreload: true);
        await using NpgsqlConnection first = await cluster.OpenConnectionAsync(token);
        await using NpgsqlConnection second = await cluster.OpenConnectionAsync(token);
        Assert.AreNotEqual(first.ProcessID, second.ProcessID);
        await ExecutePackageGucAsync(first, "CREATE EXTENSION ankus_shared_probe");
        const string initial = "73|1|12345678-1234-5678-90ab-123456789abc|-170141183460469231731687303715884105655|1.234567890123456789|129,0,243,0,127|74";
        Assert.AreEqual(initial, await PackageGucScalarAsync(first, "SELECT shared_snapshot()"));
        Assert.AreEqual(initial, await PackageGucScalarAsync(second, "SELECT shared_snapshot()"));
        Assert.AreEqual("True|1", await PackageGucScalarAsync(first, "SELECT shared_worker_snapshot()"));
        Assert.AreEqual("True|False|255|0|9221120237041090675|9221120237041090675|-9223372036854775808",
            await PackageGucScalarAsync(first, "SELECT shared_cells()"));
        Assert.AreEqual("False|0|-9223372036854775808", await PackageGucScalarAsync(second, "SELECT shared_cell_snapshot()"));
        await Task.WhenAll(ExecutePackageGucAsync(first, "SELECT shared_work(1000, true)"), ExecutePackageGucAsync(second, "SELECT shared_work(1000, true)"));
        Assert.AreEqual(8073L, await PackageGucScalarAsync(first, "SELECT shared_count()"));
        Assert.AreEqual(8073L, await PackageGucScalarAsync(second, "SELECT shared_compare(91, 8072)"));
        Assert.AreEqual(8073L, await PackageGucScalarAsync(first, "SELECT shared_count()"));
        Assert.AreEqual(8073L, await PackageGucScalarAsync(second, "SELECT shared_compare(91, 8073)"));
        Assert.AreEqual(91L, await PackageGucScalarAsync(first, "SELECT shared_count()"));
        foreach ((bool native, string state, long value) in new (bool, string, long)[] { (false, "P7820", 97), (true, "22012", 101) })
        {
            PostgresException failure = await Assert.ThrowsExactlyAsync<PostgresException>(() => ExecutePackageGucAsync(first, $"SELECT shared_fail({native}, {value})"));
            Assert.AreEqual(state, failure.SqlState);
            Assert.AreEqual(value, await PackageGucScalarAsync(second, "SELECT shared_count()"));
            Assert.AreEqual(value, await PackageGucScalarAsync(first, $"SELECT shared_compare({value + 1}, {value})"));
        }

        await ExecutePackageGucAsync(first, "BEGIN; SELECT shared_compare(107, 102); ROLLBACK");
        Assert.AreEqual(107L, await PackageGucScalarAsync(second, "SELECT shared_count()"));
        PostgresException packed = await Assert.ThrowsExactlyAsync<PostgresException>(() => ExecutePackageGucAsync(first, "SELECT shared_unaligned()"));
        Assert.Contains("eight-byte alignment", packed.MessageText);
        Assert.AreEqual(107L, await PackageGucScalarAsync(first, "SELECT shared_count()"));

        // FATAL exits from inside the read's PostgreSQL call, so the backend's own admission is never released.
        await using (NpgsqlConnection sleeper = await cluster.OpenConnectionAsync(token))
        {
            int sleeperProcess = sleeper.ProcessID;
            Task sleeping = ExecutePackageGucAsync(sleeper, "SELECT shared_sleep()");
            using var exiting = CancellationTokenSource.CreateLinkedTokenSource(token);
            exiting.CancelAfter(TimeSpan.FromSeconds(30));
            while (!Equals(true, await PackageGucScalarAsync(second,
                $"SELECT EXISTS (SELECT FROM pg_catalog.pg_stat_activity WHERE pid = {sleeperProcess} AND wait_event = 'PgSleep')")))
            {
                await Task.Delay(10, exiting.Token);
            }

            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(second, $"SELECT pg_catalog.pg_terminate_backend({sleeperProcess})")));
            PostgresException terminated = await Assert.ThrowsExactlyAsync<PostgresException>(() => sleeping);
            Assert.AreEqual("57P01", terminated.SqlState);
            while (!Equals(false, await PackageGucScalarAsync(second,
                $"SELECT EXISTS (SELECT FROM pg_catalog.pg_stat_activity WHERE pid = {sleeperProcess})")))
            {
                await Task.Delay(10, exiting.Token);
            }
        }

        Assert.AreEqual(107L, await PackageGucScalarAsync(second, "SELECT shared_count()"));
        int logLength = cluster.ReadServerLog().Length;
        using (Process backend = Process.GetProcessById(first.ProcessID))
        {
            backend.Kill();
            await backend.WaitForExitAsync(token);
        }

        using CancellationTokenSource timeout = CrashRecovery.CreateDeadline(token);
        while (!cluster.ReadServerLog()[logLength..].Contains("database system is ready to accept connections", StringComparison.Ordinal))
        {
            await Task.Delay(10, timeout.Token);
        }

        await using NpgsqlConnection recovered = await cluster.OpenConnectionAsync(timeout.Token);
        Assert.AreEqual(initial.Replace("73|1|", "73|2|", StringComparison.Ordinal), await PackageGucScalarAsync(recovered, "SELECT shared_snapshot()"));
        Assert.AreEqual("True|1", await PackageGucScalarAsync(recovered, "SELECT shared_worker_snapshot()"));
        await ExecutePackageGucAsync(recovered, "SELECT shared_work(10, false)");
        Assert.AreEqual(113L, await PackageGucScalarAsync(recovered, "SELECT shared_count()"));
        Assert.AreEqual(42, await PackageGucScalarAsync(recovered, "SELECT 42"));
        await ExecutePackageGucAsync(recovered, "SELECT shared_stop_worker()");
    }

    /// <summary>
    /// Combines immutable aggregates, inline arrays and atomic cells in a packaged Native AOT consumer.
    /// </summary>
    private const string SharedAggregateSource = """
        using Ankus;
        using System.Collections.Concurrent;
        using System.Diagnostics;
        using System.Globalization;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;

        public readonly struct SharedState(int epoch)
        {
            public readonly PgAtomicValue<long> Count = new(73);
            public readonly PgAtomicValue<bool> Flag = new(true);
            public readonly PgAtomicValue<byte> Tiny = new(255);
            public readonly PgAtomicValue<double> Bits = new(BitConverter.Int64BitsToDouble(0x7FF8000000000073));
            public readonly PgAtomicValue<int> Gate;
            public readonly PgAtomicValue<long> Pulse;
            public readonly PgAtomicValue<int> WorkerEpoch;
            public readonly PgAtomicValue<bool> Stop;
            public int Epoch { get; } = epoch;
            public Guid Identity { get; } = new("12345678-1234-5678-90ab-123456789abc");
            public Int128 Bound { get; } = Int128.MinValue + 73;
            public decimal Ratio { get; } = 1.234567890123456789m;
        }

        [InlineArray(5)]
        public struct Bytes
        {
            private byte _element;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Packed
        {
            public byte Prefix;
            public PgAtomicValue<long> Cell;
        }

        public static partial class SharedFunctions
        {
            private static readonly PgShared<SharedState> State = new("ankus_shared_probe.state");
            private static readonly PgShared<Bytes> Data = new("ankus_shared_probe.bytes");
            private static readonly PgShared<long> Order = new("ankus_shared_probe.order");
            private static readonly PgShared<Packed> Unaligned = new("ankus_shared_probe.packed");
            private static int s_initializations;
            private static Timer? s_worker;

            [PgGucBool("ankus_shared_probe.fail_startup", false, "Fail initialization")]
            public static partial bool FailStartup { get; }

            [PgGucBool("ankus_shared_probe.conflict", false, "Conflicting storage kind")]
            public static partial bool Conflict { get; }

            [PgModuleLoad]
            public static void Register()
            {
                PgSharedMemory.Initialize(State, () => FailStartup
                    ? throw new PgException("P7821", "Shared initializer deliberately failed.")
                    : new SharedState(++s_initializations));
                PgSharedMemory.Initialize(Data, () =>
                {
                    Bytes bytes = default;
                    bytes[0] = 129;
                    bytes[2] = 243;
                    bytes[4] = 127;
                    return bytes;
                });
                PgSharedMemory.Initialize(Order, () =>
                {
                    StartWorker();
                    return Task.Run(() => State.Read(static (in SharedState value) => value.Count.Value + 1)).GetAwaiter().GetResult();
                });
                PgSharedMemory.Initialize(Unaligned);
                PgSharedMemory.Initialize(State, () => throw new InvalidOperationException("Replaced initializer."));
                if (Conflict)
                {
                    PgSharedMemory.Initialize(new PgAtomic<long>(State.Name));
                }
            }

            [PgFunction]
            public static string SharedSnapshot() => Task.Run(() => State.Read(static (in SharedState value) => string.Create(CultureInfo.InvariantCulture,
                $"{value.Count.Value}|{value.Epoch}|{value.Identity:D}|{value.Bound}|{value.Ratio}|{Data.Read(static (in Bytes bytes) => string.Join(',', ((ReadOnlySpan<byte>)bytes).ToArray()))}|{Order.Read(static (in long order) => order)}"))).GetAwaiter().GetResult();

            [PgFunction]
            public static long SharedCount() => State.Read(static (in SharedState value) => value.Count.Value);

            [PgFunction]
            public static long SharedCompare(long replacement, long comparand) => State.Read((in SharedState value) => value.Count.CompareExchange(replacement, comparand));

            [PgFunction]
            public static string SharedWorkerSnapshot() => State.Read(static (in SharedState value) => string.Create(CultureInfo.InvariantCulture, $"{value.Pulse.Value > 0}|{value.WorkerEpoch.Value}"));

            [PgFunction]
            public static void SharedStopWorker() => State.Read(static (in SharedState value) => value.Stop.Exchange(true));

            [PgFunction]
            public static long SharedUnaligned() => Unaligned.Read(static (in Packed value) => value.Cell.Value);

            [PgFunction]
            public static string SharedCells() => State.Read(static (in SharedState value) => string.Create(CultureInfo.InvariantCulture,
                $"{value.Flag.And(false)}|{value.Flag.Value}|{value.Tiny.Value}|{value.Tiny.Increment()}|{BitConverter.DoubleToInt64Bits(value.Bits.CompareExchange(19, BitConverter.Int64BitsToDouble(0x7FF8000000000074)))}|{BitConverter.DoubleToInt64Bits(value.Bits.Exchange(-0d))}|{BitConverter.DoubleToInt64Bits(value.Bits.CompareExchange(23, +0d))}"));

            [PgFunction]
            public static string SharedCellSnapshot() => State.Read(static (in SharedState value) => string.Create(CultureInfo.InvariantCulture,
                $"{value.Flag.Value}|{value.Tiny.Value}|{BitConverter.DoubleToInt64Bits(value.Bits.Value)}"));

            [PgFunction]
            public static long SharedSleep() => State.Read(static (in SharedState value) => Spi.Execute("SELECT pg_catalog.pg_sleep(60)"));

            [PgFunction]
            public static void SharedFail(bool native, long replacement) => State.Read<int>((in SharedState value) =>
            {
                value.Count.Exchange(replacement);
                if (native)
                {
                    Spi.Execute("SELECT 1/0");
                }

                throw new PgException("P7820", "Shared callback deliberately failed.");
            });

            [PgFunction]
            public static void SharedWork(int iterations, bool synchronize)
            {
                if (synchronize)
                {
                    State.Read(static (in SharedState value) => value.Gate.Increment());
                    Stopwatch elapsed = Stopwatch.StartNew();
                    while (State.Read(static (in SharedState value) => value.Gate.Value) < 2)
                    {
                        if (elapsed.Elapsed > TimeSpan.FromSeconds(15))
                        {
                            throw new TimeoutException("Independent backend did not enter the shared work barrier.");
                        }

                        Thread.Yield();
                    }
                }

                ConcurrentQueue<Exception> failures = new();
                Thread[] threads = new Thread[4];
                for (int index = 0; index < threads.Length; index++)
                {
                    threads[index] = new Thread(() =>
                    {
                        try
                        {
                            State.Read((in SharedState value) =>
                            {
                                for (int iteration = 0; iteration < iterations; iteration++)
                                {
                                    value.Count.Increment();
                                }

                                return value.Count.Value;
                            });
                        }
                        catch (Exception exception)
                        {
                            failures.Enqueue(exception);
                        }
                    });
                    threads[index].Start();
                }

                foreach (Thread thread in threads)
                {
                    thread.Join();
                }

                if (!failures.IsEmpty)
                {
                    throw new AggregateException(failures);
                }
            }

            private static void StartWorker()
            {
                if (s_worker is null)
                {
                    int epoch = State.Read(static (in SharedState value) => value.Epoch);
                    int owner = Environment.ProcessId;
                    s_worker = new Timer(_ =>
                    {
                        if (Environment.ProcessId != owner)
                        {
                            s_worker?.Change(Timeout.Infinite, Timeout.Infinite);
                            return;
                        }

                        try
                        {
                            State.Read((in SharedState value) =>
                            {
                                if (value.Stop.Value)
                                {
                                    s_worker?.Change(Timeout.Infinite, Timeout.Infinite);
                                    return false;
                                }

                                value.Pulse.Increment();
                                value.WorkerEpoch.Exchange(epoch);
                                return true;
                            });
                        }
                        catch (InvalidOperationException)
                        {
                            // Retirement closes admission until replacement storage is published.
                        }
                    }, null, 0, 1);
                }

                Stopwatch elapsed = Stopwatch.StartNew();
                while (!State.Read(static (in SharedState value) => value.Pulse.Value > 0 && value.WorkerEpoch.Value > 0))
                {
                    if (elapsed.Elapsed > TimeSpan.FromSeconds(15))
                    {
                        throw new TimeoutException("The original postmaster callback did not attach to shared storage.");
                    }

                    Thread.Yield();
                }
            }
        }
        """;
}
