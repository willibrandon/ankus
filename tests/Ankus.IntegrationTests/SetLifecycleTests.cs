using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies the published set sample through installation, relocation and reinstallation.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class SetLifecycleTests(TestContext context)
{
    /// <summary>
    /// Scalar and table set functions retain their native behavior and extension ownership through DDL.
    /// </summary>
    [TestMethod]
    public Task SetSampleRelocatesAndReinstalls()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SetSampleRelocatesAndReinstalls), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                CREATE SCHEMA sets_first;
                CREATE SCHEMA sets_second;
                CREATE EXTENSION ankus_sets WITH SCHEMA sets_first;
                SELECT array_agg(value) FROM sets_first.series(-2,5) value
                """, connection, transaction);
            Assert.AreSequenceEqual([-2, -1, 0, 1, 2],
                Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token)));
            command.CommandText = """
                SELECT string_agg(word_number || ':' || word, ',' ORDER BY word_number)
                FROM sets_first.words(E'  hello\t世界\n café  ')
                """;
            Assert.AreEqual("1:hello,2:世界,3:café", await command.ExecuteScalarAsync(token));
            command.CommandText = """
                SELECT count(*) FROM pg_depend d JOIN pg_extension e ON e.oid = d.refobjid
                JOIN pg_proc p ON p.oid = d.objid
                WHERE d.refclassid = 'pg_extension'::regclass AND d.classid = 'pg_proc'::regclass
                    AND d.deptype = 'e' AND e.extname = 'ankus_sets' AND p.proretset
                """;
            Assert.AreEqual(3L, await command.ExecuteScalarAsync(token));
            command.CommandText = """
                ALTER EXTENSION ankus_sets SET SCHEMA sets_second;
                SELECT array_agg(word ORDER BY word_number) FROM sets_second.materialized_words('a b c')
                """;
            Assert.AreSequenceEqual(["a", "b", "c"],
                Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(token)));
            command.CommandText = """
                DROP EXTENSION ankus_sets;
                SELECT count(*) FROM pg_proc WHERE pronamespace = 'sets_second'::regnamespace
                """;
            Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
            command.CommandText = """
                CREATE EXTENSION ankus_sets WITH SCHEMA sets_first;
                SELECT string_agg(word, ',' ORDER BY word_number) FROM sets_first.words('fresh iterator')
                """;
            Assert.AreEqual("fresh,iterator", await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);
}
