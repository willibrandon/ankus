using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies typed native ownership, shallow byte copies, and checked raw lifetimes in PostgreSQL.
/// </summary>
/// <param name="context">The per-test cancellation context.</param>
[TestClass]
public sealed class NativeBoxTests(TestContext context)
{
    /// <summary>
    /// Initialized, zeroed, adopted, and borrowed values preserve the pgrx five-value and null-pointer witnesses.
    /// </summary>
    [TestMethod]
    public Task NativeBoxConstructionPreservesFiveZeroAndNullPointerContracts()
        => CheckAsync(nameof(NativeBoxConstructionPreservesFiveZeroAndNullPointerContracts),
            "SELECT datatype.native_box_basics()", "5,0,5,5,5,5,5,5|True|True,True|stale");

    /// <summary>
    /// Relinquishing individual disposal retains all existing borrows until native owner cleanup.
    /// </summary>
    /// <param name="deleteParent">Whether final cleanup deletes the parent rather than resetting the owner.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task ReleaseToContextPreservesExistingBorrowsAndConsumesOnlyIndividualOwnership(bool deleteParent)
        => CheckAsync(nameof(ReleaseToContextPreservesExistingBorrowsAndConsumesOnlyIndividualOwnership),
            $"SELECT datatype.native_box_release_to_context({deleteParent})",
            $"73,73,73|True|5|True|stale,stale,stale|{!deleteParent}");

    /// <summary>
    /// Detaching from either ownership form expires every checked alias before a fresh owner adopts the same pointer.
    /// </summary>
    /// <param name="fromContext">Whether raw transfer starts from a context-owned value.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task RawDetachInvalidatesSharedViewsBeforeFreshAdoption(bool fromContext)
        => CheckAsync(nameof(RawDetachInvalidatesSharedViewsBeforeFreshAdoption),
            $"SELECT datatype.native_box_detach_and_adopt({fromContext})",
            "True|stale,stale|5|91|True|True|stale|stale|True");

    /// <summary>
    /// Every clone entry point preserves deliberately initialized padding and shallow pointers in independent native storage.
    /// </summary>
    /// <param name="sourceKind">Owned box, context value, checked borrow, or raw borrow.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public Task NativeClonesPreservePaddingShallowPointersAndTargetOwnership(int sourceKind)
        => CheckAsync(nameof(NativeClonesPreservePaddingShallowPointersAndTargetOwnership),
            $"SELECT datatype.native_box_clone({sourceKind})",
            "True|True|True|True|True|123|999|902,902,902,902|123,123,123");

    /// <summary>
    /// Individual disposal and native reset never call IDisposable on the unmanaged pointee.
    /// </summary>
    [TestMethod]
    public Task NativeCleanupNeverInvokesPointeeDispose()
        => CheckAsync(nameof(NativeCleanupNeverInvokesPointeeDispose),
            "SELECT datatype.native_box_does_not_dispose_pointee()", "19|0|1|-1");

    /// <summary>
    /// Individual disposal returns a large chunk to PostgreSQL immediately while preserving its context for further use.
    /// </summary>
    [TestMethod]
    public Task IndividualDisposalImmediatelyReclaimsNativeChunkWithoutResettingItsContext()
        => CheckAsync(nameof(IndividualDisposalImmediatelyReclaimsNativeChunkWithoutResettingItsContext),
            "SELECT datatype.native_box_individual_free()", "True|5,73|True|True|True|True|True");

    /// <summary>
    /// Collected wrappers leave observable first and last native bytes allocated until their context is reset.
    /// </summary>
    /// <param name="owned">Whether the collected wrapper has individual disposal rights.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task ManagedCollectionLeavesNativeTypedStorageOwnedByItsContext(bool owned)
        => CheckAsync(nameof(ManagedCollectionLeavesNativeTypedStorageOwnedByItsContext),
            $"SELECT datatype.native_box_collection({owned})", "True|True|True|5,73|True|True");

    /// <summary>
    /// Stack and interior aliases share mutations and expire with their declared anchor independently of the actual byte owner.
    /// </summary>
    /// <param name="cleanup">Reset-only, reset, reset-children, or parent deletion.</param>
    /// <param name="fresh">The freshly captured value or deleted-anchor marker.</param>
    [TestMethod]
    [DataRow(0, "73")]
    [DataRow(1, "73")]
    [DataRow(2, "73")]
    [DataRow(3, "deleted")]
    public Task RawStackAndInteriorAliasesExpireWithTheirAnchorGeneration(int cleanup, string fresh)
        => CheckAsync(nameof(RawStackAndInteriorAliasesExpireWithTheirAnchorGeneration),
            $"SELECT datatype.native_box_raw_aliases({cleanup})",
            $"7|9|73|True|500|True|True|stale,stale,stale|True|True|{fresh}|stale");

    /// <summary>
    /// Each native reset form preserves its exact tree boundary and invalidates captures from two successive generations.
    /// </summary>
    /// <param name="operation">Reset-only, reset-children, or reset of the root context.</param>
    /// <param name="first">The raw and tracked values, live contexts, and native inventory after the first reset.</param>
    /// <param name="middle">The freshly captured value between resets.</param>
    [TestMethod]
    [DataRow(0, "stale,22,33|stale,222,333|True,True,True|3", 11)]
    [DataRow(1, "11,stale,stale|111,stale,stale|True,True,True|3", 22)]
    [DataRow(2, "stale,stale,stale|stale,stale,stale|True,False,False|1", 11)]
    public Task SuccessiveRawGenerationsRespectParentAndDescendantResetBoundaries(int operation, string first, int middle)
        => CheckAsync(nameof(SuccessiveRawGenerationsRespectParentAndDescendantResetBoundaries),
            $"SELECT datatype.native_box_generation_tree({operation})", $"{first}|True|{middle}|stale|stale|44|44");

    /// <summary>
    /// Typed offset views follow a moved allocation, reject narrowed ranges, recover after growth, and expire after free.
    /// </summary>
    [TestMethod]
    public Task BorrowedOffsetViewsFollowResizeAndRevalidateBounds()
        => CheckAsync(nameof(BorrowedOffsetViewsFollowResizeAndRevalidateBounds),
            "SELECT datatype.native_box_offset_views()", "73|True|True|73|4|0|11|91|True|stale|True");

    /// <summary>
    /// A failed cleanup callback leaves old payload and generation readable until a successful retry reaches invalidation.
    /// </summary>
    [TestMethod]
    public Task FailedResetPreservesRawGenerationUntilSuccessfulRetry()
        => CheckAsync(nameof(FailedResetPreservesRawGenerationUntilSuccessfulRetry),
            "SELECT datatype.native_box_failed_reset_generation()",
            "22023|raw generation reset failure|raw detail|raw hint|True|5|9|True,False|B5|B5,A9|stale,stale|False,False|True");

    /// <summary>
    /// Owned, transferred, and re-adopted values survive separate SQL calls and expire after native transaction cleanup.
    /// </summary>
    /// <param name="ownership">Individual ownership, release to context, or raw adoption into context ownership.</param>
    /// <param name="commit">Whether the transaction commits rather than rolling back.</param>
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(0, true)]
    [DataRow(1, false)]
    [DataRow(1, true)]
    [DataRow(2, false)]
    [DataRow(2, true)]
    public async Task TransactionCleanupExpiresOwnedContextAndRawNativeViews(int ownership, bool commit)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await using var command = new NpgsqlCommand($"SELECT datatype.native_box_save({ownership}, false)", connection, transaction);
            Assert.AreEqual("73|73|73|True", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT datatype.native_box_saved_state()";
            Assert.AreEqual("73|73|73|True", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT count(*) FROM pg_backend_memory_contexts WHERE ident = 'saved native typed value'";
            Assert.AreEqual(1L, await command.ExecuteScalarAsync(token));
            if (commit)
            {
                await transaction.CommitAsync(token);
            }
            else
            {
                await transaction.RollbackAsync(token);
            }
        }

        await using var check = new NpgsqlCommand("SELECT datatype.native_box_saved_state()", connection);
        Assert.AreEqual("stale|stale|stale|False", await check.ExecuteScalarAsync(token));
        check.CommandText = "SELECT count(*) FROM pg_backend_memory_contexts WHERE ident = 'saved native typed value'";
        Assert.AreEqual(0L, await check.ExecuteScalarAsync(token));
        await AssertRecoveredAsync(check, backend, token);
    }

    /// <summary>
    /// Savepoint rollback expires every subtransaction view while preserving top-transaction ownership and its aliases.
    /// </summary>
    /// <param name="ownership">Individual ownership, release to context, or raw adoption into context ownership.</param>
    /// <param name="subtransaction">Whether the native owner belongs to the rolled-back subtransaction.</param>
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(0, true)]
    [DataRow(1, false)]
    [DataRow(1, true)]
    [DataRow(2, false)]
    [DataRow(2, true)]
    public Task SavepointCleanupRespectsTypedOwnershipAndRawAnchor(int ownership, bool subtransaction)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SavepointCleanupRespectsTypedOwnershipAndRawAnchor),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await transaction.SaveAsync("native_box_scope", token);
                await using var command = new NpgsqlCommand($"SELECT datatype.native_box_save({ownership}, {subtransaction})", connection, transaction);
                Assert.AreEqual("73|73|73|True", await command.ExecuteScalarAsync(token));
                await transaction.RollbackAsync("native_box_scope", token);
                command.CommandText = "SELECT datatype.native_box_saved_state()";
                Assert.AreEqual(subtransaction ? "stale|stale|stale|False" : "73|73|73|True", await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT count(*) FROM pg_backend_memory_contexts WHERE ident = 'saved native typed value'";
                Assert.AreEqual(subtransaction ? 0L : 1L, await command.ExecuteScalarAsync(token));
                await AssertRecoveredAsync(command, backend, token);
            }, context.CancellationToken);

    private Task CheckAsync(string name, string sql, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(name, async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            Assert.AreEqual(expected, Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token)));
            command.CommandText = """
                SELECT count(*) FROM pg_backend_memory_contexts
                WHERE ident LIKE 'native box %' OR ident LIKE 'native raw reference %'
                   OR ident LIKE 'native allocation view %' OR ident LIKE 'native generation %'
                   OR ident = 'raw generation failed reset'
                """;
            Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
            await AssertRecoveredAsync(command, backend, token);
        }, context.CancellationToken);

    private static async Task AssertRecoveredAsync(NpgsqlCommand command, int backend, CancellationToken token)
    {
        command.CommandText = "SELECT 42";
        Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        NpgsqlConnection? connection = command.Connection;
        Assert.IsNotNull(connection);
        Assert.AreEqual(backend, connection.ProcessID);
    }
}
