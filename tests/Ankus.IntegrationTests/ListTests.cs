using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies typed native List behavior through published Native AOT callbacks and independent C observations.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class ListTests(TestContext context)
{
    /// <summary>
    /// Controlled native acquisition/growth failures preserve values and release records, while repeated large buffers remain bounded.
    /// </summary>
    /// <param name="mode">The failure or release boundary.</param>
    /// <param name="expected">The independently expected error, cleanup and retained-value observations.</param>
    [TestMethod]
    [DataRow(1, "53200|1|1|0|retry|0")]
    [DataRow(2, "53200|1|1|1|retry|0")]
    [DataRow(3, "53200|0|0|0|retry|0")]
    [DataRow(4, "53200|1|1|0|retry|0")]
    [DataRow(5, "53200|1|preserved|2|0")]
    [DataRow(6, "53200|128|preserved|2|0")]
    [DataRow(7, "128|exact|256|empty")]
    [DataRow(8, "128|exact|0|empty")]
    [DataRow(9, "128|exact|0|empty")]
    [DataRow(10, "128|exact|256|empty")]
    [DataRow(11, "128|exact|256|empty")]
    [DataRow(12, "XX000|1|1|0|retry|0")]
    [DataRow(13, "XX000|1|1|0|retry|0")]
    public Task NativeFaultsAndRepeatedReleasePreserveOwnership(int mode, string expected)
        => CheckAsync($"SELECT tests.list_fault({mode})", expected);

    /// <summary>
    /// Each native tag selects the correct union member with exact boundary values, including zero transaction IDs.
    /// </summary>
    /// <param name="kind">The native list cell discriminator.</param>
    /// <param name="expected">Independent native interpretation of the exact cell bits.</param>
    [TestMethod]
    [DataRow(1, "True|ptr:0,18446744073709551615,9223372036854775808,9223372036854775807")]
    [DataRow(2, "True|int:-2147483648,-1,0,2147483647")]
    [DataRow(3, "True|oid:0,2147483648,4294967295")]
    [DataRow(4, "True|xid:0,2147483648,4294967295")]
    public Task NativeCellKindsPreserveBoundaryValues(int kind, string expected)
        => CheckAsync($"SELECT datatype.list_cells({kind})", expected);

    /// <summary>
    /// pgrx's small and large append cases verify every cell through independent C code after an ambient context is deleted.
    /// </summary>
    /// <param name="count">The number of sequential values.</param>
    [TestMethod]
    [DataRow(10)]
    [DataRow(1000)]
    public Task GrowthUsesOriginalOwner(int count)
        => CheckAsync($"SELECT datatype.list_growth({count})", $"True|{count}|True|True|True|int:{string.Join(',', Enumerable.Range(0, count))}");

    /// <summary>
    /// NIL and full-capacity TryAdd leave the list unchanged, reserve counts additional cells, and clear restores NIL.
    /// </summary>
    [TestMethod]
    public Task ReserveAndTryAddPreserveValues()
        => CheckAsync("SELECT datatype.list_reserve()", "True|True|True|True|True|int:42");

    /// <summary>
    /// Mutations preserve the whole ordered sequence and leave cloned/copied containers independent.
    /// </summary>
    [TestMethod]
    public Task MutationsPreserveCompleteSequences()
        => CheckAsync("SELECT datatype.list_mutations()", "int:0,10,30,40|int:0,10,20,30,40,50,60|True|True|True|True|2|False");

    /// <summary>
    /// Draining head, middle, tail, all or no cells eagerly repairs the entire retained sequence.
    /// </summary>
    /// <param name="index">The first removed cell.</param>
    /// <param name="count">The removed cell count.</param>
    /// <param name="expected">The exact removed and retained cells.</param>
    [TestMethod]
    [DataRow(0, 3, "0,1,2|int:3,4,5,6,7,8,9|True|True")]
    [DataRow(3, 4, "3,4,5,6|int:0,1,2,7,8,9|True|True")]
    [DataRow(7, 3, "7,8,9|int:0,1,2,3,4,5,6|True|True")]
    [DataRow(0, 10, "0,1,2,3,4,5,6,7,8,9|NIL|True|True")]
    [DataRow(5, 1, "5|int:0,1,2,3,4,6,7,8,9|True|True")]
    [DataRow(0, 0, "|int:0,1,2,3,4,5,6,7,8,9|True|True")]
    [DataRow(10, 0, "|int:0,1,2,3,4,5,6,7,8,9|True|True")]
    public Task RemovalAndDrainPreserveRemainingCells(int index, int count, string expected)
        => CheckAsync($"SELECT datatype.list_drain({index}, {count})", expected);

    /// <summary>
    /// Borrowed and consuming iteration return exact values and reject use of invalidated wrappers.
    /// </summary>
    [TestMethod]
    public Task IterationAndConsumption()
        => CheckAsync("SELECT datatype.list_iteration()", "2,3,5|True|True|True|11,13|False");

    /// <summary>
    /// Native context reset/deletion invalidates borrowed containers and even bound NIL lists.
    /// </summary>
    /// <param name="delete">Whether the context is deleted.</param>
    /// <param name="expected">The rejected lifetime count and fresh state.</param>
    [TestMethod]
    [DataRow(false, "3|int:42")]
    [DataRow(true, "3|deleted")]
    public Task LifetimesRejectStaleStorage(bool delete, string expected)
        => CheckAsync($"SELECT datatype.list_lifetime({delete})", expected);

    /// <summary>
    /// Native ERROR returns through owned diagnostics after managed finally and preserves both list and session.
    /// </summary>
    [TestMethod]
    public Task NativeErrorsPreserveValuesAndSameSessionRecovery()
        => CheckAsync("SELECT datatype.list_errors()", "54000|1|int:17,23,31|42");

    /// <summary>
    /// Clearing and disposing pointer containers cannot free the independently owned pointee.
    /// </summary>
    [TestMethod]
    public Task PointerContainersDoNotOwnElements()
        => CheckAsync("SELECT datatype.list_pointers()", "True|112200FF|True");

    /// <summary>
    /// Singleton pop, remove and drain always restore NIL and empty owning enumeration terminates safely.
    /// </summary>
    [TestMethod]
    public Task SingletonRemovalRestoresNil()
        => CheckAsync("SELECT datatype.list_empty_transitions()", "True|True|True|17|True|False|False");

    /// <summary>
    /// An incorrect lifetime context cannot acquire a mutable borrow or change the original cells.
    /// </summary>
    [TestMethod]
    public Task BorrowingRequiresTheActualNativeOwner()
        => CheckAsync("SELECT datatype.list_borrow_owner()", "22023|int:17,31");

    /// <summary>
    /// Native callers observe mutations after borrowed disposal, NIL growth/clear, and safe wrong-tag rejection.
    /// </summary>
    /// <param name="mode">The native fixture scenario.</param>
    /// <param name="expected">The exact native cells after managed disposal.</param>
    [TestMethod]
    [DataRow(0, "int:-7,11,2,42")]
    [DataRow(1, "int:-7,42")]
    [DataRow(2, "int:-7,1,2,42")]
    [DataRow(3, "NIL")]
    public Task NativeBorrowAndTransfer(int mode, string expected)
        => CheckAsync($"SELECT tests.list_borrow('datatype.list_borrow(internal,integer)'::regprocedure, {mode})", expected);

    /// <summary>
    /// Retained native lists survive callback boundaries but expire at either transaction commit or rollback.
    /// </summary>
    /// <param name="commit">Whether to commit instead of rolling back.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TransactionEndInvalidatesRetainedList(bool commit)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await using var command = new NpgsqlCommand("SELECT datatype.list_save(false)", connection, transaction);
            Assert.AreEqual("saved", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT datatype.list_saved()";
            Assert.AreEqual("17", await command.ExecuteScalarAsync(token));
            if (commit) { await transaction.CommitAsync(token); }
            else { await transaction.RollbackAsync(token); }
        }

        await using var check = new NpgsqlCommand("SELECT datatype.list_saved()", connection);
        Assert.AreEqual("stale", await check.ExecuteScalarAsync(token));
        check.CommandText = "SELECT 42";
        Assert.AreEqual(42, await check.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Savepoint rollback expires only the list whose actual selected owner was reclaimed.
    /// </summary>
    /// <param name="subtransaction">Whether to allocate in the current subtransaction.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task SubtransactionRollbackRespectsSelectedOwner(bool subtransaction)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SubtransactionRollbackRespectsSelectedOwner), async (connection, transaction, token) =>
        {
            await transaction.SaveAsync("list_scope", token);
            await using var command = new NpgsqlCommand($"SELECT datatype.list_save({subtransaction})", connection, transaction);
            Assert.AreEqual("saved", await command.ExecuteScalarAsync(token));
            await transaction.RollbackAsync("list_scope", token);
            command.CommandText = "SELECT datatype.list_saved()";
            Assert.AreEqual(subtransaction ? "stale" : "17", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT 42";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Compares exact callback output and then independently verifies same-session backend recovery.
    /// </summary>
    private Task CheckAsync(string sql, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ListTests), async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT 42";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }, context.CancellationToken);
}
