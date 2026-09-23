using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies managed callback lifetime, native drain order, teardown protection, and backend recovery.
/// </summary>
/// <param name="context">The per-test cancellation context.</param>
[TestClass]
public sealed class MemoryCallbackTests(TestContext context)
{
    private const string SpiCleanupState = "1|query:denied,plan:denied,cursor:denied,log:denied,cursor:disposed,plan:disposed|False|disposed,disposed|";
    private const string SpiResourceCounts = """
        SELECT ARRAY[
            (SELECT count(*) FROM pg_backend_memory_contexts WHERE name = 'SPI Plan'),
            (SELECT count(*) FROM pg_cursors)]
        """;

    /// <summary>
    /// Each registration runs once in LIFO order across repeated resets and final deletion.
    /// </summary>
    [TestMethod]
    public Task ResetCallbacksAreOneShotAndLifo()
        => CheckAsync(nameof(ResetCallbacksAreOneShotAndLifo), "SELECT datatype.memory_callback_lifo()",
            "CBA|CBA|CBAD|False,False,False,False|False");

    /// <summary>
    /// Native drain consumes the active registration before user code and sees newly added callbacks.
    /// </summary>
    /// <param name="cancelOlder">Whether the active callback cancels an older pending registration.</param>
    /// <param name="order">The expected native callback order.</param>
    [TestMethod]
    [DataRow(false, "BCA")]
    [DataRow(true, "BC")]
    public Task CallbackRegistrationAndCancellationDuringDrainPreserveNativeOrder(bool cancelOlder, string order)
        => CheckAsync(nameof(CallbackRegistrationAndCancellationDuringDrainPreserveNativeOrder),
            $"SELECT datatype.memory_callback_drain({cancelOlder})", $"{order}|True|False,False,False");

    /// <summary>
    /// A callback error consumes only that registration, preserves exact diagnostics and data, and permits retry.
    /// </summary>
    /// <param name="operation">Reset, reset-only, or delete.</param>
    /// <param name="alive">Whether the owner survives the successful retry.</param>
    [TestMethod]
    [DataRow(0, true)]
    [DataRow(1, true)]
    [DataRow(2, false)]
    public Task CallbackFailuresConsumeOnlyTheFailingRegistrationAndRetryPendingCleanup(int operation, bool alive)
        => CheckAsync(nameof(CallbackFailuresConsumeOnlyTheFailingRegistrationAndRetryPendingCleanup),
            $"SELECT datatype.memory_callback_failure({operation})",
            $"22023|callback café|detail naïve|hint déjà|CB|True,False,False|True|73|True|CBA|False,False,False|{alive}|stale|True");

    /// <summary>
    /// Reset variants and deletion invoke the exact native subtree while its original payload remains readable.
    /// </summary>
    /// <param name="operation">Reset, reset-only, reset-children, or delete.</param>
    /// <param name="expected">The expected surviving context, payload, and pending callback state.</param>
    [TestMethod]
    [DataRow(0, "True,False,False|stale,stale,stale|False,False,False")]
    [DataRow(1, "True,True,True|stale,22,33|False,True,True")]
    [DataRow(2, "True,True,True|11,stale,stale|True,False,False")]
    [DataRow(3, "False,False,False|stale,stale,stale|False,False,False")]
    public Task ResetVariantsPreserveNativeCallbackTreeOrderAndPayloadLifetime(int operation, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ResetVariantsPreserveNativeCallbackTreeOrderAndPayloadLifetime),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand("SELECT current_setting('server_version_num')::integer", connection, transaction);
                int version = Assert.IsInstanceOfType<int>(await command.ExecuteScalarAsync(token));
                string order = operation switch
                {
                    0 or 3 => "L33,C22,P11",
                    1 => "P11",
                    2 => version >= 170000 ? "C22,L33" : "L33,C22",
                    _ => throw new ArgumentOutOfRangeException(nameof(operation)),
                };
                command.CommandText = $"SELECT datatype.memory_callback_tree({operation})";
                Assert.AreEqual($"{order}|{expected}", await command.ExecuteScalarAsync(token));
                await AssertRecoveredAsync(command, backend, token);
            }, context.CancellationToken);

    /// <summary>
    /// Independent nested resets cannot clear the enclosing operation's intent to retain its context.
    /// </summary>
    /// <param name="children">Whether the nested reset traverses descendants.</param>
    /// <param name="values">The expected independent allocation states.</param>
    [TestMethod]
    [DataRow(false, "stale,33")]
    [DataRow(true, "22,stale")]
    public Task NestedIndependentResetsPreserveOuterRetainState(bool children, string values)
        => CheckAsync(nameof(NestedIndependentResetsPreserveOuterRetainState),
            $"SELECT datatype.memory_callback_nested_reset({children})",
            $"A,B,11,R44|True,True,True|stale,{values}|stale|outer callback reset");

    /// <summary>
    /// A callback cannot recursively reclaim its owner or ancestors, mutate the active tree, or use SQL and logging.
    /// </summary>
    /// <param name="delete">Whether the enclosing cleanup deletes rather than resets its owner.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task TeardownProtectsOwnerAndAncestorsWhileKeepingPayloadAndIndependentMemoryUsable(bool delete)
        => CheckAsync(nameof(TeardownProtectsOwnerAndAncestorsWhileKeepingPayloadAndIndependentMemoryUsable),
            $"SELECT datatype.memory_callback_protected_owner({delete})",
            "55000,55000,55000,55000,55000,55000,55000,55000,55000|77|91|2|stale|False|0|42");

    /// <summary>
    /// PostgreSQL roots a pending callback without its wrapper and releases captured objects on every terminal path.
    /// </summary>
    /// <param name="operation">Cancel, success, failure, or success without retaining the wrapper.</param>
    /// <param name="calls">The expected number of user callback invocations.</param>
    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(1, 1)]
    [DataRow(2, 1)]
    [DataRow(3, 1)]
    public Task PendingNativeOwnershipAndTerminalCleanupHaveExactManagedRootLifetimes(int operation, int calls)
        => CheckAsync(nameof(PendingNativeOwnershipAndTerminalCleanupHaveExactManagedRootLifetimes),
            $"SELECT datatype.memory_callback_roots({operation})", $"True|False|{calls}|False");

    /// <summary>
    /// Commit and rollback invoke native-owned registrations once after they survive separate SQL callbacks.
    /// </summary>
    /// <param name="commit">Whether the transaction ends by commit or rollback.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ImplicitCommitAndRollbackCleanupRunsExactlyOnce(bool commit)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await using var command = new NpgsqlCommand("SELECT datatype.memory_callback_prepare(1, false)", connection, transaction);
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT datatype.memory_callback_implicit_state()";
            Assert.AreEqual("|True,True|True|73", await command.ExecuteScalarAsync(token));
            if (commit)
            {
                await transaction.CommitAsync(token);
            }
            else
            {
                await transaction.RollbackAsync(token);
            }
        }

        await AssertImplicitCleanupAsync(connection, backend, token);
    }

    /// <summary>
    /// Savepoint rollback invokes subtransaction callbacks without touching callbacks owned by the outer transaction.
    /// </summary>
    /// <param name="subtransaction">Whether the callback belongs to the rolled-back subtransaction.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ImplicitSavepointCleanupRespectsItsOwningTransaction(bool subtransaction)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await using var command = new NpgsqlCommand("SAVEPOINT memory_callback_scope", connection, transaction);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = $"SELECT datatype.memory_callback_prepare({(subtransaction ? 2 : 1)}, false)";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            command.CommandText = "ROLLBACK TO SAVEPOINT memory_callback_scope";
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT datatype.memory_callback_implicit_state()";
            Assert.AreEqual(subtransaction ? "B73,A73|False,False|False|stale" : "|True,True|True|73",
                await command.ExecuteScalarAsync(token));
            await AssertRecoveredAsync(command, backend, token);
            await transaction.CommitAsync(token);
        }

        await AssertImplicitCleanupAsync(connection, backend, token);
    }

    /// <summary>
    /// A native-initiated query-end callback error reaches the caller with its diagnostics and cleanup remains one-shot.
    /// </summary>
    [TestMethod]
    public async Task ImplicitQueryCleanupPropagatesOwnedCallbackErrorAndRecovers()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand("SELECT datatype.memory_callback_prepare(0, true)", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        AssertCallbackError(error);
        await AssertImplicitCleanupAsync(connection, backend, token);
    }

    /// <summary>
    /// Commit cleanup reports the managed callback's native error and finishes pending cleanup before the next command.
    /// </summary>
    [TestMethod]
    public async Task ImplicitCommitCleanupPropagatesOwnedCallbackErrorAndRecovers()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await using var command = new NpgsqlCommand("SELECT datatype.memory_callback_prepare(1, true)", connection, transaction);
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => transaction.CommitAsync(token));
            AssertCallbackError(error);
        }

        await AssertImplicitCleanupAsync(connection, backend, token);
    }

    /// <summary>
    /// PostgreSQL records primary and cleanup errors in order, while Npgsql reports the final error before backend recovery.
    /// </summary>
    /// <param name="cleanupThrows">Whether the callback also throws while PostgreSQL is cleaning up the SQL error.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SqlFailureDrainsImplicitCallbacksAndReportsLastProtocolError(bool cleanupThrows)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand("SET log_error_verbosity = verbose", connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = $"SELECT datatype.memory_callback_prepare(1, {cleanupThrows}); DO $$ BEGIN RAISE EXCEPTION 'primary cleanup failure' USING ERRCODE = '23514', DETAIL = 'primary detail', HINT = 'primary hint'; END $$";
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        if (cleanupThrows)
        {
            AssertCallbackError(error);
        }
        else
        {
            Assert.AreEqual("23514", error.SqlState);
            Assert.AreEqual("primary cleanup failure", error.MessageText);
            Assert.AreEqual("primary detail", error.Detail);
            Assert.AreEqual("primary hint", error.Hint);
        }

        await AssertImplicitCleanupAsync(connection, backend, token);
        const string errorPrefix = ": ERROR:  ";
        string[] backendLines = [.. PostgresFixture.Cluster.ReadServerLog().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains($" [{backend}] ", StringComparison.Ordinal))];
        string[] loggedErrors = [.. backendLines.Where(line => line.Contains(errorPrefix, StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf(errorPrefix, StringComparison.Ordinal) + errorPrefix.Length)..])];
        Assert.AreSequenceEqual<string>(cleanupThrows ? ["23514: primary cleanup failure", "22023: callback café"] : ["23514: primary cleanup failure"],
            loggedErrors);
        string backendLog = string.Join('\n', backendLines);
        Assert.Contains("DETAIL:  primary detail", backendLog);
        Assert.Contains("HINT:  primary hint", backendLog);
        if (cleanupThrows)
        {
            Assert.Contains("DETAIL:  detail naïve", backendLog);
            Assert.Contains("HINT:  hint déjà", backendLog);
        }
    }

    /// <summary>
    /// Explicit reset permits only owned SPI disposal, removes both native resources, and does not repeat cleanup.
    /// </summary>
    [TestMethod]
    public Task ExplicitResetCallbacksDisposeOwnedSpiPlansAndCursors()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ExplicitResetCallbacksDisposeOwnedSpiPlansAndCursors),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand(SpiResourceCounts, connection, transaction);
                long[] baseline = Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token));
                for (int iteration = 0; iteration < 5; iteration++)
                {
                    command.CommandText = "SELECT datatype.memory_callback_spi_reset()";
                    Assert.AreEqual($"41|41|{SpiCleanupState}True|42", await command.ExecuteScalarAsync(token));
                    command.CommandText = SpiResourceCounts;
                    Assert.AreSequenceEqual(baseline, Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token)),
                        $"Owned SPI resource counts after reset {iteration}.");
                    command.CommandText = "SELECT datatype.memory_callback_spi_state()";
                    Assert.AreEqual(SpiCleanupState + "False", await command.ExecuteScalarAsync(token));
                }

                command.CommandText = "SELECT count(*) FROM pg_backend_memory_contexts WHERE ident = 'memory callback SPI owner'";
                Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
                await AssertRecoveredAsync(command, backend, token);
            }, context.CancellationToken);

    /// <summary>
    /// Transaction cleanup releases retained SPI plans and consumes cursor ownership even after a primary SQL failure.
    /// </summary>
    /// <param name="operation">Zero for commit, one for rollback, or two for SQL failure followed by rollback.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task ImplicitTransactionCallbacksDisposeOwnedSpiPlansAndCursors(int operation)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var counts = new NpgsqlCommand(SpiResourceCounts, connection);
        long[] baseline = Assert.IsInstanceOfType<long[]>(await counts.ExecuteScalarAsync(token));
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await using var command = new NpgsqlCommand("SELECT datatype.memory_callback_spi_prepare(false, false)", connection, transaction);
            Assert.AreEqual("41|41", await command.ExecuteScalarAsync(token));
            command.CommandText = SpiResourceCounts;
            Assert.AreSequenceEqual([baseline[0] + 1, baseline[1] + 1],
                Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token)));
            if (operation == 0)
            {
                await transaction.CommitAsync(token);
            }
            else
            {
                if (operation == 2)
                {
                    command.CommandText = "DO $$ BEGIN RAISE EXCEPTION 'resource primary failure' USING ERRCODE = '23514', DETAIL = 'resource detail', HINT = 'resource hint'; END $$";
                    PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
                    AssertResourcePrimaryError(error);
                }

                await transaction.RollbackAsync(token);
            }
        }

        counts.CommandText = "SELECT datatype.memory_callback_spi_state()";
        Assert.AreEqual(SpiCleanupState + "False", await counts.ExecuteScalarAsync(token));
        counts.CommandText = SpiResourceCounts;
        Assert.AreSequenceEqual(baseline, Assert.IsInstanceOfType<long[]>(await counts.ExecuteScalarAsync(token)));
        await AssertRecoveredAsync(counts, backend, token);
        counts.CommandText = "BEGIN; COMMIT; SELECT datatype.memory_callback_spi_state()";
        Assert.AreEqual(SpiCleanupState + "False", await counts.ExecuteScalarAsync(token));
        await AssertRecoveredAsync(counts, backend, token);
    }

    /// <summary>
    /// Subtransaction cleanup closes an adopted parent cursor that PostgreSQL would otherwise preserve after rollback.
    /// </summary>
    /// <param name="sqlFails">Whether a primary SQL failure precedes rollback to the savepoint.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task ImplicitSavepointCallbacksDisposeAdoptedParentCursorAndOwnedPlan(bool sqlFails)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ImplicitSavepointCallbacksDisposeAdoptedParentCursorAndOwnedPlan),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand(SpiResourceCounts, connection, transaction);
                long[] baseline = Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token));
                command.CommandText = "DECLARE memory_callback_parent_cursor CURSOR FOR SELECT generate_series(41, 43)";
                await command.ExecuteNonQueryAsync(token);
                await transaction.SaveAsync("memory_callback_spi_scope", token);
                command.CommandText = "SELECT datatype.memory_callback_spi_prepare(true, true)";
                Assert.AreEqual("41|41", await command.ExecuteScalarAsync(token));
                command.CommandText = SpiResourceCounts;
                Assert.AreSequenceEqual([baseline[0] + 1, baseline[1] + 1],
                    Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token)));
                if (sqlFails)
                {
                    command.CommandText = "DO $$ BEGIN RAISE EXCEPTION 'resource primary failure' USING ERRCODE = '23514', DETAIL = 'resource detail', HINT = 'resource hint'; END $$";
                    PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
                    AssertResourcePrimaryError(error);
                }

                await transaction.RollbackAsync("memory_callback_spi_scope", token);
                command.CommandText = "SELECT datatype.memory_callback_spi_state()";
                Assert.AreEqual(SpiCleanupState + "False", await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT EXISTS (SELECT FROM pg_cursors WHERE name = 'memory_callback_parent_cursor')";
                Assert.IsFalse(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
                command.CommandText = SpiResourceCounts;
                Assert.AreSequenceEqual(baseline, Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token)));
                await AssertRecoveredAsync(command, backend, token);
            }, context.CancellationToken);

    /// <summary>
    /// ErrorContext cannot own recovery scratch that native error flushing will reset before diagnostics are copied.
    /// </summary>
    [TestMethod]
    public Task ErrorContextFailuresKeepOwnedDiagnosticsOutsideResetScratch()
        => CheckAsync(nameof(ErrorContextFailuresKeepOwnedDiagnosticsOutsideResetScratch),
            "SELECT datatype.memory_callback_error_context()",
            "XX000|ErrorContext|22023|callback café|detail naïve|hint déjà|ErrorContext|91|False|stale|True|42");

    /// <summary>
    /// Owned diagnostics survive database encoding conversion and conversion errors leave older callbacks retryable.
    /// </summary>
    /// <param name="encoding">The database encoding.</param>
    /// <param name="unrepresentable">Whether the callback message includes an elephant character.</param>
    /// <param name="diagnostics">The exact diagnostics expected after native reconstruction.</param>
    [TestMethod]
    [DataRow("UTF8", false, "22023|callback café|detail naïve|hint déjà")]
    [DataRow("LATIN1", false, "22023|callback café|detail naïve|hint déjà")]
    [DataRow("UTF8", true, "22023")]
    [DataRow("LATIN1", true, "22P05")]
    public async Task CallbackErrorsPreserveEncodingAndPendingDrainOwnership(string encoding, bool unrepresentable, string diagnostics)
    {
        CancellationToken token = context.CancellationToken;
        string database = "callback_encoding_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection administrator = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using (var create = new NpgsqlCommand(
            $"CREATE DATABASE {database} TEMPLATE template0 ENCODING '{encoding}' LC_COLLATE 'C' LC_CTYPE 'C'", administrator))
        {
            await create.ExecuteNonQueryAsync(token);
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(administrator.ConnectionString) { Database = database };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(token);
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand("CREATE EXTENSION ankus_test", connection);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = $"SELECT memory_callback_encoding({unrepresentable})";
            Assert.AreEqual($"{diagnostics}|B|True,False|BA|False,False|42", await command.ExecuteScalarAsync(token));
            await AssertRecoveredAsync(command, backend, token);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", administrator);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private Task CheckAsync(string name, string sql, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(name, async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            Assert.AreEqual(expected, Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token)));
            await AssertRecoveredAsync(command, backend, token);
        }, context.CancellationToken);

    private static void AssertCallbackError(PostgresException error)
    {
        Assert.AreEqual("22023", error.SqlState);
        Assert.AreEqual("callback café", error.MessageText);
        Assert.AreEqual("detail naïve", error.Detail);
        Assert.AreEqual("hint déjà", error.Hint);
    }

    private static void AssertResourcePrimaryError(PostgresException error)
    {
        Assert.AreEqual("23514", error.SqlState);
        Assert.AreEqual("resource primary failure", error.MessageText);
        Assert.AreEqual("resource detail", error.Detail);
        Assert.AreEqual("resource hint", error.Hint);
    }

    private static async Task AssertRecoveredAsync(NpgsqlCommand command, int backend, CancellationToken token)
    {
        command.CommandText = "SELECT 42";
        Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, command.Connection?.ProcessID);
    }

    private static async Task AssertImplicitCleanupAsync(NpgsqlConnection connection, int backend, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT datatype.memory_callback_implicit_state()", connection);
        Assert.AreEqual("B73,A73|False,False|False|stale", await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT count(*) FROM pg_backend_memory_contexts WHERE ident = 'implicit memory callback'";
        Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
        await AssertRecoveredAsync(command, backend, token);
        command.CommandText = "BEGIN; COMMIT; SELECT datatype.memory_callback_implicit_state()";
        Assert.AreEqual("B73,A73|False,False|False|stale", await command.ExecuteScalarAsync(token));
        await AssertRecoveredAsync(command, backend, token);
    }
}
