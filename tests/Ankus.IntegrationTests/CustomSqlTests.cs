using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies custom installation SQL interleaves with generated declarations and participates in extension ownership.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class CustomSqlTests(TestContext context)
{
    /// <summary>
    /// A real installation records the bootstrap, prerequisite, file, dependent-view, and final block order.
    /// </summary>
    [TestMethod]
    public Task InstallationFollowsDeclaredDependencyOrder()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(InstallationFollowsDeclaredDependencyOrder),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT array_agg(label ORDER BY position) FROM datatype.sql_install_order", connection, transaction);
                Assert.AreSequenceEqual(["bootstrap", "support", "file", "view", "final"],
                    Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(token)));
                command.CommandText = "SELECT value, message FROM ankus_sql.summary";
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(42, reader.GetInt32(0));
                Assert.AreEqual("quotes' and ; and café \\ remain literal", reader.GetString(1));
                Assert.IsFalse(await reader.ReadAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// PostgreSQL records extension ownership of SQL-created tables, views, routines and generated functions alike.
    /// </summary>
    [TestMethod]
    public Task CustomObjectsAreExtensionMembers()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(CustomObjectsAreExtensionMembers),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("""
                    WITH expected(classid, objid) AS (
                        VALUES ('pg_class'::regclass, 'ankus_sql.messages'::regclass::oid),
                               ('pg_class'::regclass, 'ankus_sql.summary'::regclass::oid),
                               ('pg_proc'::regclass, 'ankus_sql.server_default()'::regprocedure::oid),
                               ('pg_proc'::regclass, 'ankus_sql.custom_sql_value(int)'::regprocedure::oid))
                    SELECT count(*) FROM expected x
                    JOIN pg_depend d ON d.classid = x.classid AND d.objid = x.objid
                    JOIN pg_extension e ON d.refclassid = 'pg_extension'::regclass AND d.refobjid = e.oid
                    WHERE d.deptype = 'e' AND e.extname = 'ankus_test'
                    """, connection, transaction);
                Assert.AreEqual(4L, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);
}
