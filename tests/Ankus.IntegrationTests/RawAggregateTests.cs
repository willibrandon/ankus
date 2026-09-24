using System.Text.Json;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies raw aggregate state across groups, callback owners, and actual PostgreSQL workers.
/// </summary>
public sealed partial class AggregateTests
{
    /// <summary>
    /// Retains by-reference state independently for groups and transports it through worker combination.
    /// </summary>
    [TestMethod]
    public Task RawAggregateStatesRetainStorageAcrossGroupsAndWorkers()
        => Run(nameof(RawAggregateStatesRetainStorageAcrossGroupsAndWorkers), async (connection, transaction, token) =>
        {
            Assert.AreSequenceEqual([50000, 50000], await Scalar<int[]>(connection, transaction, """
                SELECT array_agg(length(first) ORDER BY key) FROM (
                    SELECT value%2 AS key,raw_values.raw_first(repeat('owned',10000)) AS first
                    FROM generate_series(1,20) value GROUP BY key) groups
                """, token));
            Assert.AreEqual(DBNull.Value, await Scalar<object>(connection, transaction,
                "SELECT raw_values.raw_first(NULL) FROM generate_series(1,10)", token));
            Assert.AreEqual(DBNull.Value, await Scalar<object>(connection, transaction,
                "SELECT raw_values.raw_first(value) FROM (SELECT 'owned'::text AS value WHERE false) input", token));
            await PrepareParallelInput(connection, transaction, token);
            const string query = "SELECT raw_values.raw_first(repeat(CASE WHEN value>0 THEN 'owned' ELSE 'wrong' END,100)) FROM aggregate_values.parallel_input";
            string plan = await Scalar<string>(connection, transaction, "EXPLAIN(ANALYZE,FORMAT JSON) " + query, token);
            using (JsonDocument document = JsonDocument.Parse(plan))
            {
                JsonElement root = document.RootElement[0].GetProperty("Plan");
                Assert.IsGreaterThan(0, WorkersLaunched(root));
                Assert.IsTrue(HasPartialAggregate(root));
            }

            string expected = string.Concat(Enumerable.Repeat("owned", 100));
            Assert.AreEqual(expected, await Scalar<string>(connection, transaction, query, token));
        });
}
