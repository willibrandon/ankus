using System.Net.Sockets;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies native cleanup without interrupting PostgreSQL exception-subtransaction recovery.
/// </summary>
/// <param name="context">The current test cancellation context.</param>
[TestClass]
public sealed partial class CallbackSubtransactionCleanupTests(TestContext context)
{
    private const string Resources = """
        SELECT ARRAY[
            (SELECT count(*) FROM ankus_test_memory.contexts WHERE name = 'SPI Plan'),
            (SELECT count(*) FROM pg_cursors)]
        """;

    /// <summary>
    /// Iterator failures retain their primary diagnostics and release ownership before the exception handler resumes.
    /// </summary>
    /// <param name="openCursor">Whether the iterator retains a cursor alongside its prepared plan.</param>
    /// <param name="executorFailure">Whether division fails before iterator cleanup.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public Task IteratorCleanupPreservesExceptionSubtransactionRecovery(bool openCursor, bool executorFailure)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(IteratorCleanupPreservesExceptionSubtransactionRecovery),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                var notices = new List<PostgresNotice>();
                connection.Notice += (_, args) => notices.Add(args.Notice);
                await using var command = new NpgsqlCommand(string.Empty, connection, transaction);
                long[] baseline = await PrepareCaptureAsync(command, token);
                command.CommandText = "SELECT set_values.set_reset()";
                await command.ExecuteNonQueryAsync(token);
                string source = $"set_values.set_owned_resources_with_cleanup_error({openCursor})";
                string query = executorFailure
                    ? $"SELECT 1/(CASE WHEN value='Low'::datatype.enum_mood THEN 0 ELSE 1 END) FROM (SELECT {source} AS value) input"
                    : $"SELECT value::text FROM {source} value";
                for (int index = 0; index < 10; index++)
                {
                    int previousNotices = notices.Count;
                    string[] error = await CaptureAsync(command, query, token);
                    Assert.AreSequenceEqual(executorFailure
                        ? ["22012", "division by zero", string.Empty, string.Empty]
                        : ["42704", "ExtensibleNodeMethods \"ankus_missing_iterator_cleanup\" was not registered", string.Empty, string.Empty], error);
                    command.CommandText = "SELECT set_values.set_status()";
                    int[] status = Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token));
                    Assert.HasCount(13, status);
                    Assert.AreEqual(index + 1, status[12]);
                    Assert.AreEqual(executorFailure ? 0 : index + 1, status[8]);
                    Assert.AreEqual(executorFailure ? index + 1 : 0, status[9]);
                    Assert.AreEqual(0, status[10]);
                    Assert.AreEqual(0, status[11]);
                    command.CommandText = "SELECT set_values.set_native_cleanup_caught()";
                    Assert.AreEqual(index + 1, await command.ExecuteScalarAsync(token));
                    AssertNativeCleanupWarning(notices, previousNotices, executorFailure,
                        "ExtensibleNodeMethods \"ankus_missing_iterator_cleanup\" was not registered");
                    await AssertRecoveredAsync(command, baseline, backend, token);
                }

                command.CommandText = $"SELECT value::text FROM set_values.set_owned_resources({openCursor}) value";
                Assert.AreEqual("Low", await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT set_values.set_status()";
                Assert.AreEqual(11, Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token))[12]);
                await AssertRecoveredAsync(command, baseline, backend, token);
                AssertNoInterruptedAbort(notices);
            }, context.CancellationToken);

    /// <summary>
    /// Aggregate transition errors remain catchable after exact state and SPI-resource cleanup.
    /// </summary>
    /// <param name="transitionFailure">Whether transition fails before aggregate state cleanup.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task AggregateCleanupPreservesExceptionSubtransactionRecovery(bool transitionFailure)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(AggregateCleanupPreservesExceptionSubtransactionRecovery),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                var notices = new List<PostgresNotice>();
                connection.Notice += (_, args) => notices.Add(args.Notice);
                await using var command = new NpgsqlCommand(string.Empty, connection, transaction);
                long[] baseline = await PrepareCaptureAsync(command, token);
                string mode = transitionFailure ? "resources_native_transition" : "resources_native";
                for (int index = 0; index < 10; index++)
                {
                    int previousNotices = notices.Count;
                    command.CommandText = $"SELECT aggregate_values.aggregate_reset('{mode}')";
                    await command.ExecuteNonQueryAsync(token);
                    string[] error = await CaptureAsync(command,
                        "SELECT aggregate_values.managed_sum(v ORDER BY v) FROM (VALUES(1),(2),(3)) AS input(v)", token);
                    Assert.AreSequenceEqual(transitionFailure
                        ? ["P7801", "aggregate transition failed", "owned aggregate detail", "retry valid inputs"]
                        : ["42704", "ExtensibleNodeMethods \"ankus_missing_aggregate_cleanup\" was not registered", string.Empty, string.Empty], error);
                    command.CommandText = "SELECT aggregate_values.aggregate_status()";
                    int[] status = Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token));
                    Assert.AreSequenceEqual([1, 1, 0, 0], status[..4]);
                    Assert.AreEqual(transitionFailure ? 2 : 3, status[4]);
                    command.CommandText = "SELECT aggregate_values.aggregate_trace()";
                    Assert.AreSequenceEqual(["42704"], Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(token)));
                    AssertNativeCleanupWarning(notices, previousNotices, transitionFailure,
                        "ExtensibleNodeMethods \"ankus_missing_aggregate_cleanup\" was not registered");
                    await AssertRecoveredAsync(command, baseline, backend, token);
                }

                command.CommandText = "SELECT aggregate_values.aggregate_reset('normal')";
                await command.ExecuteNonQueryAsync(token);
                command.CommandText = "SELECT aggregate_values.managed_sum(v) FROM (VALUES(42)) AS input(v)";
                Assert.AreEqual(42L, await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT aggregate_values.aggregate_status()";
                Assert.AreSequenceEqual([1, 1, 0, 0], Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token))[..4]);
                await AssertRecoveredAsync(command, baseline, backend, token);
                AssertNoInterruptedAbort(notices);
            }, context.CancellationToken);

    /// <summary>
    /// A secondary memory callback failure remains visible while the original SQL diagnostic and native drain survive.
    /// </summary>
    /// <param name="cleanupThrows">Whether the newer memory callback reports its own managed error.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task MemoryCleanupPreservesPrimaryErrorAndRemainingCallbacks(bool cleanupThrows)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(MemoryCleanupPreservesPrimaryErrorAndRemainingCallbacks),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                var notices = new List<PostgresNotice>();
                connection.Notice += (_, args) => notices.Add(args.Notice);
                await using var command = new NpgsqlCommand(string.Empty, connection, transaction);
                long[] baseline = await PrepareCaptureAsync(command, token);
                string query = $"""
                    SELECT datatype.memory_callback_prepare(2, {cleanupThrows});
                    DO $failure$ BEGIN
                        RAISE EXCEPTION 'primary callback failure' USING
                            ERRCODE = '23514', DETAIL = 'primary detail', HINT = 'primary hint';
                    END $failure$
                    """;
                for (int index = 0; index < 10; index++)
                {
                    int previousNotices = notices.Count;
                    Assert.AreSequenceEqual(["23514", "primary callback failure", "primary detail", "primary hint"],
                        await CaptureAsync(command, query, token));
                    command.CommandText = "SELECT datatype.memory_callback_implicit_state()";
                    Assert.AreEqual("B73,A73|False,False|False|stale", await command.ExecuteScalarAsync(token));
                    command.CommandText = "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident = 'implicit memory callback'";
                    Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
                    PostgresNotice[] cleanup = [.. notices.Skip(previousNotices).Where(static notice => notice.SqlState == "22023")];
                    if (cleanupThrows)
                    {
                        PostgresNotice warning = Assert.ContainsSingle(cleanup);
                        Assert.AreEqual("WARNING", warning.InvariantSeverity);
                        Assert.AreEqual("callback café", warning.MessageText);
                        Assert.AreEqual("detail naïve", warning.Detail);
                        Assert.AreEqual("hint déjà", warning.Hint);
                    }
                    else
                    {
                        Assert.IsEmpty(cleanup);
                    }

                    await AssertRecoveredAsync(command, baseline, backend, token);
                }

                AssertNoInterruptedAbort(notices);
            }, context.CancellationToken);

    /// <summary>
    /// Fabricated cancellation stays a warning while a real native cancellation remains pending through callback drain.
    /// </summary>
    /// <param name="callbackCancellation">Whether the managed callback throws cancellation instead of the report hook injecting it.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task CleanupCancellationFinishesRollbackBeforeInterruptProcessing(bool callbackCancellation)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(CleanupCancellationFinishesRollbackBeforeInterruptProcessing),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                var notices = new List<PostgresNotice>();
                connection.Notice += (_, args) => notices.Add(args.Notice);
                await using var command = new NpgsqlCommand(string.Empty, connection, transaction);
                long[] baseline = await PrepareCaptureAsync(command, token);
                command.CommandText = "SET LOCAL log_min_messages = warning";
                await command.ExecuteNonQueryAsync(token);
                if (!callbackCancellation)
                {
                    command.CommandText = "SELECT tests.log_arm('callback café', 1)";
                    await command.ExecuteNonQueryAsync(token);
                }

                await transaction.SaveAsync("callback_cancel", token);
                string prepare = callbackCancellation ? "memory_callback_prepare_cancellation(2)" : "memory_callback_prepare(2, true)";
                string query = $"SELECT datatype.{prepare}; DO $$ BEGIN RAISE EXCEPTION 'primary failure' USING ERRCODE='23514'; END $$";
                if (callbackCancellation)
                {
                    Assert.AreSequenceEqual(["23514", "primary failure", string.Empty, string.Empty],
                        await CaptureAsync(command, query, token));
                }
                else
                {
                    PostgresException cancel = await Assert.ThrowsExactlyAsync<PostgresException>(() => CaptureAsync(command, query, token));
                    Assert.AreEqual("57014", cancel.SqlState);
                    Assert.AreEqual("canceling statement due to user request", cancel.MessageText);
                    await transaction.RollbackAsync("callback_cancel", token);
                }

                await transaction.ReleaseAsync("callback_cancel", token);
                PostgresNotice warning = Assert.ContainsSingle(notices.Where(notice => notice.SqlState ==
                    (callbackCancellation ? "57014" : "22023")));
                Assert.AreEqual("WARNING", warning.InvariantSeverity);
                Assert.AreEqual(callbackCancellation ? "cleanup cancellation" : "callback café", warning.MessageText);
                command.CommandText = "SELECT datatype.memory_callback_implicit_state()";
                Assert.AreEqual("B73,A73|False,False|False|stale", await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT tests.raw_call_holdoffs()";
                Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
                if (!callbackCancellation)
                {
                    command.CommandText = "SELECT tests.log_holdoff()";
                    Assert.IsGreaterThan(0L, Assert.IsInstanceOfType<long>(await command.ExecuteScalarAsync(token)));
                }

                await AssertRecoveredAsync(command, baseline, backend, token);
                AssertNoInterruptedAbort(notices);
            }, context.CancellationToken);

    /// <summary>
    /// Managed SPI and explicit subtransactions retain the primary error and permit further SQL after complete callback drain.
    /// </summary>
    /// <param name="spi">Whether SPI owns the failed subtransaction.</param>
    /// <param name="cleanupThrows">Whether the secondary callback also throws.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public Task ManagedSubtransactionCleanupPreservesPrimaryErrorAndRecovery(bool spi, bool cleanupThrows)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ManagedSubtransactionCleanupPreservesPrimaryErrorAndRecovery),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                var notices = new List<PostgresNotice>();
                connection.Notice += (_, args) => notices.Add(args.Notice);
                await using var command = new NpgsqlCommand(Resources, connection, transaction);
                long[] baseline = Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token));
                for (int index = 0; index < 10; index++)
                {
                    int previousNotices = notices.Count;
                    command.CommandText = "SELECT datatype.callback_subtransaction_recovery($1, $2)";
                    command.Parameters.AddWithValue(spi);
                    command.Parameters.AddWithValue(cleanupThrows);
                    string[] result;
                    try
                    {
                        result = Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(token));
                    }
                    finally
                    {
                        command.Parameters.Clear();
                    }

                    Assert.AreSequenceEqual<string>(["23514", "managed primary cleanup failure", "managed primary detail",
                        "managed primary hint", "B73,A73|False,False|False|stale", "42"], result);
                    PostgresNotice[] cleanup = [.. notices.Skip(previousNotices).Where(static notice => notice.SqlState == "22023")];
                    if (cleanupThrows)
                    {
                        PostgresNotice warning = Assert.ContainsSingle(cleanup);
                        Assert.AreEqual("WARNING", warning.InvariantSeverity);
                        Assert.AreEqual("callback café", warning.MessageText);
                        Assert.AreEqual("detail naïve", warning.Detail);
                        Assert.AreEqual("hint déjà", warning.Hint);
                    }
                    else
                    {
                        Assert.IsEmpty(cleanup);
                    }

                    await AssertRecoveredAsync(command, baseline, backend, token);
                }

                AssertNoInterruptedAbort(notices);
            }, context.CancellationToken);

    /// <summary>
    /// An ERROR from the native report hook cannot undo a durable commit or prepared transaction.
    /// </summary>
    /// <param name="preparedTransaction">Whether PostgreSQL prepares the transaction instead of committing it.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task CleanupReporterFailurePanicsAfterManagedUnwindingAndPreservesCommit(bool preparedTransaction)
        => AssertTerminalCleanupAsync("datatype.memory_callback_prepare(1, true)", true,
            "P7521", "native failure before managed log handler", "owned native prefix detail", "retry native prefix report",
            preparedTransaction: preparedTransaction);

    /// <summary>
    /// Reporter allocation failures remain terminal after durable completion without invalid critical-section allocations.
    /// </summary>
    /// <param name="stage">One for context creation or two for diagnostic copying.</param>
    /// <param name="preparedTransaction">Whether PostgreSQL prepares instead of committing the transaction.</param>
    [TestMethod]
    [DataRow(1, false)]
    [DataRow(1, true)]
    [DataRow(2, false)]
    [DataRow(2, true)]
    public Task CompletionReportingAllocationFailurePreservesDurableCompletion(int stage, bool preparedTransaction)
        => AssertTerminalCleanupAsync(FormattableString.Invariant($"tests.completion_reporting_allocation_fault({stage})"), true,
            "53200", "Unable to retain cleanup reporting diagnostics after durable transaction completion", null, null,
            preparedTransaction: preparedTransaction);

    /// <summary>
    /// Caught FATAL and PANIC reports remain terminal without running abort cleanup after a durable commit.
    /// </summary>
    /// <param name="level">The terminal logging level requested by the callback.</param>
    [TestMethod]
    [DataRow((int)PgLogLevel.Fatal)]
    [DataRow((int)PgLogLevel.Panic)]
    public Task CleanupTerminalReportPanicsAndPreservesCommit(int level)
        => AssertTerminalCleanupAsync(FormattableString.Invariant($"datatype.memory_callback_prepare_terminal({level})"), false,
            "P7806", "terminal cleanup café", "terminal cleanup naïve", "restart after terminal cleanup déjà");

    /// <summary>
    /// Caught terminal intent from iterator or aggregate disposal terminates safely after an executor failure starts rollback.
    /// </summary>
    /// <param name="aggregate">Whether an aggregate owns cleanup rather than an iterator.</param>
    /// <param name="level">The callback's requested FATAL or PANIC severity.</param>
    [TestMethod]
    [DataRow(false, (int)PgLogLevel.Fatal)]
    [DataRow(false, (int)PgLogLevel.Panic)]
    [DataRow(true, (int)PgLogLevel.Fatal)]
    [DataRow(true, (int)PgLogLevel.Panic)]
    public Task ExecutorCleanupTerminalReportPanicsAndRollsBackWrites(bool aggregate, int level)
        => AssertTerminalCleanupAsync("42", false, "P7806", "terminal cleanup café", "terminal cleanup naïve",
            "restart after terminal cleanup déjà", aggregate
                ? "SELECT aggregate_values.managed_sum(value) FROM (VALUES(1),(2),(3)) AS input(value)"
                : FormattableString.Invariant($"SELECT 1/value FROM (SELECT set_values.set_terminal_cleanup({level}) AS value) AS input"),
            aggregate ? "SELECT aggregate_values.aggregate_reset('" +
                (level == (int)PgLogLevel.Fatal ? "cleanup_terminal_fatal" : "cleanup_terminal_panic") + "')" : null);

    /// <summary>
    /// Verifies complete terminal diagnostics and durable writes after cleanup terminates an isolated cluster's backend.
    /// </summary>
    /// <param name="preparation">The fixture call retaining the callback until commit.</param>
    /// <param name="failingReporter">Whether the native prefix hook rejects the callback's secondary diagnostic.</param>
    /// <param name="sqlState">The original reporter or callback SQLSTATE.</param>
    /// <param name="message">The original primary message.</param>
    /// <param name="detail">The original diagnostic detail.</param>
    /// <param name="hint">The original recovery hint.</param>
    /// <param name="abortQuery">The executor failure initiating callback cleanup, or null for commit cleanup.</param>
    /// <param name="setupStatement">Optional backend-local fixture setup before writes begin.</param>
    /// <param name="preparedTransaction">Whether the irreversible completion prepares the transaction.</param>
    private async Task AssertTerminalCleanupAsync(string preparation, bool failingReporter,
        string sqlState, string message, string? detail, string? hint, string? abortQuery = null, string? setupStatement = null,
        bool preparedTransaction = false)
    {
        CancellationToken token = context.CancellationToken;
        PostgresTestClusterOptions options = await IntegrationEnvironment.CreateOptionsAsync(token);
        if (preparedTransaction)
        {
            options = new()
            {
                Installation = options.Installation,
                DataDirectoryBase = options.DataDirectoryBase,
                LogDirectory = options.LogDirectory,
                StartupTimeout = options.StartupTimeout,
                PostgreSqlConfiguration = [.. options.PostgreSqlConfiguration, "max_prepared_transactions=10"],
            };
        }

        await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(options, token);
        await using NpgsqlConnection observer = await cluster.OpenConnectionAsync(token);
        await using var setup = new NpgsqlCommand("CREATE SCHEMA datatype; CREATE EXTENSION ankus_test WITH SCHEMA datatype; " +
            "CREATE SCHEMA tests; CREATE TABLE callback_reporter_commit(value integer); " +
            NativeRawCallFixtureCompiler.InstallationSql + AllocatorFaultFixtureCompiler.CompletionReportingSql, observer);
        await setup.ExecuteNonQueryAsync(token);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        string marker = "callback-reporter-" + Guid.NewGuid().ToString("N");
        await using var command = new NpgsqlCommand($"SET application_name='{marker}'; SET log_error_verbosity=verbose; " +
            "SET log_min_messages=warning" + (failingReporter ? "; SELECT tests.log_prefix_arm('callback café')" : string.Empty), connection);
        await command.ExecuteNonQueryAsync(token);
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            command.Transaction = transaction;
            if (setupStatement is not null)
            {
                command.CommandText = setupStatement;
                await command.ExecuteNonQueryAsync(token);
            }

            command.CommandText = "INSERT INTO callback_reporter_commit VALUES(73); SELECT " + preparation;
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            NpgsqlException failure;
            if (preparedTransaction)
            {
                command.CommandText = "PREPARE TRANSACTION 'cleanup_reporter_prepared'";
                failure = await Assert.ThrowsAsync<NpgsqlException>(() => command.ExecuteNonQueryAsync(token));
            }
            else if (abortQuery is null)
            {
                failure = await Assert.ThrowsAsync<NpgsqlException>(() => transaction.CommitAsync(token));
            }
            else
            {
                command.CommandText = abortQuery;
                failure = await Assert.ThrowsAsync<NpgsqlException>(() => command.ExecuteScalarAsync(token));
            }

            if (failure is PostgresException error)
            {
                // PostgreSQL sends the executor ERROR before aborting its transaction.
                // That message may reach the client before cleanup sends PANIC and exits.
                if (abortQuery is not null && error.InvariantSeverity == "ERROR")
                {
                    bool aggregate = setupStatement is not null;
                    Assert.AreEqual(aggregate ? "P7801" : "22012", error.SqlState);
                    Assert.AreEqual(aggregate ? "aggregate transition failed" : "division by zero", error.MessageText);
                    Assert.AreEqual(aggregate ? "owned aggregate detail" : null, error.Detail);
                    Assert.AreEqual(aggregate ? "retry valid inputs" : null, error.Hint);
                }
                else
                {
                    Assert.AreEqual("PANIC", error.InvariantSeverity);
                    Assert.AreEqual(sqlState, error.SqlState);
                    Assert.AreEqual(message, error.MessageText);
                    Assert.AreEqual(detail, error.Detail);
                    Assert.AreEqual(hint, error.Hint);
                }
            }
            else
            {
                Assert.IsTrue(OperatingSystem.IsWindows());
                IOException transport = Assert.IsInstanceOfType<IOException>(failure.InnerException);
                SocketException socket = Assert.IsInstanceOfType<SocketException>(transport.InnerException);
                Assert.AreEqual(SocketError.ConnectionReset, socket.SocketErrorCode);
            }
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        string recoveredLog = await CrashRecovery.WaitAsync(cluster, deadline.Token);
        Assert.DoesNotContain("the database system is in recovery mode", recoveredLog);
        Assert.DoesNotContain("the database system is not yet accepting connections", recoveredLog);

        Assert.AreEqual(System.Data.ConnectionState.Closed, connection.State);
        string log = string.Join('\n', recoveredLog.Split('\n')
            .Where(line => line.Contains($"[{marker}]:", StringComparison.Ordinal)));
        string terminal = $"PANIC:  {sqlState}: {message}";
        Assert.Contains(terminal, log);
        if (abortQuery is not null)
        {
            string primary = setupStatement is null ? "ERROR:  22012: division by zero" : "ERROR:  P7801: aggregate transition failed";
            Assert.Contains(primary, log);
            Assert.IsLessThan(log.IndexOf(terminal, StringComparison.Ordinal), log.IndexOf(primary, StringComparison.Ordinal));
        }

        if (detail is not null)
        {
            Assert.Contains(detail, log);
        }

        if (hint is not null)
        {
            Assert.Contains(hint, log);
        }

        Assert.DoesNotContain("TRAP: failed Assert", cluster.ReadServerLog());
        Assert.DoesNotContain("AbortTransaction while", log);
        Assert.DoesNotContain("it was already committed", log);
        await using (NpgsqlConnection recovered = await cluster.OpenConnectionAsync(deadline.Token))
        {
            if (preparedTransaction)
            {
                await using var prepared = new NpgsqlCommand("SELECT count(*) FROM pg_prepared_xacts WHERE gid='cleanup_reporter_prepared'", recovered);
                Assert.AreEqual(1L, await prepared.ExecuteScalarAsync(deadline.Token));
                prepared.CommandText = "COMMIT PREPARED 'cleanup_reporter_prepared'";
                await prepared.ExecuteNonQueryAsync(deadline.Token);
            }

            await using var query = new NpgsqlCommand("SELECT ARRAY(SELECT value FROM callback_reporter_commit)", recovered);
            Assert.AreSequenceEqual(abortQuery is null ? [73] : [], Assert.IsInstanceOfType<int[]>(await query.ExecuteScalarAsync(deadline.Token)));
            query.CommandText = "SELECT datatype.log_message(9, 'cleanup reporter recovered'), 42";
            await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(deadline.Token);
            Assert.IsTrue(await reader.ReadAsync(deadline.Token));
            Assert.AreEqual(42, reader.GetInt32(0));
            Assert.AreEqual(42, reader.GetInt32(1));
            Assert.IsFalse(await reader.ReadAsync(deadline.Token));
        }
    }

    /// <summary>
    /// Creates and warms only PostgreSQL's ordinary exception-handler expression plans before native resource measurement.
    /// </summary>
    private static async Task<long[]> PrepareCaptureAsync(NpgsqlCommand command, CancellationToken token)
    {
        command.CommandText = """
            CREATE FUNCTION pg_temp.capture_cleanup_error(command_text text) RETURNS text[] LANGUAGE plpgsql AS $capture$
            DECLARE failure_state text; failure_message text; failure_detail text; failure_hint text;
            BEGIN
                EXECUTE command_text;
                RETURN ARRAY['00000', 'No error', '', ''];
            EXCEPTION WHEN OTHERS THEN
                GET STACKED DIAGNOSTICS failure_state = RETURNED_SQLSTATE, failure_message = MESSAGE_TEXT,
                    failure_detail = PG_EXCEPTION_DETAIL, failure_hint = PG_EXCEPTION_HINT;
                RETURN ARRAY[failure_state, failure_message, failure_detail, failure_hint];
            END
            $capture$
            """;
        await command.ExecuteNonQueryAsync(token);
        Assert.AreSequenceEqual(["22012", "division by zero", string.Empty, string.Empty],
            await CaptureAsync(command, "SELECT 1/0", token));
        command.CommandText = Resources;
        return Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Reads exact diagnostics after PostgreSQL itself aborts the nested exception subtransaction.
    /// </summary>
    private static async Task<string[]> CaptureAsync(NpgsqlCommand command, string query, CancellationToken token)
    {
        command.CommandText = "SELECT pg_temp.capture_cleanup_error($1)";
        command.Parameters.AddWithValue(query);
        try
        {
            string[] diagnostic = Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(token));
            Assert.HasCount(4, diagnostic);
            return diagnostic;
        }
        finally
        {
            command.Parameters.Clear();
        }
    }

    /// <summary>
    /// Proves SPI ownership, native frame balance and the actual PostgreSQL backend remain intact.
    /// </summary>
    private static async Task AssertRecoveredAsync(NpgsqlCommand command, long[] baseline, int backend, CancellationToken token)
    {
        command.CommandText = Resources;
        Assert.AreSequenceEqual(baseline, Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token)));
        command.CommandText = "SELECT set_values.set_interrupt_state()";
        Assert.AreSequenceEqual([0, 0], Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token)));
        command.CommandText = "SELECT pg_backend_pid()";
        Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT 42";
        Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Proves a caught raw failure remains visible after callback unwinding without replacing the primary error.
    /// </summary>
    /// <param name="notices">The actual PostgreSQL warnings for this session.</param>
    /// <param name="start">The count before this iteration.</param>
    /// <param name="completion">Whether cleanup runs during transaction completion.</param>
    /// <param name="message">The independent native lookup diagnostic.</param>
    private static void AssertNativeCleanupWarning(List<PostgresNotice> notices, int start, bool completion, string message)
    {
        PostgresNotice[] warnings = [.. notices.Skip(start).Where(static notice => notice.SqlState == "42704")];
        if (completion)
        {
            PostgresNotice warning = Assert.ContainsSingle(warnings);
            Assert.AreEqual("WARNING", warning.InvariantSeverity);
            Assert.AreEqual(message, warning.MessageText);
            Assert.IsNull(warning.Detail);
            Assert.IsNull(warning.Hint);
        }
        else
        {
            Assert.IsEmpty(warnings);
        }
    }

    /// <summary>
    /// Rejects transaction-state warnings from repeated attempts to abort the same unfinished cleanup.
    /// </summary>
    private static void AssertNoInterruptedAbort(List<PostgresNotice> notices)
        => Assert.IsEmpty(notices.Where(static notice => notice.MessageText.Contains("AbortSubTransaction while", StringComparison.Ordinal) ||
            notice.MessageText.Contains("AbortTransaction while", StringComparison.Ordinal)));
}
