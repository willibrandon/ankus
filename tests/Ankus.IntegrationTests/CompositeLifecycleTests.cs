using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Proves that the public composite sample retains type identity and ownership across extension DDL.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class CompositeLifecycleTests(TestContext context)
{
    /// <summary>
    /// Named tuple functions, arrays, sets, and anonymous records survive relocation and a fresh type OID.
    /// </summary>
    [TestMethod]
    public Task CompositeSampleRelocatesAndReinstalls()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(CompositeSampleRelocatesAndReinstalls), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                CREATE SCHEMA composites_first;
                CREATE SCHEMA composites_second;
                CREATE EXTENSION ankus_composites WITH SCHEMA composites_first;
                SELECT (composites_first.birthday(ROW('Ada',3)::composites_first.dog)).age
                """, connection, transaction);
            Assert.AreEqual(4, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT 'composites_first.dog'::regtype::oid";
            uint originalOid = Assert.IsInstanceOfType<uint>(await command.ExecuteScalarAsync(token));
            command.CommandText = """
                SELECT count(*) FROM pg_depend d JOIN pg_extension e ON e.oid=d.refobjid
                WHERE d.refclassid='pg_extension'::regclass AND d.classid='pg_type'::regclass
                    AND d.objid='composites_first.dog'::regtype AND d.deptype='e' AND e.extname='ankus_composites'
                """;
            Assert.AreEqual(1L, await command.ExecuteScalarAsync(token));
            command.CommandText = """
                ALTER EXTENSION ankus_composites SET SCHEMA composites_second;
                SELECT string_agg(name || ':' || age, ',' ORDER BY age)
                FROM composites_second.birthdays(ROW('Ada',3)::composites_second.dog,3)
                """;
            Assert.AreEqual("Ada:4,Ada:5,Ada:6", await command.ExecuteScalarAsync(token));
            command.CommandText = """
                SELECT array_send(composites_second.echo_dogs(ARRAY[NULL,ROW('Bo',4)::composites_second.dog]))
                    = array_send(ARRAY[NULL,ROW('Bo',4)::composites_second.dog])
                """;
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT name || ':' || age FROM composites_second.make_record('record',7) AS r(name text,age integer)";
            Assert.AreEqual("record:7", await command.ExecuteScalarAsync(token));
            command.CommandText = """
                DROP EXTENSION ankus_composites;
                SELECT to_regtype('composites_second.dog') IS NULL
                    AND NOT EXISTS(SELECT FROM pg_proc WHERE pronamespace='composites_second'::regnamespace)
                """;
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = """
                CREATE EXTENSION ankus_composites WITH SCHEMA composites_first;
                SELECT 'composites_first.dog'::regtype::oid
                """;
            uint newOid = Assert.IsInstanceOfType<uint>(await command.ExecuteScalarAsync(token));
            Assert.AreNotEqual(originalOid, newOid);
            command.CommandText = "SELECT (composites_first.birthday(ROW('new',11)::composites_first.dog)).age";
            Assert.AreEqual(12, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);
}
