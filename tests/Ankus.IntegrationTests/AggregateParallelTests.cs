using System.Text.Json;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies actual PostgreSQL workers, process-independent state bytes and temporary deserializer ownership.
/// </summary>
public sealed partial class AggregateTests
{
    /// <summary>
    /// A launched worker and callback-carried PIDs prove that partial state really crossed a process boundary.
    /// </summary>
    [TestMethod]
    public Task ParallelWorkersSerializeDeserializeAndCombineOwnedState()
        => Run(nameof(ParallelWorkersSerializeDeserializeAndCombineOwnedState), async (connection, transaction, token) =>
        {
            await PrepareParallelInput(connection, transaction, token);
            string plan = await Scalar<string>(connection, transaction,
                "EXPLAIN (ANALYZE, FORMAT JSON) SELECT aggregate_values.parallel_sum(value) FROM aggregate_values.parallel_input", token);
            using (JsonDocument document = JsonDocument.Parse(plan))
            {
                JsonElement root = document.RootElement[0].GetProperty("Plan");
                Assert.IsGreaterThan(0, WorkersLaunched(root), "EXPLAIN ANALYZE must report a worker that actually launched.");
                Assert.IsTrue(HasPartialAggregate(root), "The worker plan must perform partial aggregation.");
            }

            await Execute(connection, transaction, "SELECT aggregate_values.parallel_reset('normal')", token);
            long[] result = await Scalar<long[]>(connection, transaction,
                "SELECT aggregate_values.parallel_sum(value) FROM aggregate_values.parallel_input", token);
            Assert.AreEqual(450015000L, result[0]);
            Assert.AreEqual(30000L, result[1]);
            Assert.IsGreaterThan(0L, result[2], "Combine must actually run.");
            Assert.IsGreaterThan(0L, result[3], "Serialized worker states must reach the final result.");
            Assert.AreEqual(result[3], result[4], "Every transported state must be deserialized.");
            Assert.IsNotEmpty(result.Skip(5).Where(process => process != connection.ProcessID), "A foreign backend must have processed input rows.");
            await AssertParallelReleased(connection, transaction, token);
            await Execute(connection, transaction, "SET LOCAL max_parallel_workers_per_gather=0; SELECT aggregate_values.parallel_reset('normal')", token);
            long[] serial = await Scalar<long[]>(connection, transaction,
                "SELECT aggregate_values.parallel_sum(value) FROM aggregate_values.parallel_input", token);
            Assert.AreSequenceEqual([450015000, 30000, 0, 0, 0, connection.ProcessID], serial);
            await AssertParallelReleased(connection, transaction, token);
        });

    /// <summary>
    /// Errors from distinct transport roles release temporary leader state and preserve PostgreSQL error identity.
    /// </summary>
    /// <param name="failure">The input sentinel selecting a support callback.</param>
    /// <param name="sqlState">The exact callback SQLSTATE.</param>
    /// <param name="message">The exact managed diagnostic message.</param>
    [TestMethod]
    [DataRow(1, "P7811", "aggregate serialization failed")]
    [DataRow(2, "P7812", "aggregate deserialization failed")]
    [DataRow(3, "P7813", "aggregate combine failed")]
    [DataRow(4, "38000", "Unable to read beyond the end of the stream.")]
    [DataRow(5, "38000", "Unable to read beyond the end of the stream.")]
    [DataRow(6, "22P03", "invalid aggregate state format")]
    public Task ParallelCallbackErrorsReleaseOwnedStateAndRecover(int failure, string sqlState, string message)
        => Run(nameof(ParallelCallbackErrorsReleaseOwnedStateAndRecover), async (connection, transaction, token) =>
        {
            await PrepareParallelInput(connection, transaction, token);
            await Execute(connection, transaction, "SELECT aggregate_values.parallel_reset('normal')", token);
            await transaction.SaveAsync("parallel_error", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection, transaction,
                $"SELECT aggregate_values.parallel_sum(-{failure}) FROM aggregate_values.parallel_input", token));
            Assert.AreEqual(sqlState, error.SqlState);
            Assert.AreEqual(message, error.MessageText);
            await transaction.RollbackAsync("parallel_error", token);
            await AssertParallelReleased(connection, transaction, token);
            await Execute(connection, transaction, "SELECT aggregate_values.parallel_reset('normal')", token);
            long[] recovered = await Scalar<long[]>(connection, transaction,
                "SELECT aggregate_values.parallel_sum(value) FROM aggregate_values.parallel_input", token);
            Assert.AreEqual(450015000L, recovered[0]);
            Assert.AreEqual(30000L, recovered[1]);
            Assert.IsGreaterThan(0L, recovered[4]);
            await AssertParallelReleased(connection, transaction, token);
        });

    /// <summary>
    /// Returning a borrowed temporary deserializer wrapper cannot silently adopt another owner's native pointer.
    /// </summary>
    [TestMethod]
    public Task ParallelCombineRejectsBorrowedTemporaryStateAndRecovers()
        => Run(nameof(ParallelCombineRejectsBorrowedTemporaryStateAndRecovers), async (connection, transaction, token) =>
        {
            await PrepareParallelInput(connection, transaction, token);
            await Execute(connection, transaction, "SELECT aggregate_values.parallel_reset('borrow')", token);
            await transaction.SaveAsync("borrowed_state", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection, transaction,
                "SELECT aggregate_values.parallel_sum(value) FROM aggregate_values.parallel_input", token));
            Assert.AreEqual("38000", error.SqlState);
            Assert.Contains("owner", error.MessageText);
            await transaction.RollbackAsync("borrowed_state", token);
            await AssertParallelReleased(connection, transaction, token);
            await Execute(connection, transaction, "SELECT aggregate_values.parallel_reset('normal')", token);
            long[] recovered = await Scalar<long[]>(connection, transaction,
                "SELECT aggregate_values.parallel_sum(value) FROM aggregate_values.parallel_input", token);
            Assert.AreEqual(450015000L, recovered[0]);
            Assert.IsGreaterThan(0L, recovered[4]);
            await AssertParallelReleased(connection, transaction, token);
        });

    /// <summary>
    /// NULL serialized partial states skip strict deserialization and safely reach a non-strict NULL combine path.
    /// </summary>
    [TestMethod]
    public Task NullSerializedAndEmptyPartialStatesRemainSqlNull()
        => Run(nameof(NullSerializedAndEmptyPartialStatesRemainSqlNull), async (connection, transaction, token) =>
        {
            await PrepareParallelInput(connection, transaction, token);
            await Execute(connection, transaction, "SELECT aggregate_values.parallel_reset('normal')", token);
            long[] empty = [0, 0, 0, 0, 0];
            Assert.AreSequenceEqual(empty, await Scalar<long[]>(connection, transaction,
                "SELECT aggregate_values.parallel_sum(-7) FROM aggregate_values.parallel_input", token));
            await AssertParallelReleased(connection, transaction, token);
            Assert.AreSequenceEqual(empty, await Scalar<long[]>(connection, transaction,
                "SELECT aggregate_values.parallel_sum(value) FROM aggregate_values.parallel_input WHERE value<0", token));
            await AssertParallelReleased(connection, transaction, token);
        });

    /// <summary>
    /// Ordinary SQL-datum partial states each receive INITCOND, including the final combine state.
    /// </summary>
    [TestMethod]
    public Task OrdinaryPartialStatesReceiveInitialConditionIndependently()
        => Run(nameof(OrdinaryPartialStatesReceiveInitialConditionIndependently), async (connection, transaction, token) =>
        {
            await PrepareParallelInput(connection, transaction, token);
            string plan = await Scalar<string>(connection, transaction,
                "EXPLAIN(ANALYZE,FORMAT JSON) SELECT aggregate_values.parallel_seeded_array(value) FROM aggregate_values.parallel_input", token);
            using (JsonDocument document = JsonDocument.Parse(plan))
            {
                JsonElement root = document.RootElement[0].GetProperty("Plan");
                Assert.IsGreaterThan(0, WorkersLaunched(root));
                Assert.IsTrue(HasPartialAggregate(root));
            }

            long[] parallel = await Scalar<long[]>(connection, transaction,
                "SELECT aggregate_values.parallel_seeded_array(value) FROM aggregate_values.parallel_input", token);
            Assert.HasCount(2, parallel);
            Assert.IsGreaterThan(1L, parallel[1], "The partial workers and final combine phase each contribute a seed.");
            Assert.AreEqual(450015000L + 10 * parallel[1], parallel[0]);
            await Execute(connection, transaction, "SET LOCAL max_parallel_workers_per_gather=0", token);
            long[] serial = [450015010, 1];
            Assert.AreSequenceEqual(serial, await Scalar<long[]>(connection, transaction,
                "SELECT aggregate_values.parallel_seeded_array(value) FROM aggregate_values.parallel_input", token));
        });

    /// <summary>
    /// Creates a real relation and low-cost parallel plan with leader participation disabled.
    /// </summary>
    private static Task<int> PrepareParallelInput(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
        => Execute(connection, transaction, """
            CREATE TABLE aggregate_values.parallel_input AS SELECT generate_series(1,30000) AS value;
            ALTER TABLE aggregate_values.parallel_input SET (parallel_workers=2);
            ANALYZE aggregate_values.parallel_input;
            SET LOCAL max_parallel_workers_per_gather=2;
            SET LOCAL min_parallel_table_scan_size=0;
            SET LOCAL parallel_setup_cost=0;
            SET LOCAL parallel_tuple_cost=0;
            SET LOCAL parallel_leader_participation=off;
            """, token);

    /// <summary>
    /// Checks independently balanced lifecycle counters after successful or aborted parallel work.
    /// </summary>
    private static async Task AssertParallelReleased(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        int[] status = await Scalar<int[]>(connection, transaction, "SELECT aggregate_values.parallel_status()", token);
        Assert.AreEqual(status[0], status[1], "Every allocated leader payload must be released.");
        Assert.AreEqual(0, status[2], "No temporary or final leader payload remains live.");
        Assert.AreEqual(0, status[3], "No state is disposed twice.");
    }

    /// <summary>
    /// Totals actually launched workers throughout the PostgreSQL JSON execution plan.
    /// </summary>
    private static int WorkersLaunched(JsonElement plan)
    {
        int result = plan.TryGetProperty("Workers Launched", out JsonElement workers) ? workers.GetInt32() : 0;
        if (plan.TryGetProperty("Plans", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                result += WorkersLaunched(child);
            }
        }

        return result;
    }

    /// <summary>
    /// Finds the partial aggregation phase rather than treating a Gather node as sufficient evidence.
    /// </summary>
    private static bool HasPartialAggregate(JsonElement plan)
    {
        if (plan.TryGetProperty("Partial Mode", out JsonElement mode) && mode.GetString() == "Partial")
        {
            return true;
        }

        if (plan.TryGetProperty("Plans", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                if (HasPartialAggregate(child))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
