using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies borrowed array values, native identity, ownership and recovery in PostgreSQL.
/// </summary>
/// <param name="context">The cancellation context for this case.</param>
[TestClass]
public sealed partial class BorrowedArrayTests(TestContext context)
{
    /// <summary>
    /// Preserves empty, singleton, nullable and multidimensional arrays with exact native identities.
    /// </summary>
    /// <param name="expression">The native array input.</param>
    /// <param name="expected">Literal metadata and cell values in native order.</param>
    [TestMethod]
    [DataRow("ARRAY[]::integer[]", new string?[] { "1007", "23", "0", "0", "False", "", "" })]
    [DataRow("ARRAY[0]", new string?[] { "1007", "23", "1", "1", "False", "1", "1", "0" })]
    [DataRow("ARRAY[NULL,NULL]::integer[]", new string?[] { "1007", "23", "1", "2", "True", "2", "1", null, null })]
    [DataRow("'[-2:-1][4:6]={{0,NULL,7},{-9,11,15}}'::integer[]",
        new string?[] { "1007", "23", "2", "6", "True", "2,3", "-2,4", "0", null, "7", "-9", "11", "15" })]
    [DataRow("ARRAY['café 🐘','','last',NULL]::text[]",
        new string?[] { "1009", "25", "1", "4", "True", "4", "1", "café 🐘", "", "last", null })]
    public async Task BorrowedArraysPreserveExactShapeTypesAndNulls(string expression, string?[] expected)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual(expected, await Scalar<string?[]>(connection, $"SELECT borrowed_arrays.array_view_snapshot({expression})"));
    }

    /// <summary>
    /// Native deconstruction witnesses original array and element addresses, NULL flags and zero-valued cells.
    /// </summary>
    /// <param name="expression">The native representation partition.</param>
    [TestMethod]
    [DataRow("ARRAY[]::integer[]")]
    [DataRow("ARRAY[false,true,false,true,false]::boolean[]")]
    [DataRow("ARRAY[-32768,0,32767]::smallint[]")]
    [DataRow("ARRAY[-2147483648,0,2147483647]::integer[]")]
    [DataRow("ARRAY[-9223372036854775808,0,9223372036854775807]::bigint[]")]
    [DataRow("ARRAY['-Infinity','-0','NaN','Infinity']::real[]")]
    [DataRow("ARRAY['-Infinity','-0','NaN','Infinity']::double precision[]")]
    [DataRow("ARRAY['00000000-0000-0000-0000-000000000000','12345678-90ab-cdef-0123-456789abcdef','ffffffff-ffff-ffff-ffff-ffffffffffff']::uuid[]")]
    [DataRow("'[-1:0][4:5][-2:-1]={{{1,2},{3,4}},{{5,6},{7,8}}}'::integer[]")]
    [DataRow("'[2:2][3:3][4:4][5:5][6:6][-2:0]={{{{{{1,NULL,3}}}}}}'::integer[]")]
    [DataRow("'[-2147483648:-2147483646]={0,1,2}'::integer[]")]
    [DataRow("'[2147483644:2147483646]={-1,0,1}'::integer[]")]
    [DataRow("ARRAY[0,NULL,2,3,4,5,6,NULL,8,9,10,11,12,13,14,NULL,16]::integer[]")]
    [DataRow("ARRAY['a',NULL,'bbb','','e','ff','ggg',NULL,'i','jj','kkk','l','mm','nnn','o',NULL,'tail']::text[]")]
    [DataRow("ARRAY[-2147483648,0,NULL,2147483647]")]
    [DataRow("ARRAY[NULL,NULL]::text[]")]
    [DataRow("ARRAY['short',repeat('long 🐘',10000),NULL,'']")]
    [DataRow("ARRAY['\\x0000ff'::bytea,NULL,'\\x'::bytea]")]
    [DataRow("ARRAY['00000000-0000-0000-0000-000000000000'::uuid,NULL,'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid]")]
    [DataRow("ARRAY[ROW(7,'first')::raw_values.pair,NULL,ROW(-1,'last')::raw_values.pair]")]
    public async Task BorrowedArrayCellsShareNativeStorage(string expression)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.IsTrue(await Scalar<bool>(connection, $"SELECT borrowed_arrays.array_view_storage({expression})"));
    }

    /// <summary>
    /// Cursor disposal does not invalidate a cell, and simultaneous cursors retain independent positions.
    /// </summary>
    [TestMethod]
    public async Task BorrowedArrayIteratorsAdvanceIndependently()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string?>(["InvalidOperationException", "True", "first", "True", "first", "True", "True",
            "ObjectDisposedException", "first", null, "third", "fourth", "False", "InvalidOperationException",
            "NotSupportedException", "first,NULL,third,fourth"],
            await Scalar<string?[]>(connection, "SELECT borrowed_arrays.array_view_iterators()"));
    }

    /// <summary>
    /// View disposal, source reset and source deletion expire all aliases while independent copies survive.
    /// </summary>
    /// <param name="mode">The owner-ending operation.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public async Task BorrowedArrayOwnersInvalidateEscapedViews(int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string?>(["ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException",
            "ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException",
            "owned", "2", "4", "True"], await Scalar<string?[]>(connection, $"SELECT borrowed_arrays.array_view_owners({mode})"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Source-only resets expire nested aliases while live child contexts are explicitly cleaned up and copies survive.
    /// </summary>
    [TestMethod]
    public async Task BorrowedArrayNestedSourcesExpireWithoutDeletingChildren()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        for (int index = 0; index < 3; index++)
        {
            Assert.AreSequenceEqual<string?>(["True", "True", "3", "3", "ObjectDisposedException",
                "ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException",
                "ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException",
                "ObjectDisposedException", "owned", "[4:5]={owned,NULL}", "2", "4", "42"],
                await Scalar<string?[]>(connection, "SELECT borrowed_arrays.array_view_source_reset()"));
            Assert.AreEqual(0L, await Scalar<long>(connection,
                "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident LIKE 'Ankus borrowed array%'"));
        }

        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Adjacent flat and subscript boundaries reject exactly and preserve valid NULL, zero and interior reads.
    /// </summary>
    [TestMethod]
    public async Task BorrowedArrayBoundsRejectAndRecover()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string?>(["ArgumentOutOfRangeException:index", "ArgumentOutOfRangeException:index",
            "ArgumentException:subscripts", "ArgumentOutOfRangeException:subscripts", "ArgumentOutOfRangeException:subscripts",
            "ArgumentOutOfRangeException:subscripts", "ArgumentOutOfRangeException:subscripts", "0", null, "15", "-9"],
            await Scalar<string?[]>(connection, "SELECT borrowed_arrays.array_view_bounds()"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Null, SQL NULL, scalar and stale inputs reject without losing the original backend.
    /// </summary>
    [TestMethod]
    public async Task BorrowedArrayConstructionErrorsPreserveBackend()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string>(["ArgumentNullException:value", "ArgumentException:value",
            "42804:The borrowed value is not an array", "ObjectDisposedException", "42"],
            await Scalar<string[]>(connection, "SELECT borrowed_arrays.array_view_construction_errors()"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Historical array and element domains retain exact identity without rechecking new constraints during reads.
    /// </summary>
    [TestMethod]
    public async Task BorrowedArraysRetainDomainAndCompositeIdentity()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(context.CancellationToken);
        await using var command = new NpgsqlCommand("""
            CREATE DOMAIN pg_temp.borrowed_cell AS integer;
            CREATE DOMAIN pg_temp.borrowed_container AS pg_temp.borrowed_cell[];
            CREATE TEMP TABLE borrowed_array_source(value pg_temp.borrowed_container);
            INSERT INTO borrowed_array_source VALUES (ARRAY[-7,NULL,0]::pg_temp.borrowed_cell[]);
            ALTER DOMAIN pg_temp.borrowed_cell ADD CHECK (VALUE > 0) NOT VALID;
            ALTER DOMAIN pg_temp.borrowed_container ADD CHECK (array_length(VALUE,1) < 2) NOT VALID;
            """, connection, transaction);
        await command.ExecuteNonQueryAsync(context.CancellationToken);
        string arrayOid = await Scalar<string>(connection, "SELECT 'pg_temp.borrowed_container'::regtype::oid::text");
        string elementOid = await Scalar<string>(connection, "SELECT 'pg_temp.borrowed_cell'::regtype::oid::text");
        Assert.AreSequenceEqual<string?>([arrayOid, elementOid, "1", "3", "True", "3", "1", "-7", null, "0"],
            await Scalar<string?[]>(connection, "SELECT borrowed_arrays.array_view_snapshot(value) FROM borrowed_array_source"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT borrowed_arrays.array_view_storage(value) FROM borrowed_array_source"));
        arrayOid = await Scalar<string>(connection, "SELECT 'raw_values.pair[]'::regtype::oid::text");
        elementOid = await Scalar<string>(connection, "SELECT 'raw_values.pair'::regtype::oid::text");
        Assert.AreSequenceEqual<string?>([arrayOid, elementOid, "1", "3", "True", "3", "1", "(7,first)", null, "(-1,last)"],
            await Scalar<string?[]>(connection, """
                SELECT borrowed_arrays.array_view_snapshot(
                    ARRAY[ROW(7,'first')::raw_values.pair,NULL,ROW(-1,'last')::raw_values.pair])
                """));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Executes one typed SQL observation on the original connection.
    /// </summary>
    private async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        object? result = await command.ExecuteScalarAsync(context.CancellationToken);
        return Assert.IsInstanceOfType<T>(result);
    }
}
