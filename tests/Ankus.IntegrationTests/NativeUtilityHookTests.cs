using Ankus.Testing;
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
    /// A hook registration retried after a failed load saves its own hook as the previous hook; the callback boundary turns
    /// the resulting unbounded recursion into PostgreSQL's stack-depth error instead of a native stack overflow.
    /// </summary>
    [TestMethod]
    public async Task RetriedUnguardedHookRegistrationReportsStackDepth()
    {
        CancellationToken token = context.CancellationToken;
        PostgresTestClusterOptions options = await IntegrationEnvironment.CreateOptionsAsync(token);
        await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(options, token);
        await using (NpgsqlConnection setup = await cluster.OpenConnectionAsync(token))
        {
            await using var install = new NpgsqlCommand("CREATE SCHEMA datatype; CREATE EXTENSION ankus_test WITH SCHEMA datatype", setup);
            await install.ExecuteNonQueryAsync(token);
        }

        await using NpgsqlConnection observer = await cluster.OpenConnectionAsync(token);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand("SET ankus_test.module_load = 'unguarded-hook'", connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "LOAD 'Ankus.TestExtension'";
        PostgresException failed = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        Assert.AreEqual("P7870", failed.SqlState);
        Assert.AreEqual("Module registration failed after installing its hook.", failed.MessageText);

        // The first attempt's hook remains installed. Its next invocation retries the failed registration before
        // running the handler, and the repeated installation saves the hook as its own previous hook.
        command.CommandText = "SET application_name = 'ankus self-chained registration'";
        PostgresException recursion = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        Assert.AreEqual("54001", recursion.SqlState);
        Assert.AreEqual("stack depth limit exceeded", recursion.MessageText);
        command.CommandText = "SELECT datatype.module_load_attempts() || '|' || utility_hook_values.native_utility_hook_self_chained()";
        Assert.AreEqual("2|true", await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT utility_hook_values.native_utility_hook_result()";
        string[] result = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token)).Split('|');
        Assert.AreEqual("none", result[0]);
        Assert.IsGreaterThan(10, int.Parse(result[1], System.Globalization.CultureInfo.InvariantCulture),
            "The self-chained hook must recurse until PostgreSQL's stack-depth check stops it.");

        // Restoring the original hook recovers the same backend; other backends were never affected.
        command.CommandText = "SELECT utility_hook_values.native_utility_hook_restore()";
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
        command.CommandText = "SET application_name = 'ankus restored registration'; SELECT pg_catalog.pg_backend_pid()";
        Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
        await using var peer = new NpgsqlCommand("SELECT 6 * 7", observer);
        Assert.AreEqual(42, await peer.ExecuteScalarAsync(token));
        Assert.DoesNotContain("terminated by signal", cluster.ReadServerLog());
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
