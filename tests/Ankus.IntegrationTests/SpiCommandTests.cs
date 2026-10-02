using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes interpolated commands in the published Native AOT extension and checks backend recovery.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class SpiCommandTests(TestContext context)
{
    /// <summary>
    /// Every standalone and scoped command role transports present values and typed NULLs.
    /// </summary>
    /// <returns>The command role, owner selection and NULL partition.</returns>
    public static IEnumerable<(int Mode, bool Scoped, bool Null)> CommandRoles()
    {
        for (int mode = 0; mode <= 10; mode++)
        {
            foreach (bool scoped in new[] { false, true })
            {
                foreach (bool nullValues in new[] { false, true })
                {
                    yield return (mode, scoped, nullValues);
                }
            }
        }
    }

    /// <summary>
    /// Verifies actual values, metadata, limits and cursor exhaustion across all command roles.
    /// </summary>
    /// <param name="mode">The SPI role.</param>
    /// <param name="scoped">Whether the command uses a scoped connection.</param>
    /// <param name="nullValues">Whether all value bindings are SQL NULL.</param>
    [TestMethod]
    [DynamicData(nameof(CommandRoles))]
    public Task CommandRolesPreserveBoundValuesAndLimits(int mode, bool scoped, bool nullValues)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(CommandRolesPreserveBoundValuesAndLimits),
            async (connection, transaction, token) =>
            {
                const string text = "雪'); DROP TABLE important; -- $2";
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand("SELECT datatype.command_role($1,$2,$3,$4,$5)", connection, transaction);
                command.Parameters.AddWithValue(mode);
                command.Parameters.AddWithValue(scoped);
                command.Parameters.AddWithValue(NpgsqlDbType.Integer, nullValues ? DBNull.Value : 42);
                command.Parameters.AddWithValue(NpgsqlDbType.Text, nullValues ? DBNull.Value : text);
                command.Parameters.AddWithValue(NpgsqlDbType.Bytea, nullValues ? DBNull.Value : new byte[] { 0, 255, 42 });
                string actual = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
                string cells = nullValues ? "<null>|<null>|<null>" : "42|" + text + "|00FF2A";
                if (mode == 8)
                {
                    using JsonDocument plan = JsonDocument.Parse(actual);
                    JsonElement node = plan.RootElement[0].GetProperty("Plan");
                    Assert.AreEqual("Function Scan", node.GetProperty("Node Type").GetString());
                    Assert.AreEqual(3, node.GetProperty("Plan Rows").GetInt32());
                }
                else
                {
                    string expected = mode switch
                    {
                        0 => "3",
                        >= 1 and <= 4 => "2|" + cells + "|23|25|17",
                        5 => nullValues ? "<null>" : "42",
                        6 => nullValues ? "<null>|<null>" : "42|" + text,
                        7 => cells,
                        _ => "2|1|0|" + cells,
                    };
                    Assert.AreEqual(expected, actual);
                }

                Assert.AreEqual(backend, connection.ProcessID);
            }, context.CancellationToken);

    /// <summary>
    /// Failed writes roll back, explicit read-only writes fail, and the same native backend remains usable.
    /// </summary>
    /// <param name="scoped">Whether to use a scoped SPI connection.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task FailedCommandsRollbackAndRecoverTheSameBackend(bool scoped)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(FailedCommandsRollbackAndRecoverTheSameBackend),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand("SELECT datatype.command_recovery($1)", connection, transaction);
                command.Parameters.AddWithValue(scoped);
                Assert.AreEqual("22012|0A000|40|42", await command.ExecuteScalarAsync(token));
                command.Parameters.Clear();
                command.CommandText = "SELECT sum(value)::int FROM ankus_command_values";
                Assert.AreEqual(40, await command.ExecuteScalarAsync(token));
                Assert.AreEqual(backend, connection.ProcessID);
            }, context.CancellationToken);

    /// <summary>
    /// Catching a command's cancellation cannot turn it into success; the original session recovers afterward.
    /// </summary>
    [TestMethod]
    public async Task SwallowedCommandCancellationRemainsSticky()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand("LOAD 'Ankus.TestExtension'", connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SET statement_timeout = '50ms'";
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT datatype.command_swallow_cancellation()";
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual("57014", error.SqlState);
        command.CommandText = "SET statement_timeout = 0; SELECT 42";
        Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Custom, mapped and nullable-array bindings retain exact values and SQL type identity.
    /// </summary>
    /// <param name="scoped">Whether to use a scoped connection.</param>
    /// <param name="nullValues">Whether the complete values are SQL NULL.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public Task CustomMappedAndArrayBindingsRetainDeclaredTypes(bool scoped, bool nullValues)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(CustomMappedAndArrayBindingsRetainDeclaredTypes),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.command_typed_values($1,$2)", connection, transaction);
                command.Parameters.AddWithValue(scoped);
                command.Parameters.AddWithValue(nullValues);
                string values = nullValues ? "<null>|<null>|<null>" : "key☃:42|mapped☃|1,<null>,3";
                Assert.AreEqual(values + "|derived_ops.key|text|integer[]", await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// A copied result survives source disposal while a command cannot rebind its stale source datum.
    /// </summary>
    /// <param name="scoped">Whether to use a scoped connection.</param>
    /// <param name="nullValue">Whether the typed raw datum is SQL NULL.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public Task RawCommandsPreserveCopiedValuesAndRejectStaleOwners(bool scoped, bool nullValue)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RawCommandsPreserveCopiedValuesAndRejectStaleOwners),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand("SELECT datatype.command_raw_owner($1,$2)", connection, transaction);
                command.Parameters.AddWithValue(nullValue ? "SELECT NULL::datum_mappings.positive" : "SELECT 42::datum_mappings.positive");
                command.Parameters.AddWithValue(scoped);
                string actual = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
                command.Parameters.Clear();
                command.CommandText = "SELECT 'datum_mappings.positive'::regtype::oid";
                uint typeOid = Assert.IsInstanceOfType<uint>(await command.ExecuteScalarAsync(token));
                Assert.AreEqual($"{typeOid}|{(nullValue ? "<null>" : "42")}|{nullValue}|ObjectDisposedException|42", actual);
                Assert.AreEqual(backend, connection.ProcessID);
            }, context.CancellationToken);

    /// <summary>
    /// All command overloads reject a scoped connection after its callback ends and preserve backend recovery.
    /// </summary>
    /// <param name="mode">The SPI execution role.</param>
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
    public Task ExpiredSessionsRejectEveryCommandRole(int mode)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ExpiredSessionsRejectEveryCommandRole),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.command_expired_session($1)", connection, transaction);
                command.Parameters.AddWithValue(mode);
                Assert.AreEqual("ObjectDisposedException|42", await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Selection keeps immutable transactions unassigned and sees writes after explicit writable intent.
    /// </summary>
    /// <param name="scoped">Whether to use a scoped connection.</param>
    /// <param name="writable">Whether a scalar command establishes writable intent first.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task CommandSelectionPreservesTransactionRoles(bool scoped, bool writable)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var setup = new NpgsqlCommand(
            "LOAD 'Ankus.TestExtension'; CREATE TEMP TABLE ankus_command_snapshot(value int); INSERT INTO ankus_command_snapshot VALUES (40)", connection);
        Assert.AreEqual(1, await setup.ExecuteNonQueryAsync(token));
        int backend = connection.ProcessID;
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await using var command = new NpgsqlCommand("SELECT datatype.command_snapshot($1,$2)", connection, transaction);
            command.Parameters.AddWithValue(scoped);
            command.Parameters.AddWithValue(writable);
            Assert.AreEqual(writable ? "True|40|42" : "False|0A000|40", await command.ExecuteScalarAsync(token));
            await transaction.RollbackAsync(token);
        }

        await using NpgsqlTransaction fresh = await connection.BeginTransactionAsync(token);
        await using var check = new NpgsqlCommand("SELECT datatype.command_snapshot($1,false)", connection, fresh);
        check.Parameters.AddWithValue(scoped);
        Assert.AreEqual("False|0A000|40", await check.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, connection.ProcessID);
    }
}
