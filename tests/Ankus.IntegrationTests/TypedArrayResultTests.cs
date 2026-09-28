using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class BorrowedArrayTests
{
    /// <summary>
    /// Every result path retains present shape/NULL cells and represents a whole-array SQL NULL distinctly.
    /// </summary>
    /// <param name="mode">The raw, SPI, prepared, cursor, catalog or native-address result path.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    public async Task TypedArrayResultsUseCheckedRawTransport(int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        foreach (bool absent in new[] { false, true })
        {
            string sql = absent ? "SELECT NULL::integer[]" : "SELECT '[-1:1]={7,NULL,19}'::integer[]";
            if (mode is 6 or 7)
            {
                await using var create = new NpgsqlCommand($"""
                    CREATE OR REPLACE FUNCTION pg_temp.typed_array_result() RETURNS integer[] LANGUAGE sql AS $body${sql}$body$
                    """, connection);
                await create.ExecuteNonQueryAsync(context.CancellationToken);
            }

            string?[] expected = absent ? ["SQL NULL"] : mode == 8
                ? ["6", "-1", "7", null, "19", "7", null, "19"] : ["3", "-1", "7", null, "19"];
            Assert.AreSequenceEqual(expected, await TypedResult(connection, sql, mode, 0));
        }

        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident LIKE 'Ankus borrowed array%'"));
    }

    /// <summary>
    /// Whole-array NULL results still require the correct builtin or nominal element contract at every safe boundary.
    /// </summary>
    /// <param name="mode">The result boundary, excluding caller-asserted native addresses.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    public async Task TypedArrayNullResultsValidateDeclaredElements(int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using var sequence = new NpgsqlCommand("CREATE TEMP SEQUENCE typed_array_effect", connection);
        await sequence.ExecuteNonQueryAsync(context.CancellationToken);
        foreach ((string type, int kind) in new (string, int)[]
        {
            ("real[]", 0), ("integer[]", 5), ("datum_mappings.other_positive[]", 5), ("varchar[]", 7)
        })
        {
            string sql = $"SELECT NULL::{type}";
            if (mode is 6 or 7)
            {
                await using var create = new NpgsqlCommand($"""
                    DROP FUNCTION IF EXISTS pg_temp.typed_array_result();
                    CREATE FUNCTION pg_temp.typed_array_result() RETURNS {type} LANGUAGE plpgsql AS $body$
                    BEGIN PERFORM nextval('typed_array_effect'); RETURN NULL::{type}; END $body$
                    """, connection);
                await create.ExecuteNonQueryAsync(context.CancellationToken);
            }

            await using var command = new NpgsqlCommand("SELECT borrowed_arrays.typed_array_result_failure($1,$2,$3)", connection);
            command.Parameters.AddWithValue(sql);
            command.Parameters.AddWithValue(mode);
            command.Parameters.AddWithValue(kind);
            Assert.AreSequenceEqual<string>(["42804:Array element type does not match the requested managed type", "42", "0"],
                Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(context.CancellationToken)));
        }

        Assert.IsFalse(await Scalar<bool>(connection, "SELECT is_called FROM typed_array_effect"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Generated enum, custom native-layout, mapped and borrowed-buffer element factories remain statically closed and usable.
    /// </summary>
    /// <param name="sql">The independently constructed value.</param>
    /// <param name="kind">The exact generated element representation.</param>
    /// <param name="expected">Literal shape and converted cells.</param>
    [TestMethod]
    [DataRow("SELECT ARRAY[7,NULL,-9]", 1, new string?[] { "3", "1", "7", null, "-9" })]
    [DataRow("SELECT ARRAY['High',NULL,'café']::datatype.enum_mood[]", 2, new string?[] { "3", "1", "High", null, "Cafe" })]
    [DataRow("SELECT ARRAY['café 🐘','',NULL]", 3, new string?[] { "3", "1", "café 🐘", "", null })]
    [DataRow("SELECT ARRAY['7',NULL,'-9']::native_layout.packet[]", 4, new string?[] { "3", "1", "7", null, "-9" })]
    [DataRow("SELECT ARRAY[7,NULL,11]::datum_mappings.positive[]", 5, new string?[] { "3", "1", "7", null, "11" })]
    [DataRow("SELECT ARRAY['mapped','',NULL]", 7, new string?[] { "3", "1", "mapped", "", null })]
    [DataRow("SELECT ARRAY[]::integer[]", 1, new string?[] { "", "" })]
    [DataRow("SELECT NULL::datum_mappings.positive[]", 5, new string?[] { "SQL NULL" })]
    [DataRow("SELECT ARRAY['7',NULL,'-9']::native_layout.packet[]", 8, new string?[] { "3", "1", "7", null, "-9" })]
    [DataRow("SELECT ARRAY['9223372036854775807',NULL,'-9']::custom_values.number[]", 9,
        new string?[] { "3", "1", "9223372036854775807", null, "-9" })]
    [DataRow("SELECT ARRAY['café',NULL,'']::custom_values.message[]", 10, new string?[] { "3", "1", "café", null, "" })]
    [DataRow("SELECT ARRAY[decode('00ff','hex'),NULL,decode('','hex')]", 11, new string?[] { "3", "1", "00FF", null, "" })]
    [DataRow("SELECT ARRAY['[1,7)',NULL,'empty']::int4range[]", 13, new string?[] { "3", "1", "[1,7)", null, "empty" })]
    [DataRow("SELECT ARRAY['[1,7)',NULL,'empty']::int4range[]", 14, new string?[] { "3", "1", "1001:1007", null, "empty" })]
    public async Task TypedArrayFactoriesUseGeneratedScalarRegistrations(string sql, int kind, string?[] expected)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        string type = await Scalar<string>(connection, $"SELECT pg_typeof(({sql}))::text");
        await using var create = new NpgsqlCommand($"""
            CREATE FUNCTION pg_temp.typed_array_result() RETURNS {type} LANGUAGE sql AS $body${sql}$body$
            """, connection);
        await create.ExecuteNonQueryAsync(context.CancellationToken);
        for (int mode = 0; mode <= 7; mode++)
        {
            Assert.AreSequenceEqual(expected, await TypedResult(connection, sql, mode, kind));
        }

        string?[] concatenated = expected.Length <= 2 ? expected :
            [((expected.Length - 2) * 2).ToString(System.Globalization.CultureInfo.InvariantCulture), "1",
                .. expected.Skip(2), .. expected.Skip(2)];
        Assert.AreSequenceEqual(concatenated, await TypedResult(connection, sql, 8, kind));
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident LIKE 'Ankus borrowed array%' OR ident LIKE 'Ankus borrowed buffer%'"));
    }

    /// <summary>
    /// Missing element readers reject scalar APIs before SQL or catalog functions can advance a sequence.
    /// </summary>
    /// <param name="mode">The SPI or catalog scalar result boundary.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(6)]
    [DataRow(7)]
    public async Task TypedArrayResultsRequireReadersBeforeExecution(int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using var create = new NpgsqlCommand("""
            CREATE TEMP SEQUENCE typed_array_effect;
            CREATE FUNCTION pg_temp.typed_array_result() RETURNS integer[] LANGUAGE sql AS $body$
                SELECT ARRAY[nextval('typed_array_effect')::integer]
            $body$
            """, connection);
        await create.ExecuteNonQueryAsync(context.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT borrowed_arrays.typed_array_result_failure($1,$2,6)", connection);
        command.Parameters.AddWithValue("SELECT ARRAY[nextval('typed_array_effect')::integer]");
        command.Parameters.AddWithValue(mode);
        Assert.AreSequenceEqual<string>(["NotSupportedException", "42", "0"],
            Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(context.CancellationToken)));
        Assert.IsFalse(await Scalar<bool>(connection, "SELECT is_called FROM typed_array_effect"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Raw typed parameters preserve source values even when their mapped element has no writer.
    /// </summary>
    [TestMethod]
    public async Task TypedArrayParametersDoNotRequireElementWriters()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT borrowed_arrays.typed_read_only_array_parameter()", connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(context.CancellationToken);
        Assert.IsTrue(await reader.ReadAsync(context.CancellationToken));
        Assert.AreSequenceEqual<int?>([107, null, 111, 7, null, 11],
            await reader.GetFieldValueAsync<int?[]>(0, context.CancellationToken));
    }

    /// <summary>
    /// Named composites remain readable through generic record elements, including typed whole-array NULL results.
    /// </summary>
    /// <param name="mode">The raw, SPI or catalog result boundary.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    public async Task TypedArrayResultsAcceptNamedCompositeElements(int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        foreach (bool absent in new[] { false, true })
        {
            string sql = absent ? "SELECT NULL::tuple_values.dog[]" :
                "SELECT ARRAY[ROW('Ada',3)::tuple_values.dog,NULL,ROW('',9)::tuple_values.dog]";
            await using var create = new NpgsqlCommand($"""
                CREATE OR REPLACE FUNCTION pg_temp.typed_array_result() RETURNS tuple_values.dog[] LANGUAGE sql AS $body${sql}$body$
                """, connection);
            await create.ExecuteNonQueryAsync(context.CancellationToken);
            string?[] expected = absent ? ["SQL NULL"] : ["3", "1", "Ada", null, ""];
            Assert.AreSequenceEqual(expected, await TypedResult(connection, sql, mode, 12));
        }
    }

    /// <summary>
    /// A later managed or native failure disposes both previously converted typed array owners.
    /// </summary>
    /// <param name="nativeFailure">Whether the third column fails a native identity check.</param>
    /// <param name="mode">The direct, session or prepared scalar tuple path.</param>
    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(true, 0)]
    [DataRow(false, 1)]
    [DataRow(true, 1)]
    [DataRow(false, 2)]
    [DataRow(true, 2)]
    [DataRow(false, 3)]
    [DataRow(true, 3)]
    public async Task TypedArrayResultsCleanUpAfterLaterFailures(bool nativeFailure, int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        string failure = nativeFailure ? "42804:Array element type does not match the requested managed type" : "InvalidCastException";
        Assert.AreSequenceEqual<string>([failure, "0", "42"], await Scalar<string[]>(connection,
            $"SELECT borrowed_arrays.typed_array_result_cleanup({nativeFailure.ToString().ToLowerInvariant()},{mode})"));
    }

    /// <summary>
    /// Raw row ownership expires borrowed arrays and cells, while independently copied scalar results remain usable.
    /// </summary>
    [TestMethod]
    public async Task TypedArrayResultsRejectAfterOwnerExpiration()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string?>(["raw", "ObjectDisposedException", "ObjectDisposedException", "owned", null],
            await Scalar<string?[]>(connection, "SELECT borrowed_arrays.typed_array_result_owners()"));
    }

    /// <summary>
    /// Present parameters retain an outer domain identity and typed NULL parameters infer the declared builtin array.
    /// </summary>
    [TestMethod]
    public async Task TypedArrayParametersPreserveOriginalIdentity()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using var create = new NpgsqlCommand("CREATE DOMAIN pg_temp.typed_parameter AS integer[]", connection);
        await create.ExecuteNonQueryAsync(context.CancellationToken);
        string oid = await Scalar<string>(connection, "SELECT 'pg_temp.typed_parameter'::regtype::oid::text");
        foreach (bool absent in new[] { false, true })
        {
            await using var command = new NpgsqlCommand("SELECT borrowed_arrays.typed_array_parameter($1,$2)", connection);
            command.Parameters.AddWithValue("SELECT '[-1:1]={7,NULL,19}'::pg_temp.typed_parameter");
            command.Parameters.AddWithValue(absent);
            string?[] expected = absent ? ["1007", "1007", null, "true"] : [oid, oid, "[-1:1]={7,NULL,19}", "false"];
            Assert.AreSequenceEqual(expected, Assert.IsInstanceOfType<string?[]>(await command.ExecuteScalarAsync(context.CancellationToken)));
        }
    }

    /// <summary>
    /// Executes a typed result probe without substituting SQL source text through string escaping.
    /// </summary>
    private async Task<string?[]> TypedResult(NpgsqlConnection connection, string sql, int mode, int kind)
    {
        await using var command = new NpgsqlCommand("SELECT borrowed_arrays.typed_array_result($1,$2,$3)", connection);
        command.Parameters.AddWithValue(sql);
        command.Parameters.AddWithValue(mode);
        command.Parameters.AddWithValue(kind);
        return Assert.IsInstanceOfType<string?[]>(await command.ExecuteScalarAsync(context.CancellationToken));
    }
}
