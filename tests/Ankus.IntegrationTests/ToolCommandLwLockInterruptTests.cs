using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class ToolCommandTests
{
    /// <summary>
    /// Idle checks succeed under ordinary lightweight-lock guards and pending cancellation waits for release.
    /// </summary>
    /// <param name="exclusive">Whether the caller holds an exclusive rather than shared guard.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SharedLocksDeferPendingInterruptsUntilRelease(bool exclusive)
    {
        CancellationToken token = context.CancellationToken;
        string output = await PublishPackageConsumerAsync("LwLockInterrupts", "ankus_lwlock_interrupts", LwLockInterruptSource, token);
        await using PostgresTestCluster cluster = await StartPublishedClusterAsync(output, token, sharedPreload: true);
        await using NpgsqlConnection caller = await cluster.OpenConnectionAsync(token);
        await using NpgsqlConnection observer = await cluster.OpenConnectionAsync(token);
        int process = caller.ProcessID;
        Assert.AreNotEqual(process, observer.ProcessID);
        await ExecutePackageGucAsync(caller, "CREATE EXTENSION ankus_lwlock_interrupts");
        string mode = exclusive ? "true" : "false";
        for (int iteration = 0; iteration < 4; iteration++)
        {
            Assert.AreEqual(2, await PackageGucScalarAsync(caller, "SELECT locked_poll(" + mode + ", false)"));
            Assert.AreEqual(0, await PackageGucScalarAsync(caller, "SELECT locked_holdoff()"));
            PostgresException cancelled = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
                PackageGucScalarAsync(caller, "SELECT locked_poll(" + mode + ", true)"));
            Assert.AreEqual("57014", cancelled.SqlState);
            Assert.AreEqual("canceling statement due to user request", cancelled.MessageText);
            Assert.AreEqual(2, await PackageGucScalarAsync(observer, "SELECT locked_stage()"),
                "Both checks must return while the guard still holds PostgreSQL interrupts.");
            Assert.AreEqual(31, await PackageGucScalarAsync(observer, "SELECT locked_value()"));
            Assert.AreEqual(0, await PackageGucScalarAsync(caller, "SELECT locked_holdoff()"));
            Assert.AreEqual(process, caller.ProcessID);
            Assert.AreEqual(42, await PackageGucScalarAsync(caller, "SELECT 42"));
            Assert.AreEqual(2, await PackageGucScalarAsync(observer, "SELECT locked_poll(" + mode + ", false)"));
            Assert.AreEqual(0, await PackageGucScalarAsync(observer, "SELECT locked_holdoff()"));
        }
    }

    /// <summary>
    /// Uses actual backend flags and shared progress to distinguish held-lock deferral from prematurely thrown errors.
    /// </summary>
    private const string LwLockInterruptSource = """
        using Ankus;
        using Ankus.Postgres;

        public static class LockedInterrupts
        {
            private static readonly PgLwLock<int> Value = new("ankus_lwlock_interrupts.value");
            private static readonly PgAtomic<int> Stage = new("ankus_lwlock_interrupts.stage");

            [PgModuleLoad]
            public static void Register()
            {
                PgSharedMemory.Initialize(Value, static () => 31);
                PgSharedMemory.Initialize(Stage);
            }

            [PgFunction]
            public static int LockedPoll(bool exclusive, bool cancel)
            {
                Stage.Value = 0;
                if (exclusive)
                {
                    using (PgLwLockExclusiveGuard<int> guard = Value.Exclusive())
                    {
                        Poll(cancel);
                        if (guard.Value != 31)
                        {
                            throw new InvalidOperationException("The held exclusive value changed.");
                        }
                    }
                }
                else
                {
                    using (PgLwLockShareGuard<int> guard = Value.Share())
                    {
                        Poll(cancel);
                        if (guard.Value != 31)
                        {
                            throw new InvalidOperationException("The held shared value changed.");
                        }
                    }
                }

                PgInterrupts.Check();
                return Stage.Value;
            }

            private static void Poll(bool cancel)
            {
                PgInterrupts.Check();
                Stage.Value = 1;
                if (cancel)
                {
                    unsafe
                    {
                        NativeGlobals.QueryCancelPending = 1;
                        NativeGlobals.InterruptPending = 1;
                    }
                }

                PgInterrupts.Check();
                Stage.Value = 2;
            }

            [PgFunction]
            public static int LockedStage() => Stage.Value;

            [PgFunction]
            public static int LockedHoldoff()
            {
                unsafe
                {
                    return checked((int)NativeGlobals.InterruptHoldoffCount);
                }
            }

            [PgFunction]
            public static int LockedValue()
            {
                using PgLwLockExclusiveGuard<int> guard = Value.Exclusive();
                return guard.Value;
            }
        }
        """;
}
