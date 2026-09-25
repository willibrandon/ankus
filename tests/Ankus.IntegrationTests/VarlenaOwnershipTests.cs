using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies borrowing, copy-on-write and native owner lifetimes through real PostgreSQL callbacks.
/// </summary>
/// <param name="context">The per-test cancellation context.</param>
[TestClass]
public sealed class VarlenaOwnershipTests(TestContext context)
{
    /// <summary>
    /// Reads ordinary input without copying and copies only its first mutation, leaving the stored row intact.
    /// </summary>
    [TestMethod]
    public async Task VarlenaBorrowingAndMutationPreserveOriginalStorage()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await Scalar<object>(connection, """
            CREATE TEMP TABLE varlena_full(value varlena_ownership.bytes127);
            INSERT INTO varlena_full VALUES (varlena_ownership.varlena_full_make());
            CREATE TEMP TABLE varlena_short(value native_layout.packet);
            INSERT INTO varlena_short VALUES (native_layout.native_make(42)); SELECT 1
            """);
        Assert.AreEqual(131, await Scalar<int>(connection, "SELECT pg_column_size(value) FROM varlena_full"));
        Assert.AreEqual(25, await Scalar<int>(connection, "SELECT pg_column_size(value) FROM varlena_short"));
        Assert.AreEqual("True|True|True|True|False|True|42|44|211|211|True", await Scalar<string>(connection,
            "SELECT varlena_ownership.varlena_full_probe(value,value,value) FROM varlena_full"));
        Assert.AreEqual("False|False|True|False|False|True|42|44", await Scalar<string>(connection,
            "SELECT varlena_ownership.varlena_probe(value,value) FROM varlena_short"));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT (SELECT encode(varlena_ownership.bytes127_send(value),'hex')='2a'||repeat('00',125)||'d3' FROM varlena_full)
                AND (SELECT value::text='42' FROM varlena_short)
            """));
    }

    /// <summary>
    /// Uses native short headers through126 bytes and a regular header at127 while zeroing every payload byte.
    /// </summary>
    [TestMethod]
    public async Task VarlenaShortHeaderBoundaryPreservesZeroPayload()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("True|True|False|True|False|False|False", await Scalar<string>(connection,
            "SELECT varlena_ownership.varlena_headers()"));
    }

    /// <summary>
    /// Mutates isolated detoast storage in place and preserves the external or compressed original.
    /// </summary>
    /// <param name="compressed">Whether the source is compressed or stored externally.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task VarlenaDetoastedValuesMutateWithoutReusingSource(bool compressed)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await Scalar<object>(connection, $"CREATE TEMP TABLE varlena_toast(value native_layout.block); ALTER TABLE varlena_toast ALTER COLUMN value SET STORAGE {(compressed ? "EXTENDED" : "EXTERNAL")}; INSERT INTO varlena_toast VALUES(native_layout.native_block({(compressed ? 0 : 1)})); SELECT 1");
        Assert.IsTrue(await Scalar<bool>(connection, compressed
            ? "SELECT pg_column_size(value)<1000 FROM varlena_toast" : "SELECT pg_column_size(value)=8192 FROM varlena_toast"));
        Assert.AreEqual(compressed ? "False|False|7|7|99|7" : "False|False|0|238|99|0", await Scalar<string>(connection,
            "SELECT varlena_ownership.varlena_block_probe(value,value) FROM varlena_toast"));
        Assert.IsTrue(await Scalar<bool>(connection, $"SELECT native_layout.block_send(value)=native_layout.block_send(native_layout.native_block({(compressed ? 0 : 1)})) FROM varlena_toast"));
    }

    /// <summary>
    /// Preserves exact fields, underlying SQL identity and whole-value NULL across each ownership path.
    /// </summary>
    /// <param name="mode">The direct or SPI path.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    public async Task VarlenaValuesCrossOwnershipPaths(int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("AB:-2147483648:255:-32768:4660:32767:80000000:7FF8000000000042", await Scalar<string>(connection,
            $"SELECT native_layout.native_describe(varlena_ownership.varlena_echo(native_layout.native_make(-2147483648),{mode}))"));
        Assert.IsTrue(await Scalar<bool>(connection, $$"""
            SELECT varlena_ownership.varlena_echo(NULL,{{mode}}) IS NULL
                AND pg_typeof(varlena_ownership.varlena_echo(native_layout.native_make(42),{{mode}}))='native_layout.packet'::regtype
                AND encode(native_layout.packet_send(varlena_ownership.varlena_echo(native_layout.native_zero(),{{mode}})),'hex')=repeat('00',24)
            """));
    }

    /// <summary>
    /// Preserves array bounds, nullable cells, vectors and tuple identities across wrapper conversion.
    /// </summary>
    /// <param name="mode">The direct or SPI path.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(4)]
    [DataRow(7)]
    public async Task VarlenaArraysAndTuplesRetainTypeShapeAndNull(int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.IsTrue(await Scalar<bool>(connection, $$$"""
            SELECT varlena_ownership.varlena_array('[2:3][-1:0]={{1,NULL},{0,9}}',{{{mode}}})::text='[2:3][-1:0]={{1,NULL},{0,9}}'
                AND varlena_ownership.varlena_array('{}',{{{mode}}})::text='{}'
                AND varlena_ownership.varlena_array(NULL,{{{mode}}}) IS NULL
                AND varlena_ownership.varlena_vector('{7,NULL,9}',{{{mode}}})::text='{7,NULL,9}'
                AND varlena_ownership.varlena_vector('{}',{{{mode}}})::text='{}'
                AND varlena_ownership.varlena_vector(NULL,{{{mode}}}) IS NULL
                AND pg_typeof(varlena_ownership.varlena_array('{7}',{{{mode}}}))='native_layout.packet[]'::regtype
            """));
        foreach (string members in new[]
        {
            "'7'::native_layout.packet,'[3:4]={9,NULL}'::native_layout.packet[]",
            "NULL::native_layout.packet,'{}'::native_layout.packet[]",
            "NULL::native_layout.packet,NULL::native_layout.packet[]",
        })
        {
            Assert.IsTrue(await Scalar<bool>(connection, $"WITH input AS(SELECT ROW({members}) value) SELECT record_send(value)=record_send(varlena_ownership.varlena_tuple(value,{mode})) FROM input"));
        }
    }

    /// <summary>
    /// Retains canonical bare SPI results, independent typed copies and live source aliases after binding.
    /// </summary>
    [TestMethod]
    public async Task VarlenaBindingCopiesWithoutConsumingAliases()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("True|True|False|42|42|91|42|42|True|True", await Scalar<string>(connection,
            "SELECT varlena_ownership.varlena_conversions(native_layout.native_make(42))"));
        Assert.AreEqual("True|True|2,2|2,-1|1,NULL,0,9", await Scalar<string>(connection,
            "SELECT varlena_ownership.varlena_array_conversions('[2:3][-1:0]={{1,NULL},{0,9}}')"));
        Assert.AreEqual("True|True|||", await Scalar<string>(connection,
            "SELECT varlena_ownership.varlena_array_conversions('{}')"));
    }

    /// <summary>
    /// Explicit ownership transfer invalidates aliases without freeing the datum and preserves exact SQL identity.
    /// </summary>
    [TestMethod]
    public async Task VarlenaTransferConsumesOnlyExplicitOwners()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("True|5|42|42|True", await Scalar<string>(connection, "SELECT varlena_ownership.varlena_transfer(42)"));
        await Scalar<object>(connection, """
            CREATE TEMP TABLE transfer_full(value varlena_ownership.bytes127);
            INSERT INTO transfer_full VALUES(varlena_ownership.varlena_full_make());
            CREATE TEMP TABLE transfer_short(value native_layout.packet);
            INSERT INTO transfer_short VALUES(native_layout.native_make(9)); SELECT 1
            """);
        Assert.AreEqual("True|True|5|42|42|211|211", await Scalar<string>(connection,
            "SELECT varlena_ownership.varlena_full_input_transfer(value,value) FROM transfer_full"));
        Assert.AreEqual("True|5|9|9", await Scalar<string>(connection,
            "SELECT varlena_ownership.varlena_input_transfer(value,value) FROM transfer_short"));
        Assert.IsTrue(await Scalar<bool>(connection,
            "SELECT (SELECT encode(varlena_ownership.bytes127_send(value),'hex')='2a'||repeat('00',125)||'d3' FROM transfer_full) AND (SELECT value::text='9' FROM transfer_short)"));
    }

    /// <summary>
    /// Callback leases expire immediately, while COW promotion and clones follow their live source context.
    /// </summary>
    /// <param name="mode">Borrow, COW, default clone or explicit clone.</param>
    /// <param name="expected">The observable value after the producer callback exits.</param>
    [TestMethod]
    [DataRow(0, "expired")]
    [DataRow(1, "43")]
    [DataRow(2, "42")]
    [DataRow(3, "42")]
    public async Task VarlenaCallbackLeasesExpireWithoutRevival(int mode, string expected)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual(expected, await Scalar<string>(connection,
            $"SELECT varlena_ownership.varlena_saved_after(varlena_ownership.varlena_capture(native_layout.native_make(42),{mode}))"));
        Assert.AreEqual("expired", await Scalar<string>(connection, "SELECT varlena_ownership.varlena_saved_after(0)"));
        await Clear(connection);
    }

    /// <summary>
    /// An explicit destination survives the source query and expires only when its own context resets.
    /// </summary>
    [TestMethod]
    public async Task VarlenaClonesSurviveSourceAndExpireWithDestination()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(context.CancellationToken);
        Assert.AreEqual(3, await Scalar<int>(connection,
            "SELECT varlena_ownership.varlena_capture(native_layout.native_make(42),3)"));
        Assert.AreEqual("42", await Scalar<string>(connection, "SELECT varlena_ownership.varlena_saved_after(0)"));
        Assert.AreEqual("expired", await Scalar<string>(connection, "SELECT varlena_ownership.varlena_reset_owner()"));
        await Clear(connection);
    }

    /// <summary>
    /// A writable detoast temporary remains leased to its callback even after an in-place mutation.
    /// </summary>
    [TestMethod]
    public async Task VarlenaWritableInputsRemainCallbackScoped()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await Scalar<object>(connection, "CREATE TEMP TABLE writable_input(value native_layout.packet); INSERT INTO writable_input VALUES(native_layout.native_make(42)); SELECT 1");
        Assert.AreEqual(25, await Scalar<int>(connection, "SELECT pg_column_size(value) FROM writable_input"));
        Assert.AreEqual("expired", await Scalar<string>(connection,
            "SELECT varlena_ownership.varlena_saved_after(varlena_ownership.varlena_capture(value,1)) FROM writable_input"));
        Assert.AreEqual("42", await Scalar<string>(connection, "SELECT value::text FROM writable_input"));
        await Clear(connection);
    }

    /// <summary>
    /// Ordinary return conversion leaves a retained alias usable and stores an independent SQL value.
    /// </summary>
    [TestMethod]
    public async Task VarlenaOrdinaryReturnsPreserveOwnedAliases()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(context.CancellationToken);
        await Scalar<object>(connection, "CREATE TEMP TABLE returned_value(value native_layout.packet); INSERT INTO returned_value VALUES(varlena_ownership.varlena_retained_return(42)); SELECT 1");
        Assert.AreEqual("42", await Scalar<string>(connection, "SELECT varlena_ownership.varlena_saved_after(0)"));
        await Clear(connection);
        Assert.AreEqual("42", await Scalar<string>(connection, "SELECT value::text FROM returned_value"));
    }

    /// <summary>
    /// A nested callback can read its ancestor lease but cannot leave its own input alive.
    /// </summary>
    [TestMethod]
    public async Task VarlenaNestedCallbacksPreserveOuterInput()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("42:7|42|expired", await Scalar<string>(connection,
            "SELECT varlena_ownership.varlena_nested(native_layout.native_make(42))"));
    }

    /// <summary>
    /// Deferred set bodies access promoted input on first and later advances, then release it on completion or close.
    /// </summary>
    /// <param name="function">The set execution mode.</param>
    /// <param name="early">Whether the executor stops after the first value.</param>
    [TestMethod]
    [DataRow("varlena_rows", false)]
    [DataRow("varlena_rows", true)]
    [DataRow("varlena_materialized", false)]
    public async Task VarlenaDeferredSetsRetainInputsAndReleaseOwners(string function, bool early)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual(early ? "42" : "42,NULL,43", await Scalar<string>(connection, early
            ? $"SELECT value::text FROM varlena_ownership.{function}(native_layout.native_make(42)) value LIMIT 1"
            : $"SELECT string_agg(coalesce(value::text,'NULL'),',' ORDER BY ordinal) FROM varlena_ownership.{function}(native_layout.native_make(42)) WITH ORDINALITY row(value,ordinal)"));
        Assert.AreEqual("1|expired", await Scalar<string>(connection, "SELECT varlena_ownership.varlena_set_state()"));
        Assert.AreEqual("NULL,NULL", await Scalar<string>(connection,
            $"SELECT string_agg(coalesce(value::text,'NULL'),',' ORDER BY ordinal) FROM varlena_ownership.{function}(NULL) WITH ORDINALITY row(value,ordinal)"));
        Assert.AreEqual("2|NULL", await Scalar<string>(connection, "SELECT varlena_ownership.varlena_set_state()"));
    }

    /// <summary>
    /// Aggregate finalization reads wrapper values retained across earlier transition callbacks.
    /// </summary>
    [TestMethod]
    public async Task VarlenaAggregatesRetainWrapperInputs()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("7,NULL,9", await Scalar<string>(connection, """
            SELECT varlena_ownership.varlena_collect(value ORDER BY ordinal)
            FROM (VALUES(1,native_layout.native_make(7)),(2,NULL),(3,native_layout.native_make(9))) row(ordinal,value)
            """));
        Assert.AreEqual("expired", await Scalar<string>(connection, "SELECT varlena_ownership.varlena_aggregate_state()"));
        Assert.AreEqual("empty", await Scalar<string>(connection,
            "SELECT varlena_ownership.varlena_collect(value) FROM (SELECT NULL::native_layout.packet value WHERE false) row"));
    }

    /// <summary>
    /// Deferred array iterators retain every element across advances and correctly dispose an empty iterator.
    /// </summary>
    [TestMethod]
    public async Task VarlenaDeferredArraySetsRetainEveryElement()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("7,NULL,9", await Scalar<string>(connection, """
            SELECT string_agg(coalesce(value::text,'NULL'),',' ORDER BY ordinal)
            FROM varlena_ownership.varlena_array_rows('[3:5]={7,NULL,9}') WITH ORDINALITY row(value,ordinal)
            """));
        Assert.AreEqual("1|expired", await Scalar<string>(connection, "SELECT varlena_ownership.varlena_set_state()"));
        Assert.AreEqual(0L, await Scalar<long>(connection, "SELECT count(*) FROM varlena_ownership.varlena_array_rows('{}')"));
        Assert.AreEqual("2|NULL", await Scalar<string>(connection, "SELECT varlena_ownership.varlena_set_state()"));
    }

    /// <summary>
    /// Retained shaped-array and vector elements survive temporary aggregate contexts and preserve NULLs and bounds.
    /// </summary>
    [TestMethod]
    public async Task VarlenaAggregateArraysRetainEveryElement()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("3:7,NULL;NULL;-1:9,11", await Scalar<string>(connection, """
            SELECT varlena_ownership.varlena_collect_arrays(value ORDER BY ordinal)
            FROM (VALUES(1,'[3:4]={7,NULL}'::native_layout.packet[]),(2,NULL),(3,'[-1:0]={9,11}'::native_layout.packet[])) row(ordinal,value)
            """));
        Assert.AreEqual("7,NULL;NULL;9,11", await Scalar<string>(connection, """
            SELECT varlena_ownership.varlena_collect_vectors(value ORDER BY ordinal)
            FROM (VALUES(1,'{7,NULL}'::native_layout.packet[]),(2,NULL),(3,'{9,11}'::native_layout.packet[])) row(ordinal,value)
            """));
    }

    /// <summary>
    /// Reports expired wrappers and wrong SQL identities without replacing the backend or damaging later conversion.
    /// </summary>
    [TestMethod]
    public async Task VarlenaErrorsPreserveBackendAndIdentity()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int process = connection.ProcessID;
        Assert.AreEqual(0, await Scalar<int>(connection,
            "SELECT varlena_ownership.varlena_capture(native_layout.native_make(42),0)"));
        PostgresException expired = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<object>(connection,
            "SELECT varlena_ownership.varlena_expired_read()"));
        Assert.AreEqual("38000", expired.SqlState);
        PostgresException wrongType = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<object>(connection,
            "SELECT varlena_ownership.varlena_raw_read(native_layout.native_fault(42))"));
        Assert.AreEqual("38000", wrongType.SqlState);
        Assert.AreEqual(7, await Scalar<int>(connection,
            "SELECT varlena_ownership.varlena_raw_read(native_layout.native_make(7))"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
        await Clear(connection);
    }

    /// <summary>
    /// Executes one exact typed scalar with fixture cancellation.
    /// </summary>
    private async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        object? result = await command.ExecuteScalarAsync(context.CancellationToken);
        return Assert.IsInstanceOfType<T>(result);
    }

    /// <summary>
    /// Executes cleanup whose SQL function deliberately returns void.
    /// </summary>
    private async Task Clear(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT varlena_ownership.varlena_clear()", connection);
        await command.ExecuteNonQueryAsync(context.CancellationToken);
    }
}
