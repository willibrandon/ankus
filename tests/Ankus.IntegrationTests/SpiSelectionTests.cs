using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies pgrx-compatible SPI snapshot selection against real PostgreSQL transactions.
/// </summary>
/// <param name="context">The per-test cancellation context.</param>
[TestClass]
public sealed class SpiSelectionTests(TestContext context)
{
    /// <summary>
    /// Empty results retain metadata without producing placeholder rows, across raw and managed lifetimes.
    /// </summary>
    /// <param name="mode">The selection transport.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    public Task EmptySelectionsRetainColumnIdentity(int mode)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(EmptySelectionsRetainColumnIdentity),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.selection_empty_rows($1)", connection, transaction);
                command.Parameters.AddWithValue(mode);
                Assert.AreEqual("0|0|value:23|label:25", await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// A kept session plan remains selectable across callback and transaction boundaries without allocating an identity.
    /// </summary>
    [TestMethod]
    public async Task KeptPlanSelectionSurvivesItsOriginalTransaction()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand("SELECT datatype.selection_cache_plan()", connection);
        await command.ExecuteNonQueryAsync(token);
        try
        {
            command.CommandText = "SELECT datatype.selection_cached_value(40)";
            Assert.AreEqual("42|False", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT datatype.selection_cached_value(10)";
            Assert.AreEqual("12|False", await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }
        finally
        {
            command.CommandText = "SELECT datatype.selection_dispose_plan()";
            await command.ExecuteNonQueryAsync(token);
        }
    }

    /// <summary>
    /// Pure selections remain unassigned and reject mutation while recovering the original backend session.
    /// </summary>
    /// <param name="mode">The plain, session, prepared, raw or cursor transport.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    [DataRow(10)]
    [DataRow(11)]
    public async Task SelectionPreservesImmutableTransaction(int mode)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await CreateValuesAsync(connection, token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await using var command = new NpgsqlCommand("SELECT pg_current_xact_id_if_assigned() IS NULL", connection, transaction);
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
        command.CommandText = "SELECT datatype.selection_immutable($1)";
        command.Parameters.AddWithValue(mode);
        Assert.AreEqual("False|42|0A000|0A000|22012|40|False", await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT sum(value)::int, pg_current_xact_id_if_assigned() IS NULL FROM pg_temp.ankus_selection_values";
        command.Parameters.Clear();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        Assert.IsTrue(await reader.ReadAsync(token));
        Assert.AreEqual(40, reader.GetInt32(0));
        Assert.IsTrue(reader.GetBoolean(1));
        Assert.IsFalse(await reader.ReadAsync(token));
    }

    /// <summary>
    /// A writable scalar read establishes intent and selection uses fresh snapshots until the transaction ends.
    /// </summary>
    /// <param name="mode">The selection transport.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    [DataRow(10)]
    [DataRow(11)]
    public async Task WritableIntentSelectsFreshSnapshotsAndResetsAfterRollback(int mode)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await CreateValuesAsync(connection, token);
        int backend = connection.ProcessID;
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await using var command = new NpgsqlCommand("SELECT datatype.selection_writable($1, true)", connection, transaction);
            command.Parameters.AddWithValue(mode);
            Assert.AreEqual("True|40|42|0A000|True", await command.ExecuteScalarAsync(token));
            await transaction.RollbackAsync(token);
        }

        await using NpgsqlTransaction fresh = await connection.BeginTransactionAsync(token);
        await using var check = new NpgsqlCommand("SELECT datatype.selection_immutable($1)", connection, fresh);
        check.Parameters.AddWithValue(mode);
        Assert.AreEqual("False|42|0A000|0A000|22012|40|False", await check.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Caller writes remain visible through recovery children and scoped connection or prepared-plan owners.
    /// </summary>
    /// <param name="mode">The selection transport.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    [DataRow(10)]
    [DataRow(11)]
    public async Task SelectionObservesCallerMutation(int mode)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await CreateValuesAsync(connection, token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await using var write = new NpgsqlCommand("INSERT INTO pg_temp.ankus_selection_values VALUES (2)", connection, transaction);
        Assert.AreEqual(1, await write.ExecuteNonQueryAsync(token));
        await using var command = new NpgsqlCommand("SELECT datatype.selection_writable($1, false)", connection, transaction);
        command.Parameters.AddWithValue(mode);
        Assert.AreEqual("True|2|44|0A000|True", await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// A nested managed rollback restores values without hiding the writable caller transaction.
    /// </summary>
    [TestMethod]
    public async Task NestedRecoveryPreservesOuterSelectionState()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await CreateValuesAsync(connection, token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await using var command = new NpgsqlCommand("INSERT INTO pg_temp.ankus_selection_values VALUES (2)", connection, transaction);
        Assert.AreEqual(1, await command.ExecuteNonQueryAsync(token));
        command.CommandText = "SELECT datatype.selection_nested()";
        Assert.AreEqual("42|42|Roll back the nested mutation.", await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT sum(value)::int FROM pg_temp.ankus_selection_values";
        Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Limits, typed NULLs and native lifetimes survive plan and session cleanup.
    /// </summary>
    /// <param name="mode">The managed or raw transport.</param>
    /// <param name="limit">The requested limit.</param>
    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(0, 1)]
    [DataRow(1, 0)]
    [DataRow(1, 1)]
    [DataRow(2, 0)]
    [DataRow(2, 1)]
    [DataRow(3, 0)]
    [DataRow(3, 1)]
    [DataRow(4, 0)]
    [DataRow(4, 1)]
    [DataRow(5, 0)]
    [DataRow(5, 1)]
    [DataRow(6, 0)]
    [DataRow(6, 1)]
    [DataRow(7, 0)]
    [DataRow(7, 1)]
    public Task SelectionPreservesParametersLimitsAndOwnedResults(int mode, int limit)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SelectionPreservesParametersLimitsAndOwnedResults),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.selection_rows($1, $2)", connection, transaction);
                command.Parameters.AddWithValue(mode);
                command.Parameters.AddWithValue(limit);
                string expected = limit == 0
                    ? "3|3|value:23|label:25|40:NULL,41:NULL,42:NULL"
                    : "1|1|value:23|label:25|40:NULL";
                if (mode >= 4)
                {
                    expected += "|expired";
                }

                Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Swallowing cancellation from selection cannot make the callback succeed, and the backend remains usable.
    /// </summary>
    [TestMethod]
    public async Task SelectionCancellationRemainsSticky()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand("LOAD 'Ankus.TestExtension'", connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SET statement_timeout = '50ms'";
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT datatype.selection_swallow_cancellation()";
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        Assert.AreEqual("57014", error.SqlState);
        command.CommandText = "SET statement_timeout = 0; SELECT 42";
        Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Creates committed session-local data before the transaction whose selection policy is tested.
    /// Completes test extension initialization before observing the fresh transaction's selection state.
    /// </summary>
    /// <param name="connection">The session connection.</param>
    /// <param name="token">The test cancellation token.</param>
    /// <returns>The setup completion task.</returns>
    private static async Task CreateValuesAsync(NpgsqlConnection connection, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(
            "LOAD 'Ankus.TestExtension'; CREATE TEMP TABLE ankus_selection_values (value int); INSERT INTO ankus_selection_values VALUES (40)", connection);
        Assert.AreEqual(1, await command.ExecuteNonQueryAsync(token));
    }
}
