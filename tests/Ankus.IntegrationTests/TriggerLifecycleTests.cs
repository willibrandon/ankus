using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Exercises the public trigger sample's generated dependencies and extension lifecycle in PostgreSQL.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class TriggerLifecycleTests(TestContext context)
{
    /// <summary>
    /// Triggers normalize and skip rows before and after extension relocation and reinstall with new relation identity.
    /// </summary>
    [TestMethod]
    public Task TriggerSampleRelocatesAndReinstalls()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(TriggerSampleRelocatesAndReinstalls), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                CREATE SCHEMA triggers_first;
                CREATE SCHEMA triggers_second;
                CREATE EXTENSION ankus_triggers WITH SCHEMA triggers_first;
                INSERT INTO triggers_first.pets(name) VALUES (' Ada '), (' '), (NULL), ('Bo');
                SELECT string_agg(name || ':' || visits, ',' ORDER BY name) FROM triggers_first.pets
                """, connection, transaction);
            Assert.AreEqual("Ada:0,Bo:0", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT 'triggers_first.pets'::regclass::oid";
            uint originalOid = Assert.IsInstanceOfType<uint>(await command.ExecuteScalarAsync(token));
            command.CommandText = """
                ALTER EXTENSION ankus_triggers SET SCHEMA triggers_second;
                UPDATE triggers_second.pets SET name=' Cy ', visits=visits+1 WHERE name='Ada';
                UPDATE triggers_second.pets SET name=' ', visits=visits+1 WHERE name='Bo';
                SELECT string_agg(name || ':' || visits, ',' ORDER BY name) FROM triggers_second.pets
                """;
            Assert.AreEqual("Bo:0,Cy:1", await command.ExecuteScalarAsync(token));
            command.CommandText = """
                DROP EXTENSION ankus_triggers;
                SELECT to_regclass('triggers_second.pets') IS NULL
                    AND NOT EXISTS(SELECT FROM pg_proc WHERE pronamespace='triggers_second'::regnamespace)
                """;
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = """
                CREATE EXTENSION ankus_triggers WITH SCHEMA triggers_first;
                SELECT 'triggers_first.pets'::regclass::oid
                """;
            uint newOid = Assert.IsInstanceOfType<uint>(await command.ExecuteScalarAsync(token));
            Assert.AreNotEqual(originalOid, newOid);
            command.CommandText = "INSERT INTO triggers_first.pets VALUES (' 新しい ',7) RETURNING name || ':' || visits";
            Assert.AreEqual("新しい:7", await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);
}
