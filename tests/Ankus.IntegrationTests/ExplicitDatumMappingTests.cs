using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Proves independently declared closed SQL identities through real Native AOT callbacks and raw reads.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class ExplicitDatumMappingTests(TestContext context)
{
    /// <summary>
    /// Exact closed converters preserve all int4/int8 bits and install independent SQL overloads.
    /// </summary>
    [TestMethod]
    public async Task ExplicitMappingsPreserveIndependentScalarStorage()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreEqual("0|0|0|0", await Scalar<string>(connection, "SELECT explicit_mappings.counts()"));
        Assert.AreEqual(1073741866, await Scalar<int>(connection, "SELECT explicit_mappings.read_int(42)"));
        Assert.AreEqual("1|1|0|0", await Scalar<string>(connection, "SELECT explicit_mappings.counts()"));
        Assert.AreEqual(-4294967314L, await Scalar<long>(connection, "SELECT explicit_mappings.read_long(4294967313)"));
        Assert.AreEqual("2|1|1|0", await Scalar<string>(connection, "SELECT explicit_mappings.counts()"));
        Assert.AreEqual(-1073741824, await Scalar<int>(connection, "SELECT explicit_mappings.read_int('-2147483648'::integer)"));
        Assert.AreEqual(1073741823, await Scalar<int>(connection, "SELECT explicit_mappings.read_int(2147483647)"));
        Assert.AreEqual(long.MaxValue, await Scalar<long>(connection, "SELECT explicit_mappings.read_long('-9223372036854775808'::bigint)"));
        Assert.AreEqual(long.MinValue, await Scalar<long>(connection, "SELECT explicit_mappings.read_long('9223372036854775807'::bigint)"));
        Assert.AreEqual(1073741866, await Scalar<int>(connection, "SELECT explicit_mappings.make_int(42)::integer"));
        Assert.AreEqual(-4294967314L, await Scalar<long>(connection, "SELECT explicit_mappings.make_long(4294967313)::bigint"));
        Assert.AreEqual("integer|bigint", await Scalar<string>(connection, """
            SELECT pg_typeof(explicit_mappings.echo(42::integer))::text||'|'||
                pg_typeof(explicit_mappings.echo(4294967313::bigint))::text
            """));
        Assert.AreEqual(4294967313L, await Scalar<long>(connection, "SELECT explicit_mappings.echo(4294967313::bigint)::bigint"));
    }

    /// <summary>
    /// NULL bypasses factories while int8 arrays preserve rank, bounds, NULL cells and extrema.
    /// </summary>
    [TestMethod]
    public async Task ExplicitMappingsPreserveNullAndArrayContracts()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT explicit_mappings.echo(NULL::integer) IS NULL"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT explicit_mappings.echo(NULL::bigint) IS NULL"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT explicit_mappings.echo_array(NULL) IS NULL"));
        Assert.AreEqual("0|0|0|0", await Scalar<string>(connection, "SELECT explicit_mappings.counts()"));
        Assert.AreEqual("[0:1][-2:0]={{-9223372036854775808,NULL,4294967313},{9223372036854775807,-17,0}}",
            await Scalar<string>(connection, """
                SELECT explicit_mappings.echo_array(
                    '[0:1][-2:0]={{-9223372036854775808,NULL,4294967313},{9223372036854775807,-17,0}}'::bigint[])::text
                """));
        Assert.AreEqual("[0:1][-2:0]", await Scalar<string>(connection, """
            SELECT array_dims(explicit_mappings.echo_array('[0:1][-2:0]={{1,NULL,3},{4,5,6}}'::bigint[]))
            """));
        Assert.AreEqual("{}", await Scalar<string>(connection, "SELECT explicit_mappings.echo_array(ARRAY[]::bigint[])::text"));
        Assert.AreEqual("{NULL,NULL}", await Scalar<string>(connection, "SELECT explicit_mappings.echo_array(ARRAY[NULL,NULL]::bigint[])::text"));
        Assert.AreEqual("bigint[]", await Scalar<string>(connection, "SELECT pg_typeof(explicit_mappings.echo_array(ARRAY[17]::bigint[]))::text"));
    }

    /// <summary>
    /// A metadata-only root executes without a callback slot and all detached reads survive owner disposal.
    /// </summary>
    [TestMethod]
    public async Task ExplicitMappingsRootRawOnlyConstructedTypes()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreEqual("0|0|0|0", await Scalar<string>(connection, "SELECT explicit_mappings.counts()"));
        Assert.AreEqual("1017|1073741866|-4294967314", await Scalar<string>(connection, "SELECT explicit_mappings.raw_and_spi()"));
        Assert.AreEqual("3|1|1|1", await Scalar<string>(connection, "SELECT explicit_mappings.counts()"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT explicit_mappings.expired()"));
    }

    /// <summary>
    /// A present or NULL datum cannot select a sibling construction by shared generic definition.
    /// </summary>
    /// <param name="wide">Whether the requested root maps int8 instead of the actual int4.</param>
    /// <param name="isNull">Whether the actual datum carries SQL NULL.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task ExplicitMappingsRejectSiblingIdentityBeforeConversion(bool wide, bool isNull)
    {
        await using NpgsqlConnection connection = await Open();
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            $"SELECT explicit_mappings.wrong_identity({wide},{isNull})"));
        Assert.AreEqual("38000", error.SqlState);
        Assert.AreEqual(wide ? "PostgreSQL datum type OID 23 does not match mapped type OID 20." :
            "PostgreSQL datum type OID 20 does not match mapped type OID 23.", error.MessageText);
        Assert.AreEqual("0|0|0|0", await Scalar<string>(connection, "SELECT explicit_mappings.counts()"));
        Assert.AreEqual(-18L, await Scalar<long>(connection, "SELECT explicit_mappings.read_long(17::bigint)"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// The selected converter's owned error leaves its sibling usable in the same backend.
    /// </summary>
    [TestMethod]
    public async Task ExplicitMappingsPreserveSelectedConverterErrorsAndRecovery()
    {
        await using NpgsqlConnection connection = await Open();
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            "SELECT explicit_mappings.read_long(9011::bigint)"));
        Assert.AreEqual("P8603", error.SqlState);
        Assert.AreEqual("explicit long reader failed", error.MessageText);
        Assert.AreEqual("int8 construction", error.Detail);
        Assert.AreEqual("use another value", error.Hint);
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT explicit_mappings.expired()"));
        Assert.AreEqual(1073750835, await Scalar<int>(connection, "SELECT explicit_mappings.read_int(9011)"));
        Assert.AreEqual(-18L, await Scalar<long>(connection, "SELECT explicit_mappings.read_long(17::bigint)"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Opens an independent backend for each converter-state observation.
    /// </summary>
    private Task<NpgsqlConnection> Open() => PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);

    /// <summary>
    /// Executes a query and returns its exact scalar value.
    /// </summary>
    private async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        object? value = await command.ExecuteScalarAsync(context.CancellationToken);
        return (T)value!;
    }

    /// <summary>
    /// Consumes statement completion before the next same-backend assertion.
    /// </summary>
    private async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(context.CancellationToken);
    }
}
