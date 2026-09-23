using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies native context ownership, allocator boundaries, cleanup, and recovery in PostgreSQL.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class MemoryContextTests(TestContext context)
{
    /// <summary>
    /// Native reset variants preserve the documented contexts and invalidate exactly the affected allocations.
    /// </summary>
    /// <param name="mode">The reset variant.</param>
    /// <param name="expected">The exact context and allocation states.</param>
    [TestMethod]
    [DataRow(0, "True,False,False|stale,stale,stale")]
    [DataRow(1, "True,True,True|stale,22,33")]
    [DataRow(2, "True,True,True|11,stale,stale")]
    public Task ResetVariantsPreserveNamesAndInvalidateTheirExactSubtree(int mode, string expected)
        => CheckAsync(nameof(ResetVariantsPreserveNamesAndInvalidateTheirExactSubtree),
            $"SELECT datatype.memory_reset_tree({mode})", $"{expected}|memory root café|memory root café|False,False");

    /// <summary>
    /// Real palloc chunks preserve copied values, reallocation contents, ranges, zeroes, and native owner identity.
    /// </summary>
    [TestMethod]
    public Task AllocationBoundariesPreserveBytesAndActualNativeOwner()
        => CheckAsync(nameof(AllocationBoundariesPreserveBytesAndActualNativeOwner),
            "SELECT datatype.memory_allocation_boundaries()", "0000000000000000|0102000000060708|4128|True|4|0|0");

    /// <summary>
    /// Invalid sizes retain PostgreSQL errors, the selected context, original contents, and the same backend session.
    /// </summary>
    /// <param name="operation">Allocate, no-OOM allocate, or reallocate.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public Task AllocationErrorsPreserveContentsAndSelectedContext(int operation)
        => CheckAsync(nameof(AllocationErrorsPreserveContentsAndSelectedContext),
            $"SELECT datatype.memory_allocation_error({operation})", "XX000|73|4|True|True|42");

    /// <summary>
    /// Nested managed failures restore the exact context and failed deletion can be retried after leaving that context.
    /// </summary>
    [TestMethod]
    public Task NestedScopesRestoreAfterFailureAndAllowDeletionRetry()
        => CheckAsync(nameof(NestedScopesRestoreAfterFailureAndAllowDeletionRetry),
            "SELECT datatype.memory_nested_scopes()", "True|True|True|55000|True|False");

    /// <summary>
    /// Native catalog statistics independently witness context creation, parent selection, allocation growth, and deletion.
    /// </summary>
    [TestMethod]
    public Task NativeInventoryProvesOwnedAndBorrowedContextLifetimes()
        => CheckAsync(nameof(NativeInventoryProvesOwnedAndBorrowedContextLifetimes),
            "SELECT datatype.memory_native_inventory()", "True|True|True|True|True|1|0|True");

    /// <summary>
    /// Destructive resets cannot reclaim active native callback storage or its ancestors after a managed context switch.
    /// </summary>
    [TestMethod]
    public Task ActiveNativeCallbackStorageRejectsDestructiveResets()
        => CheckAsync(nameof(ActiveNativeCallbackStorageRejectsDestructiveResets),
            "SELECT datatype.memory_protected_contexts()", "55000,55000,55000|True|True|42");

    /// <summary>
    /// Stable provider identities allow later callback access and transaction end invalidates both context and chunk.
    /// </summary>
    /// <param name="commit">Whether the transaction commits or rolls back.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SavedHandlesSurviveCallbacksAndExpireAtTransactionEnd(bool commit)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await using var command = new NpgsqlCommand("SELECT datatype.memory_save(137, false)", connection, transaction);
            Assert.AreEqual(137, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT datatype.memory_read_saved()";
            Assert.AreEqual(137, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT datatype.memory_saved_alive()";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            if (commit)
            {
                await transaction.CommitAsync(token);
            }
            else
            {
                await transaction.RollbackAsync(token);
            }
        }

        await using var check = new NpgsqlCommand("SELECT datatype.memory_saved_alive()", connection);
        Assert.IsFalse(Assert.IsInstanceOfType<bool>(await check.ExecuteScalarAsync(token)));
        check.CommandText = "SELECT datatype.memory_read_saved()";
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => check.ExecuteScalarAsync(token));
        Assert.Contains("reset, freed, or deleted", error.MessageText);
        check.CommandText = "SELECT datatype.memory_release_saved()";
        await check.ExecuteNonQueryAsync(token);
        check.CommandText = "SELECT 42";
        Assert.AreEqual(42, await check.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Subtransaction rollback reclaims its context while a top-transaction allocation remains usable.
    /// </summary>
    /// <param name="subtransaction">Whether the allocation belongs to the rolled-back subtransaction.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task SubtransactionRollbackInvalidatesOnlyItsOwnedContext(bool subtransaction)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SubtransactionRollbackInvalidatesOnlyItsOwnedContext),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SAVEPOINT memory_scope", connection, transaction);
                await command.ExecuteNonQueryAsync(token);
                command.CommandText = "SELECT datatype.memory_save(251, $1)";
                command.Parameters.AddWithValue(subtransaction);
                Assert.AreEqual(251, await command.ExecuteScalarAsync(token));
                command.Parameters.Clear();
                command.CommandText = "ROLLBACK TO SAVEPOINT memory_scope";
                await command.ExecuteNonQueryAsync(token);
                command.CommandText = "SELECT datatype.memory_saved_alive()";
                Assert.AreEqual(!subtransaction, await command.ExecuteScalarAsync(token));
                if (!subtransaction)
                {
                    command.CommandText = "SELECT datatype.memory_read_saved()";
                    Assert.AreEqual(251, await command.ExecuteScalarAsync(token));
                }

                command.CommandText = "SELECT datatype.memory_release_saved()";
                await command.ExecuteNonQueryAsync(token);
            }, context.CancellationToken);

    /// <summary>
    /// Iterators retain values between callbacks and release transaction-owned storage after exhaustion or early shutdown.
    /// </summary>
    /// <param name="count">The requested rows, negative to fail if iteration resumes after its first yield.</param>
    /// <param name="limit">Whether PostgreSQL stops after the first row.</param>
    [TestMethod]
    [DataRow(3, false)]
    [DataRow(-3, true)]
    public Task IteratorCallbacksRetainAndDisposeNativeStorage(int count, bool limit)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(IteratorCallbacksRetainAndDisposeNativeStorage),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.memory_sequence_disposals()", connection, transaction);
                int before = Assert.IsInstanceOfType<int>(await command.ExecuteScalarAsync(token));
                command.CommandText = $"SELECT array_agg(v) FROM (SELECT datatype.memory_sequence({count}) v{(limit ? " LIMIT 1" : string.Empty)}) rows";
                Assert.AreSequenceEqual<int>(limit ? [81] : [81, 82, 83],
                    Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token)));
                command.CommandText = "SELECT datatype.memory_sequence_disposals()";
                Assert.AreEqual(before + 1, await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT count(*) FROM pg_backend_memory_contexts WHERE ident = 'memory sequence'";
                Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Iterator failure unwinds its native allocations before savepoint recovery in the same backend.
    /// </summary>
    [TestMethod]
    public Task IteratorFailureReclaimsNativeStorageAndRecovers()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(IteratorFailureReclaimsNativeStorageAndRecovers),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.memory_sequence_disposals()", connection, transaction);
                int before = Assert.IsInstanceOfType<int>(await command.ExecuteScalarAsync(token));
                command.CommandText = "SAVEPOINT iterator_failure";
                await command.ExecuteNonQueryAsync(token);
                command.CommandText = "SELECT * FROM datatype.memory_sequence(-3)";
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
                Assert.AreEqual("38000", error.SqlState);
                Assert.AreEqual("memory sequence failure", error.MessageText);
                command.CommandText = "ROLLBACK TO SAVEPOINT iterator_failure";
                await command.ExecuteNonQueryAsync(token);
                command.CommandText = "SELECT datatype.memory_sequence_disposals()";
                Assert.AreEqual(before + 1, await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT count(*) FROM pg_backend_memory_contexts WHERE ident = 'memory sequence'";
                Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Native context names convert through the database encoding, and failed creation leaves no context behind.
    /// </summary>
    /// <param name="encoding">The database encoding.</param>
    /// <param name="state">The expected outcome when naming a context with an elephant character.</param>
    [TestMethod]
    [DataRow("UTF8", "representable")]
    [DataRow("LATIN1", "22P05")]
    public async Task ContextNamesUseServerEncodingAndRecoverWithoutLeaking(string encoding, string state)
    {
        CancellationToken token = context.CancellationToken;
        string database = "memory_encoding_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection administrator = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using (var create = new NpgsqlCommand(
            $"CREATE DATABASE {database} TEMPLATE template0 ENCODING '{encoding}' LC_COLLATE 'C' LC_CTYPE 'C'", administrator))
        {
            await create.ExecuteNonQueryAsync(token);
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(administrator.ConnectionString) { Database = database };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(token);
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand("CREATE EXTENSION ankus_test", connection);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT memory_encoded_name()";
            Assert.AreEqual($"memory café|1|{state}|0|42", await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", administrator);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private Task CheckAsync(string name, string sql, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(name, async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            Assert.AreEqual(expected, Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT 42";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }, context.CancellationToken);
}
