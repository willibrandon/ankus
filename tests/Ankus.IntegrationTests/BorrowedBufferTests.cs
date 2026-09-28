using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes concrete borrowed text and binary contracts in a real PostgreSQL backend.
/// </summary>
/// <param name="context">The cancellation context for the current case.</param>
[TestClass]
public sealed partial class BorrowedBufferTests(TestContext context)
{
    /// <summary>
    /// Generated scalar results preserve empty, embedded-zero, multibyte and large values exactly.
    /// </summary>
    /// <param name="expression">The literal text or binary value.</param>
    /// <param name="method">The generated concrete identity function.</param>
    [TestMethod]
    [DataRow("''::text", "text_identity")]
    [DataRow("'café 🐘'::text", "text_identity")]
    [DataRow("repeat('large 🐘',10000)", "text_identity")]
    [DataRow("'\\x'::bytea", "bytea_identity")]
    [DataRow("'\\x00007f80ff00'::bytea", "bytea_identity")]
    [DataRow("decode(repeat('00ff',10000),'hex')", "bytea_identity")]
    [DataRow("NULL::text", "text_identity")]
    [DataRow("NULL::bytea", "bytea_identity")]
    public async Task BorrowedBufferScalarReturnsPreserveExactValues(string expression, string method)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.IsTrue(await Scalar<bool>(connection,
            $"SELECT borrowed_buffers.{method}({expression}) IS NOT DISTINCT FROM ({expression})"));
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident = 'Ankus borrowed buffer'"));
    }

    /// <summary>
    /// UTF-8 length measures native bytes and exact conversion never trims or replaces Unicode text.
    /// </summary>
    [TestMethod]
    public async Task BorrowedTextPreservesUtf8AndCharacterBoundaries()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string>(["25", "10", "café 🐘", "636166C3A920F09F9098"],
            await Scalar<string[]>(connection, "SELECT borrowed_buffers.text_snapshot('café 🐘')"));
        Assert.AreSequenceEqual<string>(["25", "0", "", ""],
            await Scalar<string[]>(connection, "SELECT borrowed_buffers.text_snapshot('')"));
    }

    /// <summary>
    /// Lazy set callbacks retain independent native snapshots, including SQL NULL and empty values.
    /// </summary>
    /// <param name="sql">The set expression reduced to exact text observations.</param>
    /// <param name="expected">The two literal observations.</param>
    [TestMethod]
    [DataRow("SELECT ARRAY(SELECT borrowed_buffers.text_rows('café 🐘'))", new string?[] { "café 🐘", "café 🐘" })]
    [DataRow("SELECT ARRAY(SELECT borrowed_buffers.text_rows(''))", new string?[] { "", "" })]
    [DataRow("SELECT ARRAY(SELECT borrowed_buffers.text_rows(NULL))", new string?[] { null, null })]
    [DataRow("SELECT ARRAY(SELECT encode(borrowed_buffers.bytea_rows('\\x0000ff'::bytea),'hex'))", new string?[] { "0000ff", "0000ff" })]
    [DataRow("SELECT ARRAY(SELECT encode(borrowed_buffers.bytea_rows('\\x'::bytea),'hex'))", new string?[] { "", "" })]
    [DataRow("SELECT ARRAY(SELECT encode(borrowed_buffers.bytea_rows(NULL),'hex'))", new string?[] { null, null })]
    public async Task BorrowedBufferSetsRetainTheirInputs(string sql, string?[] expected)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual(expected, await Scalar<string?[]>(connection, sql));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// SPI and function result views survive their temporary result owners and preserve typed NULL parameters.
    /// </summary>
    [TestMethod]
    public async Task BorrowedBuffersUseRawSpiAndFunctionResults()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string>(["café!", "0000FF7F", "raw", "007F", "cafécafé!", "True", "True", "True", "True", "25", "17"],
            await Scalar<string[]>(connection, "SELECT borrowed_buffers.buffer_spi()"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Copies one PostgreSQL observation through the independent client connection.
    /// </summary>
    private async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(context.CancellationToken));
    }
}
