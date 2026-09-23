using System.Globalization;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies native registry acquisition rollback using the emitted guarded bridge in PostgreSQL.
/// </summary>
/// <param name="context">The current test's cancellation context.</param>
[TestClass]
public sealed class AllocatorRegistryFailureTests(TestContext context)
{
    /// <summary>
    /// Failure before Bump allocation leaves existing payloads live and permits a successful retry.
    /// </summary>
    /// <param name="mode">The native allocation-record failure to inject.</param>
    /// <param name="allocated">The number of temporary records successfully allocated.</param>
    [TestMethod]
    [DataRow(1, 0)]
    [DataRow(2, 1)]
    public Task RegistryExhaustionPrecedesBumpStorageAndPreservesLivePayload(int mode, int allocated)
        => CheckAsync(mode, 1, "53200", "unable to register an Ankus allocation", allocated, 0, 0);

    /// <summary>
    /// An actual Slab wrong-size ERROR releases the unpublished record and keeps a separate Bump payload readable.
    /// </summary>
    [TestMethod]
    public Task SlabAllocatorErrorReleasesUnpublishedReservationAndAllowsRetry()
        => CheckAsync(3, 1, "XX000", "unexpected alloc chunk size 63 (expected 64)", 1, 1, 0);

    /// <summary>
    /// A controlled NO_OOM NULL releases its unpublished record and preserves existing Bump storage.
    /// </summary>
    [TestMethod]
    public Task NoOomNullReleasesUnpublishedReservationAndAllowsRetry()
        => CheckAsync(4, 0, "00000", "", 1, 1, 0);

    /// <summary>
    /// Registry failure during adoption leaves the exact raw payload with its original owner until retry succeeds.
    /// </summary>
    /// <param name="mode">The native adoption-record failure to inject.</param>
    /// <param name="allocated">The number of temporary records successfully allocated.</param>
    [TestMethod]
    [DataRow(5, 0)]
    [DataRow(6, 1)]
    public Task FailedAdoptionRetainsRawOwnershipUntilSuccessfulRetry(int mode, int allocated)
        => CheckAsync(mode, 1, "53200", "unable to register an Ankus allocation", allocated, 0, 7654321);

    /// <summary>
    /// Checks independent native allocation counters, transported diagnostics, payloads, cleanup, and backend recovery.
    /// </summary>
    /// <param name="mode">The bounded native failure scenario.</param>
    /// <param name="status">The expected guarded callback status.</param>
    /// <param name="sqlState">The exact native diagnostic code.</param>
    /// <param name="message">The exact native diagnostic message.</param>
    /// <param name="allocated">The expected successful temporary record allocations and matching frees.</param>
    /// <param name="storageCalls">The expected calls to the native payload allocation boundary.</param>
    /// <param name="rawValue">The expected retained raw payload after failed adoption.</param>
    private Task CheckAsync(int mode, int status, string sqlState, string message, int allocated, int storageCalls, long rawValue)
        => PostgresFixture.Cluster.RunInTransactionAsync($"{nameof(AllocatorRegistryFailureTests)}_{mode}",
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand("SELECT tests.allocator_registry_fault($1)", connection, transaction);
                command.Parameters.AddWithValue(mode);
                // Repeating in this backend independently checks that native context
                // deletion released records, reset controls, and retained no poisoned state.
                for (int invocation = 0; invocation < 2; invocation++)
                {
                    string report = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
                    string[] fields = report.Split('|');
                    Assert.HasCount(11, fields);
                    Assert.AreEqual(status, int.Parse(fields[0], CultureInfo.InvariantCulture));
                    Assert.AreEqual(sqlState, fields[1]);
                    Assert.AreEqual(message, fields[2]);
                    Assert.AreEqual(allocated, int.Parse(fields[3], CultureInfo.InvariantCulture), "Reserved native records.");
                    Assert.AreEqual(allocated, int.Parse(fields[4], CultureInfo.InvariantCulture), "Released unpublished records.");
                    Assert.AreEqual(storageCalls, int.Parse(fields[5], CultureInfo.InvariantCulture), "Payload calls before failure.");
                    Assert.AreEqual(0, int.Parse(fields[6], CultureInfo.InvariantCulture), "Outstanding native bookkeeping change.");
                    Assert.AreEqual(0, int.Parse(fields[7], CultureInfo.InvariantCulture), "Published checked allocations change.");
                    Assert.AreEqual(1193046L, long.Parse(fields[8], CultureInfo.InvariantCulture), "Existing Bump payload.");
                    Assert.AreEqual(rawValue, long.Parse(fields[9], CultureInfo.InvariantCulture), "Raw ownership after failed adoption.");
                    Assert.AreEqual(7654321L, long.Parse(fields[10], CultureInfo.InvariantCulture), "Payload after successful retry.");
                }

                command.Parameters.Clear();
                command.CommandText = "SELECT pg_backend_pid(), 42, count(*)::integer FROM pg_backend_memory_contexts WHERE name LIKE 'Ankus registry fault%'";
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(backend, reader.GetInt32(0));
                Assert.AreEqual(42, reader.GetInt32(1));
                Assert.AreEqual(0, reader.GetInt32(2));
                Assert.IsFalse(await reader.ReadAsync(token));
            }, context.CancellationToken);
}
