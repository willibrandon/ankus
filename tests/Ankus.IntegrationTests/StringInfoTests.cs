using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies exact StringInfo bytes and ownership through a published Native AOT extension.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class StringInfoTests(TestContext context)
{
    /// <summary>
    /// Native acquisition rollback frees each acquired chunk and unpublished record before a successful retry.
    /// </summary>
    /// <param name="mode">The failure or repeated release boundary.</param>
    /// <param name="expected">Exact native release accounting and surviving payload evidence.</param>
    [TestMethod]
    [DataRow(1, "53200|1|1|1|live|0")]
    [DataRow(2, "53200|1|1|2|live|0")]
    [DataRow(3, "53200|0|0|0|live|0")]
    [DataRow(4, "53200|1|1|0|live|0")]
    [DataRow(5, "128|live|256|1")]
    [DataRow(6, "128|live|0|1")]
    [DataRow(7, "128|live|128|1")]
    [DataRow(8, "XX000|0|0|0|live|0")]
    [DataRow(9, "XX000|1|1|0|live|0")]
    public Task NativeFaultsAndRepeatedReleaseDoNotRetainChunksOrRegistryRecords(int mode, string expected)
        => CheckAsync($"SELECT tests.stringinfo_fault({mode})", expected);

    /// <summary>
    /// Native binary, Unicode, replacement, and terminator behavior has independent exact expectations.
    /// </summary>
    [TestMethod]
    public Task ExactBytesPreserveNulMalformedUtf8AndUnicode()
        => CheckAsync("SELECT datatype.string_info_bytes()",
            "0080FF636166C3A9F09F909800|001122636166C3A9F09F909800|112263|13|0|strict|�|True");

    /// <summary>
    /// Normal .NET formatting keeps the stream open and malformed text leaves the existing bytes unchanged.
    /// </summary>
    [TestMethod]
    public Task StreamWriterFormatsUtf8AndMalformedTextCannotPartiallyAppend()
        => CheckAsync("SELECT datatype.string_info_writer()", "café 🐘:-42:1.25|café 🐘:-42:1.25|True|True");

    /// <summary>
    /// Actual C cursor reads and exact byte comparisons witness self-append, independent native ownership, and reset.
    /// </summary>
    [TestMethod]
    public Task GrowthPreservesAliasesCursorBytesAndOwnerAcrossContextSwitches()
        => CheckAsync("SELECT datatype.string_info_growth()", "True|True|True|23|True|True|True|True|0|again");

    /// <summary>
    /// Stack structs, unterminated read-only bytes, and NULL empty payloads retain the native caller's ownership.
    /// </summary>
    /// <param name="mode">The independent native input shape.</param>
    /// <param name="expected">The exact callback bytes and capability state.</param>
    [TestMethod]
    [DataRow(0, "616263|False|6111632A|False")]
    [DataRow(1, "4100FF|True|read-only:65,0,255|False")]
    [DataRow(2, "|True|read-only:True|False")]
    [DataRow(3, "416263|True|22023:416263|False")]
    [DataRow(4, "616263|False|22023:616263:0|False")]
    public Task BorrowingPreservesExternalStructAndPayloadLifetimes(int mode, string expected)
        => CheckAsync($"SELECT tests.stringinfo_borrow('datatype.string_info_borrow(internal,integer)'::regprocedure, {mode})", expected);

    /// <summary>
    /// Transfers consume wrappers while raw bytes remain owned by PostgreSQL and can be adopted independently.
    /// </summary>
    /// <param name="mode">Whole-struct, byte-data, or validated C-string transfer.</param>
    /// <param name="expected">The independently expected transferred bytes and ownership.</param>
    [TestMethod]
    [DataRow(0, "none|6100626300|True|False")]
    [DataRow(1, "none|61006200|True|False")]
    [DataRow(2, "22023|61786200|True|False")]
    public Task TransfersKeepPayloadAliveAndCStringRejectionPreservesTheWrapper(int mode, string expected)
        => CheckAsync($"SELECT datatype.string_info_transfer({mode})", expected);

    /// <summary>
    /// Native size and bounds errors unwind managed finally blocks and leave the original payload usable.
    /// </summary>
    [TestMethod]
    public Task NativeErrorsRetainPayloadContextAndSameSessionRecovery()
        => CheckAsync("SELECT datatype.string_info_errors()", "54000,22000,22000,22000,22023|5|ok!|True|42|Cannot enlarge string buffer containing 2 bytes by 2147483647 more bytes.");

    /// <summary>
    /// Context reset and deletion reject both owned and borrowed handles before even an empty copy.
    /// </summary>
    /// <param name="delete">Whether the owner is deleted.</param>
    /// <param name="expected">The exact invalidation and fresh-owner state.</param>
    [TestMethod]
    [DataRow(false, "2|fresh|42")]
    [DataRow(true, "2|deleted|42")]
    public Task ContextCleanupInvalidatesOwnedAndBorrowedHandles(bool delete, string expected)
        => CheckAsync($"SELECT datatype.string_info_lifetime({delete})", expected);

    /// <summary>
    /// Retained buffers survive separate callbacks and expire at commit or rollback without native use-after-free.
    /// </summary>
    /// <param name="commit">Whether to commit the native transaction.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TransactionEndInvalidatesRetainedBuffer(bool commit)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await using var command = new NpgsqlCommand("SELECT datatype.string_info_save(false)", connection, transaction);
            Assert.AreEqual("saved", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT datatype.string_info_saved(false)";
            Assert.AreEqual("saved!", await command.ExecuteScalarAsync(token));
            if (commit) { await transaction.CommitAsync(token); }
            else { await transaction.RollbackAsync(token); }
        }

        await using var check = new NpgsqlCommand("SELECT datatype.string_info_saved(true)", connection);
        Assert.AreEqual("stale", await check.ExecuteScalarAsync(token));
        check.CommandText = "SELECT 42";
        Assert.AreEqual(42, await check.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Savepoint rollback expires only buffers whose selected native owner was reclaimed.
    /// </summary>
    /// <param name="subtransaction">Whether to allocate in the savepoint's native context.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task SubtransactionRollbackRespectsSelectedOwner(bool subtransaction)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SubtransactionRollbackRespectsSelectedOwner), async (connection, transaction, token) =>
        {
            await transaction.SaveAsync("stringinfo_scope", token);
            await using var command = new NpgsqlCommand($"SELECT datatype.string_info_save({subtransaction})", connection, transaction);
            Assert.AreEqual("saved", await command.ExecuteScalarAsync(token));
            await transaction.RollbackAsync("stringinfo_scope", token);
            command.CommandText = "SELECT datatype.string_info_saved(true)";
            Assert.AreEqual(subtransaction ? "stale" : "saved!", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT 42";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Compares a published callback with its independent expected result and checks backend recovery.
    /// </summary>
    private Task CheckAsync(string sql, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(StringInfoTests), async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT 42";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }, context.CancellationToken);
}
