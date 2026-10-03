using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes pgrx's SPI and SPI table examples through an independently published extension.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
[DoNotParallelize]
public sealed class SpiExampleTests(TestContext context)
{
    /// <summary>
    /// Reads missing and nullable values and binds hostile text through both insertion shapes.
    /// </summary>
    [TestMethod]
    public Task SpiSampleReadsAndWritesBoundValues()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SpiSampleReadsAndWritesBoundValues), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, "CREATE EXTENSION ankus_spi", token);
            Assert.AreEqual("This is a test", await Scalar<string>(connection, transaction, "SELECT spi.spi_query_by_id(1)", token));
            Assert.AreEqual(2L, await Scalar<long>(connection, transaction, "SELECT spi.spi_query_title('Hello There!')", token));
            Assert.IsTrue(await Scalar<bool>(connection, transaction, "SELECT spi.spi_query_random_id() IN (1,2,3)", token));
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT spi.spi_query_by_id(9223372036854775807) IS NULL
                    AND spi.spi_query_title('absent') IS NULL
                    AND spi.spi_query_by_id(NULL) IS NULL AND spi.spi_query_title(NULL) IS NULL
                    AND spi.spi_insert_title(NULL) IS NULL
                """, token));
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction, "SELECT count(*) FROM spi.spi_insert_title2(NULL)", token));
            Assert.AreEqual(3L, await Scalar<long>(connection, transaction, "SELECT count(*) FROM spi.spi_example", token));

            string[] titles = ["", "O'Brien'); DROP TABLE spi.spi_example; -- 雪", "Hello\nPostgreSQL"];
            foreach (string title in titles)
            {
                long id = await Scalar<long>(connection, transaction, "SELECT spi.spi_insert_title($1)", token, new NpgsqlParameter { Value = title });
                Assert.AreEqual(title, await Scalar<string>(connection, transaction, "SELECT spi.spi_query_by_id($1)", token, new NpgsqlParameter { Value = id }));
                Assert.AreEqual(id, await Scalar<long>(connection, transaction, "SELECT spi.spi_query_title($1)", token, new NpgsqlParameter { Value = title }));
                string[] returned = await Scalar<string[]>(connection, transaction,
                    "SELECT ARRAY[id::text, title] FROM spi.spi_insert_title2(title => $1)", token, new NpgsqlParameter { Value = title });
                Assert.HasCount(2, returned);
                Assert.AreEqual(title, returned[1]);
                Assert.AreEqual(title, await Scalar<string>(connection, transaction,
                    "SELECT title FROM spi.spi_example WHERE id=$1::bigint", token, new NpgsqlParameter { Value = returned[0] }));
            }

            Assert.AreEqual(9L, await Scalar<long>(connection, transaction, "SELECT count(*) FROM spi.spi_example", token));
            await Execute(connection, transaction, "INSERT INTO spi.spi_example(id,title) VALUES (-1,NULL)", token);
            Assert.IsTrue(await Scalar<bool>(connection, transaction, "SELECT spi.spi_query_by_id(-1) IS NULL", token));
            await Execute(connection, transaction, "TRUNCATE spi.spi_example", token);
            Assert.IsTrue(await Scalar<bool>(connection, transaction, "SELECT spi.spi_query_random_id() IS NULL", token));
            await Execute(connection, transaction, "INSERT INTO spi.spi_example(id,title) VALUES (42,'only row')", token);
            Assert.AreEqual(42L, await Scalar<long>(connection, transaction, "SELECT spi.spi_query_random_id()", token));
        }, context.CancellationToken);

    /// <summary>
    /// Preserves PostgreSQL constraint diagnostics and rolls back each failed insertion before reuse.
    /// </summary>
    /// <param name="function">The scalar or table insertion function.</param>
    [TestMethod]
    [DataRow("spi_insert_title")]
    [DataRow("spi_insert_title2")]
    public Task SpiSampleRollsBackFailedWrites(string function)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SpiSampleRollsBackFailedWrites), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, """
                CREATE EXTENSION ankus_spi;
                ALTER TABLE spi.spi_example ADD CONSTRAINT accepted_title CHECK (title <> 'rejected');
                """, token);
            await transaction.SaveAsync("rejected_title", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
                Execute(connection, transaction, $"SELECT * FROM spi.{function}('rejected')", token));
            Assert.AreEqual("23514", error.SqlState);
            Assert.AreEqual("accepted_title", error.ConstraintName);
            await transaction.RollbackAsync("rejected_title", token);
            Assert.AreEqual(3L, await Scalar<long>(connection, transaction, "SELECT count(*) FROM spi.spi_example", token));
            long id = await Scalar<long>(connection, transaction, "SELECT spi.spi_insert_title('accepted')", token);
            Assert.AreEqual("accepted", await Scalar<string>(connection, transaction,
                "SELECT spi.spi_query_by_id($1)", token, new NpgsqlParameter { Value = id }));
            Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, transaction, "SELECT pg_backend_pid()", token));
        }, context.CancellationToken);

    /// <summary>
    /// Compares exact catalog identities and reuses detached cursor text after native resources close.
    /// </summary>
    [TestMethod]
    public Task SpiSampleDetachesCatalogAndCursorResults()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SpiSampleDetachesCatalogAndCursorResults), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, "CREATE EXTENSION ankus_spi", token);
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction, """
                WITH actual AS (SELECT * FROM spi.spi_return_query()),
                expected AS (SELECT oid, relname::text || '-pg' ||
                    (current_setting('server_version_num')::integer / 10000)::text AS name FROM pg_class)
                SELECT count(*) FROM ((TABLE actual EXCEPT ALL TABLE expected)
                    UNION ALL (TABLE expected EXCEPT ALL TABLE actual)) AS differences
                """, token));
            long portals = await Scalar<long>(connection, transaction, "SELECT count(*) FROM pg_cursors", token);
            Assert.AreSequenceEqual<string>([.. Enumerable.Repeat("hello", 10)], await Scalar<string[]>(connection, transaction,
                "SELECT array_agg(spi.issue1209_fixed()) FROM generate_series(1,10)", token));
            Assert.AreEqual(portals, await Scalar<long>(connection, transaction, "SELECT count(*) FROM pg_cursors", token));
            Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, transaction, "SELECT pg_backend_pid()", token));
        }, context.CancellationToken);

    /// <summary>
    /// Preserves all seeded rows, exact filtering, nullable fields and empty result sets.
    /// </summary>
    [TestMethod]
    public Task SpiSampleMapsAndFiltersRows()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SpiSampleMapsAndFiltersRows), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, "CREATE EXTENSION ankus_spi", token);
            Assert.AreSequenceEqual<string>([
                "Fido:3:Labrador:21", "Spot:5:Poodle:35", "Rover:7:Golden Retriever:49", "Snoopy:9:Beagle:63",
                "Lassie:11:Collie:77", "Scooby:13:Great Dane:91", "Moomba:15:Labrador:105",
            ], await Scalar<string[]>(connection, transaction, """
                SELECT array_agg(format('%s:%s:%s:%s',dog_name,dog_age,dog_breed,human_age) ORDER BY dog_age)
                FROM spi_srf.calculate_human_years()
                """, token));
            Assert.AreSequenceEqual<string>(["Fido:3:Labrador", "Moomba:15:Labrador"], await Scalar<string[]>(connection, transaction, """
                SELECT array_agg(format('%s:%s:%s',dog_name,dog_age,dog_breed) ORDER BY dog_age)
                FROM spi_srf.filter_by_breed('Labrador')
                """, token));
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction,
                "SELECT count(*) FROM spi_srf.filter_by_breed('Labrador'' OR true --')", token));
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction, "SELECT count(*) FROM spi_srf.filter_by_breed(NULL)", token));
            await Execute(connection, transaction, """
                INSERT INTO spi_srf.dog_daycare VALUES (NULL,0,NULL), (NULL,NULL,'nullable');
                """, token);
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT dog_name IS NULL AND dog_age IS NULL AND dog_breed='nullable'
                FROM spi_srf.filter_by_breed('nullable')
                """, token));
            await Execute(connection, transaction, "DELETE FROM spi_srf.dog_daycare WHERE dog_age IS NULL", token);
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT dog_name IS NULL AND dog_breed IS NULL AND human_age=0
                FROM spi_srf.calculate_human_years() WHERE dog_age=0
                """, token));
            await Execute(connection, transaction, "TRUNCATE spi_srf.dog_daycare", token);
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction, "SELECT count(*) FROM spi_srf.calculate_human_years()", token));
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction, "SELECT count(*) FROM spi_srf.filter_by_breed('Labrador')", token));
        }, context.CancellationToken);

    /// <summary>
    /// Rejects an absent required age or arithmetic overflow and recovers in the same backend.
    /// </summary>
    /// <param name="age">A SQL age expression outside the mapping's contract.</param>
    [TestMethod]
    [DataRow("NULL")]
    [DataRow("306783379")]
    [DataRow("-306783379")]
    public Task SpiSampleRejectsInvalidAgesAndRecovers(string age)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SpiSampleRejectsInvalidAgesAndRecovers), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, "CREATE EXTENSION ankus_spi", token);
            await transaction.SaveAsync("invalid_age", token);
            await Execute(connection, transaction, $"INSERT INTO spi_srf.dog_daycare VALUES ('invalid',{age},'invalid')", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
                Execute(connection, transaction, "SELECT * FROM spi_srf.calculate_human_years()", token));
            Assert.AreEqual("38000", error.SqlState);
            await transaction.RollbackAsync("invalid_age", token);
            Assert.AreEqual(441L, await Scalar<long>(connection, transaction, "SELECT sum(human_age) FROM spi_srf.calculate_human_years()", token));
            await Execute(connection, transaction, """
                TRUNCATE spi_srf.dog_daycare;
                INSERT INTO spi_srf.dog_daycare VALUES ('minimum',-306783378,NULL), ('maximum',306783378,NULL);
                """, token);
            Assert.AreSequenceEqual<int>([-2147483646, 2147483646], await Scalar<int[]>(connection, transaction,
                "SELECT array_agg(human_age ORDER BY dog_age) FROM spi_srf.calculate_human_years()", token));
            Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, transaction, "SELECT pg_backend_pid()", token));
        }, context.CancellationToken);

    /// <summary>
    /// Checks exact SQL declarations and ownership, then proves clean installation after removal.
    /// </summary>
    [TestMethod]
    public Task SpiSampleOwnsItsSchemasAndReinstalls()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SpiSampleOwnsItsSchemasAndReinstalls), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, "CREATE EXTENSION ankus_spi", token);
            Assert.AreSequenceEqual<string>([
                "spi.issue1209_fixed::25:false", "spi.spi_insert_title:25:20:false", "spi.spi_insert_title2:25:2249:true",
                "spi.spi_query_by_id:20:25:false", "spi.spi_query_random_id::20:false", "spi.spi_query_title:25:20:false",
                "spi.spi_return_query::2249:true", "spi_srf.calculate_human_years::2249:true", "spi_srf.filter_by_breed:25:2249:true",
            ], await Scalar<string[]>(connection, transaction, """
                SELECT array_agg(n.nspname||'.'||p.proname||':'||p.proargtypes::text||':'||p.prorettype::text||':'||p.proretset::text
                    ORDER BY n.nspname COLLATE "C",p.proname COLLATE "C")
                FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname IN ('spi','spi_srf')
                """, token));
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT bool_and(p.provolatile='v' AND p.proparallel='u' AND (p.pronargs=0 OR p.proisstrict)
                    AND EXISTS(SELECT FROM pg_depend d JOIN pg_extension e ON e.oid=d.refobjid
                        WHERE d.classid='pg_proc'::regclass AND d.objid=p.oid AND d.refclassid='pg_extension'::regclass
                            AND d.deptype='e' AND e.extname='ankus_spi'))
                FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname IN ('spi','spi_srf')
                """, token));
            Assert.IsFalse(await Scalar<bool>(connection, transaction, "SELECT extrelocatable FROM pg_extension WHERE extname='ankus_spi'", token));
            Assert.AreSequenceEqual<string>(["title", "id", "title"], await Scalar<string[]>(connection, transaction,
                "SELECT proargnames FROM pg_proc WHERE oid='spi.spi_insert_title2(text)'::regprocedure", token));
            Assert.AreEqual("i,t,t", await Scalar<string>(connection, transaction,
                "SELECT array_to_string(proargmodes, ',') FROM pg_proc WHERE oid='spi.spi_insert_title2(text)'::regprocedure", token));
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction, "SELECT count(*) FROM spi.foo", token));
            await Execute(connection, transaction, "DROP EXTENSION ankus_spi", token);
            Assert.IsTrue(await Scalar<bool>(connection, transaction, "SELECT to_regnamespace('spi') IS NULL AND to_regnamespace('spi_srf') IS NULL", token));
            await Execute(connection, transaction, "CREATE EXTENSION ankus_spi", token);
            Assert.AreSequenceEqual<string>(["This is a test", "Hello There!", "I like pudding"], await Scalar<string[]>(connection, transaction,
                "SELECT array_agg(title ORDER BY id) FROM spi.spi_example", token));
            Assert.AreEqual(441L, await Scalar<long>(connection, transaction, "SELECT sum(human_age) FROM spi_srf.calculate_human_years()", token));
        }, context.CancellationToken);

    /// <summary>
    /// Returns one typed observation using bound values when required.
    /// </summary>
    private static async Task<T> Scalar<T>(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token,
        params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Completes DDL or an error-producing operation before the next assertion.
    /// </summary>
    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(token);
    }
}
