using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies statically inferred converter templates in the published Native AOT extension.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class DatumConverterTemplateTests(TestContext context)
{
    /// <summary>
    /// Distinct closed generic math implementations preserve all int4 and int8 bits and share each direction's factory.
    /// </summary>
    [TestMethod]
    public async Task InferredConvertersPreserveExactNumericValuesAndLazyIdentity()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreEqual("0|0|0|0|0", await Scalar<string>(connection, "SELECT template_mappings.counts()"));
        Assert.AreEqual(-18, await Scalar<int>(connection, "SELECT template_mappings.read_int(17)"));
        Assert.AreEqual("1|0|0|1|0", await Scalar<string>(connection, "SELECT template_mappings.counts()"));
        Assert.AreEqual(-4294967298L, await Scalar<long>(connection, "SELECT template_mappings.read_long(4294967297)"));
        Assert.AreEqual("1|1|0|2|0", await Scalar<string>(connection, "SELECT template_mappings.counts()"));
        Assert.AreEqual(17, await Scalar<int>(connection, "SELECT template_mappings.make_int(-18)"));
        Assert.AreEqual(4294967297L, await Scalar<long>(connection, "SELECT template_mappings.make_long(-4294967298)"));
        Assert.AreEqual("1|1|0|2|2", await Scalar<string>(connection, "SELECT template_mappings.counts()"));
        Assert.AreEqual(int.MaxValue, await Scalar<int>(connection, "SELECT template_mappings.read_int('-2147483648'::integer)"));
        Assert.AreEqual(int.MinValue, await Scalar<int>(connection, "SELECT template_mappings.make_int(2147483647)"));
        Assert.AreEqual(long.MaxValue, await Scalar<long>(connection, "SELECT template_mappings.read_long('-9223372036854775808'::bigint)"));
        Assert.AreEqual(long.MinValue, await Scalar<long>(connection, "SELECT template_mappings.read_long(9223372036854775807)"));
        Assert.AreEqual(long.MinValue, await Scalar<long>(connection, "SELECT template_mappings.make_long(9223372036854775807)"));
        Assert.AreEqual(long.MaxValue, await Scalar<long>(connection, "SELECT template_mappings.make_long('-9223372036854775808'::bigint)"));
        Assert.AreEqual("integer|bigint", await Scalar<string>(connection,
            "SELECT pg_typeof(template_mappings.make_int(0))::text || '|' || pg_typeof(template_mappings.make_long(0))::text"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// NULL and empty arrays avoid construction while rank, bounds, cells and wide integer bits survive both directions.
    /// </summary>
    [TestMethod]
    public async Task InferredConvertersPreserveNullAndArrayShapes()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreEqual("absent", await Scalar<string>(connection, "SELECT template_mappings.optional(NULL)"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT template_mappings.read_int(NULL) IS NULL"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT template_mappings.echo_array(NULL) IS NULL"));
        Assert.AreEqual("{}", await Scalar<string>(connection, "SELECT template_mappings.echo_array(ARRAY[]::bigint[])::text"));
        Assert.AreEqual("{NULL,NULL}", await Scalar<string>(connection, "SELECT template_mappings.echo_array(ARRAY[NULL,NULL]::bigint[])::text"));
        Assert.AreEqual("0|0|0|0|0", await Scalar<string>(connection, "SELECT template_mappings.counts()"));
        Assert.AreEqual("-1", await Scalar<string>(connection, "SELECT template_mappings.optional(0)"));
        Assert.AreEqual("1|0|0|1|0", await Scalar<string>(connection, "SELECT template_mappings.counts()"));
        Assert.AreEqual("[-2:-1][4:5]={{-9223372036854775808,NULL},{4294967297,9223372036854775807}}", await Scalar<string>(connection,
            "SELECT template_mappings.echo_array('[-2:-1][4:5]={{-9223372036854775808,NULL},{4294967297,9223372036854775807}}'::bigint[])::text"));
        Assert.AreEqual("1|1|0|4|3", await Scalar<string>(connection, "SELECT template_mappings.counts()"));
        Assert.AreEqual("[-2:-1][4:5]", await Scalar<string>(connection,
            "SELECT array_dims(template_mappings.echo_array('[-2:-1][4:5]={{17,NULL},{23,0}}'::bigint[]))"));
        Assert.AreEqual("bigint[]", await Scalar<string>(connection,
            "SELECT pg_typeof(template_mappings.echo_array(ARRAY[17,NULL,23]::bigint[]))::text"));
    }

    /// <summary>
    /// Metadata-only roots and reversed nested generic arguments produce independent detached native values.
    /// </summary>
    [TestMethod]
    public async Task InferredConvertersSupportRawOnlyAndNestedRoots()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreEqual("-18|3023|-4294967298", await Scalar<string>(connection, "SELECT template_mappings.raw_and_spi()"));
        Assert.AreEqual("0|1|1|2|0", await Scalar<string>(connection, "SELECT template_mappings.counts()"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT template_mappings.expired()"));
        Assert.AreEqual(-43L, await Scalar<long>(connection, "SELECT template_mappings.read_long(42)"));
        Assert.AreEqual("0|1|1|3|0", await Scalar<string>(connection, "SELECT template_mappings.counts()"));
    }

    /// <summary>
    /// Wrong SQL identity is rejected before generic construction for both present and absent operands.
    /// </summary>
    /// <param name="absent">Whether the incorrectly typed datum is NULL.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InferredConvertersRejectWrongOidBeforeConstruction(bool absent)
    {
        await using NpgsqlConnection connection = await Open();
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            "SELECT template_mappings.wrong_oid(" + (absent ? "true" : "false") + ")"));
        Assert.AreEqual("38000", error.SqlState);
        Assert.AreEqual("PostgreSQL datum type OID 20 does not match mapped type OID 21.", error.MessageText);
        Assert.AreEqual("0|0|0|0|0", await Scalar<string>(connection, "SELECT template_mappings.counts()"));
        Assert.AreEqual("-18|3023|-4294967298", await Scalar<string>(connection, "SELECT template_mappings.raw_and_spi()"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Inferred generic reader and writer failures carry owned diagnostics and preserve the backend session.
    /// </summary>
    [TestMethod]
    public async Task InferredConverterErrorsPreserveDiagnosticsAndRecover()
    {
        await using NpgsqlConnection connection = await Open();
        PostgresException read = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            "SELECT template_mappings.read_int(9011)"));
        Assert.AreEqual("P8604", read.SqlState);
        Assert.AreEqual("inferred converter reader failed", read.MessageText);
        Assert.AreEqual("word 9011", read.Detail);
        Assert.AreEqual("use another word", read.Hint);
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT template_mappings.expired()"));
        PostgresException write = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            "SELECT template_mappings.make_long(9012)"));
        Assert.AreEqual("P8605", write.SqlState);
        Assert.AreEqual("inferred converter writer failed", write.MessageText);
        Assert.AreEqual("number 9012", write.Detail);
        Assert.AreEqual("use another number", write.Hint);
        Assert.AreEqual(42L, await Scalar<long>(connection, "SELECT template_mappings.make_long(-43)"));
        Assert.AreEqual(-43, await Scalar<int>(connection, "SELECT template_mappings.read_int(42)"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Opens a fresh backend for independent lazy-construction observations.
    /// </summary>
    private Task<NpgsqlConnection> Open() => PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);

    /// <summary>
    /// Obtains one exact independently expected SQL value.
    /// </summary>
    private async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        object? value = await command.ExecuteScalarAsync(context.CancellationToken);
        return (T)value!;
    }

    /// <summary>
    /// Consumes statement completion before same-session recovery assertions.
    /// </summary>
    private async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(context.CancellationToken);
    }
}
