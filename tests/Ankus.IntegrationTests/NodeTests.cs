using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes generated PostgreSQL node casts against their real native allocation and lifetime contracts.
/// </summary>
/// <param name="context">The test's cancellation context.</param>
[TestClass]
public sealed partial class NodeTests(TestContext context)
{
    /// <summary>
    /// pgrx's RangeTblRef roundtrip preserves the original address, tag, index and shared mutation.
    /// </summary>
    /// <param name="raw">Whether the root view starts with an explicit raw extent.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task RangeTableNodeRoundtripPreservesNativeValueAndAddress(bool raw)
        => CheckAsync($"SELECT datatype.node_range_table_roundtrip({raw})", "9|73|True|True|True");

    /// <summary>
    /// Native AlternativeSubPlan casts through Expr and Node retain identity while rejecting an unrelated Var.
    /// </summary>
    [TestMethod]
    public Task NativeInheritanceRetainsTagAndRejectsUnrelatedNodes()
        => CheckAsync("SELECT datatype.node_inheritance()", "True|True|True|True");

    /// <summary>
    /// Both native OpExpr aliases retain their distinct tags, shared address and OID payload.
    /// </summary>
    /// <param name="nullIf">Whether to use NullIfExpr rather than DistinctExpr.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task NativeAliasesRetainExactTagsAndSharedPayload(bool nullIf)
        => CheckAsync($"SELECT datatype.node_alias({nullIf})", "True|711|True|True");

    /// <summary>
    /// A measured ABI mismatch crosses the guarded native diagnostic boundary and leaves the same backend usable.
    /// </summary>
    [TestMethod]
    public async Task IncompatibleBindingRaisesOwnedNativeErrorAndRecovers()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand("SELECT datatype.node_incompatible_binding()", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual("0A000", error.SqlState);
        Assert.AreEqual("the node representation does not match the active extension's PostgreSQL binding ABI", error.MessageText);
        command.CommandText = "SELECT datatype.node_range_table_roundtrip(false)";
        Assert.AreEqual("9|73|True|True|True", await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT pg_backend_pid()";
        Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Root views retain full allocation bounds through growth and shrink without reviving incomplete source views.
    /// </summary>
    [TestMethod]
    public Task NodeViewsRevalidateAllocationBoundsAndRelease()
        => CheckAsync("SELECT datatype.node_allocation_bounds()", "9|True|True|True|0|True|True");

    /// <summary>
    /// An expired raw cast cannot capture a new anchor generation even while its external bytes remain valid.
    /// </summary>
    /// <param name="delete">Whether anchor cleanup deletes or resets the context.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task RawNodeCastsRetainTheirOriginalAnchorGeneration(bool delete)
        => CheckAsync($"SELECT datatype.node_raw_anchor({delete})", $"True|True|True|9|{(delete ? -1 : 9)}|True");

    private async Task CheckAsync(string sql, string expected)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand(sql, connection);
        Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT 42, pg_backend_pid()";
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        Assert.IsTrue(await reader.ReadAsync(token));
        Assert.AreEqual(42, reader.GetInt32(0));
        Assert.AreEqual(backend, reader.GetInt32(1));
        Assert.IsFalse(await reader.ReadAsync(token));
    }
}
