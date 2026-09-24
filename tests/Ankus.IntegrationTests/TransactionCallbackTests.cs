using System.Net.Sockets;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies managed transaction callbacks against PostgreSQL commit, abort, and savepoint machinery.
/// </summary>
/// <param name="context">The per-test cancellation context.</param>
[TestClass]
public sealed class TransactionCallbackTests(TestContext context)
{
    private const string NoSubtransactions = "null,null,null,null";

    /// <summary>
    /// Runs pre-commit and commit callbacks in order, cancels one callback, logs, and clears the abort callback.
    /// </summary>
    [TestMethod]
    public async Task CommitRunsOrderedCallbacksAndClearsMutuallyExclusiveRoots()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await ResetAsync(connection, token);
        var notices = new List<PostgresNotice>();
        connection.Notice += (_, args) => notices.Add(args.Notice);
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            Assert.IsTrue(await ScalarAsync<bool>(connection, transaction,
                "SELECT datatype.transaction_callback_register_outer(true, false, false, false)", token));
            await transaction.CommitAsync(token);
        }

        Assert.AreEqual("pre1:1,commit|False,False,False,False,null,null," + NoSubtransactions,
            await StateAsync(connection, token));
        Assert.ContainsSingle(notices.Where(static notice => notice.MessageText == "Ankus pre-commit callback"));
    }

    /// <summary>
    /// Runs only the abort callback on rollback and clears callbacks for every other outer-transaction phase.
    /// </summary>
    [TestMethod]
    public async Task RollbackRunsAbortAndClearsCommitRegistrations()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await ResetAsync(connection, token);
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            Assert.IsTrue(await ScalarAsync<bool>(connection, transaction,
                "SELECT datatype.transaction_callback_register_outer(false, false, false, false)", token));
            await transaction.RollbackAsync(token);
        }

        Assert.AreEqual("abort|False,False,False,False,null,null," + NoSubtransactions,
            await StateAsync(connection, token));
    }

    /// <summary>
    /// Cancels a registration in a later managed function call on the same backend and transaction.
    /// </summary>
    [TestMethod]
    public async Task ReceiptCancelsAcrossGeneratedFunctionCalls()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await ResetAsync(connection, token);
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await ExecuteAsync(connection, transaction, "SELECT datatype.transaction_callback_register_saved()", token);
            Assert.IsTrue(await ScalarAsync<bool>(connection, transaction,
                "SELECT datatype.transaction_callback_cancel_saved()", token));
            await transaction.CommitAsync(token);
        }

        Assert.AreEqual("|null,null,null,null,null,False," + NoSubtransactions,
            await StateAsync(connection, token));
    }

    /// <summary>
    /// Defers a same-phase registration while allowing a newly registered commit callback to run in order.
    /// </summary>
    [TestMethod]
    public async Task PreCommitRegistrationCanTargetLaterPhaseOnly()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await ResetAsync(connection, token);
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            Assert.IsTrue(await ScalarAsync<bool>(connection, transaction,
                "SELECT datatype.transaction_callback_register_outer(false, false, false, true)", token));
            await transaction.CommitAsync(token);
        }

        Assert.AreEqual("pre1:1,pre2,commit,late-commit|False,False,False,False,False,null," + NoSubtransactions,
            await StateAsync(connection, token));
    }

    /// <summary>
    /// Reports exact savepoint IDs in all successful phases while callback SPI does not create visible recursive events.
    /// </summary>
    [TestMethod]
    public async Task SavepointReleaseRunsAllPhasesOnceWithoutGuardSubtransactions()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await ResetAsync(connection, token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await ExecuteAsync(connection, transaction,
            "SELECT datatype.transaction_callback_register_subtransactions(false)", token);
        await transaction.SaveAsync("managed_callbacks", token);
        await transaction.ReleaseAsync("managed_callbacks", token);
        string[] events = Events(await StateAsync(connection, transaction, token));
        Assert.HasCount(3, events);
        string[] start = events[0].Split(':');
        string[] preCommit = events[1].Split(':');
        string[] commit = events[2].Split(':');
        Assert.AreSequenceEqual(["start", start[1], start[2], "1"], start);
        Assert.AreSequenceEqual(["pre-sub", start[1], start[2], "1"], preCommit);
        Assert.AreSequenceEqual(["commit-sub", start[1], start[2]], commit);
        await transaction.RollbackAsync(token);
    }

    /// <summary>
    /// Rolling back to a savepoint reports its abort and the replacement subtransaction with exact IDs.
    /// </summary>
    [TestMethod]
    public async Task SavepointRollbackReportsAbortAndReplacementIds()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await ResetAsync(connection, token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await ExecuteAsync(connection, transaction,
            "SELECT datatype.transaction_callback_register_subtransactions(false)", token);
        await transaction.SaveAsync("rolled_back", token);
        await transaction.RollbackAsync("rolled_back", token);
        await transaction.ReleaseAsync("rolled_back", token);
        string[] events = Events(await StateAsync(connection, transaction, token));
        Assert.HasCount(5, events);
        string[] initialStart = events[0].Split(':');
        string[] abort = events[1].Split(':');
        string[] replacementStart = events[2].Split(':');
        string[] preCommit = events[3].Split(':');
        string[] commit = events[4].Split(':');
        Assert.AreSequenceEqual(["start", initialStart[1], initialStart[2], "1"], initialStart);
        Assert.AreSequenceEqual(["abort-sub", initialStart[1], initialStart[2]], abort);
        Assert.AreSequenceEqual(["start", replacementStart[1], initialStart[2], "1"], replacementStart);
        Assert.AreNotEqual(initialStart[1], replacementStart[1]);
        Assert.AreSequenceEqual(["pre-sub", replacementStart[1], initialStart[2], "1"], preCommit);
        Assert.AreSequenceEqual(["commit-sub", replacementStart[1], initialStart[2]], commit);
        await transaction.RollbackAsync(token);
    }

    /// <summary>
    /// A subtransaction pre-commit exception aborts the transaction and leaves the backend usable.
    /// </summary>
    [TestMethod]
    public async Task SubtransactionPreCommitFailureAbortsAndRecoversTheSameBackend()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await ResetAsync(connection, token);
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await ExecuteAsync(connection, transaction,
                "SELECT datatype.transaction_callback_register_subtransactions(true)", token);
            await transaction.SaveAsync("managed_failure", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(
                () => transaction.ReleaseAsync("managed_failure", token));
            Assert.AreEqual("P0002", error.SqlState);
            Assert.AreEqual("managed subtransaction pre-commit failure", error.MessageText);
            await transaction.RollbackAsync(token);
        }

        Assert.AreEqual(backend, connection.ProcessID);
        Assert.AreEqual(42, await ScalarAsync<int>(connection, null, "SELECT 42", token));
    }

    /// <summary>
    /// A PostgreSQL error caught by managed callback code is rethrown after managed frames unwind.
    /// </summary>
    [TestMethod]
    public async Task CaughtSpiFailureStillAbortsAndRecoversTheSameBackend()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await ResetAsync(connection, token);
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await ExecuteAsync(connection, transaction,
                "SELECT datatype.transaction_callback_register_subtransaction_spi_failure()", token);
            await transaction.SaveAsync("spi_failure", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(
                () => transaction.ReleaseAsync("spi_failure", token));
            Assert.AreEqual("42P01", error.SqlState);
            Assert.Contains("ankus_missing_callback_relation", error.MessageText);
            await transaction.RollbackAsync(token);
        }

        Assert.AreEqual(backend, connection.ProcessID);
        Assert.AreEqual(42, await ScalarAsync<int>(connection, null, "SELECT 42", token));
    }

    /// <summary>
    /// SPI may invoke the same managed subtransaction dispatcher recursively without losing either callback frame.
    /// </summary>
    [TestMethod]
    public async Task PreCommitSpiSupportsNestedSubtransactionCallbacks()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await ResetAsync(connection, token);
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await ExecuteAsync(connection, transaction,
                "SELECT datatype.transaction_callback_register_nested_subtransaction()", token);
            await transaction.CommitAsync(token);
        }

        string[] events = Events(await StateAsync(connection, token));
        Assert.HasCount(3, events);
        string[] start = events[0].Split(':');
        string[] preCommit = events[1].Split(':');
        string[] commit = events[2].Split(':');
        Assert.AreSequenceEqual(["nested-start", start[1], start[2]], start);
        Assert.AreSequenceEqual(["nested-pre", start[1], start[2]], preCommit);
        Assert.AreSequenceEqual(["nested-commit", start[1], start[2]], commit);
    }

    /// <summary>
    /// A managed pre-commit error aborts the transaction with exact diagnostics and the same backend remains usable.
    /// </summary>
    [TestMethod]
    public async Task PreCommitFailureAbortsAndRecoversTheSameBackend()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await ResetAsync(connection, token);
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            Assert.IsTrue(await ScalarAsync<bool>(connection, transaction,
                "SELECT datatype.transaction_callback_register_outer(false, true, false, false)", token));
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => transaction.CommitAsync(token));
            Assert.AreEqual("P0001", error.SqlState);
            Assert.AreEqual("managed pre-commit failure", error.MessageText);
            Assert.AreEqual("callback detail", error.Detail);
            Assert.AreEqual("callback hint", error.Hint);
        }

        Assert.AreEqual(backend, connection.ProcessID);
        Assert.AreEqual("pre1:1,abort|False,False,False,False,null,null," + NoSubtransactions,
            await StateAsync(connection, token));
        Assert.AreEqual(42, await ScalarAsync<int>(connection, null, "SELECT 42", token));
    }

    /// <summary>
    /// Post-commit exceptions unwind before PANIC and preserve committed writes through PostgreSQL recovery.
    /// </summary>
    /// <param name="write">Whether the transaction assigns an XID and durably commits a row.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CommitFailurePreservesTransactionOutcomeThroughRecovery(bool write)
    {
        CancellationToken token = context.CancellationToken;
        PostgresTestClusterOptions options = await IntegrationEnvironment.CreateOptionsAsync(token);
        await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(options, token);
        await using NpgsqlConnection observer = await cluster.OpenConnectionAsync(token);
        await ExecuteAsync(observer, null,
            "CREATE SCHEMA datatype; CREATE EXTENSION ankus_test WITH SCHEMA datatype; " +
            "CREATE TABLE callback_commit(value integer)", token);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        string session = "ankus-commit-error-" + Guid.NewGuid().ToString("N");
        await ExecuteAsync(connection, null,
            $"SET application_name = '{session}'; SET log_error_verbosity = verbose", token);
        await ResetAsync(connection, token);
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            if (write)
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO callback_commit VALUES (42)", token);
            }

            Assert.IsTrue(await ScalarAsync<bool>(connection, transaction,
                "SELECT datatype.transaction_callback_register_outer(false, false, true, false)", token));
            NpgsqlException failure = await Assert.ThrowsAsync<NpgsqlException>(() => transaction.CommitAsync(token));
            if (failure is PostgresException error)
            {
                Assert.AreEqual("38000", error.SqlState);
                Assert.AreEqual("managed post-commit failure", error.MessageText);
                Assert.AreEqual("PANIC", error.InvariantSeverity);
            }
            else
            {
                // Windows can reset the socket before the client reads PostgreSQL's terminal error.
                Assert.IsTrue(OperatingSystem.IsWindows());
                IOException transport = Assert.IsInstanceOfType<IOException>(failure.InnerException);
                SocketException socket = Assert.IsInstanceOfType<SocketException>(transport.InnerException);
                Assert.AreEqual(SocketError.ConnectionReset, socket.SocketErrorCode);
            }
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        while (!cluster.ReadServerLog().Contains("reinitializing", StringComparison.Ordinal))
        {
            await Task.Delay(50, deadline.Token);
        }

        string log = cluster.ReadServerLog();
        string diagnostic = $"[{session}]: PANIC:  38000: managed post-commit failure";
        string cleanup = $"[{session}]: WARNING:  01000: managed post-commit finally";
        Assert.Contains(diagnostic, log);
        Assert.Contains(cleanup, log);
        Assert.IsLessThan(log.IndexOf(diagnostic, StringComparison.Ordinal), log.IndexOf(cleanup, StringComparison.Ordinal));
        Assert.DoesNotContain("AbortTransaction while in COMMIT state", log);
        Assert.DoesNotContain("it was already committed", log);
        Assert.AreEqual(System.Data.ConnectionState.Closed, connection.State);
        await Assert.ThrowsAsync<NpgsqlException>(() => ScalarAsync<int>(observer, null, "SELECT 42", deadline.Token));

        NpgsqlConnection recovered;
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            try
            {
                recovered = await cluster.OpenConnectionAsync(deadline.Token);
                break;
            }
            catch (NpgsqlException error) when (error is not PostgresException ||
                error is PostgresException { SqlState: PostgresErrorCodes.CannotConnectNow or
                    PostgresErrorCodes.AdminShutdown or PostgresErrorCodes.CrashShutdown })
            {
                await Task.Delay(50, deadline.Token);
            }
        }

        await using (recovered)
        {
            Assert.AreEqual(write ? 1L : 0L,
                await ScalarAsync<long>(recovered, null, "SELECT count(*) FROM callback_commit", deadline.Token));
            if (write)
            {
                Assert.AreEqual(42, await ScalarAsync<int>(recovered, null, "SELECT value FROM callback_commit", deadline.Token));
            }

            await ResetAsync(recovered, deadline.Token);
            Assert.IsTrue(await ScalarAsync<bool>(recovered, null,
                "SELECT datatype.transaction_callback_register_outer(true, false, false, false)", deadline.Token));
            Assert.AreEqual("pre1:1,commit|False,False,False,False,null,null," + NoSubtransactions,
                await StateAsync(recovered, deadline.Token));
        }
    }

    /// <summary>
    /// An unmatched callback remains rooted before commit and releases its captured payload at transaction end.
    /// </summary>
    [TestMethod]
    public async Task OuterTransactionOwnsDroppedReceiptAndCapturedPayload()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await ResetAsync(connection, token);
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await ExecuteAsync(connection, transaction, "SELECT datatype.transaction_callback_register_root()", token);
            Assert.IsTrue(await ScalarAsync<bool>(connection, transaction,
                "SELECT datatype.transaction_callback_root_alive()", token));
            await transaction.CommitAsync(token);
        }

        Assert.IsFalse(await ScalarAsync<bool>(connection, null,
            "SELECT datatype.transaction_callback_root_alive()", token));
    }

    private static string[] Events(string state)
    {
        string text = state[..state.IndexOf('|')];
        return text.Length == 0 ? [] : text.Split(',');
    }

    private static Task ResetAsync(NpgsqlConnection connection, CancellationToken token)
        => ExecuteAsync(connection, null, "SELECT datatype.transaction_callback_reset()", token);

    private static Task<string> StateAsync(NpgsqlConnection connection, CancellationToken token)
        => ScalarAsync<string>(connection, null, "SELECT datatype.transaction_callback_state()", token);

    private static Task<string> StateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken token)
        => ScalarAsync<string>(connection, transaction, "SELECT datatype.transaction_callback_state()", token);

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<T> ScalarAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }
}
