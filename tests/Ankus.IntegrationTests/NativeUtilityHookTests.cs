using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Proves native callbacks can run read-only SQL where PostgreSQL provides no active snapshot.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class NativeUtilityHookTests(TestContext context)
{
    /// <summary>
    /// Read-only scalar and cursor SQL inside a utility hook take the transaction snapshot instead of crashing the backend.
    /// </summary>
    /// <param name="mode">A read-only scalar query or a read-only cursor.</param>
    /// <param name="expected">The values observed by the hook.</param>
    [TestMethod]
    [DataRow(1, "42")]
    [DataRow(2, "1,2,3")]
    public async Task UtilityHookReadOnlySqlUsesTransactionSnapshot(int mode, string expected)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand("SELECT utility_hook_values.native_utility_hook_install($1)", connection);
        command.Parameters.AddWithValue(mode);
        try
        {
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.Parameters.Clear();

            // SET runs in a transaction without an XID or an active snapshot, so the hook's SQL is read-only.
            command.CommandText = "SET application_name = 'ankus utility hook'";
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT utility_hook_values.native_utility_hook_result()";
            Assert.AreEqual(expected + "|1", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT pg_catalog.pg_backend_pid()";
            Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
        }
        finally
        {
            command.Parameters.Clear();
            command.CommandText = "SELECT utility_hook_values.native_utility_hook_restore()";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(CancellationToken.None)));
        }
    }

    /// <summary>
    /// A callback takes the transaction snapshot only when it runs SQL, preserving REPEATABLE READ snapshot timing otherwise.
    /// </summary>
    /// <param name="mode">No SQL, or a read-only scalar query.</param>
    /// <param name="visible">The rows a concurrent commit after the utility statement contributes to the transaction.</param>
    [TestMethod]
    [DataRow(0, 1L)]
    [DataRow(1, 0L)]
    public async Task UtilityHookSnapshotTimingFollowsSqlUse(int mode, long visible)
    {
        CancellationToken token = context.CancellationToken;
        string table = "utility_hook_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection observer = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using NpgsqlConnection writer = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var write = new NpgsqlCommand($"CREATE TABLE {table} (value integer)", writer);
        await write.ExecuteNonQueryAsync(token);
        await using var command = new NpgsqlCommand("SELECT utility_hook_values.native_utility_hook_install($1)", observer);
        command.Parameters.AddWithValue(mode);
        try
        {
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.Parameters.Clear();
            command.CommandText = "BEGIN ISOLATION LEVEL REPEATABLE READ";
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SET LOCAL application_name = 'ankus snapshot timing'";
            await command.ExecuteNonQueryAsync(token);
            write.CommandText = $"INSERT INTO {table} VALUES (1)";
            await write.ExecuteNonQueryAsync(token);
            command.CommandText = $"SELECT count(*) FROM {table}";
            Assert.AreEqual(visible, await command.ExecuteScalarAsync(token));
            command.CommandText = "COMMIT";
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT utility_hook_values.native_utility_hook_result()";
            Assert.AreEqual((mode == 0 ? "none" : "42") + "|1", await command.ExecuteScalarAsync(token));
        }
        finally
        {
            command.Parameters.Clear();
            command.CommandText = "ROLLBACK";
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            command.CommandText = "SELECT utility_hook_values.native_utility_hook_restore()";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(CancellationToken.None)));
            write.CommandText = $"DROP TABLE {table}";
            await write.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
