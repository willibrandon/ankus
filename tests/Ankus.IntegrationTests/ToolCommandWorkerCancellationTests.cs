using System.Diagnostics;
using System.Globalization;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class ToolCommandTests
{
    /// <summary>
    /// A canceled worker transaction rolls back before recovery while a swallowed terminal report still ends the worker.
    /// </summary>
    /// <param name="mode">Zero propagates cancellation, one swallows it, two swallows FATAL and four cancels a transaction's latch wait.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(4)]
    public async Task WorkerTransactionsRecoverCancellationButRetainTerminalReports(int mode)
    {
        CancellationToken token = context.CancellationToken;
        string output = await PublishPackageConsumerAsync("WorkerCancellation", "ankus_worker_cancellation", WorkerCancellationSource, token);
        await using PostgresTestCluster cluster = await StartPublishedClusterAsync(output, token, sharedPreload: true,
            additionalConfiguration: ["max_worker_processes = 4", "max_parallel_workers = 0", "max_logical_replication_workers = 0", "log_error_verbosity = verbose"]);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        await ExecutePackageGucAsync(connection, "CREATE EXTENSION ankus_worker_cancellation; CREATE TABLE worker_cancel_values(value integer)");
        int process = Assert.IsInstanceOfType<int>(await PackageGucScalarAsync(connection,
            "SELECT cancellation_start(" + mode.ToString(CultureInfo.InvariantCulture) + ")"));
        Assert.IsGreaterThan(0, process);
        Assert.AreNotEqual(connection.ProcessID, process);
        string pid = process.ToString(CultureInfo.InvariantCulture);
        try
        {
            await WaitForCancellationWorkerAsync(connection, cluster, process,
                "cancellation_pid() = " + pid, requireAlive: false);
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(connection,
                "SELECT EXISTS(SELECT FROM pg_stat_activity WHERE pid = " + pid + ")")));
            await ExecutePackageGucAsync(connection, "SELECT cancellation_release()");
            if (mode == 2)
            {
                await WaitForCancellationWorkerAsync(connection, cluster, process,
                    "NOT EXISTS(SELECT FROM pg_stat_activity WHERE pid = " + pid + ")", requireAlive: false);
                Assert.AreEqual(0L, await PackageGucScalarAsync(connection, "SELECT count(*) FROM worker_cancel_values"));
                string log = cluster.ReadServerLog();
                Assert.Contains("FATAL:  P7861: owned worker transaction fatal", log);
                Assert.Contains("owned worker fatal detail", log);
                Assert.DoesNotContain("worker cancellation recovered", log);
            }
            else
            {
                await WaitForCancellationWorkerAsync(connection, cluster, process,
                    "EXISTS(SELECT FROM pg_stat_activity WHERE pid = " + pid + " AND wait_event = '" +
                    (mode == 4 ? "Extension" : "PgSleep") + "' AND xact_start IS NOT NULL)");
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(connection, "SELECT pg_cancel_backend(" + pid + ")")));
                await WaitForCancellationWorkerAsync(connection, cluster, process, "(cancellation_status())[4] = 1");
                int[] status = Assert.IsInstanceOfType<int[]>(await PackageGucScalarAsync(connection, "SELECT cancellation_status()"));
                Assert.AreSequenceEqual([mode == 0 ? 0 : 1, 1, 1, 1], status);
                Assert.AreSequenceEqual([2], Assert.IsInstanceOfType<int[]>(await PackageGucScalarAsync(connection,
                    "SELECT array_agg(value ORDER BY value) FROM worker_cancel_values")));
                Assert.AreEqual(process, await PackageGucScalarAsync(connection, "SELECT cancellation_pid()"));
                Assert.Contains("worker cancellation recovered", cluster.ReadServerLog());
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(connection, "SELECT pg_reload_conf()")));
                await WaitForCancellationWorkerAsync(connection, cluster, process, "cancellation_reloaded() = 1");
                Assert.AreEqual(process, await PackageGucScalarAsync(connection, "SELECT cancellation_pid()"));
            }
        }
        finally
        {
            bool alive = Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(connection,
                "SELECT EXISTS(SELECT FROM pg_stat_activity WHERE pid = " + pid + ")"));
            if (alive)
            {
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(connection, "SELECT pg_terminate_backend(" + pid + ")")));
            }

            await WaitForCancellationWorkerAsync(connection, cluster, process,
                "NOT EXISTS(SELECT FROM pg_stat_activity WHERE pid = " + pid + ")", requireAlive: false);
        }

        Assert.AreEqual(42, await PackageGucScalarAsync(connection, "SELECT 42"));
    }

    /// <summary>
    /// Cancellation during an idle latch wait leaves the same worker able to wait, log and commit again.
    /// </summary>
    [TestMethod]
    public async Task WorkerIdleWaitRecoversRepeatedCancellation()
    {
        CancellationToken token = context.CancellationToken;
        string output = await PublishPackageConsumerAsync("WorkerCancellation", "ankus_worker_cancellation", WorkerCancellationSource, token);
        await using PostgresTestCluster cluster = await StartPublishedClusterAsync(output, token, sharedPreload: true,
            additionalConfiguration: ["max_worker_processes = 4", "max_parallel_workers = 0", "max_logical_replication_workers = 0", "log_error_verbosity = verbose"]);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        await ExecutePackageGucAsync(connection, "CREATE EXTENSION ankus_worker_cancellation; CREATE TABLE worker_cancel_values(value integer)");
        int process = Assert.IsInstanceOfType<int>(await PackageGucScalarAsync(connection, "SELECT cancellation_start(3)"));
        Assert.IsGreaterThan(0, process);
        Assert.AreNotEqual(connection.ProcessID, process);
        string pid = process.ToString(CultureInfo.InvariantCulture);
        try
        {
            await WaitForCancellationWorkerAsync(connection, cluster, process, "cancellation_pid() = " + pid, requireAlive: false);
            await ExecutePackageGucAsync(connection, "SELECT cancellation_release()");
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                string count = attempt.ToString(CultureInfo.InvariantCulture);
                await WaitForCancellationWorkerAsync(connection, cluster, process,
                    "cancellation_waiting() = " + count + " AND EXISTS(SELECT FROM pg_stat_activity WHERE pid = " + pid +
                    " AND wait_event = 'Extension' AND xact_start IS NULL)");
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(connection, "SELECT pg_cancel_backend(" + pid + ")")));
                await WaitForCancellationWorkerAsync(connection, cluster, process, "(cancellation_status())[4] = " + count);
                Assert.AreSequenceEqual([0, attempt, attempt, attempt],
                    Assert.IsInstanceOfType<int[]>(await PackageGucScalarAsync(connection, "SELECT cancellation_status()")));
                Assert.AreEqual((long)attempt, await PackageGucScalarAsync(connection, "SELECT count(*) FROM worker_cancel_values"));
                Assert.AreEqual(process, await PackageGucScalarAsync(connection, "SELECT cancellation_pid()"));
            }

            Assert.AreSequenceEqual([1, 2], Assert.IsInstanceOfType<int[]>(await PackageGucScalarAsync(connection,
                "SELECT array_agg(value ORDER BY value) FROM worker_cancel_values")));
            Assert.Contains("worker idle cancellation recovered", cluster.ReadServerLog());
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(connection, "SELECT pg_reload_conf()")));
            await WaitForCancellationWorkerAsync(connection, cluster, process, "cancellation_reloaded() = 1");
        }
        finally
        {
            bool alive = Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(connection,
                "SELECT EXISTS(SELECT FROM pg_stat_activity WHERE pid = " + pid + ")"));
            if (alive)
            {
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(connection, "SELECT pg_terminate_backend(" + pid + ")")));
            }

            await WaitForCancellationWorkerAsync(connection, cluster, process,
                "NOT EXISTS(SELECT FROM pg_stat_activity WHERE pid = " + pid + ")", requireAlive: false);
        }

        Assert.AreEqual(42, await PackageGucScalarAsync(connection, "SELECT 42"));
    }

    /// <summary>
    /// Observes actual worker activity and fails promptly if the worker exits before its requested state.
    /// </summary>
    /// <param name="connection">The independent controlling backend.</param>
    /// <param name="cluster">The owning server and diagnostic log.</param>
    /// <param name="process">The worker process under observation.</param>
    /// <param name="condition">The fixed test predicate to observe through SQL.</param>
    /// <param name="requireAlive">Whether an early worker exit contradicts the expected state.</param>
    private async Task WaitForCancellationWorkerAsync(NpgsqlConnection connection, PostgresTestCluster cluster, int process,
        string condition, bool requireAlive = true)
    {
        var elapsed = Stopwatch.StartNew();
        while (!Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(connection, "SELECT " + condition)))
        {
            Assert.IsLessThan(TimeSpan.FromSeconds(30), elapsed.Elapsed, cluster.ReadServerLog());
            if (requireAlive)
            {
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(connection,
                    "SELECT EXISTS(SELECT FROM pg_stat_activity WHERE pid = " + process.ToString(CultureInfo.InvariantCulture) + ")")),
                    cluster.ReadServerLog());
            }

            await Task.Delay(25, context.CancellationToken);
        }
    }

    /// <summary>
    /// Exercises a real worker transaction cancellation without replacing PostgreSQL's SIGINT cancellation handler.
    /// </summary>
    private const string WorkerCancellationSource = """
        using Ankus;

        public static class CancellationWorker
        {
            private static readonly PgAtomic<int> Process = new("ankus_worker_cancel.process");
            private static readonly PgAtomic<int> Gate = new("ankus_worker_cancel.gate");
            private static readonly PgAtomic<int> Swallowed = new("ankus_worker_cancel.swallowed");
            private static readonly PgAtomic<int> Canceled = new("ankus_worker_cancel.canceled");
            private static readonly PgAtomic<int> Cleanup = new("ankus_worker_cancel.cleanup");
            private static readonly PgAtomic<int> Complete = new("ankus_worker_cancel.complete");
            private static readonly PgAtomic<int> Reloaded = new("ankus_worker_cancel.reloaded");
            private static readonly PgAtomic<int> Waiting = new("ankus_worker_cancel.waiting");

            [PgModuleLoad]
            public static void Load()
            {
                PgSharedMemory.Initialize(Process);
                PgSharedMemory.Initialize(Gate);
                PgSharedMemory.Initialize(Swallowed);
                PgSharedMemory.Initialize(Canceled);
                PgSharedMemory.Initialize(Cleanup);
                PgSharedMemory.Initialize(Complete);
                PgSharedMemory.Initialize(Reloaded);
                PgSharedMemory.Initialize(Waiting);
            }

            [PgFunction(Name = "cancellation_start")]
            public static int Start(int mode)
            {
                string database = Spi.ExecuteScalar<string>("SELECT current_database()::text");
                if (!PgBackgroundWorker.TryStart(new("Ankus cancellation worker", "WorkerCancellation", nameof(Run))
                    { DatabaseAccess = true, Extra = database, Argument = (nuint)mode, NotifyProcessId = Environment.ProcessId },
                    out PgBackgroundWorkerHandle? worker))
                {
                    throw new InvalidOperationException("No cancellation worker slot was available.");
                }

                using (worker)
                {
                    PgBackgroundWorkerState state = worker.WaitForStartup();
                    return state.Status == PgBackgroundWorkerStatus.Started && state.ProcessId is int process
                        ? process : throw new InvalidOperationException("The cancellation worker failed to start.");
                }
            }

            [PgFunction(Name = "cancellation_release")]
            public static void Release() => Gate.Exchange(1);

            [PgFunction(Name = "cancellation_status")]
            public static int[] Status() => [Swallowed.Value, Canceled.Value, Cleanup.Value, Complete.Value];

            [PgFunction(Name = "cancellation_pid")]
            public static int Pid() => Process.Value;

            [PgFunction(Name = "cancellation_reloaded")]
            public static int ReloadCount() => Reloaded.Value;

            [PgFunction(Name = "cancellation_waiting")]
            public static int WaitingAttempt() => Waiting.Value;

            [PgBackgroundWorker]
            public static void Run(nuint mode)
            {
                PgBackgroundWorker.Connect(PgBackgroundWorker.Extra);
                Process.Exchange(Environment.ProcessId);
                while (Gate.Value == 0 && PgBackgroundWorker.Wait(TimeSpan.FromMilliseconds(10)))
                {
                }

                if (mode == 3)
                {
                    RunIdleCancellation();
                    return;
                }

                int swallowed = 0;
                int canceled = 0;
                int cleanup = 0;
                try
                {
                    PgBackgroundWorker.RunTransaction(() =>
                    {
                        Spi.Execute("INSERT INTO worker_cancel_values VALUES (1)");
                        try
                        {
                            if (mode == 2)
                            {
                                PgLog.Write(PgLogLevel.Fatal, new PgDiagnostic("owned worker transaction fatal")
                                    { SqlState = "P7861", Detail = "owned worker fatal detail" });
                            }
                            else if (mode == 4)
                            {
                                PgBackgroundWorker.Wait();
                            }
                            else
                            {
                                Spi.Execute("SELECT pg_sleep(60)");
                            }
                        }
                        catch (Exception) when (mode != 0)
                        {
                            swallowed++;
                        }
                        finally
                        {
                            cleanup++;
                        }
                    });
                }
                catch (PgQueryCanceledException exception) when (exception.Diagnostic.SqlState == "57014" &&
                    exception.Diagnostic.Message == "canceling statement due to user request")
                {
                    canceled++;
                }

                PgInterrupts.Check();
                if (!PgBackgroundWorker.CanContinue || !PgBackgroundWorker.Wait(TimeSpan.Zero))
                {
                    throw new InvalidOperationException("The canceled worker lost its postmaster.");
                }

                PgLog.Write(PgLogLevel.Notice, "worker cancellation recovered");
                PgBackgroundWorker.RunTransaction(() =>
                {
                    Spi.Execute("INSERT INTO worker_cancel_values VALUES (2)");
                    if (Spi.ExecuteScalar<long>("SELECT count(*) FROM worker_cancel_values") != 1)
                    {
                        throw new InvalidOperationException("The canceled transaction committed its row.");
                    }
                });
                Swallowed.Exchange(swallowed);
                Canceled.Exchange(canceled);
                Cleanup.Exchange(cleanup);
                Complete.Exchange(1);
                while (PgBackgroundWorker.Wait())
                {
                    if ((PgBackgroundWorker.ConsumeSignals() & PgBackgroundWorkerSignals.Reload) != 0)
                    {
                        PgBackgroundWorker.ReloadConfiguration();
                        Reloaded.Exchange(1);
                    }
                }
            }

            private static void RunIdleCancellation()
            {
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    int canceled = 0;
                    int cleanup = 0;
                    try
                    {
                        Waiting.Exchange(attempt);
                        PgBackgroundWorker.Wait();
                    }
                    catch (PgQueryCanceledException exception) when (exception.Diagnostic.SqlState == "57014" &&
                        exception.Diagnostic.Message == "canceling statement due to user request")
                    {
                        canceled = 1;
                    }
                    finally
                    {
                        cleanup = 1;
                    }

                    if (canceled != 1 || cleanup != 1)
                    {
                        throw new InvalidOperationException("The idle wait did not receive cancellation.");
                    }

                    PgInterrupts.Check();
                    if (!PgBackgroundWorker.CanContinue || !PgBackgroundWorker.Wait(TimeSpan.Zero))
                    {
                        throw new InvalidOperationException("The idle canceled worker lost its postmaster.");
                    }

                    PgLog.Notice("worker idle cancellation recovered");
                    int value = attempt;
                    PgBackgroundWorker.RunTransaction(() => Spi.Execute("INSERT INTO worker_cancel_values VALUES ($1)", SpiParameter.Create(value)));
                    Canceled.Exchange(attempt);
                    Cleanup.Exchange(attempt);
                    Complete.Exchange(attempt);
                }

                while (PgBackgroundWorker.Wait())
                {
                    if ((PgBackgroundWorker.ConsumeSignals() & PgBackgroundWorkerSignals.Reload) != 0)
                    {
                        PgBackgroundWorker.ReloadConfiguration();
                        Reloaded.Exchange(1);
                    }
                }
            }
        }
        """;
}
