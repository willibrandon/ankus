using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes the public aggregate example through ordinary SQL, windows, and extension lifecycle changes.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class AggregateLifecycleTests(TestContext context)
{
    /// <summary>
    /// The public example distinguishes empty and all-null input while matching PostgreSQL's average.
    /// </summary>
    /// <param name="input">The typed SQL input array.</param>
    /// <param name="expected">The independently expected average, or null.</param>
    [TestMethod]
    [DataRow("ARRAY[]::integer[]", null)]
    [DataRow("ARRAY[NULL,NULL]::integer[]", null)]
    [DataRow("ARRAY[7]", 7d)]
    [DataRow("ARRAY[1,NULL,2,NULL]", 1.5d)]
    [DataRow("ARRAY[-10,0,10]", 0d)]
    [DataRow("ARRAY[2147483647,2147483647]", 2147483647d)]
    public Task AverageSamplePreservesEmptyAndNullSemantics(string input, double? expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(AverageSamplePreservesEmptyAndNullSemantics), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("CREATE EXTENSION ankus_aggregates", connection, transaction);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT integer_average(value), avg(value)::double precision FROM unnest(" + input + ") AS value";
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            double? actual = reader.IsDBNull(0) ? null : reader.GetDouble(0);
            double? native = reader.IsDBNull(1) ? null : reader.GetDouble(1);
            Assert.AreEqual(expected, actual);
            Assert.AreEqual(native, actual);
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Repeated moving finalization leaves state available for inverse and forward transitions.
    /// </summary>
    [TestMethod]
    public Task AverageSampleMatchesMovingWindowResults()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(AverageSampleMatchesMovingWindowResults), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("CREATE EXTENSION ankus_aggregates", connection, transaction);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = """
                SELECT position, integer_average(value) OVER frame, avg(value) OVER frame
                FROM (VALUES (1,NULL::integer),(2,1),(3,2),(4,NULL),(5,10),(6,-2)) AS inputs(position,value)
                WINDOW frame AS (ORDER BY position ROWS BETWEEN 1 PRECEDING AND CURRENT ROW)
                ORDER BY position
                """;
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            double?[] expected = [null, 1d, 1.5d, 2d, 10d, 4d];
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(index + 1, reader.GetInt32(0));
                double? actual = reader.IsDBNull(1) ? null : reader.GetDouble(1);
                double? native = reader.IsDBNull(2) ? null : (double)reader.GetDecimal(2);
                Assert.AreEqual(expected[index], actual);
                Assert.AreEqual(native, actual);
            }

            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// The public example transports state through actual workers and preserves the independent arithmetic average.
    /// </summary>
    [TestMethod]
    public Task AverageSampleCombinesActualParallelWorkers()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(AverageSampleCombinesActualParallelWorkers), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                CREATE EXTENSION ankus_aggregates;
                CREATE TABLE average_input AS SELECT generate_series(1,30000) AS value;
                ALTER TABLE average_input SET (parallel_workers=2);
                ANALYZE average_input;
                SET LOCAL max_parallel_workers_per_gather=2;
                SET LOCAL min_parallel_table_scan_size=0;
                SET LOCAL parallel_setup_cost=0;
                SET LOCAL parallel_tuple_cost=0;
                SET LOCAL parallel_leader_participation=off;
                EXPLAIN(ANALYZE,FORMAT JSON) SELECT integer_average(value) FROM average_input
                """, connection, transaction);
            string plan = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
            using (JsonDocument document = JsonDocument.Parse(plan))
            {
                JsonElement[] nodes = [.. PlanNodes(document.RootElement[0].GetProperty("Plan"))];
                Assert.Contains(static node => node.TryGetProperty("Workers Launched", out JsonElement workers) && workers.GetInt32() > 0, nodes);
                Assert.Contains(static node => node.TryGetProperty("Partial Mode", out JsonElement mode) && mode.GetString() == "Partial", nodes);
                Assert.Contains(static node => node.TryGetProperty("Partial Mode", out JsonElement mode) && mode.GetString() == "Finalize", nodes);
            }

            command.CommandText = "SELECT integer_average(value),avg(value)::double precision FROM average_input";
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(15000.5d, reader.GetDouble(0));
            Assert.AreEqual(reader.GetDouble(1), reader.GetDouble(0));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// The sample serializer emits the documented version and fixed-width little-endian values.
    /// </summary>
    [TestMethod]
    public Task AverageSampleSerializesExactPortableBytes()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(AverageSampleSerializesExactPortableBytes), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                CREATE EXTENSION ankus_aggregates;
                CREATE AGGREGATE average_bytes(integer)(SFUNC=integer_average_transition,STYPE=internal,FINALFUNC=integer_average_serialize);
                SELECT average_bytes(value) FROM (VALUES(2),(NULL),(-5)) AS input(value)
                """, connection, transaction);
            byte[] expected = [1, 253, 255, 255, 255, 255, 255, 255, 255, 2, 0, 0, 0, 0, 0, 0, 0];
            Assert.AreSequenceEqual(expected, Assert.IsInstanceOfType<byte[]>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT average_bytes(value) FROM (VALUES(NULL::integer)) AS input(value)";
            byte[] empty = [1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            Assert.AreSequenceEqual(empty, Assert.IsInstanceOfType<byte[]>(await command.ExecuteScalarAsync(token)));
        }, context.CancellationToken);

    /// <summary>
    /// Aggregate and support function identities follow relocation and disappear with their extension.
    /// </summary>
    [TestMethod]
    public Task AggregateSampleRelocatesDropsAndReinstalls()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(AggregateSampleRelocatesDropsAndReinstalls), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                CREATE SCHEMA average_first;
                CREATE SCHEMA average_second;
                CREATE EXTENSION ankus_aggregates WITH SCHEMA average_first;
                SELECT 'average_first.integer_average(integer)'::regprocedure::oid
                """, connection, transaction);
            uint original = Assert.IsInstanceOfType<uint>(await command.ExecuteScalarAsync(token));
            command.CommandText = """
                SELECT aggtranstype='internal'::regtype AND aggmtranstype='internal'::regtype
                    AND aggtransfn='average_first.integer_average_transition(internal,integer)'::regprocedure
                    AND aggcombinefn='average_first.integer_average_combine(internal,internal)'::regprocedure
                    AND aggserialfn='average_first.integer_average_serialize(internal)'::regprocedure
                    AND aggdeserialfn='average_first.integer_average_deserialize(bytea,internal)'::regprocedure
                    AND aggmtransfn='average_first.integer_average_moving_transition(internal,integer)'::regprocedure
                    AND aggminvtransfn='average_first.integer_average_moving_inverse(internal,integer)'::regprocedure
                    AND aggfinalmodify='r' AND aggmfinalmodify='r'
                FROM pg_aggregate WHERE aggfnoid='average_first.integer_average(integer)'::regprocedure
                """;
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = """
                ALTER EXTENSION ankus_aggregates SET SCHEMA average_second;
                SELECT 'average_second.integer_average(integer)'::regprocedure::oid
                """;
            Assert.AreEqual(original, Assert.IsInstanceOfType<uint>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT average_second.integer_average(value) FROM (VALUES (2),(4)) AS inputs(value)";
            Assert.AreEqual(3d, Assert.IsInstanceOfType<double>(await command.ExecuteScalarAsync(token)));
            command.CommandText = """
                DROP EXTENSION ankus_aggregates;
                SELECT NOT EXISTS (SELECT FROM pg_proc WHERE pronamespace='average_second'::regnamespace)
                    AND NOT EXISTS (SELECT FROM pg_aggregate WHERE aggfnoid=@original)
                """;
            command.Parameters.AddWithValue("original", NpgsqlDbType.Oid, original);
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.Parameters.Clear();
            command.CommandText = """
                CREATE EXTENSION ankus_aggregates WITH SCHEMA average_first;
                SELECT 'average_first.integer_average(integer)'::regprocedure::oid
                """;
            Assert.AreNotEqual(original, Assert.IsInstanceOfType<uint>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT average_first.integer_average(value) FROM (VALUES (10),(NULL),(20)) AS inputs(value)";
            Assert.AreEqual(15d, Assert.IsInstanceOfType<double>(await command.ExecuteScalarAsync(token)));
        }, context.CancellationToken);

    private static IEnumerable<JsonElement> PlanNodes(JsonElement node)
    {
        yield return node;
        if (node.TryGetProperty("Plans", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                foreach (JsonElement descendant in PlanNodes(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
