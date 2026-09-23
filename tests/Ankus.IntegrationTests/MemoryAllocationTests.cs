using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies actual PostgreSQL allocation policies, native byte ownership, and transient context boundaries.
/// </summary>
/// <param name="context">The per-test cancellation context.</param>
[TestClass]
public sealed class MemoryAllocationTests(TestContext context)
{
    /// <summary>
    /// Typed counts preserve exact values and byte lengths while copied buffers are independent of their sources.
    /// </summary>
    [TestMethod]
    public Task TypedAllocationsAndCopiesPreserveExactValuesAndCheckedCounts()
        => CheckAsync(nameof(TypedAllocationsAndCopiesPreserveExactValuesAndCheckedCounts),
            "SELECT datatype.memory_typed_allocations()",
            "24|-9223372036854775808,731,9223372036854775807|00000000000000000000000000000000|000000000000|00017F80FF|24|-73,0,9123|0,0,0|True|True|XX000|True");

    /// <summary>
    /// Native aligned pointers retain their byte prefix, exact zero extension, and actual owner through resizing.
    /// </summary>
    /// <param name="alignment">The requested power-of-two alignment.</param>
    /// <param name="huge">Whether the bounded native chunks use the huge-allocation flag.</param>
    /// <param name="tryResize">Whether allocation and resizing use the no-OOM path.</param>
    [TestMethod]
    [DataRow(1, false, false)]
    [DataRow(8, false, true)]
    [DataRow(64, false, false)]
    [DataRow(4096, false, false)]
    [DataRow(4096, true, true)]
    public Task ThirtyTwoAlignedPgrxWitnessesPreserveBytesAndNativeOwnership(int alignment, bool huge, bool tryResize)
        => CheckAsync(nameof(ThirtyTwoAlignedPgrxWitnessesPreserveBytesAndNativeOwnership),
            $"SELECT datatype.memory_aligned_allocations({alignment}, {huge}, {tryResize})",
            "32|32|32|32|32|32|32|32|True");

    /// <summary>
    /// Invalid payload and aligned-padding limits raise native errors without consuming or changing existing storage.
    /// </summary>
    /// <param name="huge">Whether the request uses the huge size limit.</param>
    /// <param name="aligned">Whether the invalid bound includes native aligned-allocation overhead.</param>
    /// <param name="operation">Allocate, try-allocate, resize, or try-resize.</param>
    [TestMethod]
    [DataRow(false, false, 0)]
    [DataRow(false, false, 1)]
    [DataRow(false, false, 2)]
    [DataRow(false, false, 3)]
    [DataRow(false, true, 0)]
    [DataRow(false, true, 1)]
    [DataRow(false, true, 2)]
    [DataRow(false, true, 3)]
    [DataRow(true, false, 0)]
    [DataRow(true, false, 1)]
    [DataRow(true, false, 2)]
    [DataRow(true, false, 3)]
    [DataRow(true, true, 0)]
    [DataRow(true, true, 1)]
    [DataRow(true, true, 2)]
    [DataRow(true, true, 3)]
    public Task InvalidSizeAndAlignmentPaddingErrorsPreservePointerLengthBytesAndSession(bool huge, bool aligned, int operation)
        => CheckAsync(nameof(InvalidSizeAndAlignmentPaddingErrorsPreservePointerLengthBytesAndSession),
            $"SELECT datatype.memory_allocation_limits({huge}, {aligned}, {operation})",
            "XX000|True|64|True|True|True|True|True");

    /// <summary>
    /// PostgreSQL statistics independently show the requested retained block sizes, growth, reset, and deletion.
    /// </summary>
    /// <param name="preset">Default, small, start-small, or custom non-power-of-two sizing.</param>
    /// <param name="initialBytes">The expected first native block size.</param>
    [TestMethod]
    [DataRow(0, 8192)]
    [DataRow(1, 1024)]
    [DataRow(2, 1024)]
    [DataRow(3, 6144)]
    public Task NativeAllocSetSizingPreservesPresetsAndNonPowerOfTwoMinimumBlocks(int preset, int initialBytes)
        => CheckAsync(nameof(NativeAllocSetSizingPreservesPresetsAndNonPowerOfTwoMinimumBlocks),
            $"SELECT datatype.memory_context_sizing({preset})",
            $"{initialBytes}|{initialBytes}|True|731|{initialBytes}|{initialBytes}|0|True");

    /// <summary>
    /// Two hundred ordinary allocations prove custom initial growth, maximum-sized blocks, and preservation of all earlier bytes.
    /// </summary>
    [TestMethod]
    public Task NativeAllocSetGrowthUsesCustomInitialAndMaximumBlockSizes()
        => CheckAsync(nameof(NativeAllocSetGrowthUsesCustomInitialAndMaximumBlockSizes),
            "SELECT datatype.memory_context_block_growth()",
            "6144|3072,6144,12288|24576|True|True|True|200|6144|1|stale|0|True");

    /// <summary>
    /// Native alignment and chunk-offset checks reject invalid AllocSet options before allocator assertions.
    /// </summary>
    /// <param name="invalidPart">The malformed native size constraint.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public Task NativeAllocSetValidationRejectsMalformedSizesWithoutLeakingContexts(int invalidPart)
        => CheckAsync(nameof(NativeAllocSetValidationRejectsMalformedSizesWithoutLeakingContexts),
            $"SELECT datatype.memory_context_native_sizing_error({invalidPart})", "22023|0|True");

    /// <summary>
    /// Transient work restores its caller, releases escaped native handles, and preserves action and cleanup failures.
    /// </summary>
    /// <param name="mode">The success or failure path exercised by the transient callback.</param>
    /// <param name="outcome">The exact result or exception evidence.</param>
    /// <param name="partial">The cleanup state before an explicitly retryable deletion is resumed.</param>
    [TestMethod]
    [DataRow(0, "result:92", "False|stale|False,False|B91,A91")]
    [DataRow(1, "managed:True", "False|stale|False,False|B91,A91")]
    [DataRow(2, "native:XX000", "False|stale|False,False|B91,A91")]
    [DataRow(3, "cleanup:22023:transient cleanup café:transient detail:transient hint", "True|91|True,False|B91")]
    [DataRow(4, "combined:True:22023:transient cleanup café:transient detail:transient hint", "True|91|True,False|B91")]
    [DataRow(5, "result:92", "False|stale|False,False|B91,A91")]
    public Task TransientContextsRestoreAndDeleteAcrossActionNativeAndCleanupFailures(int mode, string outcome, string partial)
        => CheckAsync(nameof(TransientContextsRestoreAndDeleteAcrossActionNativeAndCleanupFailures),
            $"SELECT datatype.memory_transient_lifetime({mode})",
            $"{outcome}|True|True|{partial}|B91,A91|False|stale|False,False|0|True|True");

    /// <summary>
    /// Nested scopes with the same name still own distinct contexts and unwind in the expected native order.
    /// </summary>
    [TestMethod]
    public Task NestedTransientContextsHaveIndependentIdentitiesAndExactCurrentRestoration()
        => CheckAsync(nameof(NestedTransientContextsHaveIndependentIdentitiesAndExactCurrentRestoration),
            "SELECT datatype.memory_nested_transients()", "42|True|True|True|True|1,2,1,0|inner,outer|False,False|True");

    /// <summary>
    /// Raw transfer consumes the old handle, rejects duplicate owners, and preserves checked native ownership after adoption.
    /// </summary>
    /// <param name="aligned">Whether to transfer an over-aligned native chunk.</param>
    /// <param name="huge">Whether the chunk uses the huge-allocation policy.</param>
    /// <param name="cleanup">Individual free, owner reset, or parent deletion.</param>
    /// <param name="alive">Whether the native owner and its parent survive final cleanup.</param>
    [TestMethod]
    [DataRow(false, false, 0, true)]
    [DataRow(false, true, 1, true)]
    [DataRow(false, false, 2, false)]
    [DataRow(true, false, 0, true)]
    [DataRow(true, false, 1, true)]
    [DataRow(true, true, 0, true)]
    [DataRow(true, true, 1, true)]
    [DataRow(true, true, 2, false)]
    public Task TransferredPointersAcquireFreshExclusiveOwnershipAndExpireWithNativeCleanup(bool aligned, bool huge, int cleanup, bool alive)
        => CheckAsync(nameof(TransferredPointersAcquireFreshExclusiveOwnershipAndExpireWithNativeCleanup),
            $"SELECT datatype.memory_allocation_transfer({aligned}, {huge}, {cleanup})",
            $"22023|True|stale|0|22023|True|True|stale|True|True|stale|{alive}|{alive}");

    /// <summary>
    /// Detached native bytes remain allocated after managed disposal and are reclaimed by their context's reset.
    /// </summary>
    [TestMethod]
    public Task DetachedStorageRemainsContextOwnedUntilNativeReset()
        => CheckAsync(nameof(DetachedStorageRemainsContextOwnedUntilNativeReset),
            "SELECT datatype.memory_detached_context_lifetime()", "17,231|True|True|True|stale|True");

    /// <summary>
    /// Adopted aligned huge-policy allocations survive separate callbacks and expire after commit or rollback.
    /// </summary>
    /// <param name="commit">Whether the transaction ends by committing rather than rolling back.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AdoptedAlignedAllocationsExpireAfterNativeTransactionCleanup(bool commit)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await using var command = new NpgsqlCommand("SELECT datatype.memory_transferred_save(false)", connection, transaction);
            Assert.AreEqual("731|True|64|4|4096", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT datatype.memory_transferred_state()";
            Assert.AreEqual("True|731|64|4|4096", await command.ExecuteScalarAsync(token));
            if (commit)
            {
                await transaction.CommitAsync(token);
            }
            else
            {
                await transaction.RollbackAsync(token);
            }
        }

        await using var check = new NpgsqlCommand("SELECT datatype.memory_transferred_state()", connection);
        Assert.AreEqual("False|stale|64|4|4096", await check.ExecuteScalarAsync(token));
        check.CommandText = "SELECT count(*) FROM pg_backend_memory_contexts WHERE ident = 'saved transferred allocation'";
        Assert.AreEqual(0L, await check.ExecuteScalarAsync(token));
        await AssertRecoveredAsync(check, backend, token);
    }

    /// <summary>
    /// Savepoint rollback invalidates adopted subtransaction storage while preserving a top-transaction allocation.
    /// </summary>
    /// <param name="subtransaction">Whether the adopted allocation belongs to the rolled-back subtransaction.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task AdoptedAlignedAllocationsRespectSubtransactionOwnership(bool subtransaction)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(AdoptedAlignedAllocationsRespectSubtransactionOwnership),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await transaction.SaveAsync("transferred_allocation_scope", token);
                await using var command = new NpgsqlCommand($"SELECT datatype.memory_transferred_save({subtransaction})", connection, transaction);
                Assert.AreEqual("731|True|64|4|4096", await command.ExecuteScalarAsync(token));
                await transaction.RollbackAsync("transferred_allocation_scope", token);
                command.CommandText = "SELECT datatype.memory_transferred_state()";
                Assert.AreEqual(subtransaction ? "False|stale|64|4|4096" : "True|731|64|4|4096", await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT count(*) FROM pg_backend_memory_contexts WHERE ident = 'saved transferred allocation'";
                Assert.AreEqual(subtransaction ? 0L : 1L, await command.ExecuteScalarAsync(token));
                await AssertRecoveredAsync(command, backend, token);
            }, context.CancellationToken);

    /// <summary>
    /// Raw C-string copies retain UTF-8 bytes even when the database cannot represent their Unicode characters.
    /// </summary>
    /// <param name="encoding">The server database encoding.</param>
    [TestMethod]
    [DataRow("UTF8")]
    [DataRow("LATIN1")]
    public async Task NativeUtf8StringsKeepExactTerminatedBytesAcrossDatabaseEncodings(string encoding)
    {
        CancellationToken token = context.CancellationToken;
        string database = "allocation_encoding_" + Guid.NewGuid().ToString("N");
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
            command.CommandText = "SELECT memory_utf8_allocations()";
            Assert.AreEqual("00|1|636166C3A920F09F909865CC8100|14|2|True", await command.ExecuteScalarAsync(token));
            command.CommandText = "SHOW server_encoding";
            Assert.AreEqual(encoding, await command.ExecuteScalarAsync(token));
            await AssertRecoveredAsync(command, backend, token);
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
            await AssertRecoveredAsync(command, backend, token);
        }, context.CancellationToken);

    private static async Task AssertRecoveredAsync(NpgsqlCommand command, int backend, CancellationToken token)
    {
        command.CommandText = "SELECT 42";
        Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, command.Connection?.ProcessID);
    }
}
