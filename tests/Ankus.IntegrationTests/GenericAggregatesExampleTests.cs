using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes pgrx's generic_agg example through its independently published extension.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
[DoNotParallelize]
public sealed class GenericAggregatesExampleTests(TestContext context)
{
    /// <summary>
    /// Mirrors each pgrx test and adds by-value, fixed-length by-reference and byte-level numeric cases.
    /// </summary>
    /// <param name="sql">The aggregate query.</param>
    /// <param name="expected">The number of changes.</param>
    [TestMethod]
    [DataRow("SELECT count_changes(v ORDER BY ord) FROM (VALUES (1,'a'),(2,'a'),(3,'b'),(4,'b'),(5,'b'),(6,'c')) t(ord, v)", 2L)]
    [DataRow("""
        SELECT count_changes(v ORDER BY ord) FROM (VALUES
            (1,1.5::numeric),(2,1.5),(3,1.5),(4,2.0),(5,2.0),(6,3.0),(7,1.5)) t(ord, v)
        """, 3L)]
    [DataRow("SELECT count_changes(v ORDER BY ord) FROM (VALUES (1,1),(2,1),(3,2),(4,2),(5,2),(6,1),(7,1)) t(ord, v)", 2L)]
    [DataRow("""
        SELECT count_changes(v ORDER BY ord) FROM (VALUES
            (1,'a'),(2,NULL),(3,'a'),(4,'b'),(5,NULL),(6,'b')) t(ord, v)
        """, 1L)]
    [DataRow("SELECT count_changes(v) FROM (SELECT 1 WHERE false) t(v)", 0L)]
    [DataRow("SELECT count_changes(v) FROM (VALUES (NULL::text), (NULL)) t(v)", 0L)]
    [DataRow("SELECT count_changes(v ORDER BY ord) FROM (VALUES (1,1::bigint),(2,-1),(3,-1),(4,9223372036854775807)) t(ord, v)", 2L)]
    [DataRow("""
        SELECT count_changes(v ORDER BY ord) FROM (VALUES
            (1,'00000000-0000-0000-0000-000000000001'::uuid),(2,'00000000-0000-0000-0000-000000000001'),
            (3,'ffffffff-ffff-ffff-ffff-ffffffffffff'),(4,'00000000-0000-0000-0000-000000000001')) t(ord, v)
        """, 2L)]
    [DataRow("SELECT count_changes(v ORDER BY ord) FROM (VALUES (1,'x'::name),(2,'x'),(3,'y')) t(ord, v)", 1L)]
    [DataRow("SELECT count_changes(v ORDER BY ord) FROM (VALUES (1,1.0::numeric),(2,1.00),(3,1.00)) t(ord, v)", 1L)]
    public Task GenericAggregateSampleCountsChanges(string sql, long expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(GenericAggregateSampleCountsChanges), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, "CREATE EXTENSION ankus_generic_aggregates", token);
            Assert.AreEqual(expected, await Scalar<long>(connection, transaction, sql, token));
        }, context.CancellationToken);

    /// <summary>
    /// Compares equal values from short-header, compressed and external storage with computed values as equal.
    /// </summary>
    /// <param name="storage">The column storage strategy for the table values.</param>
    [TestMethod]
    [DataRow("EXTENDED")]
    [DataRow("EXTERNAL")]
    public Task GenericAggregateSampleNormalizesStoredValues(string storage)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(GenericAggregateSampleNormalizesStoredValues), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, $"""
                CREATE EXTENSION ankus_generic_aggregates;
                CREATE TABLE stored_changes(ord integer, v text);
                ALTER TABLE stored_changes ALTER v SET STORAGE {storage};
                INSERT INTO stored_changes VALUES (1, 'a'), (3, repeat('x', 100000)), (5, repeat('x', 100000) || 'y');
                """, token);
            Assert.AreEqual(2, await Scalar<int>(connection, transaction, "SELECT pg_column_size(v) FROM stored_changes WHERE ord = 1", token));
            Assert.AreEqual(5, await Scalar<int>(connection, transaction, "SELECT pg_column_size('a'::text)", token));
            Assert.IsTrue(await Scalar<bool>(connection, transaction, $"""
                SELECT CASE '{storage}' WHEN 'EXTENDED' THEN pg_column_size(v) < 10000 ELSE pg_column_size(v) >= 100000 END
                FROM stored_changes WHERE ord = 3
                """, token));
            Assert.AreEqual(2L, await Scalar<long>(connection, transaction, """
                SELECT count_changes(v ORDER BY ord) FROM (
                    SELECT ord, v FROM stored_changes
                    UNION ALL VALUES (2, 'a'), (4, repeat('x', 100000)), (6, repeat('x', 100000) || 'y')) AS inputs(ord, v)
                """, token));
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction, """
                SELECT count_changes(v ORDER BY copy) FROM (
                    SELECT 1, v FROM stored_changes WHERE ord = 3
                    UNION ALL SELECT 2, repeat('x', 100000) UNION ALL SELECT 3, v FROM stored_changes WHERE ord = 3) AS inputs(copy, v)
                """, token));
        }, context.CancellationToken);

    /// <summary>
    /// Keeps independent group states, recomputes moving frames, retains running window state and handles large inputs.
    /// </summary>
    [TestMethod]
    public Task GenericAggregateSampleSupportsGroupsAndWindows()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(GenericAggregateSampleSupportsGroupsAndWindows), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, """
                CREATE EXTENSION ankus_generic_aggregates;
                CREATE TYPE change_pair AS (number integer, label text);
                """, token);
            Assert.AreSequenceEqual<string>(["x:1", "y:1", "z:0"], await Scalar<string[]>(connection, transaction, """
                SELECT array_agg(g || ':' || c ORDER BY g) FROM (
                    SELECT g, count_changes(v ORDER BY ord) AS c
                    FROM (VALUES ('x',1,1),('x',2,2),('y',1,5),('y',2,5),('y',3,6),('z',1,NULL)) t(g, ord, v)
                    GROUP BY g) AS groups
                """, token));
            Assert.AreSequenceEqual<long>([0, 0, 1, 1, 1, 2], await Scalar<long[]>(connection, transaction, """
                SELECT array_agg(c ORDER BY ord) FROM (
                    SELECT ord, count_changes(v) OVER (ORDER BY ord) AS c
                    FROM (VALUES (1,'a'),(2,'a'),(3,'b'),(4,'b'),(5,'b'),(6,'c')) t(ord, v)) AS running
                """, token));
            Assert.AreSequenceEqual<long>([0, 0, 1, 0, 0, 1], await Scalar<long[]>(connection, transaction, """
                SELECT array_agg(c ORDER BY ord) FROM (
                    SELECT ord, count_changes(v) OVER (ORDER BY ord ROWS BETWEEN 1 PRECEDING AND CURRENT ROW) AS c
                    FROM (VALUES (1,'a'),(2,'a'),(3,'b'),(4,'b'),(5,'b'),(6,'c')) t(ord, v)) AS moving
                """, token));
            Assert.AreEqual(2L, await Scalar<long>(connection, transaction, """
                SELECT count_changes(v ORDER BY ord) FROM (VALUES
                    (1, ROW(1, 'a')::change_pair), (2, ROW(1, 'a')::change_pair), (3, ROW(1, NULL)::change_pair),
                    (4, ROW(1, NULL)::change_pair), (5, ROW(2, 'a')::change_pair)) t(ord, v)
                """, token));
            Assert.AreEqual(99999L, await Scalar<long>(connection, transaction,
                "SELECT count_changes(md5(i::text) ORDER BY i) FROM generate_series(1, 100000) AS i", token));
            Assert.AreEqual(1L, await Scalar<long>(connection, transaction,
                "SELECT count_changes(i / 50000 ORDER BY i) FROM generate_series(1, 99999) AS i", token));
        }, context.CancellationToken);

    /// <summary>
    /// Declares PostgreSQL's polymorphic aggregate contract and recovers after an input error ends a group mid-aggregation.
    /// </summary>
    [TestMethod]
    public Task GenericAggregateSampleDeclaresAggregateAndRecovers()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(GenericAggregateSampleDeclaresAggregateAndRecovers),
            async (connection, transaction, token) =>
            {
                await Execute(connection, transaction, "CREATE EXTENSION ankus_generic_aggregates", token);
                Assert.AreEqual("count_changes_transition:count_changes_final:internal:r:bigint:anyelement", await Scalar<string>(
                    connection, transaction, """
                    SELECT a.aggtransfn::text || ':' || a.aggfinalfn::text || ':' || a.aggtranstype::regtype::text || ':'
                        || a.aggfinalmodify::text || ':' || p.prorettype::regtype::text || ':' || format_type(p.proargtypes[0], NULL)
                    FROM pg_aggregate a JOIN pg_proc p ON p.oid = a.aggfnoid
                    WHERE a.aggfnoid = 'count_changes(anyelement)'::regprocedure
                    """, token));
                Assert.IsFalse(await Scalar<bool>(connection, transaction,
                    "SELECT extrelocatable FROM pg_extension WHERE extname = 'ankus_generic_aggregates'", token));

                await transaction.SaveAsync("input_error", token);
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
                    Execute(connection, transaction, "SELECT count_changes(10 / (v - 3)) FROM generate_series(1, 5) AS v", token));
                Assert.AreEqual("22012", error.SqlState);
                Assert.AreEqual("division by zero", error.MessageText);
                await transaction.RollbackAsync("input_error", token);
                Assert.AreEqual(3L, await Scalar<long>(connection, transaction,
                    "SELECT count_changes(10 / (v - 6)) FROM generate_series(1, 5) AS v", token));
                Assert.AreEqual(1L, await Scalar<long>(connection, transaction,
                    "SELECT count_changes(v::text ORDER BY v) FROM (VALUES (1), (2)) AS t(v)", token));
                Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, transaction, "SELECT pg_backend_pid()", token));
            }, context.CancellationToken);

    /// <summary>
    /// Returns one typed observation.
    /// </summary>
    private static async Task<T> Scalar<T>(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
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
