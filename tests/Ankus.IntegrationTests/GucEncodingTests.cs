using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies native configuration metadata and hook conversions in a non-UTF8 database.
/// </summary>
/// <param name="context">The test cancellation and diagnostic context.</param>
[TestClass]
[DoNotParallelize]
public sealed class GucEncodingTests(TestContext context)
{
    /// <summary>
    /// LATIN1 values survive check normalization, assign restoration, show, and reporting outside transactions.
    /// </summary>
    [TestMethod]
    public async Task Latin1HooksPreserveValuesDuringRollbackAndReporting()
    {
        CancellationToken token = context.CancellationToken;
        string database = "guc_latin1_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection administrator = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using (var create = new NpgsqlCommand(
            $"CREATE DATABASE {database} TEMPLATE template0 ENCODING 'LATIN1' LC_COLLATE 'C' LC_CTYPE 'C'", administrator))
        {
            await create.ExecuteNonQueryAsync(token);
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(administrator.ConnectionString) { Database = database, Pooling = false };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(token);
            await using var command = new NpgsqlCommand("CREATE EXTENSION ankus_test; LOAD 'Ankus.TestExtension'", connection);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT extra_desc FROM pg_settings WHERE name = 'ankus_guc.enabled'";
            Assert.AreEqual("A retained description with café.", await command.ExecuteScalarAsync(token));
            command.CommandText = "SET ankus_guc.text = 'café'; SELECT guc_retained_text(true)";
            Assert.AreEqual("café", await command.ExecuteScalarAsync(token));
            command.CommandText = "SET ankus_guc.hook_text = '  café  '; SHOW ankus_guc.hook_text";
            Assert.AreEqual("text=café;extra=2020636166C3A92020", await command.ExecuteScalarAsync(token));

            // Use only ASCII protocol data after switching the client encoding: PostgreSQL constructs
            // non-ASCII values, and assertions return Boolean results rather than encoded text.
            command.CommandText = "SET client_encoding = 'LATIN1'; BEGIN; SELECT set_config('ankus_guc.hook_text', chr(233), true) IS NOT NULL";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "ROLLBACK; SET ankus_guc.reported = '2'";
            await command.ExecuteNonQueryAsync(token);
            Assert.AreEqual("report=2;sql=unavailable", connection.PostgresParameters["ankus_guc.reported"]);
            command.CommandText = "SELECT guc_hook_values() = ('False|10|1.25|caf' || chr(233) || '|18446744073709551615')";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT current_setting('ankus_guc.hook_text') = ('text=caf' || chr(233) || ';extra=2020636166C3A92020')";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "RESET ankus_guc.text; SELECT guc_retained_text(false) = ('caf' || chr(233))";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SET ankus_guc.hook_int = '13'";
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
            Assert.AreEqual("22P05", error.SqlState);
            command.CommandText = "SET ankus_guc.hook_int = '20'; SHOW ankus_guc.hook_int";
            Assert.AreEqual("integer=20;extra=14000000", await command.ExecuteScalarAsync(token));
            command.CommandText = "SET ankus_guc.control = 'unrepresentable'";
            await command.ExecuteNonQueryAsync(token);
            for (int iteration = 0; iteration < 25; iteration++)
            {
                command.CommandText = "SET ankus_guc.hook_text = 'ascii'";
                PostgresException conversion = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
                Assert.AreEqual("22P05", conversion.SqlState);
                command.CommandText = "SELECT current_setting('ankus_guc.hook_text') = ('text=caf' || chr(233) || ';extra=2020636166C3A92020')";
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            }

            command.CommandText = "RESET ankus_guc.control; SET ankus_guc.hook_text = 'recovered'; SHOW ankus_guc.hook_text";
            Assert.AreEqual("text=recovered;extra=7265636F7665726564", await command.ExecuteScalarAsync(token));
            command.CommandText = "SET client_encoding = 'UTF8'; SELECT 42";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", administrator);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
