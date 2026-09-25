using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies virtual context SQL contracts and retained executor lifetimes in actual PostgreSQL callbacks.
/// </summary>
/// <param name="context">The current test's cancellation context.</param>
[TestClass]
[DoNotParallelize]
public sealed class VirtualMemoryContextTests(TestContext context)
{
    /// <summary>
    /// Virtual dependencies preserve SQL argument positions, nulls, names, defaults, and variadic elements.
    /// </summary>
    /// <param name="sql">The concrete SQL invocation.</param>
    /// <param name="expected">The exact value and ownership observations.</param>
    [TestMethod]
    [DataRow("SELECT virtual_memory.vm_scalar(7, 'café', 9000000000)", "7|café|9000000000|True|True|True|True")]
    [DataRow("SELECT virtual_memory.vm_scalar(input_left => -3, text => NULL, right => 9123)", "-3|null|9123|True|True|True|True")]
    [DataRow("SELECT virtual_memory.vm_defaults()", "17|True|True")]
    [DataRow("SELECT virtual_memory.vm_defaults(-2)", "-2|True|True")]
    [DataRow("SELECT virtual_memory.vm_nullable(NULL)", "null|True")]
    [DataRow("SELECT virtual_memory.vm_nullable(5)", "5|True")]
    [DataRow("SELECT virtual_memory.vm_only()::text", "true")]
    [DataRow("SELECT virtual_memory.vm_variadic(3, 1, NULL, 7)", "3|1,null,7|True")]
    public Task ScalarVirtualContextsPreserveSqlInputsAndIndependentBorrowedWrappers(string sql, string expected)
        => CheckAsync(nameof(ScalarVirtualContextsPreserveSqlInputsAndIndependentBorrowedWrappers), sql, expected);

    /// <summary>
    /// PostgreSQL's installed catalog contains only real SQL arguments and infers strictness independently of virtual nullability.
    /// </summary>
    [TestMethod]
    public Task CatalogSignaturesDefaultsAndStrictnessExcludeVirtualDependencies()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(CatalogSignaturesDefaultsAndStrictnessExcludeVirtualDependencies),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand("""
                    SELECT proname, pronargs::integer, proisstrict, pronargdefaults::integer,
                           provariadic::integer, array_to_string(proargnames, ','), oidvectortypes(proargtypes)
                    FROM pg_proc WHERE pronamespace = 'virtual_memory'::regnamespace
                      AND proname IN ('vm_defaults','vm_nullable','vm_only','vm_scalar','vm_streaming','vm_variadic')
                    ORDER BY proname
                    """, connection, transaction);
                (string Name, int Count, bool Strict, int Defaults, int Variadic, string Names, string Types)[] expected =
                [
                    ("vm_defaults", 1, true, 1, 0, "value", "integer"),
                    ("vm_nullable", 1, false, 0, 0, "value", "integer"),
                    ("vm_only", 0, true, 0, 0, "", ""),
                    ("vm_scalar", 3, false, 0, 0, "input_left,text,right", "integer, text, bigint"),
                    ("vm_streaming", 3, true, 0, 0, "key,count,failure", "integer, integer, integer"),
                    ("vm_variadic", 2, true, 0, 23, "factor,values", "integer, integer[]"),
                ];
                await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
                {
                    foreach ((string name, int count, bool strict, int defaults, int variadic, string names, string types) in expected)
                    {
                        Assert.IsTrue(await reader.ReadAsync(token));
                        Assert.AreEqual(name, reader.GetString(0));
                        Assert.AreEqual(count, reader.GetInt32(1));
                        Assert.AreEqual(strict, reader.GetBoolean(2));
                        Assert.AreEqual(defaults, reader.GetInt32(3));
                        Assert.AreEqual(variadic, reader.GetInt32(4));
                        Assert.AreEqual(names, reader.IsDBNull(5) ? "" : reader.GetString(5));
                        Assert.AreEqual(types, reader.GetString(6));
                    }

                    Assert.IsFalse(await reader.ReadAsync(token));
                }

                command.CommandText = "SELECT virtual_memory.vm_defaults(NULL) IS NULL, virtual_memory.vm_scalar(NULL, 'text', 9) IS NULL";
                await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
                {
                    Assert.IsTrue(await reader.ReadAsync(token));
                    Assert.IsTrue(reader.GetBoolean(0));
                    Assert.IsTrue(reader.GetBoolean(1));
                }

                await AssertRecoveredAsync(command, backend, token);
            }, context.CancellationToken);

    /// <summary>
    /// PostgreSQL operator and cast resolution pass actual operands, typmod, and explicitness around virtual context slots.
    /// </summary>
    [TestMethod]
    public Task OperatorsAndCastsRetainSqlOperandAndTypmodOrdinals()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(OperatorsAndCastsRetainSqlOperandAndTypmodOrdinals),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand("""
                    SELECT 7 OPERATOR(virtual_memory.#@#) 11::bigint,
                           CAST('Value'::virtual_memory.vm_token AS numeric(8,0))::text
                    """, connection, transaction);
                await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
                {
                    Assert.IsTrue(await reader.ReadAsync(token));
                    Assert.AreEqual(6989L, reader.GetInt64(0));
                    Assert.AreEqual("5242928", reader.GetString(1));
                }

                await AssertRecoveredAsync(command, backend, token);
            }, context.CancellationToken);

    /// <summary>
    /// Nested SPI and managed scope errors preserve the outer snapshot and current context while inner scratch expires.
    /// </summary>
    /// <param name="failure">The success or error path.</param>
    /// <param name="outcome">The exact returned result or owned diagnostic.</param>
    [TestMethod]
    [DataRow(0, "True")]
    [DataRow(1, "P7601:virtual context failure 1:virtual detail café:virtual hint")]
    [DataRow(2, "22012:division by zero::")]
    [DataRow(3, "local scope failure")]
    public Task NestedCallbacksPreserveSnapshotProviderAndCurrentRestoration(int failure, string outcome)
        => CheckAsync(nameof(NestedCallbacksPreserveSnapshotProviderAndCurrentRestoration),
            $"SELECT virtual_memory.vm_nested({failure})", $"{outcome}|True|True|True|17|False|stale|42");

    /// <summary>
    /// A retained scalar context and both tracked and raw views expire after its executor has finished.
    /// </summary>
    [TestMethod]
    public Task ScalarInjectedStorageExpiresAfterExecutorCompletion()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ScalarInjectedStorageExpiresAfterExecutorCompletion),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand("SELECT virtual_memory.vm_save_scalar()", connection, transaction);
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
                command.CommandText = "SELECT virtual_memory.vm_scalar_state()";
                Assert.AreEqual("False|stale|stale", await command.ExecuteScalarAsync(token));
                await AssertRecoveredAsync(command, backend, token);
            }, context.CancellationToken);

    /// <summary>
    /// Factory and GetEnumerator allocate in the multi-call owner while each later row observes expired scratch and live retained bytes.
    /// </summary>
    /// <param name="function">The streaming or materialized implementation.</param>
    /// <param name="projectSet">Whether the SQL call occurs in the target list rather than FROM.</param>
    [TestMethod]
    [DataRow("vm_streaming", false)]
    [DataRow("vm_streaming", true)]
    [DataRow("vm_materialized", false)]
    [DataRow("vm_materialized", true)]
    public Task SetFactoriesRetainOwnerStorageAcrossActualPerRowScratchResets(string function, bool projectSet)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SetFactoriesRetainOwnerStorageAcrossActualPerRowScratchResets),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                string source = projectSet
                    ? $"(SELECT virtual_memory.{function}(1,3,0) AS value) AS rows"
                    : $"virtual_memory.{function}(1,3,0) AS value";
                await using var command = new NpgsqlCommand($"SELECT array_agg(value ORDER BY value) FROM {source}", connection, transaction);
                Assert.AreSequenceEqual([101, 102, 103], Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token)));
                await AssertStateAsync(command, 1, "True,True,True|1,True|4,3,3|1|True,103,103,42|False,stale,stale", token);
                await AssertRecoveredAsync(command, backend, token);
            }, context.CancellationToken);

    /// <summary>
    /// The context parameter remains captured correctly when a compiler-generated iterator defers its entire body until MoveNext.
    /// </summary>
    [TestMethod]
    public Task DeferredIteratorBodiesUseCapturedOwnerRatherThanCurrentScratch()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DeferredIteratorBodiesUseCapturedOwnerRatherThanCurrentScratch),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand("SELECT array_agg(value ORDER BY value) FROM virtual_memory.vm_deferred(1,3) AS value", connection, transaction);
                Assert.AreSequenceEqual([101, 102, 103], Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token)));
                await AssertStateAsync(command, 1, "True,False,True|0,False|4,3,3|1|True,103,103,42|False,stale,stale", token);
                await AssertRecoveredAsync(command, backend, token);
            }, context.CancellationToken);

    /// <summary>
    /// Empty iterators still dispose exactly once with a live factory-owned payload.
    /// </summary>
    /// <param name="function">The streaming or materialized implementation.</param>
    [TestMethod]
    [DataRow("vm_streaming")]
    [DataRow("vm_materialized")]
    public Task EmptySetsDisposeLiveInjectedStorageWithoutProducingRows(string function)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(EmptySetsDisposeLiveInjectedStorageWithoutProducingRows),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand($"SELECT count(*) FROM virtual_memory.{function}(1,0,0)", connection, transaction);
                Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
                await AssertStateAsync(command, 1, "True,True,True|1,True|1,0,0|1|True,100,100,42|False,stale,stale", token);
                await AssertRecoveredAsync(command, backend, token);
            }, context.CancellationToken);

    /// <summary>
    /// Two suspended cursors preserve independent native owners until each cursor is explicitly closed.
    /// </summary>
    [TestMethod]
    public Task InterleavedPortalsRetainDistinctOwnersAndExpireIndependently()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(InterleavedPortalsRetainDistinctOwnersAndExpireIndependently),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand("""
                    DECLARE virtual_first NO SCROLL CURSOR FOR SELECT virtual_memory.vm_streaming(1,3,0);
                    DECLARE virtual_second NO SCROLL CURSOR FOR SELECT virtual_memory.vm_streaming(2,3,0)
                    """, connection, transaction);
                await command.ExecuteNonQueryAsync(token);
                command.CommandText = "FETCH 1 FROM virtual_first";
                Assert.AreEqual(101, await command.ExecuteScalarAsync(token));
                command.CommandText = "FETCH 1 FROM virtual_second";
                Assert.AreEqual(201, await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT virtual_memory.vm_distinct(1,2)";
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
                await AssertStateAsync(command, 1, "True,True,True|1,True|1,1,0|0|none|True,101,101", token);
                await AssertStateAsync(command, 2, "True,True,True|1,True|1,1,0|0|none|True,201,201", token);
                command.CommandText = "CLOSE virtual_first";
                await command.ExecuteNonQueryAsync(token);
                await AssertStateAsync(command, 1, "True,True,True|1,True|1,1,0|1|True,101,101,42|False,stale,stale", token);
                command.CommandText = "FETCH 1 FROM virtual_second";
                Assert.AreEqual(202, await command.ExecuteScalarAsync(token));
                command.CommandText = "CLOSE virtual_second";
                await command.ExecuteNonQueryAsync(token);
                await AssertStateAsync(command, 2, "True,True,True|1,True|2,2,1|1|True,202,202,42|False,stale,stale", token);
                await AssertRecoveredAsync(command, backend, token);
            }, context.CancellationToken);

    /// <summary>
    /// Reserved owners and ancestors reject destructive reentry between FETCH calls, while owner child-only reset preserves direct state.
    /// </summary>
    /// <param name="operation">The selected native reset boundary.</param>
    /// <param name="expected">The exact rejection or permitted child-only reset.</param>
    [TestMethod]
    [DataRow(0, "55000:a live set-returning function memory context cannot be reset or deleted|101,101,101|True|True,51")]
    [DataRow(1, "55000:a live set-returning function memory context cannot be reset or deleted|101,101,101|True|True,51")]
    [DataRow(2, "55000:a live set-returning function memory context cannot be reset or deleted|101,101,101|True|True,51")]
    [DataRow(3, "55000:a live set-returning function memory context cannot be reset or deleted|101,101,101|True|True,51")]
    [DataRow(4, "55000:a live set-returning function memory context cannot be reset or deleted|101,101,101|True|True,51")]
    [DataRow(5, "allowed|101,101,101|True|True,stale")]
    public Task SuspendedOwnerGuardsPreserveCursorRecoveryAndExactChildResetSemantics(int operation, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SuspendedOwnerGuardsPreserveCursorRecoveryAndExactChildResetSemantics),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand("DECLARE virtual_guard NO SCROLL CURSOR FOR SELECT virtual_memory.vm_streaming(1,3,0)", connection, transaction);
                await command.ExecuteNonQueryAsync(token);
                command.CommandText = "FETCH 1 FROM virtual_guard";
                Assert.AreEqual(101, await command.ExecuteScalarAsync(token));
                command.CommandText = $"SELECT virtual_memory.vm_guard(1,{operation})";
                Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
                command.CommandText = "FETCH 1 FROM virtual_guard";
                Assert.AreEqual(102, await command.ExecuteScalarAsync(token));
                command.CommandText = "CLOSE virtual_guard";
                await command.ExecuteNonQueryAsync(token);
                await AssertStateAsync(command, 1, "True,True,True|1,True|2,2,1|1|True,102,102,42|False,stale,stale", token);
                await AssertRecoveredAsync(command, backend, token);
            }, context.CancellationToken);

    /// <summary>
    /// LIMIT and explicit cursor close dispose retained storage exactly once before native owner invalidation.
    /// </summary>
    /// <param name="portal">Whether early shutdown uses explicit cursor close.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task EarlyShutdownDisposesWithLiveInjectedPayload(bool portal)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(EarlyShutdownDisposesWithLiveInjectedPayload),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand("SELECT virtual_memory.vm_streaming(1,5,0) LIMIT 2", connection, transaction);
                if (portal)
                {
                    command.CommandText = "DECLARE virtual_early NO SCROLL CURSOR FOR SELECT virtual_memory.vm_streaming(1,5,0)";
                    await command.ExecuteNonQueryAsync(token);
                    command.CommandText = "FETCH 2 FROM virtual_early";
                }

                await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
                {
                    Assert.IsTrue(await reader.ReadAsync(token));
                    Assert.AreEqual(101, reader.GetInt32(0));
                    Assert.IsTrue(await reader.ReadAsync(token));
                    Assert.AreEqual(102, reader.GetInt32(0));
                    Assert.IsFalse(await reader.ReadAsync(token));
                }

                if (portal)
                {
                    command.CommandText = "CLOSE virtual_early";
                    await command.ExecuteNonQueryAsync(token);
                }

                await AssertStateAsync(command, 1, "True,True,True|1,True|2,2,1|1|True,102,102,42|False,stale,stale", token);
                await AssertRecoveredAsync(command, backend, token);
            }, context.CancellationToken);

    /// <summary>
    /// Factory, GetEnumerator, and row failures preserve exact diagnostics and dispose only acquired iterators with live payloads.
    /// </summary>
    /// <param name="function">The streaming or materialized implementation.</param>
    /// <param name="failure">The failing lifecycle stage.</param>
    [TestMethod]
    [DataRow("vm_streaming", 1)]
    [DataRow("vm_streaming", 2)]
    [DataRow("vm_streaming", 3)]
    [DataRow("vm_streaming", 5)]
    [DataRow("vm_materialized", 1)]
    [DataRow("vm_materialized", 2)]
    [DataRow("vm_materialized", 3)]
    [DataRow("vm_materialized", 5)]
    public Task LifecycleErrorsPreserveDiagnosticsAndCleanupBeforeOwnerExpiry(string function, int failure)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(LifecycleErrorsPreserveDiagnosticsAndCleanupBeforeOwnerExpiry),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                var notices = new List<PostgresNotice>();
                connection.Notice += (_, args) => notices.Add(args.Notice);
                await transaction.SaveAsync("virtual_failure", token);
                await using var command = new NpgsqlCommand($"SELECT array_agg(value) FROM virtual_memory.{function}(1,3,{failure}) AS value", connection, transaction);
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
                AssertFailure(error, failure == 5 ? 3 : failure);
                await transaction.RollbackAsync("virtual_failure", token);
                string expected = failure switch
                {
                    1 => "True,True,True|0,False|0,0,0|0|none|False,stale,stale",
                    2 => "True,True,True|1,True|0,0,0|0|none|False,stale,stale",
                    _ => "True,True,True|1,True|2,1,1|1|True,101,101,42|False,stale,stale",
                };
                await AssertStateAsync(command, 1, expected, token);
                PostgresNotice[] cleanupWarnings = [.. notices.Where(static notice => notice.InvariantSeverity == "WARNING" &&
                    notice.MessageText == "Ankus iterator disposal also failed after a row error")];
                Assert.HasCount(failure == 5 ? 1 : 0, cleanupWarnings);
                await AssertRecoveredAsync(command, backend, token);
            }, context.CancellationToken);

    /// <summary>
    /// A normal disposal error retains its own diagnostics and cannot trigger a second iterator disposal during abort.
    /// </summary>
    /// <param name="function">The streaming or materialized implementation.</param>
    [TestMethod]
    [DataRow("vm_streaming")]
    [DataRow("vm_materialized")]
    public Task DisposalErrorsPreserveLivePayloadObservationAndExactOnceCleanup(string function)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DisposalErrorsPreserveLivePayloadObservationAndExactOnceCleanup),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await transaction.SaveAsync("virtual_dispose", token);
                await using var command = new NpgsqlCommand($"SELECT array_agg(value) FROM virtual_memory.{function}(1,3,4) AS value", connection, transaction);
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
                AssertFailure(error, 4);
                await transaction.RollbackAsync("virtual_dispose", token);
                await AssertStateAsync(command, 1, "True,True,True|1,True|4,3,3|1|True,103,103,42|False,stale,stale", token);
                await AssertRecoveredAsync(command, backend, token);
            }, context.CancellationToken);

    /// <summary>
    /// Native executor abort runs iterator cleanup before invalidating the injected owner and preserves the original division error.
    /// </summary>
    [TestMethod]
    public Task ExecutorAbortPreservesDirectOwnerPayloadUntilRestrictedCleanupFinishes()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ExecutorAbortPreservesDirectOwnerPayloadUntilRestrictedCleanupFinishes),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await transaction.SaveAsync("virtual_executor", token);
                await using var command = new NpgsqlCommand("SELECT 1/(value-101) FROM (SELECT virtual_memory.vm_streaming(1,3,0) AS value OFFSET 0) AS rows", connection, transaction);
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
                Assert.AreEqual("22012", error.SqlState);
                Assert.AreEqual("division by zero", error.MessageText);
                await transaction.RollbackAsync("virtual_executor", token);
                await AssertStateAsync(command, 1, "True,True,True|1,True|1,1,0|1|True,101,101,denied|False,stale,stale", token);
                await AssertRecoveredAsync(command, backend, token);
            }, context.CancellationToken);

    /// <summary>
    /// Native enum output failures exercise abort cleanup after managed row conversion in both set execution modes.
    /// </summary>
    /// <param name="function">The streaming or materialized enum function.</param>
    [TestMethod]
    [DataRow("vm_enum_streaming")]
    [DataRow("vm_enum_materialized")]
    public Task NativeOutputFailurePreservesOwnerPayloadThroughAbortDisposal(string function)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NativeOutputFailurePreservesOwnerPayloadThroughAbortDisposal),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                var notices = new List<PostgresNotice>();
                connection.Notice += (_, args) => notices.Add(args.Notice);
                await transaction.SaveAsync("virtual_conversion", token);
                await using var command = new NpgsqlCommand("ALTER TYPE datatype.enum_mood RENAME VALUE 'Low' TO 'virtual_renamed'", connection, transaction);
                await command.ExecuteNonQueryAsync(token);
                command.CommandText = $"SELECT value::text FROM virtual_memory.{function}(1) AS value";
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
                Assert.AreEqual("22P02", error.SqlState);
                await transaction.RollbackAsync("virtual_conversion", token);
                await AssertStateAsync(command, 1, "True,True,True|1,True|1,1,0|1|True,101,101,denied|False,stale,stale", token);
                Assert.IsEmpty(notices.Where(static notice => notice.InvariantSeverity == "WARNING" && notice.MessageText.Contains("iterator disposal", StringComparison.Ordinal)));
                await AssertRecoveredAsync(command, backend, token);
            }, context.CancellationToken);

    /// <summary>
    /// Savepoint abort expires a suspended set owner while preserving independently owned top-transaction control storage.
    /// </summary>
    [TestMethod]
    public Task SavepointAbortExpiresSetOwnerAndPreservesTopTransactionControl()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SavepointAbortExpiresSetOwnerAndPreservesTopTransactionControl),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand("SELECT virtual_memory.vm_control(true)", connection, transaction);
                Assert.AreEqual("41", await command.ExecuteScalarAsync(token));
                await transaction.SaveAsync("virtual_scope", token);
                command.CommandText = "DECLARE virtual_savepoint NO SCROLL CURSOR FOR SELECT virtual_memory.vm_streaming(1,3,0)";
                await command.ExecuteNonQueryAsync(token);
                command.CommandText = "FETCH 1 FROM virtual_savepoint";
                Assert.AreEqual(101, await command.ExecuteScalarAsync(token));
                await transaction.RollbackAsync("virtual_scope", token);
                await AssertStateAsync(command, 1, "True,True,True|1,True|1,1,0|1|True,101,101,denied|False,stale,stale", token);
                command.CommandText = "SELECT virtual_memory.vm_control(false)";
                Assert.AreEqual("41", await command.ExecuteScalarAsync(token));
                await AssertRecoveredAsync(command, backend, token, expectedContexts: 1);
            }, context.CancellationToken);

    /// <summary>
    /// Transaction completion releases suspended iterator storage with the appropriate normal or restricted cleanup phase.
    /// </summary>
    /// <param name="commit">Whether to commit rather than abort the transaction.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TransactionCompletionReclaimsSuspendedOwnerAndItsCapturedViews(bool commit)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await using var command = new NpgsqlCommand("DECLARE virtual_transaction NO SCROLL CURSOR FOR SELECT virtual_memory.vm_streaming(1,3,0)", connection, transaction);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "FETCH 1 FROM virtual_transaction";
            Assert.AreEqual(101, await command.ExecuteScalarAsync(token));
            await AssertStateAsync(command, 1, "True,True,True|1,True|1,1,0|0|none|True,101,101", token);
            if (commit)
            {
                await transaction.CommitAsync(token);
            }
            else
            {
                await transaction.RollbackAsync(token);
            }
        }

        await using var check = new NpgsqlCommand("SELECT 42", connection);
        await AssertStateAsync(check, 1, $"True,True,True|1,True|1,1,0|1|True,101,101,{(commit ? "42" : "denied")}|False,stale,stale", token);
        await AssertRecoveredAsync(check, backend, token);
    }

    /// <summary>
    /// Materialized row scratch remains bounded while every row preserves exact retained native state and output bytes.
    /// </summary>
    [TestMethod]
    public Task MaterializedInjectedStorageRemainsSeparateFromBoundedRowScratch()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(MaterializedInjectedStorageRemainsSeparateFromBoundedRowScratch),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand("""
                    SELECT count(*), sum(row_id), sum(octet_length(payload)),
                           bool_and(length(payload) = 8192 AND left(payload, length(row_id::text)) = row_id::text)
                    FROM virtual_memory.vm_bounded(1,64,8192)
                    """, connection, transaction);
                await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
                {
                    Assert.IsTrue(await reader.ReadAsync(token));
                    Assert.AreEqual(64L, reader.GetInt64(0));
                    Assert.AreEqual(8480L, reader.GetInt64(1));
                    Assert.AreEqual(524288L, reader.GetInt64(2));
                    Assert.IsTrue(reader.GetBoolean(3));
                }

                command.CommandText = "SELECT virtual_memory.vm_maximum_scratch(1)";
                long maximum = Assert.IsInstanceOfType<long>(await command.ExecuteScalarAsync(token));
                Assert.IsGreaterThan(0L, maximum);
                Assert.IsLessThanOrEqualTo(131072L, maximum);
                await AssertStateAsync(command, 1, "True,True,True|1,True|65,64,64|1|True,164,164,42|False,stale,stale", token);
                await AssertRecoveredAsync(command, backend, token);
            }, context.CancellationToken);

    private Task CheckAsync(string name, string sql, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(name, async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            Assert.AreEqual(expected, Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token)));
            await AssertRecoveredAsync(command, backend, token);
        }, context.CancellationToken);

    private static void AssertFailure(PostgresException error, int stage)
    {
        Assert.AreEqual("P760" + stage, error.SqlState);
        Assert.AreEqual("virtual context failure " + stage, error.MessageText);
        Assert.AreEqual("virtual detail café", error.Detail);
        Assert.AreEqual("virtual hint", error.Hint);
    }

    private static async Task AssertStateAsync(NpgsqlCommand command, int key, string expected, CancellationToken token)
    {
        command.CommandText = $"SELECT virtual_memory.vm_state({key})";
        Assert.AreEqual(expected, Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token)));
    }

    private static async Task AssertRecoveredAsync(NpgsqlCommand command, int backend, CancellationToken token, long expectedContexts = 0)
    {
        command.CommandText = "SELECT count(*) FROM pg_backend_memory_contexts WHERE ident LIKE 'virtual context %'";
        Assert.AreEqual(expectedContexts, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT 42";
        Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        NpgsqlConnection? connection = command.Connection;
        Assert.IsNotNull(connection);
        Assert.AreEqual(backend, connection.ProcessID);
    }
}
