using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Observes real catalog references and native locks before and after actual rollback of emitted value calls.
/// </summary>
/// <param name="context">The test cancellation and diagnostic context.</param>
[TestClass]
public sealed class NativeValueOwnershipTests(TestContext context)
{
    /// <summary>
    /// Native ERROR blocks backend work until rollback, while deliberate input recovery releases only its own resources.
    /// </summary>
    /// <param name="family">The numeric, temporal, network, geometry, range or array-cell operation.</param>
    /// <param name="invalidated">Whether an obsolete cache reference survives invalidation.</param>
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(0, true)]
    [DataRow(1, false)]
    [DataRow(1, true)]
    [DataRow(2, false)]
    [DataRow(2, true)]
    [DataRow(3, false)]
    [DataRow(3, true)]
    [DataRow(4, false)]
    [DataRow(4, true)]
    [DataRow(5, false)]
    [DataRow(5, true)]
    public Task NativeFaultPreservesCallerOwnershipAndRequiresRollback(int family, bool invalidated)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NativeFaultPreservesCallerOwnershipAndRequiresRollback), async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand("CREATE TEMP TABLE value_fault_writes(value integer); INSERT INTO value_fault_writes VALUES (42)",
                connection, transaction);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT count(*) FROM pg_locks WHERE relation='value_fault_writes'::regclass AND pid=pg_backend_pid() AND mode='RowExclusiveLock' AND granted";
            Assert.AreEqual(1L, await command.ExecuteScalarAsync(token));
            for (int mode = 0; mode < 4; mode++)
            {
                bool cancel = (mode & 1) != 0;
                bool recoverInput = (mode & 2) != 0;
                // From PostgreSQL 16, input recovery relies on soft input errors and opens no subtransaction, so an
                // injected hard fault is not recovered, as pg_input_is_valid does not recover one either.
                bool recovered = recoverInput && family < 5 && PostgresFixture.Cluster.Installation.Version.Major < 16;
                command.CommandText = "SELECT tests.value_ownership($1,$2,$3,$4)";
                command.Parameters.Clear();
                command.Parameters.AddWithValue(family);
                command.Parameters.AddWithValue(invalidated);
                command.Parameters.AddWithValue(cancel);
                command.Parameters.AddWithValue(recoverInput);
                int[] observed = Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token));
                Assert.AreSequenceEqual<int>([recovered ? 0 : 1, recovered && !cancel ? 0 : 1,
                    recovered ? 1 : 2, 1, !recovered && !cancel ? 1 : 0, 0, 1, 1], observed,
                    $"family={family}, invalidated={invalidated}, cancellation={cancel}, inputRecovery={recoverInput}");
                command.Parameters.Clear();
                command.CommandText = "SELECT count(*) FROM pg_locks WHERE relation='value_fault_writes'::regclass AND pid=pg_backend_pid() AND mode='RowExclusiveLock' AND granted";
                Assert.AreEqual(1L, await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT sum(value) FROM value_fault_writes";
                Assert.AreEqual(42L, await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT pg_backend_pid()";
                Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
            }
        }, context.CancellationToken);
}
