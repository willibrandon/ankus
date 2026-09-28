using System.Diagnostics;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class ToolCommandTests
{
    /// <summary>
    /// Published scalar atomics retain values across native processes, managed threads, errors and replacement segments.
    /// </summary>
    [TestMethod]
    public async Task SharedAtomicsPreserveValuesAcrossProcessesAndThreads()
    {
        CancellationToken token = context.CancellationToken;
        string output = await PublishPackageConsumerAsync("SharedAtomics", "ankus_atomic_probe", SharedAtomicSource, token);
        await using (PostgresTestCluster ordinary = await StartPublishedClusterAsync(output, token))
        {
            await using NpgsqlConnection connection = await ordinary.OpenConnectionAsync(token);
            PostgresException late = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
                ExecutePackageGucAsync(connection, "LOAD 'SharedAtomics'"));
            Assert.AreEqual("55000", late.SqlState);
            Assert.Contains("shared_preload_libraries", late.MessageText);
            Assert.AreEqual(42, await PackageGucScalarAsync(connection, "SELECT 42"));
        }

        foreach ((string setting, string message) in new (string, string)[]
        {
            ("ankus_atomic_probe.fail_startup = on", "Atomic initializer deliberately failed."),
            ("ankus_atomic_probe.conflict = on", "is already registered"),
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
        await ExecutePackageGucAsync(first, "CREATE EXTENSION ankus_atomic_probe");
        // The first operation in each backend runs on a managed worker, with no lazy backend-thread access.
        Assert.AreEqual("73|74|1", await PackageGucScalarAsync(first, "SELECT atomic_snapshot()"));
        Assert.AreEqual("73|74|1", await PackageGucScalarAsync(second, "SELECT atomic_snapshot()"));
        Assert.AreEqual("True|1", await PackageGucScalarAsync(first, "SELECT atomic_worker_snapshot()"));
        Assert.AreEqual("True|False|255|0|-32768|32767|4294967295|0|18446744073709551615|0|-1|0|65535|4660|18446744073709551615|7",
            await PackageGucScalarAsync(first, "SELECT atomic_scalars()"));
        Assert.AreEqual("False|0|32767|0|0|0|4660|7", await PackageGucScalarAsync(second, "SELECT atomic_scalar_snapshot()"));
        Assert.AreEqual("9221120237041090675|9221120237041090675|-9223372036854775808|2143289459|-2147483648",
            await PackageGucScalarAsync(first, "SELECT atomic_floating()"));

        await Task.WhenAll(ExecutePackageGucAsync(first, "SELECT atomic_work(1000, true)"),
            ExecutePackageGucAsync(second, "SELECT atomic_work(1000, true)"));
        Assert.AreEqual("8073|74|1", await PackageGucScalarAsync(first, "SELECT atomic_snapshot()"));
        Assert.AreEqual(8073L, await PackageGucScalarAsync(second, "SELECT atomic_compare(91, 8072)"));
        Assert.AreEqual("8073|74|1", await PackageGucScalarAsync(first, "SELECT atomic_snapshot()"));
        Assert.AreEqual(8073L, await PackageGucScalarAsync(second, "SELECT atomic_compare(91, 8073)"));
        Assert.AreEqual("91|74|1", await PackageGucScalarAsync(first, "SELECT atomic_snapshot()"));

        foreach ((bool native, string state, long value) in new (bool, string, long)[] { (false, "P7810", 97), (true, "22012", 101) })
        {
            PostgresException failure = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
                ExecutePackageGucAsync(first, $"SELECT atomic_fail({native}, {value})"));
            Assert.AreEqual(state, failure.SqlState);
            Assert.AreEqual($"{value}|74|1", await PackageGucScalarAsync(second, "SELECT atomic_snapshot()"));
            Assert.AreEqual(value, await PackageGucScalarAsync(first, $"SELECT atomic_compare({value + 1}, {value})"));
        }

        await ExecutePackageGucAsync(first, "BEGIN; SELECT atomic_compare(107, 102); ROLLBACK");
        Assert.AreEqual("107|74|1", await PackageGucScalarAsync(second, "SELECT atomic_snapshot()"));
        int logLength = cluster.ReadServerLog().Length;
        using (Process backend = Process.GetProcessById(first.ProcessID))
        {
            backend.Kill();
            await backend.WaitForExitAsync(token);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!cluster.ReadServerLog()[logLength..].Contains("database system is ready to accept connections", StringComparison.Ordinal))
        {
            await Task.Delay(10, timeout.Token);
        }

        await using NpgsqlConnection recovered = await cluster.OpenConnectionAsync(timeout.Token);
        Assert.AreEqual("73|74|2", await PackageGucScalarAsync(recovered, "SELECT atomic_snapshot()"));
        Assert.AreEqual("True|1", await PackageGucScalarAsync(recovered, "SELECT atomic_worker_snapshot()"),
            "The original postmaster worker must update the replacement segment without being recreated.");
        await ExecutePackageGucAsync(recovered, "SELECT atomic_work(10, false)");
        Assert.AreEqual("113|74|2", await PackageGucScalarAsync(recovered, "SELECT atomic_snapshot()"));
        Assert.AreEqual(42, await PackageGucScalarAsync(recovered, "SELECT 42"));
        await ExecutePackageGucAsync(recovered, "SELECT atomic_stop_worker()");
    }

    /// <summary>
    /// Exercises Native AOT scalar intrinsics and early worker access in a packaged consumer.
    /// </summary>
    private const string SharedAtomicSource = """
        using Ankus;
        using System.Collections.Concurrent;
        using System.Diagnostics;
        using System.Globalization;

        public enum Marker : ulong
        {
            Zero,
            Seven = 7,
            High = ulong.MaxValue,
        }

        public static partial class AtomicFunctions
        {
            private static readonly PgAtomic<long> Count = new("ankus_atomic_probe.count");
            private static readonly PgAtomic<long> Order = new("ankus_atomic_probe.order");
            private static readonly PgAtomic<int> Epoch = new("ankus_atomic_probe.epoch");
            private static readonly PgAtomic<int> Gate = new("ankus_atomic_probe.gate");
            private static readonly PgAtomic<long> Pulse = new("ankus_atomic_probe.pulse");
            private static readonly PgAtomic<int> WorkerEpoch = new("ankus_atomic_probe.worker_epoch");
            private static readonly PgAtomic<bool> Stop = new("ankus_atomic_probe.stop");
            private static readonly PgAtomic<bool> Flag = new("ankus_atomic_probe.flag");
            private static readonly PgAtomic<byte> Tiny = new("ankus_atomic_probe.tiny");
            private static readonly PgAtomic<short> Small = new("ankus_atomic_probe.small");
            private static readonly PgAtomic<uint> Medium = new("ankus_atomic_probe.medium");
            private static readonly PgAtomic<ulong> Wide = new("ankus_atomic_probe.wide");
            private static readonly PgAtomic<nint> Native = new("ankus_atomic_probe.native");
            private static readonly PgAtomic<char> Character = new("ankus_atomic_probe.character");
            private static readonly PgAtomic<Marker> Tag = new("ankus_atomic_probe.tag");
            private static readonly PgAtomic<double> DoubleValue = new("ankus_atomic_probe.double");
            private static readonly PgAtomic<float> SingleValue = new("ankus_atomic_probe.single");
            private static int s_initializations;
            private static Timer? s_worker;

            [PgGucBool("ankus_atomic_probe.fail_startup", false, "Fail initialization")]
            public static partial bool FailStartup { get; }

            [PgGucBool("ankus_atomic_probe.conflict", false, "Conflicting storage kind")]
            public static partial bool Conflict { get; }

            [PgModuleLoad]
            public static void Register()
            {
                PgSharedMemory.Initialize(Epoch, () => ++s_initializations);
                PgSharedMemory.Initialize(Count, () => FailStartup
                    ? throw new PgException("P7811", "Atomic initializer deliberately failed.", "Owned atomic detail.") : 73);
                PgSharedMemory.Initialize(Pulse);
                PgSharedMemory.Initialize(WorkerEpoch);
                PgSharedMemory.Initialize(Stop);
                PgSharedMemory.Initialize(Order, () =>
                {
                    StartWorker();
                    return Task.Run(() => Count.Value + 1).GetAwaiter().GetResult();
                });
                PgSharedMemory.Initialize(Gate);
                PgSharedMemory.Initialize(Flag, () => true);
                PgSharedMemory.Initialize(Tiny, () => byte.MaxValue);
                PgSharedMemory.Initialize(Small, () => short.MinValue);
                PgSharedMemory.Initialize(Medium, () => uint.MaxValue);
                PgSharedMemory.Initialize(Wide, () => ulong.MaxValue);
                PgSharedMemory.Initialize(Native, () => -1);
                PgSharedMemory.Initialize(Character, () => '\uffff');
                PgSharedMemory.Initialize(Tag, () => Marker.High);
                PgSharedMemory.Initialize(DoubleValue, () => BitConverter.Int64BitsToDouble(0x7FF8000000000073));
                PgSharedMemory.Initialize(SingleValue, () => BitConverter.Int32BitsToSingle(0x7FC00073));
                PgSharedMemory.Initialize(Count, () => throw new InvalidOperationException("Duplicate factory ran."));
                if (Conflict)
                {
                    PgSharedMemory.Initialize(new PgLwLock<long>(Count.Name));
                }
            }

            [PgFunction]
            public static string AtomicSnapshot() => Task.Run(() =>
                string.Create(CultureInfo.InvariantCulture, $"{Count.Value}|{Order.Value}|{Epoch.Value}")).GetAwaiter().GetResult();

            [PgFunction]
            public static string AtomicWorkerSnapshot() => string.Create(CultureInfo.InvariantCulture,
                $"{Pulse.Value > 0}|{WorkerEpoch.Value}");

            [PgFunction]
            public static void AtomicStopWorker() => Stop.Value = true;

            private static void StartWorker()
            {
                if (s_worker is null)
                {
                    int epoch = Epoch.Value;
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
                            if (Stop.Value)
                            {
                                s_worker?.Change(Timeout.Infinite, Timeout.Infinite);
                                return;
                            }

                            Pulse.Increment();
                            WorkerEpoch.Value = epoch;
                        }
                        catch (InvalidOperationException)
                        {
                            // Retirement closes admission until the replacement segment is published.
                        }
                    }, null, 0, 1);
                }

                Stopwatch elapsed = Stopwatch.StartNew();
                while (Pulse.Value == 0 || WorkerEpoch.Value == 0)
                {
                    if (elapsed.Elapsed > TimeSpan.FromSeconds(15))
                    {
                        throw new TimeoutException("The original postmaster worker did not attach to shared storage.");
                    }

                    Thread.Yield();
                }
            }

            [PgFunction]
            public static string AtomicScalars() => string.Create(CultureInfo.InvariantCulture,
                $"{Flag.And(false)}|{Flag.Value}|{Tiny.Value}|{Tiny.Increment()}|{Small.Value}|{Small.Decrement()}|{Medium.Value}|{Medium.Increment()}|{Wide.Value}|{Wide.Increment()}|{Native.Value}|{Native.Increment()}|{(int)Character.Exchange('\u1234')}|{(int)Character.Value}|{(ulong)Tag.CompareExchange(Marker.Seven, Marker.High)}|{(ulong)Tag.Value}");

            [PgFunction]
            public static string AtomicScalarSnapshot() => string.Create(CultureInfo.InvariantCulture,
                $"{Flag.Value}|{Tiny.Value}|{Small.Value}|{Medium.Value}|{Wide.Value}|{Native.Value}|{(int)Character.Value}|{(ulong)Tag.Value}");

            [PgFunction]
            public static string AtomicFloating() => string.Create(CultureInfo.InvariantCulture,
                $"{BitConverter.DoubleToInt64Bits(DoubleValue.CompareExchange(19, BitConverter.Int64BitsToDouble(0x7FF8000000000074)))}|{BitConverter.DoubleToInt64Bits(DoubleValue.CompareExchange(-0d, BitConverter.Int64BitsToDouble(0x7FF8000000000073)))}|{BitConverter.DoubleToInt64Bits(DoubleValue.CompareExchange(23, +0d))}|{BitConverter.SingleToInt32Bits(SingleValue.Exchange(-0f))}|{BitConverter.SingleToInt32Bits(SingleValue.CompareExchange(29, +0f))}");

            [PgFunction]
            public static long AtomicCompare(long value, long comparand) => Count.CompareExchange(value, comparand);

            [PgFunction]
            public static void AtomicFail(bool native, long value)
            {
                Count.Value = value;
                if (native)
                {
                    Spi.Execute("SELECT 1/0");
                }

                throw new PgException("P7810", "Atomic write preceded managed failure.");
            }

            [PgFunction]
            public static void AtomicWork(int iterations, bool synchronize)
            {
                if (synchronize)
                {
                    Gate.Increment();
                    Stopwatch elapsed = Stopwatch.StartNew();
                    while (Gate.Value < 2)
                    {
                        if (elapsed.Elapsed > TimeSpan.FromSeconds(15))
                        {
                            throw new TimeoutException("The other backend did not reach the atomic gate.");
                        }

                        Thread.Yield();
                    }
                }

                var failures = new ConcurrentQueue<Exception>();
                Thread[] workers = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
                {
                    try
                    {
                        for (int index = 0; index < iterations; index++)
                        {
                            Count.Increment();
                        }
                    }
                    catch (Exception exception)
                    {
                        failures.Enqueue(exception);
                    }
                })).ToArray();
                foreach (Thread worker in workers)
                {
                    worker.Start();
                }

                foreach (Thread worker in workers)
                {
                    worker.Join();
                }

                if (!failures.IsEmpty)
                {
                    throw new AggregateException(failures);
                }
            }
        }
        """;
}
