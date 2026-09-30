using System.Text.Json;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class AggregateTests
{
    /// <summary>
    /// Hypothetical tuple arguments match PostgreSQL's own rank, including direction changes and NULL rows.
    /// </summary>
    /// <param name="direct">The typed direct arguments.</param>
    /// <param name="order">The selected ordering.</param>
    /// <param name="expected">The independently expected rank.</param>
    [TestMethod]
    [DataRow("'a',2", "text ASC NULLS LAST, number DESC NULLS FIRST", 3L)]
    [DataRow("NULL::text,2", "text ASC NULLS LAST, number DESC NULLS FIRST", 5L)]
    [DataRow("'a',NULL::integer", "text ASC NULLS LAST, number DESC NULLS FIRST", 1L)]
    [DataRow("'a',2", "text DESC NULLS FIRST, number ASC NULLS LAST", 4L)]
    public Task TypedAggregateHypotheticalTupleMatchesNativeRank(string direct, string order, long expected)
        => Run(nameof(TypedAggregateHypotheticalTupleMatchesNativeRank), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand($"""
                SELECT aggregate_values.typed_rank({direct}) WITHIN GROUP(ORDER BY {order}),
                    rank({direct}) WITHIN GROUP(ORDER BY {order})
                FROM (VALUES('a',1),('a',3),('b',2),(NULL,2),('a',NULL)) AS input(text,number)
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(expected, reader.GetInt64(0));
            Assert.AreEqual(reader.GetInt64(1), reader.GetInt64(0));
            Assert.IsFalse(await reader.ReadAsync(token));
        });

    /// <summary>
    /// Ordered typed callbacks preserve PostgreSQL collation, direction and NULL placement with no direct SQL inputs.
    /// </summary>
    /// <param name="order">The ordering and explicit NULL placement.</param>
    [TestMethod]
    [DataRow("ASC NULLS FIRST")]
    [DataRow("ASC NULLS LAST")]
    [DataRow("DESC NULLS FIRST")]
    [DataRow("DESC NULLS LAST")]
    public Task TypedAggregateOrderedValuesMatchNativeOrdering(string order)
        => Run(nameof(TypedAggregateOrderedValuesMatchNativeOrdering), async (connection, transaction, token) =>
        {
            Assert.IsTrue(await Scalar<bool>(connection, transaction, $"""
                SELECT aggregate_values.typed_ordered() WITHIN GROUP(ORDER BY v COLLATE "C" {order})
                    IS NOT DISTINCT FROM array_agg(v ORDER BY v COLLATE "C" {order})
                FROM (VALUES('a'),('é'),(NULL),('B'),('a')) AS input(v)
                """, token));
            Assert.AreEqual(0, await Scalar<int>(connection, transaction,
                "SELECT cardinality(aggregate_values.typed_ordered() WITHIN GROUP(ORDER BY v)) FROM (SELECT ''::text WHERE false) AS input(v)", token));
        });

    /// <summary>
    /// Generated final-extra slots resolve exact scalar, domain, composite and array result identities.
    /// </summary>
    /// <param name="expression">The concrete polymorphic input value.</param>
    [TestMethod]
    [DataRow("42")]
    [DataRow("repeat('héllo 🐘',10000)")]
    [DataRow("'owned'::pg_temp.aggregate_enum")]
    [DataRow("42::pg_temp.aggregate_domain")]
    [DataRow("ROW(42,repeat('x',10000))::pg_temp.aggregate_pair")]
    [DataRow("'[2:3][-1:0]={{1,NULL},{3,4}}'::integer[]")]
    public Task TypedAggregateFinalExtraPreservesResolvedValues(string expression)
        => Run(nameof(TypedAggregateFinalExtraPreservesResolvedValues), async (connection, transaction, token) =>
        {
            await PreparePolymorphicAggregates(connection, transaction, token);
            await using var command = new NpgsqlCommand($"""
                SELECT aggregate_values.typed_polymorphic({expression})::text, ({expression})::text,
                    pg_typeof(aggregate_values.typed_polymorphic({expression}))::oid, pg_typeof({expression})::oid
                FROM generate_series(1,4)
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(reader.GetString(1), reader.GetString(0));
            Assert.AreEqual(reader.GetFieldValue<uint>(3), reader.GetFieldValue<uint>(2));
            Assert.IsFalse(await reader.ReadAsync(token));
        });

    /// <summary>
    /// Moving final-extra resolution retains borrowed values and NULLs after each input callback expires.
    /// </summary>
    [TestMethod]
    public Task TypedAggregateMovingFinalExtraPreservesFrames()
        => Run(nameof(TypedAggregateMovingFinalExtraPreservesFrames), async (connection, transaction, token) =>
        {
            await PreparePolymorphicAggregates(connection, transaction, token);
            string?[] expected = [new('a', 10000), new('a', 10000), null, new('c', 10000)];
            Assert.AreSequenceEqual(expected, await Scalar<string?[]>(connection, transaction, """
                SELECT array_agg(value ORDER BY ord) FROM (
                    SELECT ord,aggregate_values.typed_polymorphic(CASE WHEN ord=2 THEN NULL ELSE repeat(chr(96+ord),10000) END)
                        OVER(ORDER BY ord ROWS 1 PRECEDING) AS value FROM generate_series(1,4) ord) frames
                """, token));
            int[] counts = await Scalar<int[]>(connection, transaction, "SELECT aggregate_values.poly_aggregate_counts()", token);
            Assert.IsGreaterThan(0, counts[2]);
            Assert.IsTrue(await Scalar<bool>(connection, transaction,
                "SELECT aggregate_values.typed_polymorphic(value) IS NULL FROM (SELECT ''::text WHERE false) AS input(value)", token));
            Assert.IsTrue(await Scalar<bool>(connection, transaction,
                "SELECT aggregate_values.typed_polymorphic(value) IS NULL FROM (VALUES(NULL::text),(NULL)) AS input(value)", token));
        });

    /// <summary>
    /// Independent tuple constraints round with PostgreSQL semantics before typed transition dispatch.
    /// </summary>
    [TestMethod]
    public Task TypedAggregateNumericElementsRoundIndependently()
        => Run(nameof(TypedAggregateNumericElementsRoundIndependently), async (connection, transaction, token) =>
        {
            Assert.AreEqual(5.135m, await Scalar<decimal>(connection, transaction, """
                SELECT aggregate_values.typed_prices(price, charge)
                FROM (VALUES(1.234,0.5674),(2.345,0.9876)) AS input(price,charge)
                """, token));
            Assert.AreEqual(0m, await Scalar<decimal>(connection, transaction,
                "SELECT aggregate_values.typed_prices(1.234,0.5674) WHERE false", token));
        });

    /// <summary>
    /// A tuple input overflow retains its SQLSTATE and allows recovery on the same PostgreSQL backend.
    /// </summary>
    [TestMethod]
    public Task TypedAggregateNumericElementOverflowRecovers()
        => Run(nameof(TypedAggregateNumericElementOverflowRecovers), async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            await transaction.SaveAsync("typed_numeric_error", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection, transaction,
                "SELECT aggregate_values.typed_prices(999.995,0.001)", token));
            Assert.AreEqual("22003", error.SqlState);
            await transaction.RollbackAsync("typed_numeric_error", token);
            Assert.AreEqual(999.991m, await Scalar<decimal>(connection, transaction,
                "SELECT aggregate_values.typed_prices(999.994,0.001)", token));
            Assert.AreEqual(backend, connection.ProcessID);
        });

    /// <summary>
    /// Typed tuple variadic input preserves scalar position, empty arrays, NULL arrays and NULL elements.
    /// </summary>
    [TestMethod]
    public Task TypedAggregateVariadicTupleRetainsArrayShape()
        => Run(nameof(TypedAggregateVariadicTupleRetainsArrayShape), async (connection, transaction, token) =>
        {
            Assert.AreEqual(46L, await Scalar<long>(connection, transaction,
                "SELECT aggregate_values.typed_variadic(v,v,v+1,NULL) FROM (VALUES(2),(4)) AS input(v)", token));
            Assert.AreEqual(2010L, await Scalar<long>(connection, transaction, """
                SELECT aggregate_values.typed_variadic(2,VARIADIC v)
                FROM (VALUES(ARRAY[2,NULL,3]),(ARRAY[]::integer[]),(NULL::integer[])) AS input(v)
                """, token));
        });

    /// <summary>
    /// Bound raw and composite tuple inputs retain their SQL identity and exact values across rows.
    /// </summary>
    [TestMethod]
    public Task TypedAggregateTupleBindingsReadNativeValues()
        => Run(nameof(TypedAggregateTupleBindingsReadNativeValues), async (connection, transaction, token) =>
        {
            Assert.AreEqual("2:7:café;3:11:雪;", await Scalar<string>(connection, transaction, """
                SELECT aggregate_values.typed_bound_values(number,ROW(other,label)::raw_values.pair ORDER BY number)
                FROM (VALUES(3,11,'雪'),(2,7,'café')) AS input(number,other,label)
                """, token));
        });

    /// <summary>
    /// Explicit typed dispatch preserves tuple order, per-element NULLs, empty inputs and actual zero-argument calls.
    /// </summary>
    [TestMethod]
    public Task TypedAggregateTupleAndEmptyInputsExecute()
        => Run(nameof(TypedAggregateTupleAndEmptyInputsExecute), async (connection, transaction, token) =>
        {
            Assert.AreEqual(94L, await Scalar<long>(connection, transaction, """
                SELECT aggregate_values.typed_weighted(amount, weight)
                FROM (VALUES(2,3),(NULL,5),(4,NULL),(3,3)) AS input(amount,weight)
                """, token));
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction,
                "SELECT aggregate_values.typed_weighted(1,2) WHERE false", token));
            Assert.AreEqual(3L, await Scalar<long>(connection, transaction,
                "SELECT aggregate_values.typed_rows(*) FROM (VALUES(1),(NULL),(3)) AS input(value)", token));
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction,
                "SELECT aggregate_values.typed_rows(*) WHERE false", token));
        });

    /// <summary>
    /// Ordinary and moving typed capabilities retain native state lifetimes and inverse restart semantics.
    /// </summary>
    [TestMethod]
    public Task TypedAggregateStateReleasesAndMovingInverseRestarts()
        => Run(nameof(TypedAggregateStateReleasesAndMovingInverseRestarts), async (connection, transaction, token) =>
        {
            await Reset(connection, transaction, "normal", token);
            Assert.AreEqual(6L, await Scalar<long>(connection, transaction,
                "SELECT aggregate_values.typed_owned(value) FROM (VALUES(1),(NULL),(5)) AS input(value)", token));
            await AssertReleased(connection, transaction, 1, token);
            await Reset(connection, transaction, "restart", token);
            Assert.AreSequenceEqual([1L, 21L, 320L, 4300L], await Scalar<long[]>(connection, transaction, """
                SELECT array_agg(total ORDER BY ord) FROM (
                    SELECT ord,aggregate_values.typed_owned(value) OVER(ORDER BY ord ROWS BETWEEN 1 PRECEDING AND CURRENT ROW) AS total
                    FROM (VALUES(1,1),(2,20),(3,300),(4,4000)) AS input(ord,value)) AS result
                """, token));
            int[] status = await Status(connection, transaction, token);
            Assert.AreEqual(2, status[6]);
            Assert.AreEqual(1, status[7]);
            Assert.AreEqual(status[0], status[1]);
            Assert.AreEqual(0, status[2]);
            Assert.AreEqual(0, status[3]);
        });

    /// <summary>
    /// Actual launched workers exercise explicit typed serialization, deserialization and combine calls.
    /// </summary>
    [TestMethod]
    public Task TypedAggregateParallelTransportExecutesAcrossProcesses()
        => Run(nameof(TypedAggregateParallelTransportExecutesAcrossProcesses), async (connection, transaction, token) =>
        {
            await PrepareParallelInput(connection, transaction, token);
            string plan = await Scalar<string>(connection, transaction,
                "EXPLAIN (ANALYZE, FORMAT JSON) SELECT aggregate_values.typed_parallel(value) FROM aggregate_values.parallel_input", token);
            using (JsonDocument document = JsonDocument.Parse(plan))
            {
                JsonElement root = document.RootElement[0].GetProperty("Plan");
                Assert.IsGreaterThan(0, WorkersLaunched(root));
                Assert.IsTrue(HasPartialAggregate(root));
            }

            await Execute(connection, transaction, "SELECT aggregate_values.parallel_reset('normal')", token);
            long[] result = await Scalar<long[]>(connection, transaction,
                "SELECT aggregate_values.typed_parallel(value) FROM aggregate_values.parallel_input", token);
            Assert.AreEqual(450015000L, result[0]);
            Assert.AreEqual(30000L, result[1]);
            Assert.IsGreaterThan(0L, result[2]);
            Assert.IsGreaterThan(0L, result[3]);
            Assert.AreEqual(result[3], result[4]);
            Assert.IsNotEmpty(result.Skip(5).Where(process => process != connection.ProcessID));
            await AssertParallelReleased(connection, transaction, token);
        });

    /// <summary>
    /// A typed combine error preserves diagnostics, releases transported state and recovers on the same backend.
    /// </summary>
    [TestMethod]
    public Task TypedAggregateParallelErrorReleasesStateAndRecovers()
        => Run(nameof(TypedAggregateParallelErrorReleasesStateAndRecovers), async (connection, transaction, token) =>
        {
            await PrepareParallelInput(connection, transaction, token);
            int backend = connection.ProcessID;
            await Execute(connection, transaction, "SELECT aggregate_values.parallel_reset('normal')", token);
            await transaction.SaveAsync("typed_error", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection, transaction,
                "SELECT aggregate_values.typed_parallel(-3) FROM aggregate_values.parallel_input", token));
            Assert.AreEqual("P7813", error.SqlState);
            Assert.AreEqual("aggregate combine failed", error.MessageText);
            await transaction.RollbackAsync("typed_error", token);
            await AssertParallelReleased(connection, transaction, token);
            long[] result = await Scalar<long[]>(connection, transaction,
                "SELECT aggregate_values.typed_parallel(value) FROM aggregate_values.parallel_input", token);
            Assert.AreEqual(450015000L, result[0]);
            Assert.IsGreaterThan(0L, result[4]);
            Assert.AreEqual(backend, connection.ProcessID);
            await AssertParallelReleased(connection, transaction, token);
        });
}
