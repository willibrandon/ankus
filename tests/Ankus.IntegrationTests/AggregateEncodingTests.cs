using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies aggregate state, initial conditions, comparisons and returned values under a LATIN1 backend.
/// </summary>
/// <param name="context">The current test cancellation context.</param>
[TestClass]
[DoNotParallelize]
public sealed class AggregateEncodingTests(TestContext context)
{
    /// <summary>
    /// Server encoding preserves accented state and native ordering while unrepresentable final output fails and recovers.
    /// </summary>
    [TestMethod]
    public async Task Latin1AggregateStateOrderingAndOutputPreserveEncoding()
    {
        CancellationToken token = context.CancellationToken;
        string database = "aggregate_latin1_" + Guid.NewGuid().ToString("N");
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
            await using var command = new NpgsqlCommand("CREATE EXTENSION ankus_test", connection);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT aggregate_values.text_values(v ORDER BY v) FROM (VALUES('café'),('é')) AS input(v)";
            Assert.AreEqual("a'b\\café:caféé", await command.ExecuteScalarAsync(token));
            command.CommandText = """
                SELECT aggregate_values.ordered_text() WITHIN GROUP(ORDER BY v COLLATE "C" DESC NULLS FIRST)
                    IS NOT DISTINCT FROM array_agg(v ORDER BY v COLLATE "C" DESC NULLS FIRST)
                FROM (VALUES('é'),('café'),('B'),(NULL)) AS input(v)
                """;
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT aggregate_values.generated_text(v) FROM (VALUES(1),(1)) AS input(v)";
            Assert.AreEqual("cafécafé", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT aggregate_values.generated_text(v) FROM (VALUES(1),(2)) AS input(v)";
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual("22P05", error.SqlState);
            command.CommandText = "SELECT aggregate_values.generated_text(v) FROM (VALUES(1)) AS input(v)";
            Assert.AreEqual("café", await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", administrator);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
