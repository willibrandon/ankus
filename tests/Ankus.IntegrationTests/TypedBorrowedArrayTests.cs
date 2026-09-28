using System.Globalization;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class BorrowedArrayTests
{
    /// <summary>
    /// Typed reads preserve SQL identities, every NULL position, shape and exact managed values.
    /// </summary>
    /// <param name="expression">An independently constructed SQL array.</param>
    /// <param name="mode">The requested scalar representation.</param>
    /// <param name="rank">The expected dimension count.</param>
    /// <param name="lengths">The expected dimension lengths.</param>
    /// <param name="bounds">The expected lower bounds.</param>
    /// <param name="expected">Literal converted cell values.</param>
    [TestMethod]
    [DataRow("ARRAY[]::integer[]", 0, 0, "", "", new string?[] { })]
    [DataRow("ARRAY[]::integer[]", 1, 0, "", "", new string?[] { })]
    [DataRow("ARRAY[0]", 1, 1, "1", "1", new string?[] { "0" })]
    [DataRow("ARRAY[NULL,NULL]::integer[]", 0, 1, "2", "1", new string?[] { null, null })]
    [DataRow("'[-2:-1][4:6]={{0,NULL,2147483647},{-2147483648,11,15}}'::integer[]", 0, 2, "2,3", "-2,4",
        new string?[] { "0", null, "2147483647", "-2147483648", "11", "15" })]
    [DataRow("ARRAY['café 🐘','',NULL,'last']", 2, 1, "4", "1", new string?[] { "café 🐘", "", null, "last" })]
    [DataRow("ARRAY['café','',NULL]::varchar[]", 2, 1, "3", "1", new string?[] { "café", "", null })]
    [DataRow("ARRAY['a','',NULL]::char(3)[]", 2, 1, "3", "1", new string?[] { "a  ", "   ", null })]
    [DataRow("ARRAY[decode('00ff0780','hex'),NULL,decode('','hex')]", 3, 1, "3", "1", new string?[] { "00FF0780", null, "" })]
    [DataRow("ARRAY['-0','-1.5',NULL,'-Infinity']::double precision[]", 4, 1, "4", "1",
        new string?[] { "8000000000000000", "BFF8000000000000", null, "FFF0000000000000" })]
    [DataRow("ARRAY['00112233-4455-6677-8899-aabbccddeeff',NULL]::uuid[]", 5, 1, "2", "1",
        new string?[] { "00112233-4455-6677-8899-aabbccddeeff", null })]
    [DataRow("ARRAY['High',NULL,'café','']::datatype.enum_mood[]", 6, 1, "4", "1", new string?[] { "High", null, "Cafe", "Empty" })]
    [DataRow("ARRAY[]::datatype.enum_mood[]", 6, 0, "", "", new string?[] { })]
    [DataRow("ARRAY[5,NULL,-9]", 7, 1, "3", "1", new string?[] { "5", null, "-9" })]
    [DataRow("ARRAY[]::integer[]", 7, 0, "", "", new string?[] { })]
    [DataRow("ARRAY[NULL,NULL]::integer[]", 7, 1, "2", "1", new string?[] { null, null })]
    [DataRow("ARRAY['123456789012345678901234567890.00001',NULL,'NaN']::numeric[]", 8, 1, "3", "1",
        new string?[] { "123456789012345678901234567890.00001", null, "NaN" })]
    [DataRow("ARRAY[ROW('Ada',3)::tuple_values.dog,NULL,ROW('',9)::tuple_values.dog]", 9, 1, "3", "1", new string?[] { "Ada", null, "" })]
    [DataRow("ARRAY[]::tuple_values.dog[]", 9, 0, "", "", new string?[] { })]
    [DataRow("ARRAY[NULL]::tuple_values.dog[]", 9, 1, "1", "1", new string?[] { null })]
    [DataRow("ARRAY['café 🐘','',NULL]", 10, 1, "3", "1", new string?[] { "café 🐘", "", null })]
    [DataRow("ARRAY['a',NULL]::char(3)[]", 10, 1, "2", "1", new string?[] { "a  ", null })]
    [DataRow("ARRAY[decode('00ff07','hex'),NULL,decode('','hex')]", 11, 1, "3", "1", new string?[] { "00FF07", null, "" })]
    [DataRow("ARRAY[7,NULL,11]::datum_mappings.positive[]", 12, 1, "3", "1", new string?[] { "7", null, "11" })]
    [DataRow("ARRAY[]::datum_mappings.positive[]", 12, 0, "", "", new string?[] { })]
    [DataRow("ARRAY[NULL]::datum_mappings.positive[]", 12, 1, "1", "1", new string?[] { null })]
    [DataRow("ARRAY['2024-02-29',NULL,'0001-01-01']::date[]", 14, 1, "3", "1", new string?[] { "2024-02-29", null, "0001-01-01" })]
    [DataRow("ARRAY['7',NULL,'-9']::native_layout.packet[]", 15, 1, "3", "1", new string?[] { "7", null, "-9" })]
    [DataRow("ARRAY['7',NULL,'-9']::native_layout.packet[]", 16, 1, "3", "1", new string?[] { "7", null, "-9" })]
    [DataRow("ARRAY['café','',NULL]", 17, 1, "3", "1", new string?[] { "café", "", null })]
    public async Task TypedBorrowedArraysPreserveValuesAndMetadata(string expression, int mode, int rank, string lengths,
        string bounds, string?[] expected)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        string[] identities = await Scalar<string[]>(connection, $"""
            SELECT ARRAY[oid::text,typelem::text] FROM pg_type WHERE oid=pg_typeof({expression})::oid
            """);
        string?[] complete = [.. identities, rank.ToString(CultureInfo.InvariantCulture), expected.Length.ToString(CultureInfo.InvariantCulture),
            expected.Contains(null).ToString(), lengths, bounds, .. expected];
        Assert.AreSequenceEqual(complete, await Scalar<string?[]>(connection,
            $"SELECT borrowed_arrays.typed_array_snapshot({expression},{mode})"));
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident LIKE 'Ankus borrowed array%' OR ident LIKE 'Ankus borrowed buffer%'"));
    }

    /// <summary>
    /// Wrong type identities reject empty and all-NULL arrays, and failed conversions leave the backend usable.
    /// </summary>
    /// <param name="expression">The invalid native input.</param>
    /// <param name="mode">The incompatible managed conversion.</param>
    /// <param name="expected">The exact expected rejection.</param>
    [TestMethod]
    [DataRow("ARRAY[]::real[]", 0, "42804:Array element type does not match the requested managed type")]
    [DataRow("ARRAY[NULL,NULL]::real[]", 0, "42804:Array element type does not match the requested managed type")]
    [DataRow("ARRAY[1]::oid[]", 0, "42804:Array element type does not match the requested managed type")]
    [DataRow("ARRAY[]::integer[]", 6, "42804:Array element type does not match the requested managed type")]
    [DataRow("ARRAY[NULL]::text[]", 6, "42804:Array element type does not match the requested managed type")]
    [DataRow("ARRAY[]::integer[]", 9, "42804:Array element type does not match the requested managed type")]
    [DataRow("ARRAY[NULL]::integer[]", 1, "InvalidCastException")]
    [DataRow("ARRAY[5,NULL]", 1, "InvalidCastException")]
    [DataRow("ARRAY[]::integer[]", 13, "NotSupportedException")]
    [DataRow("ARRAY[NULL]::integer[]", 13, "NotSupportedException")]
    [DataRow("ARRAY[]::integer[]", 12, "42804:Array element type does not match the requested managed type")]
    [DataRow("ARRAY[NULL]::datum_mappings.other_positive[]", 12, "42804:Array element type does not match the requested managed type")]
    [DataRow("ARRAY[7]::datum_mappings.positive[]", 7, "42804:Array element type does not match the requested managed type")]
    [DataRow("ARRAY['hello']::varchar[]", 7, "42804:Array element type does not match the requested managed type")]
    [DataRow("ARRAY[]::integer[]", 2, "42804:Array element type does not match the requested managed type")]
    [DataRow("ARRAY[]::varchar[]", 17, "42804:Array element type does not match the requested managed type")]
    [DataRow("ARRAY[NULL]::varchar[]", 17, "42804:Array element type does not match the requested managed type")]
    [DataRow("ARRAY['2024-02-29','infinity']::date[]", 14, "InvalidOperationException")]
    [DataRow("ARRAY[5,-777,9]", 7, "P8521:mapped array reader failed")]
    [DataRow("42", 0, "42804:The borrowed value is not an array")]
    public async Task TypedBorrowedArraysRejectAndRecover(string expression, int mode, string expected)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string>([expected, "42"], await Scalar<string[]>(connection,
            $"SELECT borrowed_arrays.typed_array_failure({expression},{mode})"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident LIKE 'Ankus borrowed array%'"));
    }

    /// <summary>
    /// A present raw handle can describe SQL NULL, and disposed result handles cannot open typed views.
    /// </summary>
    [TestMethod]
    public async Task TypedBorrowedArrayConstructionRejectsNullAndStaleSources()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string>(["ArgumentException:value", "ObjectDisposedException", "42"],
            await Scalar<string[]>(connection, "SELECT borrowed_arrays.typed_array_construction_errors()"));
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident LIKE 'Ankus borrowed array%'"));
    }

    /// <summary>
    /// Existing domain values retain nominal OIDs and remain readable without reapplying later constraints.
    /// </summary>
    [TestMethod]
    public async Task TypedBorrowedArraysRetainDomainIdentity()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(context.CancellationToken);
        await using var command = new NpgsqlCommand("""
            CREATE DOMAIN pg_temp.typed_cell AS integer;
            CREATE DOMAIN pg_temp.typed_array AS pg_temp.typed_cell[];
            CREATE TEMP TABLE typed_domain_source(value pg_temp.typed_array);
            INSERT INTO typed_domain_source VALUES (ARRAY[-7,NULL,19]::pg_temp.typed_cell[]);
            ALTER DOMAIN pg_temp.typed_cell ADD CHECK (VALUE > 0) NOT VALID;
            ALTER DOMAIN pg_temp.typed_array ADD CHECK (array_length(VALUE,1) < 2) NOT VALID;
            CREATE DOMAIN pg_temp.typed_mapped_array AS datum_mappings.positive[];
            """, connection, transaction);
        await command.ExecuteNonQueryAsync(context.CancellationToken);
        string array = await Scalar<string>(connection, "SELECT 'pg_temp.typed_array'::regtype::oid::text");
        string element = await Scalar<string>(connection, "SELECT 'pg_temp.typed_cell'::regtype::oid::text");
        Assert.AreSequenceEqual<string?>([array, element, "1", "3", "True", "3", "1", "-7", null, "19"],
            await Scalar<string?[]>(connection, "SELECT borrowed_arrays.typed_array_snapshot(value,0) FROM typed_domain_source"));
        string mappedArray = await Scalar<string>(connection, "SELECT 'pg_temp.typed_mapped_array'::regtype::oid::text");
        string mappedElement = await Scalar<string>(connection, "SELECT 'datum_mappings.positive'::regtype::oid::text");
        Assert.AreSequenceEqual<string?>([mappedArray, mappedElement, "1", "2", "True", "2", "1", "7", null],
            await Scalar<string?[]>(connection,
                "SELECT borrowed_arrays.typed_array_snapshot(ARRAY[7,NULL]::pg_temp.typed_mapped_array,12)"));
    }

    /// <summary>
    /// A cached mapped Current cannot invoke user conversion twice or turn a failed cell into a valid position.
    /// </summary>
    [TestMethod]
    public async Task TypedBorrowedArrayMappedCursorsConvertOnce()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string?>(["0", "True", "5", "5", "1", "True", null, "1", "P8521:mapped array reader failed",
            "InvalidOperationException", "2", "True", "9", "3", "False", "InvalidOperationException",
            "ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException", "42"],
            await Scalar<string?[]>(connection, "SELECT borrowed_arrays.typed_array_mapped_cursor()"));
    }

    /// <summary>
    /// Every supported owner ending invalidates typed aliases while copied strings and bytes survive.
    /// </summary>
    /// <param name="mode">The operation ending the source lifetime.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public async Task TypedBorrowedArrayOwnersPreserveCopiesAndExpireViews(int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string>(["ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException",
            "ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException",
            "ObjectDisposedException", "café", "00FF07", "2", "4"],
            await Scalar<string[]>(connection, $"SELECT borrowed_arrays.typed_array_owners({mode})"));
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident LIKE 'Ankus borrowed array%' OR ident LIKE 'Ankus borrowed buffer%'"));
    }

    /// <summary>
    /// Independent cursors, adjacent invalid indices and native subscripts retain row-major values and NULLs.
    /// </summary>
    [TestMethod]
    public async Task TypedBorrowedArrayCursorsAndBoundsRemainIndependent()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string?>(["InvalidOperationException", "True", "0", "True", "0", "True", null, "True", "0",
            "ObjectDisposedException", null, "7", "-9", "11", "15", "False", "InvalidOperationException", "NotSupportedException",
            "ArgumentOutOfRangeException:index", "ArgumentOutOfRangeException:index", "ArgumentException:subscripts",
            "ArgumentOutOfRangeException:subscripts", "ArgumentOutOfRangeException:subscripts", "ArgumentOutOfRangeException:subscripts",
            "ArgumentOutOfRangeException:subscripts", "0", null, "15", "-9", "ArgumentOutOfRangeException:index",
            "ArgumentException:subscripts", "False", "False", "InvalidOperationException"],
            await Scalar<string?[]>(connection, "SELECT borrowed_arrays.typed_array_cursors_and_bounds()"));
    }

    /// <summary>
    /// Callback scope cleanup expires typed arrays and cached borrowed elements on success and error paths.
    /// </summary>
    /// <param name="fail">Whether the original callback fails deliberately.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TypedBorrowedArrayCallbacksExpireElements(bool fail)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        if (fail)
        {
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
                Scalar<string>(connection, "SELECT borrowed_arrays.typed_array_save(ARRAY['café'],true)"));
            Assert.AreEqual("P8525", error.SqlState);
            Assert.AreEqual("Typed array callback failed.", error.MessageText);
        }
        else
        {
            Assert.AreEqual("café", await Scalar<string>(connection, "SELECT borrowed_arrays.typed_array_save(ARRAY['café'],false)"));
        }

        Assert.AreSequenceEqual<string>(["ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException", "42"],
            await Scalar<string[]>(connection, "SELECT borrowed_arrays.typed_array_expired()"));
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident LIKE 'Ankus borrowed array%' OR ident LIKE 'Ankus borrowed buffer%'"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }
}
