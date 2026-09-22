using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies server-encoded event metadata and detached address arrays in an isolated LATIN1 database.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
[DoNotParallelize]
public sealed class EventTriggerEncodingTests(TestContext context)
{
    /// <summary>
    /// Preserves expanded catalog identifiers, retained dropped addresses, and backend recovery after unrepresentable output.
    /// </summary>
    [TestMethod]
    public async Task Latin1EventSnapshotsPreserveNamesArraysAndRecover()
    {
        CancellationToken token = context.CancellationToken;
        string database = "event_latin1_" + Guid.NewGuid().ToString("N");
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
            int backend = connection.ProcessID;
            string name = new('é', 63);
            await using var command = new NpgsqlCommand($"""
                CREATE EXTENSION ankus_test;
                CREATE EVENT TRIGGER ddl_snapshot ON ddl_command_end EXECUTE FUNCTION event_values.event_action();
                CREATE EVENT TRIGGER drop_snapshot ON sql_drop EXECUTE FUNCTION event_values.event_action();
                CREATE TABLE event_values."{name}"("café" integer);
                """, connection);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT identity=format('%I.%I','event_values',$1) AND schema_name='event_values' FROM event_values.audit WHERE phase='snapshot' AND object_type='table'";
            command.Parameters.AddWithValue(name);
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.Parameters.Clear();
            command.CommandText = $"""
                DELETE FROM event_values.audit;
                COMMENT ON COLUMN event_values."{name}"."café" IS 'café';
                """;
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT identity=format('%I.%I.%I','event_values',$1,'café') AND sub_id=1 FROM event_values.audit WHERE phase='snapshot'";
            command.Parameters.AddWithValue(name);
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.Parameters.Clear();
            command.CommandText = $"""
                ALTER EVENT TRIGGER ddl_snapshot DISABLE;
                SET ankus.event_mode='retain';
                DROP TABLE event_values."{name}";
                """;
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = """
                SELECT object_name=$1 AND identity=format('%I.%I','event_values',$1)
                    AND address_names=ARRAY['event_values',$1] AND address_arguments=ARRAY[]::text[]
                FROM event_values.audit WHERE phase='snapshot' AND event='sql_drop' AND original AND object_type='table'
                """;
            command.Parameters.AddWithValue(name);
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.Parameters.Clear();
            command.CommandText = "SELECT event_values.event_retained()";
            Assert.AreEqual("sql_drop:DROP TABLE:InvalidOperationException:table:event_values.\"" + name + "\":event_values," + name + ":",
                await command.ExecuteScalarAsync(token));

            command.CommandText = "ALTER EVENT TRIGGER ddl_snapshot ENABLE; SET ankus.event_mode='emoji'";
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "CREATE TABLE event_values.rejected(id integer)";
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
            Assert.AreEqual("22P05", error.SqlState);
            command.CommandText = "SELECT to_regclass('event_values.rejected') IS NULL";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SET ankus.event_mode='audit'; CREATE TABLE event_values.recovered(id integer)";
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT identity FROM event_values.audit WHERE phase='snapshot' AND identity='event_values.recovered'";
            Assert.AreEqual("event_values.recovered", await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", administrator);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
