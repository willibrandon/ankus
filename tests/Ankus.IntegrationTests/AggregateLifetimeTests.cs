using System.Text.Json;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Exercises aggregate owner cleanup through spilling, suspended cursors, rescans and failing destructors.
/// </summary>
public sealed partial class AggregateTests
{
    /// <summary>
    /// Real hash spilling and sorted grouping return exact group totals while every state owner is released.
    /// </summary>
    [TestMethod]
    public Task HashSpillAndSortedGroupingReleaseEveryManagedState()
        => Run(nameof(HashSpillAndSortedGroupingReleaseEveryManagedState), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, """
                CREATE TABLE aggregate_values.group_input AS SELECT generate_series(1,3000) AS value;
                ANALYZE aggregate_values.group_input;
                SET LOCAL work_mem='64kB';
                SET LOCAL enable_sort=off;
                SET LOCAL max_parallel_workers_per_gather=0;
                """, token);
            await Reset(connection, transaction, "normal", token);
            string plan = await Scalar<string>(connection, transaction, """
                EXPLAIN(ANALYZE,FORMAT JSON)
                SELECT value,aggregate_values.managed_sum(value) FROM aggregate_values.group_input GROUP BY value
                """, token);
            using (JsonDocument document = JsonDocument.Parse(plan))
            {
                JsonElement aggregate = document.RootElement[0].GetProperty("Plan");
                Assert.AreEqual("Hashed", aggregate.GetProperty("Strategy").GetString());
                Assert.IsGreaterThan(1, aggregate.GetProperty("HashAgg Batches").GetInt32());
                Assert.IsGreaterThan(0, aggregate.GetProperty("Disk Usage").GetInt32());
            }

            await AssertReleased(connection, transaction, 3000, token);
            await Reset(connection, transaction, "normal", token);
            Assert.AreEqual("3000:4501500", await Scalar<string>(connection, transaction, """
                SELECT count(*) || ':' || sum(total) FROM (
                    SELECT value,aggregate_values.managed_sum(value) AS total
                    FROM aggregate_values.group_input GROUP BY value) AS groups
                """, token));
            await AssertReleased(connection, transaction, 3000, token);
            await Execute(connection, transaction, "SET LOCAL enable_hashagg=off; SET LOCAL enable_sort=on", token);
            await Reset(connection, transaction, "normal", token);
            Assert.AreEqual("3000:4501500", await Scalar<string>(connection, transaction, """
                SELECT count(*) || ':' || sum(total) FROM (
                    SELECT value,aggregate_values.managed_sum(value) AS total
                    FROM aggregate_values.group_input GROUP BY value) AS groups
                """, token));
            await AssertReleased(connection, transaction, 3000, token);
        });

    /// <summary>
    /// Closing an unconsumed aggregate portal and stopping under LIMIT release the unfinished group safely.
    /// </summary>
    [TestMethod]
    public Task CursorCloseAndLimitReleaseUnfinishedAggregateOwners()
        => Run(nameof(CursorCloseAndLimitReleaseUnfinishedAggregateOwners), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, "SET LOCAL enable_hashagg=off", token);
            await Reset(connection, transaction, "normal", token);
            await Execute(connection, transaction, """
                DECLARE aggregate_pending CURSOR FOR
                SELECT aggregate_values.managed_sum(value) FROM generate_series(1,100) AS value GROUP BY value ORDER BY value
                """, token);
            Assert.AreEqual(1L, await Scalar<long>(connection, transaction, "FETCH 1 FROM aggregate_pending", token));
            int[] pending = await Status(connection, transaction, token);
            Assert.IsGreaterThan(0, pending[2], "A suspended GroupAggregate retains its current state until portal cleanup.");
            await Execute(connection, transaction, "CLOSE aggregate_pending", token);
            await AssertBalancedRelease(connection, transaction, token);
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction,
                "SELECT count(*) FROM pg_cursors WHERE name='aggregate_pending'", token));
            await Reset(connection, transaction, "normal", token);
            Assert.AreEqual(1L, await Scalar<long>(connection, transaction, """
                SELECT aggregate_values.managed_sum(value) FROM generate_series(1,100) AS value
                GROUP BY value ORDER BY value LIMIT 1
                """, token));
            await AssertBalancedRelease(connection, transaction, token);
        });

    /// <summary>
    /// Native parameterized rescans begin with fresh state and do not leak previous group contents.
    /// </summary>
    [TestMethod]
    public Task ParameterizedRescansReinitializeManagedState()
        => Run(nameof(ParameterizedRescansReinitializeManagedState), async (connection, transaction, token) =>
        {
            await Reset(connection, transaction, "normal", token);
            long[] expected = [3, 5, 7];
            Assert.AreSequenceEqual(expected, await Scalar<long[]>(connection, transaction, """
                SELECT array_agg(total ORDER BY g) FROM generate_series(1,3) AS g
                CROSS JOIN LATERAL (
                    SELECT aggregate_values.managed_sum(v) AS total FROM (VALUES(g),(g+1)) AS input(v)) AS grouped
                """, token));
            await AssertReleased(connection, transaction, 3, token);
        });

    /// <summary>
    /// Throwing first-owner cleanup emits warnings without skipping the remaining owner's release.
    /// </summary>
    [TestMethod]
    public Task DisposeFailureWarnsAndReleasesRemainingOwners()
        => Run(nameof(DisposeFailureWarnsAndReleasesRemainingOwners), async (connection, transaction, token) =>
        {
            var warnings = new List<PostgresNotice>();
            connection.Notice += (_, notice) => warnings.Add(notice.Notice);
            await Reset(connection, transaction, "dispose_error", token);
            await using var command = new NpgsqlCommand("""
                SELECT aggregate_values.managed_sum(v),aggregate_values.managed_sum(v+10)
                FROM (VALUES(1),(2),(3)) AS input(v)
                """, connection, transaction);
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
            {
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(6L, reader.GetInt64(0));
                Assert.AreEqual(36L, reader.GetInt64(1));
                Assert.IsFalse(await reader.ReadAsync(token));
            }

            await AssertReleased(connection, transaction, 2, token);
            Assert.AreEqual(2, (await Status(connection, transaction, token))[8]);
            Assert.HasCount(2, warnings);
            foreach (PostgresNotice warning in warnings)
            {
                Assert.AreEqual("WARNING", warning.InvariantSeverity);
                Assert.AreEqual("Ankus aggregate state cleanup failed", warning.MessageText);
            }

            Assert.AreEqual(42, await Scalar<int>(connection, transaction, "SELECT 42", token));
        });

    /// <summary>
    /// Abort cleanup preserves the original callback error despite a failing Dispose and closes owned SPI resources safely.
    /// </summary>
    /// <param name="mode">The state resource or disposal failure probe.</param>
    [TestMethod]
    [DataRow("dispose_error_transition")]
    [DataRow("resources_error")]
    public Task AbortCleanupPreservesOriginalErrorAndReleasesResources(string mode)
        => Run(nameof(AbortCleanupPreservesOriginalErrorAndReleasesResources), async (connection, transaction, token) =>
        {
            await Reset(connection, transaction, mode, token);
            await transaction.SaveAsync("owner_abort", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection, transaction,
                "SELECT aggregate_values.managed_sum(v ORDER BY v) FROM (VALUES(1),(2),(3)) AS input(v)", token));
            Assert.AreEqual("P7801", error.SqlState);
            Assert.AreEqual("aggregate transition failed", error.MessageText);
            await transaction.RollbackAsync("owner_abort", token);
            await AssertReleased(connection, transaction, 1, token);
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction,
                "SELECT count(*) FROM pg_cursors WHERE statement='SELECT generate_series(1,100)'", token));
            await Reset(connection, transaction, "normal", token);
            Assert.AreEqual(42L, await Scalar<long>(connection, transaction,
                "SELECT aggregate_values.managed_sum(v) FROM (VALUES(42)) AS input(v)", token));
            await AssertReleased(connection, transaction, 1, token);
        });

    /// <summary>
    /// A transaction ending with a suspended aggregate portal releases its native owner before later commands.
    /// </summary>
    /// <param name="commit">Whether to commit instead of rolling back the transaction owning the portal.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task TransactionEndReleasesSuspendedAggregateState(bool commit)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await Reset(connection, transaction, "normal", token);
            await Execute(connection, transaction, """
                SET LOCAL enable_hashagg=off;
                DECLARE aggregate_transaction_end CURSOR FOR
                SELECT aggregate_values.managed_sum(value) FROM generate_series(1,100) AS value GROUP BY value ORDER BY value
                """, token);
            Assert.AreEqual(1L, await Scalar<long>(connection, transaction, "FETCH 1 FROM aggregate_transaction_end", token));
            Assert.IsGreaterThan(0, (await Status(connection, transaction, token))[2]);
            if (commit)
            {
                await transaction.CommitAsync(token);
            }
            else
            {
                await transaction.RollbackAsync(token);
            }
        }

        await using NpgsqlTransaction recovery = await connection.BeginTransactionAsync(token);
        await AssertBalancedRelease(connection, recovery, token);
        Assert.AreEqual(42, await Scalar<int>(connection, recovery, "SELECT 42", token));
        await recovery.RollbackAsync(token);
    }

    /// <summary>
    /// Requires real allocation before asserting complete release for executor-dependent group counts.
    /// </summary>
    private static async Task AssertBalancedRelease(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        int[] status = await Status(connection, transaction, token);
        Assert.IsGreaterThan(0, status[0]);
        Assert.AreEqual(status[0], status[1]);
        Assert.AreEqual(0, status[2]);
        Assert.AreEqual(0, status[3]);
    }
}
