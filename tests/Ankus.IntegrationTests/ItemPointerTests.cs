using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies tid values and native item-pointer ownership inside the published PostgreSQL extension.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class ItemPointerTests(TestContext context)
{
    /// <summary>
    /// Exact emitted native code releases failed acquisitions and thousands of individually freed, reset or transferred values.
    /// </summary>
    /// <param name="mode">The native failure or release path.</param>
    /// <param name="expected">The independent allocation, release and byte-accounting observations.</param>
    [TestMethod]
    [DataRow(1, "53200|0|0|0|retry|0")]
    [DataRow(2, "53200|1|1|0|retry|0")]
    [DataRow(3, "53200|1|1|1|retry|0")]
    [DataRow(4, "4096|exact|0|bounded")]
    [DataRow(5, "4096|exact|0|bounded")]
    [DataRow(6, "4096|exact|0|bounded")]
    public Task NativeFailuresRecoverWithoutLeaking(int mode, string expected)
        => CheckAsync($"SELECT tests.item_pointer_fault({mode})", expected);

    /// <summary>
    /// Native tidsend supplies independent big-endian bytes for every field boundary and special value.
    /// </summary>
    /// <param name="value">The native text input.</param>
    /// <param name="hex">The independent six-byte representation.</param>
    [TestMethod]
    [DataRow("(0,0)", "000000000000")]
    [DataRow("(0,1)", "000000000001")]
    [DataRow("(65535,65535)", "0000ffffffff")]
    [DataRow("(65536,0)", "000100000000")]
    [DataRow("(305419896,43981)", "12345678abcd")]
    [DataRow("(2147483648,1)", "800000000001")]
    [DataRow("(4294967295,0)", "ffffffff0000")]
    [DataRow("(4294967295,65533)", "fffffffffffd")]
    [DataRow("(4294967295,65534)", "fffffffffffe")]
    [DataRow("(4294967295,65535)", "ffffffffffff")]
    public Task ScalarValuesPreserveNativeRepresentation(string value, string hex)
        => CheckAsync($"SELECT datatype.item_pointer_echo('{value}'::tid)::text || '|' || " +
            $"encode(pg_catalog.tidsend(datatype.item_pointer_required('{value}'::tid)), 'hex') || '|' || " +
            $"encode(pg_catalog.tidsend(datatype.item_pointer_native_datum('{value}'::tid)), 'hex')", value + "|" + hex + "|" + hex);

    /// <summary>
    /// Native-address calls copy both typed and raw results before their temporary storage is reclaimed.
    /// </summary>
    /// <param name="function">The native tid comparison function.</param>
    /// <param name="raw">Whether to request a raw result owner.</param>
    /// <param name="expected">The independently expected location.</param>
    [TestMethod]
    [DataRow("tidlarger", false, "(4294967295,0)")]
    [DataRow("tidlarger", true, "(4294967295,0)")]
    [DataRow("tidsmaller", false, "(0,65535)")]
    [DataRow("tidsmaller", true, "(0,65535)")]
    public Task NativeAddressCallsPreserveCopiedResults(string function, bool raw, string expected)
        => CheckAsync($"SELECT datatype.item_pointer_direct(tests.function_address('pg_catalog.{function}(tid,tid)'::regprocedure)," +
            $"'(4294967295,0)'::tid,'(0,65535)'::tid,{raw})::text", expected);

    /// <summary>
    /// Nullable and strict functions keep SQL NULL distinct from a present invalid location.
    /// </summary>
    [TestMethod]
    public Task NullAndInvalidRemainDistinct()
        => CheckAsync("""
            SELECT concat_ws('|', datatype.item_pointer_echo(NULL::tid) IS NULL,
              datatype.item_pointer_required(NULL::tid) IS NULL,
              datatype.item_pointer_echo('(0,0)'::tid) IS NOT NULL,
              datatype.item_pointer_vector(NULL::tid[]) IS NULL,
              datatype.item_pointer_array(NULL::tid[]) IS NULL,
              (SELECT proisstrict FROM pg_proc WHERE oid='datatype.item_pointer_required(tid)'::regprocedure),
              (SELECT proisstrict FROM pg_proc WHERE oid='datatype.item_pointer_echo(tid)'::regprocedure))
            """, "t|t|t|t|t|t|f");

    /// <summary>
    /// Actual heap ctid values travel through scalar, SPI, function-call and composite result paths.
    /// </summary>
    [TestMethod]
    public Task TypedRoutesPreserveValues()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(TypedRoutesPreserveValues), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                CREATE TEMP TABLE item_pointer_rows(value integer);
                INSERT INTO item_pointer_rows SELECT generate_series(1,10);
                SELECT count(*) FROM item_pointer_rows source
                CROSS JOIN LATERAL datatype.item_pointer_routes(source.ctid) AS result(location tid, missing tid)
                WHERE result.location=source.ctid AND result.missing IS NULL
                  AND datatype.item_pointer_echo(source.ctid)=source.ctid
                """, connection, transaction);
            Assert.AreEqual(10L, await command.ExecuteScalarAsync(token));
            command.CommandText = """
                SELECT location::text || '|' || (missing IS NULL)::text
                FROM datatype.item_pointer_routes('(4294967295,0)'::tid) AS result(location tid, missing tid)
                """;
            Assert.AreEqual("(4294967295,0)|true", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT array_agg(value)::text FROM datatype.item_pointer_set('(0,0)'::tid) value";
            Assert.AreEqual("{\"(0,0)\",NULL,\"(4294967295,0)\"}", await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Vector, shaped-array and SPI transport retain exact NULL cells, lower bounds and empty arrays.
    /// </summary>
    [TestMethod]
    public Task ArraysPreserveShapeAndNulls()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ArraysPreserveShapeAndNulls), async (connection, transaction, token) =>
        {
            const string shaped = "[-2:-1][4:5]={{\"(0,0)\",NULL},{\"(4294967295,0)\",\"(4294967295,65535)\"}}";
            await using var command = new NpgsqlCommand($"SELECT datatype.item_pointer_array('{shaped}'::tid[])::text", connection, transaction);
            Assert.AreEqual(shaped, await command.ExecuteScalarAsync(token));
            command.CommandText = $"SELECT datatype.item_pointer_spi_array('{shaped}'::tid[])::text";
            Assert.AreEqual(shaped, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT datatype.item_pointer_vector(ARRAY['(0,0)'::tid,NULL,'(4294967295,65535)'::tid])::text";
            Assert.AreEqual("{\"(0,0)\",NULL,\"(4294967295,65535)\"}", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT datatype.item_pointer_required_vector('{}'::tid[])::text";
            Assert.AreEqual("{}", await command.ExecuteScalarAsync(token));
            await transaction.SaveAsync("required_cells", token);
            command.CommandText = "SELECT datatype.item_pointer_required_vector(ARRAY[NULL::tid])";
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual("38000", error.SqlState);
            await transaction.RollbackAsync("required_cells", token);
            command.CommandText = "SELECT datatype.item_pointer_required_vector(ARRAY['(0,0)'::tid])::text";
            Assert.AreEqual("{\"(0,0)\"}", await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// SQL operators independently confirm unsigned field ordering, including non-NULL invalid locations.
    /// </summary>
    [TestMethod]
    public Task OrderingMatchesPostgreSql()
        => CheckAsync("""
            WITH locations(value) AS (VALUES ('(0,0)'::tid), ('(0,65535)'), ('(1,0)'),
                ('(65535,65535)'), ('(65536,0)'), ('(2147483647,65535)'), ('(2147483648,0)'),
                ('(4294967295,0)'), ('(4294967295,65535)'))
            SELECT count(*)::text FROM locations a CROSS JOIN locations b
            WHERE datatype.item_pointer_compare(a.value,b.value) =
                CASE WHEN a.value<b.value THEN -1 WHEN a.value>b.value THEN 1 ELSE 0 END
            """, "81");

    /// <summary>
    /// Raw tid and domain reads retain exact type identity and copied storage after native reset.
    /// </summary>
    [TestMethod]
    public Task RawValuesPreserveIdentityAndLifetime()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RawValuesPreserveIdentityAndLifetime), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT datatype.item_pointer_raw('(4294967295,0)'::tid)", connection, transaction);
            const string values = "|(4294967295,0)|(4294967295,0)|(4294967295,0)|True|True";
            Assert.AreEqual("27|27" + values, await command.ExecuteScalarAsync(token));
            command.CommandText = "CREATE DOMAIN pg_temp.item_pointer_domain AS tid; SELECT 'pg_temp.item_pointer_domain'::regtype::oid";
            uint oid = (uint)(await command.ExecuteScalarAsync(token))!;
            Assert.AreNotEqual(27U, oid);
            command.CommandText = "SELECT datatype.item_pointer_raw('(4294967295,0)'::pg_temp.item_pointer_domain)";
            Assert.AreEqual($"{oid}|{oid}" + values, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Independent C fields prove the native size, block halves, offset and selected allocation owner.
    /// </summary>
    [TestMethod]
    public Task NativeValuesUseSelectedHeaderLayout()
        => CheckAsync("SELECT datatype.item_pointer_native()",
            "6|4660|22136|43981|tid owner|(4294967295,0)|(4294967295,0)|True|True|6|65535|65535|65535|tid owner|True");

    /// <summary>
    /// Native stack storage retains its guards, supports borrowed mutation, and remains subject to anchor expiry.
    /// </summary>
    /// <param name="mode">The normal, reset or null-address scenario.</param>
    /// <param name="expected">The managed and independent native observations.</param>
    [TestMethod]
    [DataRow(0, "(305419896,43981)|(4294967295,0)|65535,65535,0|guards")]
    [DataRow(1, "(305419896,43981)|True|4660,22136,43981|guards")]
    [DataRow(2, "null|4660,22136,43981|guards")]
    public Task NativeStackBorrowPreservesFields(int mode, string expected)
        => CheckAsync($"SELECT tests.item_pointer_borrow('datatype.item_pointer_borrow(internal,integer)'::regprocedure,{mode})", expected);

    /// <summary>
    /// Both reset and deletion expire every live view while independent managed and native copies survive.
    /// </summary>
    /// <param name="delete">Whether to delete the owner.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task NativeLifetimesRejectStaleAccess(bool delete)
        => CheckAsync($"SELECT datatype.item_pointer_lifetime({delete})", "True|True|True|(17,31)|(17,31)");

    /// <summary>
    /// Native Slab and Bump allocation failures unwind to managed catch/finally and leave the backend usable.
    /// </summary>
    /// <param name="kind">The Slab or Bump allocator.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    public Task NativeAllocatorErrorsRecover(int kind)
        => CheckAsync($"SELECT tests.allocator_create({kind},'datatype.item_pointer_allocator()'::regprocedure,'tid rejection')::text", "11");

    /// <summary>
    /// Retained values survive callback boundaries but expire on either transaction completion path.
    /// </summary>
    /// <param name="commit">Whether to commit rather than roll back.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TransactionEndInvalidatesRetainedValue(bool commit)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await using var command = new NpgsqlCommand("SELECT datatype.item_pointer_save(false)", connection, transaction);
            Assert.AreEqual("saved", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT datatype.item_pointer_saved()";
            Assert.AreEqual("(17,31)", await command.ExecuteScalarAsync(token));
            if (commit) { await transaction.CommitAsync(token); }
            else { await transaction.RollbackAsync(token); }
        }

        await using var check = new NpgsqlCommand("SELECT datatype.item_pointer_saved()", connection);
        Assert.AreEqual("stale", await check.ExecuteScalarAsync(token));
        check.CommandText = "SELECT 42";
        Assert.AreEqual(42, await check.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Savepoint rollback reclaims only the location allocated in the current subtransaction.
    /// </summary>
    /// <param name="subtransaction">Whether to select the current subtransaction owner.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task SubtransactionRollbackRespectsSelectedOwner(bool subtransaction)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SubtransactionRollbackRespectsSelectedOwner), async (connection, transaction, token) =>
        {
            await transaction.SaveAsync("tid_scope", token);
            await using var command = new NpgsqlCommand($"SELECT datatype.item_pointer_save({subtransaction})", connection, transaction);
            Assert.AreEqual("saved", await command.ExecuteScalarAsync(token));
            await transaction.RollbackAsync("tid_scope", token);
            command.CommandText = "SELECT datatype.item_pointer_saved()";
            Assert.AreEqual(subtransaction ? "stale" : "(17,31)", await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Asserts complete results and same-session recovery after native operations.
    /// </summary>
    private Task CheckAsync(string sql, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ItemPointerTests), async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT 42";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }, context.CancellationToken);
}
