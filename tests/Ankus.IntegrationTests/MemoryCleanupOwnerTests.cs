using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Exercises native state-owner protection when abort cleanup runs outside the owning memory tree.
/// </summary>
/// <param name="context">The test cancellation and diagnostic context.</param>
[TestClass]
public sealed class MemoryCleanupOwnerTests(TestContext context)
{
    /// <summary>
    /// Cleanup owned by the error handler cannot reenter guarded native operations and corrupt the caller's error stack.
    /// </summary>
    /// <param name="operation">Explicit child reset or error flushing of a root/child callback.</param>
    /// <param name="trigger">The original operation's expected result.</param>
    /// <param name="alive">Whether the checked context survives explicit reset.</param>
    [TestMethod]
    [DataRow(0, "reset", true)]
    [DataRow(1, "XX000", false)]
    [DataRow(2, "XX000", false)]
    public Task ErrorHandlerCallbacksPreserveNativeErrorStackAndRetainedResources(int operation, string trigger, bool alive)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ErrorHandlerCallbacksPreserveNativeErrorStackAndRetainedResources),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand($"SELECT datatype.memory_error_cleanup_phase({operation})", connection, transaction);
                Assert.AreEqual($"{trigger}|pending:False|55006:Guarded memory operations are unavailable during ErrorContext cleanup|55006:Guarded SPI operations are unavailable during ErrorContext cleanup|False|{alive}|73|42|42",
                    await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT count(*) FROM pg_backend_memory_contexts WHERE name = 'SPI Plan' OR ident IN ('error-handler callback', 'independent error-handler payload')";
                Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT 42";
                Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
                Assert.AreEqual(backend, connection.ProcessID);
            }, context.CancellationToken);

    /// <summary>
    /// Named PostgreSQL infrastructure cannot be reclaimed through the checked memory API.
    /// </summary>
    /// <param name="kind">The predefined root, portal, error, cache, message, or transaction owner.</param>
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    public Task InfrastructureResetsAreRejectedAndOwnedChildrenRemainUsable(int kind)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(InfrastructureResetsAreRejectedAndOwnedChildrenRemainUsable),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand($"SELECT datatype.memory_infrastructure_protection({kind})", connection, transaction);
                Assert.AreEqual("55000,55000,55000|42|True", await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT 42";
                Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// SPI recovery keeps copied errors outside ErrorContext while its native flush runs.
    /// </summary>
    [TestMethod]
    public Task ErrorContextSpiFailurePreservesDiagnosticsAndRestoresCaller()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ErrorContextSpiFailurePreservesDiagnosticsAndRestoresCaller),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand("SELECT datatype.memory_error_context_spi()", connection, transaction);
                Assert.AreEqual("22012|division by zero|ErrorContext|True|42", await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT 42";
                Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
                Assert.AreEqual(backend, connection.ProcessID);
            }, context.CancellationToken);

    /// <summary>
    /// Native recovery never restores a pointer to an ErrorContext child reclaimed while flushing the caught error.
    /// </summary>
    /// <param name="spi">Whether the error comes from the SPI guard.</param>
    /// <param name="state">The exact SQLSTATE.</param>
    [TestMethod]
    [DataRow(false, "XX000")]
    [DataRow(true, "22012")]
    public Task ErrorContextChildFailureRestoresLiveAncestorAndOriginalCaller(bool spi, string state)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ErrorContextChildFailureRestoresLiveAncestorAndOriginalCaller),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand($"SELECT datatype.memory_error_context_child({spi})", connection, transaction);
                Assert.AreEqual($"{state}|False|ErrorContext|True|42", await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT 42";
                Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
                Assert.AreEqual(backend, connection.ProcessID);
            }, context.CancellationToken);

    /// <summary>
    /// A PostgreSQL consumer error releases managed state without permitting recursive destruction of its native ancestor.
    /// </summary>
    /// <param name="aggregate">Whether cleanup owns aggregate state rather than an iterator.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task NativeAbortCleanupProtectsOwnerOutsideCurrentContext(bool aggregate)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NativeAbortCleanupProtectsOwnerOutsideCurrentContext),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand("SELECT datatype.memory_cleanup_status(true)", connection, transaction);
                await command.ExecuteNonQueryAsync(token);
                await transaction.SaveAsync("cleanup_owner", token);
                command.CommandText = aggregate
                    ? "SELECT datatype.memory_cleanup_sum(1 / (v - 2)) FROM (VALUES (1), (2)) AS input(v)"
                    : "SELECT 1 / (v - 1) FROM (SELECT datatype.memory_cleanup_sequence() AS v OFFSET 0) AS input";
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
                Assert.AreEqual("22012", error.SqlState);
                await transaction.RollbackAsync("cleanup_owner", token);
                command.CommandText = "SELECT datatype.memory_cleanup_status(false)";
                Assert.AreEqual("False:55000,55000,55000,55000", await command.ExecuteScalarAsync(token));
                command.CommandText = aggregate
                    ? "SELECT datatype.memory_cleanup_sum(v) FROM (VALUES (20), (22)) AS input(v)"
                    : "SELECT sum(v) FROM datatype.memory_cleanup_sequence() AS v";
                Assert.AreEqual(aggregate ? 42L : 3L, Convert.ToInt64(await command.ExecuteScalarAsync(token), System.Globalization.CultureInfo.InvariantCulture));
                command.CommandText = "SELECT 42";
                Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
                Assert.AreEqual(backend, connection.ProcessID);
            }, context.CancellationToken);
}
