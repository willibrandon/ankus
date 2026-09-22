namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies moving aggregate strategy selection with NULLs, filtering, volatility and excluded frames.
/// </summary>
public sealed partial class AggregateTests
{
    /// <summary>
    /// Strict and non-strict moving implementations match independently recomputed PostgreSQL sums across NULL runs and partitions.
    /// </summary>
    [TestMethod]
    public Task MovingNullAndFilteredRowsMatchNativeWindowResults()
        => Run(nameof(MovingNullAndFilteredRowsMatchNativeWindowResults), async (connection, transaction, token) =>
        {
            await Reset(connection, transaction, "normal", token);
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                WITH input(part,ord,v) AS (VALUES(1,1,1),(1,2,NULL),(1,3,3),(1,4,NULL),(1,5,NULL),(1,6,6),(2,1,NULL),(2,2,10)),
                output AS (
                    SELECT part,ord,
                        aggregate_values.moving_sum(v) FILTER(WHERE ord<>3) OVER w AS nonstrict,
                        aggregate_values.mutable_final_sum(v) FILTER(WHERE ord<>3) OVER w AS strict,
                        coalesce(sum(v) FILTER(WHERE ord<>3) OVER w,0)::integer AS native
                    FROM input WINDOW w AS(PARTITION BY part ORDER BY ord ROWS 1 PRECEDING))
                SELECT bool_and(nonstrict=native AND strict=native) AND array_agg(native ORDER BY part,ord)=ARRAY[1,1,0,0,0,6,0,10]
                FROM output
                """, token));
            Assert.Contains("M:NULL", await Trace(connection, transaction, token));
        });

    /// <summary>
    /// Volatile arguments or filters force ordinary recomputation while preserving the same frame results.
    /// </summary>
    /// <param name="expression">The aggregated expression.</param>
    /// <param name="filter">The optional volatile filter.</param>
    [TestMethod]
    [DataRow("v+(random()*0)::integer", "")]
    [DataRow("v", "FILTER(WHERE random()<1)")]
    public Task VolatileWindowsBypassMovingInverseWithoutChangingValues(string expression, string filter)
        => Run(nameof(VolatileWindowsBypassMovingInverseWithoutChangingValues), async (connection, transaction, token) =>
        {
            await Reset(connection, transaction, "normal", token);
            int[] expected = [1, 21, 320, 4300];
            Assert.AreSequenceEqual(expected, await Scalar<int[]>(connection, transaction, $"""
                SELECT array_agg(total ORDER BY ord) FROM (
                    SELECT ord,aggregate_values.moving_sum({expression}) {filter} OVER(ORDER BY ord ROWS 1 PRECEDING) AS total
                    FROM (VALUES(1,1),(2,20),(3,300),(4,4000)) AS input(ord,v)) AS output
                """, token));
            string[] trace = ["T:1", "T:20", "T:20", "T:300", "T:300", "T:4000"];
            Assert.AreSequenceEqual(trace, await Trace(connection, transaction, token));
            Assert.AreEqual(0, (await Status(connection, transaction, token))[6]);
        });

    /// <summary>
    /// Excluded and empty frames return stable earlier results while PostgreSQL restarts managed state owners.
    /// </summary>
    [TestMethod]
    public Task ExcludedFramesPreserveEarlierResultsAndReleaseRestartedOwners()
        => Run(nameof(ExcludedFramesPreserveEarlierResultsAndReleaseRestartedOwners), async (connection, transaction, token) =>
        {
            await Reset(connection, transaction, "normal", token);
            long[] expected = [0, 1, 20, 300];
            Assert.AreSequenceEqual(expected, await Scalar<long[]>(connection, transaction, """
                SELECT array_agg(total ORDER BY ord) FROM (
                    SELECT ord,aggregate_values.managed_sum(v) OVER(ORDER BY ord ROWS 1 PRECEDING EXCLUDE CURRENT ROW) AS total
                    FROM (VALUES(1,1),(2,20),(3,300),(4,4000)) AS input(ord,v)) AS output
                """, token));
            await AssertBalancedRelease(connection, transaction, token);
        });

    /// <summary>
    /// Distinct state converters and repeated text finals preserve exact earlier frame results.
    /// </summary>
    [TestMethod]
    public Task MovingStateAndFinalConvertersCanDifferFromOrdinaryState()
        => Run(nameof(MovingStateAndFinalConvertersCanDifferFromOrdinaryState), async (connection, transaction, token) =>
        {
            Assert.AreEqual("sum:4321", await Scalar<string>(connection, transaction,
                "SELECT aggregate_values.distinct_moving_state(v) FROM (VALUES(1),(20),(300),(4000)) AS input(v)", token));
            string[] expected = ["sum:1", "sum:21", "sum:320", "sum:4300"];
            Assert.AreSequenceEqual(expected, await Scalar<string[]>(connection, transaction, """
                SELECT array_agg(total ORDER BY ord) FROM (
                    SELECT ord,aggregate_values.distinct_moving_state(v) OVER(ORDER BY ord ROWS 1 PRECEDING) AS total
                    FROM (VALUES(1,1),(2,20),(3,300),(4,4000)) AS input(ord,v)) AS output
                """, token));
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT aggtranstype='bigint'::regtype AND aggmtranstype='bigint[]'::regtype
                FROM pg_aggregate WHERE aggfnoid='aggregate_values.distinct_moving_state(integer)'::regprocedure
                """, token));
        });

}
