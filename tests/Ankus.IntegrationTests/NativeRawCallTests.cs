using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes generated C calls beneath a real PostgreSQL guard through published Native AOT callbacks.
/// </summary>
/// <param name="context">The per-test cancellation context.</param>
[TestClass]
public sealed class NativeRawCallTests(TestContext context)
{
    /// <summary>
    /// A complete binding mismatch fails before touching the result and a later valid call recovers in the same backend.
    /// </summary>
    /// <param name="mode">The incompatible identity or server-major partition.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    public Task GeneratedRawCallsValidateActiveBindingAndRecover(int mode)
        => CheckAsync(nameof(GeneratedRawCallsValidateActiveBindingAndRecover),
            $"SELECT datatype.raw_call_binding(tests.raw_call_address(0), {mode})",
            "0A000|the generated binding does not match the active extension's PostgreSQL ABI|12345|FFFFFFFFFFFFFFFF");

    /// <summary>
    /// The native compiler's aggregate result preserves zero, high-bit and maximum values through the managed guard.
    /// </summary>
    /// <param name="hex">The independently specified aggregate value.</param>
    [TestMethod]
    [DataRow("0000000000000000")]
    [DataRow("FEDCBA9876543210")]
    [DataRow("FFFFFFFFFFFFFFFF")]
    public Task GeneratedRawCallsPreserveNativeValues(string hex)
        => CheckAsync(nameof(GeneratedRawCallsPreserveNativeValues),
            $"SELECT datatype.raw_call_aggregate(tests.raw_call_address(0), tests.raw_call_address(3), '{hex}')", hex);

    /// <summary>
    /// Invalid native frames preserve the previous result and permit a valid call in the same backend.
    /// </summary>
    /// <param name="mode">The invalid argument or result partition.</param>
    /// <param name="reason">The independently expected validation category.</param>
    [TestMethod]
    [DataRow(0, "argument count (status 1)")]
    [DataRow(1, "argument count (status 1)")]
    [DataRow(2, "argument storage (status 4)")]
    [DataRow(3, "argument storage (status 4)")]
    [DataRow(4, "argument alignment (status 5)")]
    [DataRow(5, "result storage (status 3)")]
    [DataRow(6, "result storage (status 3)")]
    [DataRow(7, "result storage (status 3)")]
    public Task GeneratedRawCallsRejectStorageAndRecover(int mode, string reason)
        => CheckAsync(nameof(GeneratedRawCallsRejectStorageAndRecover),
            $"SELECT datatype.raw_call_rejected_storage(tests.raw_call_address(1), {mode})",
            $"The generated native call rejected its {reason}.|12345|42");

    /// <summary>
    /// A native ERROR restores memory context and interrupt holdoffs, retains owned diagnostics, and permits a successful retry.
    /// </summary>
    [TestMethod]
    public Task GeneratedRawCallsRecoverFromPostgresErrors()
        => CheckAsync(nameof(GeneratedRawCallsRecoverFromPostgresErrors),
            "SELECT datatype.raw_call_errors(tests.raw_call_address(2), 'tests.raw_call_error(integer)'::regprocedure::oid, tests.raw_call_address(1))",
            "22023|raw call failure|owned native detail|retry with a valid value|True|True|True|42");

    /// <summary>
    /// Nested managed callbacks call the raw guard, unwind on failure, and restore the enclosing capability before retry.
    /// </summary>
    [TestMethod]
    public Task GeneratedRawCallsSupportNestedCallbackRecovery()
        => CheckAsync(nameof(GeneratedRawCallsSupportNestedCallbackRecovery),
            "SELECT datatype.raw_call_errors(tests.raw_call_address(2), 'datatype.raw_call_nested_target(integer)'::regprocedure::oid, tests.raw_call_address(1))",
            "22023|nested raw failure|owned managed detail|retry managed callback|True|True|True|42");

    /// <summary>
    /// A managed worker cannot use a backend-thread capability even when it knows a valid native body address.
    /// </summary>
    [TestMethod]
    public Task GeneratedRawCallsRejectWorkerThreads()
        => CheckAsync(nameof(GeneratedRawCallsRejectWorkerThreads),
            "SELECT datatype.raw_call_worker(tests.raw_call_address(1))", "True|42");

    /// <summary>
    /// Successful raw calls retain intentional PostgreSQL interrupt-state changes until the caller explicitly reverses them.
    /// </summary>
    [TestMethod]
    public Task GeneratedRawCallsPreserveSuccessfulNativeStateChanges()
        => CheckAsync(nameof(GeneratedRawCallsPreserveSuccessfulNativeStateChanges),
            "SELECT datatype.raw_call_holdoff_effects(tests.raw_call_address(2), 'tests.raw_call_control(integer)'::regprocedure::oid)",
            "True|True|True");

    /// <summary>
    /// A successful native context switch remains visible until the caller explicitly restores its original context.
    /// </summary>
    [TestMethod]
    public Task GeneratedRawCallsPreserveSuccessfulContextSwitches()
        => CheckAsync(nameof(GeneratedRawCallsPreserveSuccessfulContextSwitches),
            "SELECT datatype.raw_call_context_effects(tests.raw_call_address(2), 'tests.raw_call_control(integer)'::regprocedure::oid)",
            "True|True");

    /// <summary>
    /// A generated native pfree releases iterator-owned storage during query-abort disposal without replacing the original error.
    /// </summary>
    [TestMethod]
    public Task GeneratedRawCallsReleaseStorageDuringQueryAbort()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(GeneratedRawCallsReleaseStorageDuringQueryAbort), async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            var warnings = new List<PostgresNotice>();
            connection.Notice += (_, args) =>
            {
                if (args.Notice.InvariantSeverity == "WARNING") { warnings.Add(args.Notice); }
            };
            await transaction.SaveAsync("raw_release_abort", token);
            await using var command = new NpgsqlCommand(
                "SELECT 1/(value-1) FROM (SELECT datatype.raw_call_cleanup(tests.raw_call_address(4)) AS value) source", connection, transaction);
            PostgresException failure = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual(PostgresErrorCodes.DivisionByZero, failure.SqlState);
            await transaction.RollbackAsync("raw_release_abort", token);
            command.CommandText = "SELECT datatype.raw_call_cleanup_releases()";
            Assert.AreEqual(1, await command.ExecuteScalarAsync(token));
            Assert.IsEmpty(warnings);
            command.CommandText = "SELECT 42";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }, context.CancellationToken);

    private Task CheckAsync(string name, string sql, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(name, async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT 42";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }, context.CancellationToken);
}
