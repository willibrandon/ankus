using System.Text.Json;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies actual planner callback registration, estimates, query values and recovery.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class PlannerSupportTests(TestContext context)
{
    /// <summary>
    /// PostgreSQL registers the renamed generated support routine and uses its estimate while preserving real values.
    /// </summary>
    [TestMethod]
    public Task ManagedPlannerSupportControlsEstimates()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ManagedPlannerSupportControlsEstimates),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("""
                    SELECT prosupport = 'ankus_planner.row_estimate(internal)'::regprocedure, prorows, proretset
                    FROM pg_proc WHERE oid = 'ankus_planner.numbers()'::regprocedure
                    """, connection, transaction);
                await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
                {
                    Assert.IsTrue(await reader.ReadAsync(token));
                    Assert.IsTrue(reader.GetBoolean(0));
                    Assert.AreEqual(1000f, reader.GetFloat(1));
                    Assert.IsTrue(reader.GetBoolean(2));
                    Assert.IsFalse(await reader.ReadAsync(token));
                }

                command.CommandText = "EXPLAIN (FORMAT JSON) SELECT * FROM ankus_planner.numbers()";
                using JsonDocument plan = JsonDocument.Parse(Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token)));
                Assert.AreEqual(37, plan.RootElement[0].GetProperty("Plan").GetProperty("Plan Rows").GetInt32());
                command.CommandText = "SELECT array_agg(value ORDER BY value) FROM ankus_planner.numbers() AS n(value)";
                Assert.AreSequenceEqual<int>([1, 2, 3], Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token)));
            }, context.CancellationToken);

    /// <summary>
    /// An error inside a managed planner callback unwinds safely and permits another plan on the same backend.
    /// </summary>
    [TestMethod]
    public Task ManagedPlannerSupportRecoversAfterErrors()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ManagedPlannerSupportRecoversAfterErrors),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT pg_backend_pid()", connection, transaction);
                int backend = Assert.IsInstanceOfType<int>(await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT ankus_planner.arm_failure()";
                await command.ExecuteNonQueryAsync(token);
                await transaction.SaveAsync("planner", token);
                command.CommandText = "EXPLAIN (FORMAT JSON) SELECT * FROM ankus_planner.numbers()";
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
                Assert.AreEqual("38000", error.SqlState);
                Assert.AreEqual("managed planner estimate failed", error.MessageText);
                await transaction.RollbackAsync("planner", token);
                using JsonDocument plan = JsonDocument.Parse(Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token)));
                Assert.AreEqual(37, plan.RootElement[0].GetProperty("Plan").GetProperty("Plan Rows").GetInt32());
                command.CommandText = "SELECT pg_backend_pid()";
                Assert.AreEqual(backend, Assert.IsInstanceOfType<int>(await command.ExecuteScalarAsync(token)));
            }, context.CancellationToken);
}
