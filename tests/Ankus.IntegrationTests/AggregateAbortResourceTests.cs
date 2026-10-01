using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies aggregate SPI ownership after raw cleanup errors during ordinary and implicit abort teardown.
/// </summary>
public sealed partial class AggregateTests
{
    /// <summary>
    /// A swallowed raw cleanup error cannot leak retained plans, portals, payloads or failed recovery frames.
    /// </summary>
    /// <param name="transitionFailure">Whether a transition fails before the native cleanup error.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeAggregateCleanupErrorsReleaseOwnedResourcesAcrossImplicitAborts(bool transitionFailure)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        const string resources = """
            SELECT ARRAY[
                (SELECT count(*) FROM ankus_test_memory.contexts WHERE name = 'SPI Plan'),
                (SELECT count(*) FROM pg_cursors)]
            """;
        await using var command = new NpgsqlCommand(resources, connection);
        long[] baseline = Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token));
        string mode = transitionFailure ? "resources_native_transition" : "resources_native";
        for (int index = 0; index < 10; index++)
        {
            command.CommandText = $"SELECT aggregate_values.aggregate_reset('{mode}')";
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT aggregate_values.managed_sum(v ORDER BY v) FROM (VALUES(1),(2),(3)) AS input(v)";
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual("42704", error.SqlState);
            Assert.AreEqual("ExtensibleNodeMethods \"ankus_missing_aggregate_cleanup\" was not registered", error.MessageText);
            command.CommandText = "SELECT aggregate_values.aggregate_status()";
            int[] status = Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token));
            Assert.AreSequenceEqual([1, 1, 0, 0], status[..4], $"Aggregate ownership after failure {index}.");
            Assert.AreEqual(transitionFailure ? 2 : 3, status[4], "The abort case must fail during transition before cleanup.");
            command.CommandText = "SELECT aggregate_values.aggregate_trace()";
            Assert.AreSequenceEqual(["42704"], Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(token)));
            command.CommandText = resources;
            Assert.AreSequenceEqual(baseline, Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token)),
                $"Retained SPI resources after failure {index}.");
            command.CommandText = "SELECT set_values.set_interrupt_state()";
            Assert.AreSequenceEqual([0, 0], Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT 42";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }

        command.CommandText = "SELECT aggregate_values.aggregate_reset('normal')";
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT aggregate_values.managed_sum(v) FROM (VALUES(42)) AS input(v)";
        Assert.AreEqual(42L, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT aggregate_values.aggregate_status()";
        Assert.AreSequenceEqual([1, 1, 0, 0], Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token))[..4]);
        command.CommandText = resources;
        Assert.AreSequenceEqual(baseline, Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token)));
        Assert.AreEqual(backend, connection.ProcessID);
    }
}
