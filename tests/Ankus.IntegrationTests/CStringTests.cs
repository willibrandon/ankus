using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies C-string bytes and ownership in the published Native AOT extension.
/// </summary>
/// <param name="context">The cancellation context for the current test.</param>
[TestClass]
public sealed partial class CStringTests(TestContext context)
{
    /// <summary>
    /// Scalar callbacks preserve exact bytes, empty strings and SQL NULL through both representations.
    /// </summary>
    /// <param name="hex">The independent payload in hexadecimal, or null for SQL NULL.</param>
    /// <param name="borrowed">Whether native views or managed copies are used.</param>
    [TestMethod]
    [DataRow("", false)]
    [DataRow("", true)]
    [DataRow("0180ff", false)]
    [DataRow("0180ff", true)]
    [DataRow("636166c3a9f09f9098", false)]
    [DataRow("636166c3a9f09f9098", true)]
    [DataRow(null, false)]
    [DataRow(null, true)]
    public async Task CStringScalarsPreserveBytesAndNull(string? hex, bool borrowed)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        string input = hex is null ? "NULL::bytea" : $"decode('{hex}','hex')";
        string prefix = borrowed ? "borrowed" : "owned";
        Assert.IsTrue(await Scalar<bool>(connection,
            $"SELECT cstrings.{prefix}_bytes(cstrings.{prefix}_echo(cstrings.create_cstring({input}))) IS NOT DISTINCT FROM {input}"));
        if (hex is not null)
        {
            Assert.AreEqual(hex + "00", await Scalar<string>(connection,
                $"SELECT encode(cstrings.terminated_bytes(cstrings.create_cstring({input})),'hex')"));
        }

        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident = 'Ankus borrowed buffer'"));
    }

    /// <summary>
    /// Retained input snapshots survive the lazy iterator's successive native callbacks.
    /// </summary>
    /// <param name="borrowed">Whether input uses a retained native view.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CStringSetsRetainIndependentInputs(bool borrowed)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        string method = borrowed ? "borrowed_rows" : "owned_rows";
        Assert.AreSequenceEqual<string>(["ff80", "ff80"], await Scalar<string[]>(connection,
            $"SELECT ARRAY(SELECT encode(cstrings.{method}(cstrings.create_cstring('\\xff80'::bytea)),'hex'))"));
        Assert.AreSequenceEqual<string?>([null, null], await Scalar<string?[]>(connection,
            $"SELECT ARRAY(SELECT encode(cstrings.{method}(NULL::cstring),'hex'))"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Native arrays preserve shape, NULL cells and exact bytes in both owned and borrowed cells.
    /// </summary>
    [TestMethod]
    public async Task CStringArraysPreserveShapeAndCellBytes()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("[-3:-2][7:8]", await Scalar<string>(connection,
            "SELECT array_dims(cstrings.array_identity(cstrings.create_array()))"));
        Assert.AreSequenceEqual<string?>([null, "", "80FF", "41"], await Scalar<string?[]>(connection,
            "SELECT cstrings.array_snapshot(cstrings.array_identity(cstrings.create_array()))"));
        Assert.AreSequenceEqual<string?>([null, "", "80FF", "41"], await Scalar<string?[]>(connection,
            "SELECT cstrings.borrowed_array_snapshot(cstrings.create_array())"));
    }

    /// <summary>
    /// Typed and raw SPI and PostgreSQL function results retain bytes across temporary owner cleanup.
    /// </summary>
    [TestMethod]
    public async Task CStringsCrossTypedNativeBoundaries()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string>(["0180FF", "FF01", "80", "80", "42", "-7", "True", "True", "True", "True"],
            await Scalar<string[]>(connection, "SELECT cstrings.native_boundaries()"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Embedded zeros are rejected without truncation, and the same backend remains usable.
    /// </summary>
    [TestMethod]
    public async Task CStringInvalidPayloadRecoversOnSameBackend()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT cstrings.owned_bytes(cstrings.create_cstring('\\x010002'::bytea))", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(context.CancellationToken));
        Assert.AreEqual("38000", error.SqlState);
        Assert.Contains("embedded zero", error.MessageText);
        Assert.AreEqual("ff", await Scalar<string>(connection,
            "SELECT encode(cstrings.owned_bytes(cstrings.create_cstring('\\xff'::bytea)),'hex')"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Reads one independent client-side PostgreSQL observation.
    /// </summary>
    private async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(context.CancellationToken));
    }
}
