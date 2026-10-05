using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies iterator cleanup and retained native ownership across repeated savepoint aborts.
/// </summary>
/// <param name="context">The current cancellation and test context.</param>
[TestClass]
public sealed class IteratorSubtransactionCleanupTests(TestContext context)
{
    /// <summary>
    /// Native cleanup errors release each owned resource and recovery frame without changing the surviving backend.
    /// </summary>
    /// <param name="openCursor">Whether the iterator retains an SPI portal alongside its plan.</param>
    /// <param name="executorFailure">Whether an executor error starts restricted abort cleanup.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public Task NativeIteratorCleanupErrorsRecoverAcrossSavepointAborts(bool openCursor, bool executorFailure)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NativeIteratorCleanupErrorsRecoverAcrossSavepointAborts),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand("SELECT set_values.set_reset()", connection, transaction);
                await command.ExecuteNonQueryAsync(token);
                const string Resources = """
                    SELECT ARRAY[
                        (SELECT count(*) FROM ankus_test_memory.contexts WHERE name = 'SPI Plan'),
                        (SELECT count(*) FROM pg_cursors)]
                    """;
                command.CommandText = Resources;
                long[] baseline = Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token));
                string source = $"set_values.set_owned_resources_with_cleanup_error({openCursor})";
                for (int index = 0; index < 10; index++)
                {
                    await transaction.SaveAsync("iterator_cleanup_error", token);
                    command.CommandText = executorFailure
                        ? $"SELECT 1/(CASE WHEN value='Low'::datatype.enum_mood THEN 0 ELSE 1 END) FROM (SELECT {source} AS value) input"
                        : $"SELECT value::text FROM {source} value";
                    PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
                    Assert.AreEqual(executorFailure ? "22012" : "42704", error.SqlState);
                    Assert.AreEqual(executorFailure ? "division by zero" :
                        "ExtensibleNodeMethods \"ankus_missing_iterator_cleanup\" was not registered", error.MessageText);
                    await transaction.RollbackAsync("iterator_cleanup_error", token);
                    await transaction.ReleaseAsync("iterator_cleanup_error", token);

                    command.CommandText = "SELECT set_values.set_status()";
                    int[] status = Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token));
                    Assert.HasCount(13, status);
                    Assert.AreEqual(index + 1, status[12], "Each acquired iterator must execute its finally block once.");
                    Assert.AreEqual(executorFailure ? 0 : index + 1, status[8]);
                    Assert.AreEqual(executorFailure ? index + 1 : 0, status[9]);
                    Assert.AreEqual(0, status[10]);
                    Assert.AreEqual(0, status[11]);
                    command.CommandText = "SELECT set_values.set_native_cleanup_caught()";
                    Assert.AreEqual(index + 1, await command.ExecuteScalarAsync(token));
                    command.CommandText = Resources;
                    Assert.AreSequenceEqual(baseline, Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token)),
                        $"Retained SPI resources after savepoint abort {index}.");
                    command.CommandText = "SELECT set_values.set_interrupt_state()";
                    Assert.AreSequenceEqual([0, 0], Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token)));
                    command.CommandText = "SELECT pg_backend_pid()";
                    Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
                }

                command.CommandText = $"SELECT value::text FROM set_values.set_owned_resources({openCursor}) value";
                Assert.AreEqual("Low", await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT set_values.set_status()";
                Assert.AreEqual(11, Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token))[12]);
                command.CommandText = "SELECT set_values.set_native_cleanup_caught()";
                Assert.AreEqual(10, await command.ExecuteScalarAsync(token));
                command.CommandText = Resources;
                Assert.AreSequenceEqual(baseline, Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token)));
                command.CommandText = "SELECT pg_backend_pid()";
                Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);
}
