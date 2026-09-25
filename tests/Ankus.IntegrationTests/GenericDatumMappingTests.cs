using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Exercises finite closed generic mappings through the published Native AOT extension.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class GenericDatumMappingTests(TestContext context)
{
    /// <summary>
    /// The same native int4 words select two independent closed managed identities and writers.
    /// </summary>
    [TestMethod]
    public async Task GenericMappedScalarsSelectExactClosedReadersAndWriters()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreEqual("0|0|0", await Scalar<string>(connection, "SELECT generic_mappings.counts()"));
        Assert.AreEqual(1017, await Scalar<int>(connection, "SELECT generic_mappings.read_int(17)"));
        Assert.AreEqual("1|1|0", await Scalar<string>(connection, "SELECT generic_mappings.counts()"));
        Assert.AreEqual(2017, await Scalar<int>(connection, "SELECT generic_mappings.read_long(17)"));
        Assert.AreEqual("2|1|1", await Scalar<string>(connection, "SELECT generic_mappings.counts()"));
        Assert.AreEqual(1000, await Scalar<int>(connection, "SELECT generic_mappings.read_int(0)"));
        Assert.AreEqual(1983, await Scalar<int>(connection, "SELECT generic_mappings.read_long(-17)"));
        Assert.AreEqual(17, await Scalar<int>(connection, "SELECT generic_mappings.make_int(1017)::integer"));
        Assert.AreEqual(19, await Scalar<int>(connection, "SELECT generic_mappings.make_long(2019)::integer"));
        Assert.AreEqual("2|2|2", await Scalar<string>(connection, "SELECT generic_mappings.counts()"));
        Assert.AreEqual("integer", await Scalar<string>(connection, "SELECT pg_typeof(generic_mappings.make_int(1017))::text"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// SQL NULL bypasses factories while present zero and shaped arrays preserve exact storage and bounds.
    /// </summary>
    [TestMethod]
    public async Task GenericMappedNullAndArrayShapesRemainDistinct()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreEqual(-1, await Scalar<int>(connection, "SELECT generic_mappings.optional(NULL)"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT generic_mappings.read_int(NULL) IS NULL"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT generic_mappings.echo_array(NULL) IS NULL"));
        Assert.AreEqual("0|0|0", await Scalar<string>(connection, "SELECT generic_mappings.counts()"));
        Assert.AreEqual(1000, await Scalar<int>(connection, "SELECT generic_mappings.optional(0)"));
        Assert.AreEqual("1|1|0", await Scalar<string>(connection, "SELECT generic_mappings.counts()"));
        Assert.AreEqual("[2:4]={17,NULL,19}", await Scalar<string>(connection,
            "SELECT generic_mappings.echo_array('[2:4]={17,NULL,19}'::integer[])::text"));
        Assert.AreEqual("[2:4]", await Scalar<string>(connection,
            "SELECT array_dims(generic_mappings.echo_array('[2:4]={17,NULL,19}'::integer[]))"));
        Assert.AreEqual("{}", await Scalar<string>(connection,
            "SELECT generic_mappings.echo_array(ARRAY[]::integer[])::text"));
        Assert.AreEqual("{NULL,NULL}", await Scalar<string>(connection,
            "SELECT generic_mappings.echo_array(ARRAY[NULL,NULL]::integer[])::text"));
        Assert.AreEqual("integer[]", await Scalar<string>(connection,
            "SELECT pg_typeof(generic_mappings.echo_array(ARRAY[17,NULL,19]::integer[]))::text"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Nested containing arguments, direct raw reads and typed SPI results select their exact closed readers.
    /// </summary>
    [TestMethod]
    public async Task GenericMappedNestedRawAndSpiResultsRemainDetached()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreEqual(3017, await Scalar<int>(connection, "SELECT generic_mappings.read_nested(17)"));
        Assert.AreEqual("1017|2017|3017|1019|2023", await Scalar<string>(connection,
            "SELECT generic_mappings.raw_and_spi()"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT generic_mappings.expired()"));
        Assert.AreEqual("2|2|2", await Scalar<string>(connection, "SELECT generic_mappings.counts()"));
        Assert.AreEqual(1042, await Scalar<int>(connection, "SELECT generic_mappings.read_int(42)"));
    }

    /// <summary>
    /// Reader and writer exceptions carry owned diagnostics and preserve same-backend recovery.
    /// </summary>
    [TestMethod]
    public async Task GenericMappedErrorsPreserveDiagnosticsAndRecover()
    {
        await using NpgsqlConnection connection = await Open();
        PostgresException read = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            "SELECT generic_mappings.read_int(9011)"));
        Assert.AreEqual("P8601", read.SqlState);
        Assert.AreEqual("generic mapped reader failed", read.MessageText);
        Assert.AreEqual("int-tagged word", read.Detail);
        Assert.AreEqual("use another word", read.Hint);
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT generic_mappings.expired()"));
        PostgresException write = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            "SELECT generic_mappings.make_int(9002)"));
        Assert.AreEqual("P8602", write.SqlState);
        Assert.AreEqual("generic mapped writer failed", write.MessageText);
        Assert.AreEqual("int-tagged result", write.Detail);
        Assert.AreEqual("use another result", write.Hint);
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT generic_mappings.make_int(1042)::integer"));
        Assert.AreEqual(1042, await Scalar<int>(connection, "SELECT generic_mappings.read_int(42)"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Opens an independent PostgreSQL backend for each stateful converter observation.
    /// </summary>
    private Task<NpgsqlConnection> Open() => PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);

    /// <summary>
    /// Executes an exact PostgreSQL scalar query.
    /// </summary>
    private async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        object? value = await command.ExecuteScalarAsync(context.CancellationToken);
        return (T)value!;
    }

    /// <summary>
    /// Executes a statement and consumes its completion before the next same-session assertion.
    /// </summary>
    private async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(context.CancellationToken);
    }
}
