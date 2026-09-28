using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class BorrowedArrayTests
{
    /// <summary>
    /// Native scalar slices preserve boundaries, exact floating point bits, UUID byte order and row-major shape.
    /// </summary>
    /// <param name="expression">The independently constructed SQL array.</param>
    /// <param name="mode">The requested scalar layout.</param>
    /// <param name="expected">Literal SQL identities, shape and values.</param>
    [TestMethod]
    [DataRow("ARRAY[(-128)::\"char\",0::\"char\",127::\"char\"]", 0,
        new[] { "1002", "18", "3", "1", "-128", "0", "127" })]
    [DataRow("ARRAY[-32768,0,32767]::smallint[]", 1,
        new[] { "1005", "21", "3", "1", "-32768", "0", "32767" })]
    [DataRow("ARRAY[-2147483648,0,2147483647]::integer[]", 2,
        new[] { "1007", "23", "3", "1", "-2147483648", "0", "2147483647" })]
    [DataRow("ARRAY['-9223372036854775808','0','9223372036854775807']::bigint[]", 3,
        new[] { "1016", "20", "3", "1", "-9223372036854775808", "0", "9223372036854775807" })]
    [DataRow("ARRAY['-1.5','-0','Infinity']::real[]", 4,
        new[] { "1021", "700", "3", "1", "BFC00000", "80000000", "7F800000" })]
    [DataRow("ARRAY['-1.5','-0','-Infinity']::double precision[]", 5,
        new[] { "1022", "701", "3", "1", "BFF8000000000000", "8000000000000000", "FFF0000000000000" })]
    [DataRow("ARRAY['00112233-4455-6677-8899-aabbccddeeff','00000000-0000-0000-0000-000000000000','ffffffff-ffff-ffff-ffff-ffffffffffff']::uuid[]", 6,
        new[] { "2951", "2950", "3", "1", "00112233-4455-6677-8899-aabbccddeeff", "00000000-0000-0000-0000-000000000000", "ffffffff-ffff-ffff-ffff-ffffffffffff" })]
    [DataRow("'[-2:-1][4:6]={{0,-7,19},{2,5,-3}}'::integer[]", 2,
        new[] { "1007", "23", "2,3", "-2,4", "0", "-7", "19", "2", "5", "-3" })]
    [DataRow("ARRAY[7]", 2, new[] { "1007", "23", "1", "1", "7" })]
    [DataRow("ARRAY[]::\"char\"[]", 0, new[] { "1002", "18", "", "" })]
    [DataRow("ARRAY[]::smallint[]", 1, new[] { "1005", "21", "", "" })]
    [DataRow("ARRAY[]::integer[]", 2, new[] { "1007", "23", "", "" })]
    [DataRow("ARRAY[]::bigint[]", 3, new[] { "1016", "20", "", "" })]
    [DataRow("ARRAY[]::real[]", 4, new[] { "1021", "700", "", "" })]
    [DataRow("ARRAY[]::double precision[]", 5, new[] { "1022", "701", "", "" })]
    [DataRow("ARRAY[]::uuid[]", 6, new[] { "2951", "2950", "", "" })]
    public async Task BorrowedArraySlicesPreserveNativeValues(string expression, int mode, string[] expected)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual(expected, await Scalar<string[]>(connection,
            $"SELECT borrowed_arrays.array_slice_snapshot({expression},{mode})"));
    }

    /// <summary>
    /// Each supported native layout points at a payload independently witnessed in C before managed conversion.
    /// </summary>
    /// <param name="expression">The scalar array with a compatible native layout.</param>
    /// <param name="bitmap">Whether native storage includes a bitmap whose cells are all present.</param>
    [TestMethod]
    [DataRow("ARRAY['a','z']::\"char\"[]", false)]
    [DataRow("ARRAY[-7,0,19]::smallint[]", false)]
    [DataRow("ARRAY[-7,0,19]", false)]
    [DataRow("ARRAY[-7,0,19]", true)]
    [DataRow("ARRAY[-7,0,19]::bigint[]", false)]
    [DataRow("ARRAY[-1.5,0,7.25]::real[]", false)]
    [DataRow("ARRAY[-1.5,0,7.25]::double precision[]", false)]
    [DataRow("ARRAY['00112233-4455-6677-8899-aabbccddeeff']::uuid[]", false)]
    public async Task BorrowedArraySlicesShareNativePayload(string expression, bool bitmap)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        string function = bitmap ? "array_slice_bitmap" : "array_slice_argument";
        Assert.IsTrue(await Scalar<bool>(connection, $"""
            SELECT tests.{function}('borrowed_arrays.array_slice_address(anyarray,bigint)'::regprocedure,{expression})
            """));
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident LIKE 'Ankus borrowed array%'"));
    }

    /// <summary>
    /// NULLs, incompatible same-width types, empty wrong types and unsupported managed layouts fail and recover exactly.
    /// </summary>
    /// <param name="expression">The incompatible native array.</param>
    /// <param name="mode">The requested layout.</param>
    /// <param name="expected">The exact native or managed diagnostic.</param>
    [TestMethod]
    [DataRow("ARRAY[0,NULL,7]", 2, "22004:A native array slice cannot contain SQL NULL elements")]
    [DataRow("ARRAY[NULL]::smallint[]", 1, "22004:A native array slice cannot contain SQL NULL elements")]
    [DataRow("ARRAY[NULL]::uuid[]", 6, "22004:A native array slice cannot contain SQL NULL elements")]
    [DataRow("ARRAY[1.5]::real[]", 2, "42804:Array element type does not match the requested native slice")]
    [DataRow("ARRAY[1]", 4, "42804:Array element type does not match the requested native slice")]
    [DataRow("ARRAY[]::real[]", 2, "42804:Array element type does not match the requested native slice")]
    [DataRow("ARRAY[]::integer[]", 6, "42804:Array element type does not match the requested native slice")]
    [DataRow("ARRAY['abc']", 2, "42804:Array element type does not match the requested native slice")]
    [DataRow("ARRAY[23]::oid[]", 2, "42804:Array element type does not match the requested native slice")]
    [DataRow("ARRAY[]::uuid[]", 7, "NotSupportedException")]
    public async Task BorrowedArraySlicesRejectAndRecover(string expression, int mode, string expected)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string>([expected, "42"], await Scalar<string[]>(connection,
            $"SELECT borrowed_arrays.array_slice_failure({expression},{mode})"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Owner expiry invalidates both original and nested views while explicit managed copies survive unchanged.
    /// </summary>
    /// <param name="mode">The operation ending the view's source lifetime.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public async Task BorrowedArraySliceOwnersExpireAliasesAndPreserveCopies(int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string>(["ObjectDisposedException", "ObjectDisposedException", "-7,0,19", "3"],
            await Scalar<string[]>(connection, $"SELECT borrowed_arrays.array_slice_owners({mode})"));
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident LIKE 'Ankus borrowed array%'"));
    }

    /// <summary>
    /// Existing domain values retain their identities and remain readable without rechecking later constraints.
    /// </summary>
    [TestMethod]
    public async Task BorrowedArraySlicesRetainDomainIdentity()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(context.CancellationToken);
        await using var command = new NpgsqlCommand("""
            CREATE DOMAIN pg_temp.slice_cell AS integer;
            CREATE DOMAIN pg_temp.slice_array AS pg_temp.slice_cell[];
            CREATE TEMP TABLE slice_domain_source(value pg_temp.slice_array);
            INSERT INTO slice_domain_source VALUES (ARRAY[-7,0,19]::pg_temp.slice_cell[]);
            ALTER DOMAIN pg_temp.slice_cell ADD CHECK (VALUE > 0) NOT VALID;
            ALTER DOMAIN pg_temp.slice_array ADD CHECK (array_length(VALUE,1) < 2) NOT VALID;
            """, connection, transaction);
        await command.ExecuteNonQueryAsync(context.CancellationToken);
        string array = await Scalar<string>(connection, "SELECT 'pg_temp.slice_array'::regtype::oid::text");
        string element = await Scalar<string>(connection, "SELECT 'pg_temp.slice_cell'::regtype::oid::text");
        Assert.AreSequenceEqual<string>([array, element, "3", "1", "-7", "0", "19"], await Scalar<string[]>(connection,
            "SELECT borrowed_arrays.array_slice_domain(value) FROM slice_domain_source"));
    }

    /// <summary>
    /// Slices flatten non-flat payloads into their private owner and release it on every repeated call.
    /// </summary>
    /// <param name="kind">The physical storage independently observed by PostgreSQL macros.</param>
    [TestMethod]
    [DataRow("flat")]
    [DataRow("short")]
    [DataRow("compressed")]
    [DataRow("external")]
    [DataRow("expanded")]
    public async Task BorrowedArraySlicesFlattenAndReleaseNativeStorage(string kind)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(context.CancellationToken);
        string expression = "ARRAY[-7,0,19]";
        string count = "3";
        string sum = "12";
        if (kind is "short" or "compressed" or "external")
        {
            string storage = kind == "external" ? "EXTERNAL" : "EXTENDED";
            expression = kind == "short" ? "ARRAY[7]" : "array_fill(7,ARRAY[10000])";
            await using var command = new NpgsqlCommand($"""
                CREATE TEMP TABLE native_slice_storage(value integer[]);
                ALTER TABLE native_slice_storage ALTER COLUMN value SET STORAGE {storage};
                INSERT INTO native_slice_storage VALUES ({expression});
                """, connection, transaction);
            await command.ExecuteNonQueryAsync(context.CancellationToken);
            expression = "(SELECT value FROM native_slice_storage)";
            count = kind == "short" ? "1" : "10000";
            sum = kind == "short" ? "7" : "70000";
        }

        int expand = kind == "expanded" ? 1 : 0;
        bool copied = kind != "flat";
        Assert.AreSequenceEqual<string>([kind, copied.ToString(), copied ? "Ankus borrowed array" : "borrowed", count, sum, "0", "0"],
            await Scalar<string[]>(connection, $"""
                SELECT tests.array_storage('borrowed_arrays.array_slice_native(bigint,oid,text)'::regprocedure,{expression},{expand})
                """));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }
}
