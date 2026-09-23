using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies final-state sharing, mutable window restrictions and planner sort-operator behavior.
/// </summary>
public sealed partial class AggregateTests
{
    /// <summary>
    /// Final-modify policy changes actual transition sharing as well as native context metadata.
    /// </summary>
    /// <param name="modify">The native final modification policy.</param>
    /// <param name="shared">Whether PostgreSQL should share the transition state.</param>
    /// <param name="calls">The exact transition invocation count.</param>
    [TestMethod]
    [DataRow("READ_ONLY", true, 3)]
    [DataRow("SHAREABLE", true, 3)]
    [DataRow("READ_WRITE", false, 6)]
    public Task FinalModificationControlsActualStateSharing(string modify, bool shared, int calls)
        => Run(nameof(FinalModificationControlsActualStateSharing), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, $"""
                CREATE AGGREGATE aggregate_values.other_final(integer) (
                    SFUNC=aggregate_values.shared_sum_transition, STYPE=integer, INITCOND='0',
                    FINALFUNC=pg_catalog.int4abs, FINALFUNC_MODIFY={modify});
                SELECT aggregate_values.final_reset();
                """, token);
            await using var command = new NpgsqlCommand("""
                SELECT aggregate_values.shared_sum(v),aggregate_values.other_final(v)
                FROM (VALUES(1),(2),(3)) AS input(v)
                """, connection, transaction);
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
            {
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(6, reader.GetInt32(0));
                Assert.AreEqual(6, reader.GetInt32(1));
                Assert.IsFalse(await reader.ReadAsync(token));
            }

            Assert.AreEqual(calls, await Scalar<int>(connection, transaction, "SELECT aggregate_values.final_transitions()", token));
            Assert.AreSequenceEqual([shared], await Scalar<bool[]>(connection, transaction,
                "SELECT aggregate_values.final_shared()", token));
        });

    /// <summary>
    /// PostgreSQL refuses mutable finals in windows but selects a separate safe moving implementation when provided.
    /// </summary>
    [TestMethod]
    public Task MutableFinalWindowsRequireAReadonlyMovingImplementation()
        => Run(nameof(MutableFinalWindowsRequireAReadonlyMovingImplementation), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, """
                CREATE AGGREGATE aggregate_values.mutable_only(integer) (
                    SFUNC=aggregate_values.shared_sum_transition, STYPE=integer, INITCOND='0',
                    FINALFUNC=aggregate_values.shared_sum_final, FINALFUNC_MODIFY=READ_WRITE)
                """, token);
            await transaction.SaveAsync("mutable_final", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection, transaction,
                "SELECT aggregate_values.mutable_only(v) OVER(ORDER BY v) FROM (VALUES(1),(2)) AS input(v)", token));
            Assert.AreEqual("0A000", error.SqlState);
            await transaction.RollbackAsync("mutable_final", token);
            Assert.AreEqual(1006, await Scalar<int>(connection, transaction,
                "SELECT aggregate_values.mutable_final_sum(v) FROM (VALUES(1),(2),(3)) AS input(v)", token));
            int[] expected = [1, 3, 6];
            Assert.AreSequenceEqual(expected, await Scalar<int[]>(connection, transaction, """
                SELECT array_agg(total ORDER BY v) FROM (
                    SELECT v,aggregate_values.mutable_final_sum(v) OVER(ORDER BY v ROWS UNBOUNDED PRECEDING) AS total
                    FROM (VALUES(1),(2),(3)) AS input(v)) AS output
                """, token));
        });

    /// <summary>
    /// A qualified sort operator enables the native index optimization and preserves NULL/empty semantics.
    /// </summary>
    [TestMethod]
    public Task SortOperatorEnablesIndexMinimumWithoutChangingResults()
        => Run(nameof(SortOperatorEnablesIndexMinimumWithoutChangingResults), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, """
                CREATE TABLE aggregate_values.minimum_input(value integer);
                INSERT INTO aggregate_values.minimum_input SELECT generate_series(1,1000);
                INSERT INTO aggregate_values.minimum_input VALUES(NULL),(-17),(NULL);
                CREATE INDEX minimum_input_idx ON aggregate_values.minimum_input(value);
                ANALYZE aggregate_values.minimum_input;
                SET LOCAL enable_seqscan=off;
                """, token);
            Assert.AreEqual(-17, await Scalar<int>(connection, transaction,
                "SELECT aggregate_values.native_min(value) FROM aggregate_values.minimum_input", token));
            string plan = await Scalar<string>(connection, transaction,
                "EXPLAIN(FORMAT JSON) SELECT aggregate_values.native_min(value) FROM aggregate_values.minimum_input", token);
            Assert.Contains("minimum_input_idx", plan);
            Assert.Contains("Limit", plan);
            Assert.DoesNotContain("Aggregate", plan);
            await using var command = new NpgsqlCommand(
                "SELECT aggregate_values.native_min(value) FROM aggregate_values.minimum_input WHERE value IS NULL", connection, transaction);
            Assert.AreSame(DBNull.Value, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT aggregate_values.native_min(value) FROM aggregate_values.minimum_input WHERE false";
            Assert.AreSame(DBNull.Value, await command.ExecuteScalarAsync(token));
        });
}
