using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies generated polymorphic signatures against PostgreSQL's resolved types, storage, and executor lifetimes.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed partial class PolymorphicTests(TestContext context)
{
    /// <summary>
    /// Round trips exact values and type identities without a managed mapping for the input type.
    /// </summary>
    /// <param name="expression">A typed PostgreSQL value.</param>
    [TestMethod]
    [DataRow("0::integer")]
    [DataRow("9223372036854775807::bigint")]
    [DataRow("'-0'::double precision")]
    [DataRow("'NaN'::numeric")]
    [DataRow("repeat('héllo 🐘',10000)")]
    [DataRow("decode('0000ff01','hex')")]
    [DataRow("'owned'::pg_temp.poly_enum")]
    [DataRow("42::pg_temp.poly_domain")]
    [DataRow("ROW(42,'owned')::pg_temp.poly_pair")]
    [DataRow("ROW(42,'owned'::text)")]
    [DataRow("'[2:3][-1:0]={{1,NULL},{3,4}}'::integer[]")]
    [DataRow("NULL::text")]
    [DataRow("NULL::pg_temp.poly_domain")]
    [DataRow("pg_sleep(0)")]
    public async Task ScalarValuesPreserveResolvedTypes(string expression)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await using var command = new NpgsqlCommand($"""
            WITH input AS (SELECT {expression} AS value)
            SELECT datatype.poly_identity(value)::text, value::text,
                pg_typeof(datatype.poly_identity(value))::oid, pg_typeof(value)::oid,
                datatype.poly_describe(value)
            FROM input
            """, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        Assert.IsTrue(await reader.ReadAsync(token));
        Assert.AreEqual(reader.GetValue(1), reader.GetValue(0));
        Assert.AreEqual(reader.GetFieldValue<uint>(3), reader.GetFieldValue<uint>(2));
        Assert.AreEqual(reader.IsDBNull(1) ? "NULL" : reader.GetFieldValue<uint>(3) + "|" + reader.GetString(1), reader.GetString(4));
    }

    /// <summary>
    /// Preserves empty arrays, dimensions, bounds, nullable cells, and actual element identities.
    /// </summary>
    /// <param name="expression">The typed array.</param>
    /// <param name="element">The expected element catalog type.</param>
    /// <param name="shape">The exact shape and ordered cell values.</param>
    [TestMethod]
    [DataRow("'{}'::integer[]", "integer", "0|0||")]
    [DataRow("'[2:3][-1:0]={{1,NULL},{3,4}}'::integer[]", "integer", "2|4|2:2;-1:2|1;NULL;3;4")]
    [DataRow("ARRAY['owned',NULL,'other']::pg_temp.poly_enum[]", "pg_temp.poly_enum", "1|3|1:3|owned;NULL;other")]
    [DataRow("ARRAY[1,NULL,3]::pg_temp.poly_domain[]", "pg_temp.poly_domain", "1|3|1:3|1;NULL;3")]
    [DataRow("ARRAY[ROW(1,'a'),NULL,ROW(2,'b')]::pg_temp.poly_pair[]", "pg_temp.poly_pair", "1|3|1:3|(1,a);NULL;(2,b)")]
    public async Task ArraysPreserveShapeAndUnmappedElements(string expression, string element, string shape)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await using var command = new NpgsqlCommand($"""
            SELECT datatype.poly_array_identity({expression})::text, ({expression})::text,
                datatype.poly_array_shape({expression}), '{element}'::regtype::oid
            """, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        Assert.IsTrue(await reader.ReadAsync(token));
        Assert.AreEqual(reader.GetString(1), reader.GetString(0));
        Assert.AreEqual(reader.GetFieldValue<uint>(3) + "|" + shape, reader.GetString(2));
    }

    /// <summary>
    /// Streams or materializes elements with actual result types and SQL NULL cells.
    /// </summary>
    /// <param name="materialize">Whether PostgreSQL uses the materialized callback.</param>
    /// <param name="element">The actual element type.</param>
    /// <param name="values">The typed input array.</param>
    [TestMethod]
    [DataRow(false, "integer", "ARRAY[1,NULL,3]")]
    [DataRow(true, "integer", "ARRAY[1,NULL,3]")]
    [DataRow(false, "pg_temp.poly_enum", "ARRAY['owned',NULL,'other']::pg_temp.poly_enum[]")]
    [DataRow(true, "pg_temp.poly_enum", "ARRAY['owned',NULL,'other']::pg_temp.poly_enum[]")]
    [DataRow(false, "pg_temp.poly_domain", "ARRAY[1,NULL,3]::pg_temp.poly_domain[]")]
    [DataRow(true, "pg_temp.poly_domain", "ARRAY[1,NULL,3]::pg_temp.poly_domain[]")]
    [DataRow(false, "pg_temp.poly_pair", "ARRAY[ROW(1,'a'),NULL,ROW(2,'b')]::pg_temp.poly_pair[]")]
    [DataRow(true, "pg_temp.poly_pair", "ARRAY[ROW(1,'a'),NULL,ROW(2,'b')]::pg_temp.poly_pair[]")]
    public async Task SetResultsFollowResolvedElementTypes(bool materialize, string element, string values)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        string function = materialize ? "poly_materialized" : "poly_elements";
        // Composite NULLs materialize as all-NULL rows, matching PostgreSQL's tuple-store contract.
        string reference = materialize && element == "pg_temp.poly_pair"
            ? "SELECT (cell).* FROM unnest(" + values + ") cell"
            : "SELECT * FROM unnest(" + values + ")";
        string sql = $"""
            SELECT ARRAY(SELECT row_to_json(actual)::text FROM (SELECT * FROM datatype.{function}({values})) actual),
                ARRAY(SELECT row_to_json(expected)::text FROM ({reference}) expected)
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        Assert.IsTrue(await reader.ReadAsync(token));
        string[] actual = reader.GetFieldValue<string[]>(0);
        string[] expected = reader.GetFieldValue<string[]>(1);
        // Compare the same record field names explicitly for scalar and composite row types.
        Assert.HasCount(3, actual);
        Assert.HasCount(3, expected);
        for (int index = 0; index < actual.Length; index++)
        {
            using System.Text.Json.JsonDocument actualRow = System.Text.Json.JsonDocument.Parse(actual[index]);
            using System.Text.Json.JsonDocument expectedRow = System.Text.Json.JsonDocument.Parse(expected[index]);
            string[] actualCells = [.. actualRow.RootElement.EnumerateObject().Select(static cell => cell.Value.GetRawText())];
            string[] expectedCells = [.. expectedRow.RootElement.EnumerateObject().Select(static cell => cell.Value.GetRawText())];
            Assert.AreSequenceEqual(expectedCells, actualCells);
        }

        await reader.DisposeAsync();
        if (element != "pg_temp.poly_pair")
        {
            Assert.AreEqual(await ScalarAsync<uint>(connection, $"SELECT '{element}'::regtype::oid", token),
                await ScalarAsync<uint>(connection, $"SELECT pg_typeof(cell)::oid FROM datatype.{function}({values}) cell LIMIT 1", token));
        }
    }

    /// <summary>
    /// Retains large inputs across advances, preserves NULL rows and TABLE output types, and permits early termination.
    /// </summary>
    [TestMethod]
    public async Task IteratorOwnersRetainValuesAndTableColumns()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        Assert.AreSequenceEqual([70000, 70000, 70000], await ScalarAsync<int[]>(connection,
            "SELECT array_agg(length(value)) FROM datatype.poly_repeat(repeat('owned 🐘',10000),3) value", token));
        Assert.AreEqual(3L, await ScalarAsync<long>(connection, "SELECT count(*) FROM datatype.poly_repeat(NULL::text,3) value WHERE value IS NULL", token));
        Assert.AreEqual(0L, await ScalarAsync<long>(connection, "SELECT count(*) FROM datatype.poly_elements('{}'::integer[])", token));
        Assert.AreEqual(DBNull.Value, await ScalarAsync<object>(connection, "SELECT datatype.poly_array_identity(NULL::integer[])", token));
        Assert.AreSequenceEqual(["0:1", "1:NULL", "2:3"], await ScalarAsync<string[]>(connection,
            "SELECT array_agg(ordinal::text||':'||coalesce(value::text,'NULL') ORDER BY ordinal) FROM datatype.poly_table(ARRAY[1,NULL,3])", token));
        Assert.AreEqual("owned", await ScalarAsync<string>(connection, "SELECT value FROM datatype.poly_repeat('owned'::text,100) value LIMIT 1", token));
        Assert.AreEqual(42, await ScalarAsync<int>(connection, "SELECT datatype.poly_nested(42)", token));
        Assert.AreEqual("-6|42|-6", await ScalarAsync<string>(connection, "SELECT datatype.poly_marker_safety()", token));
    }

    /// <summary>
    /// Preserves array and cell lifetimes across copies and binds wrappers to ordinary polymorphic built-ins.
    /// </summary>
    /// <param name="expression">The independently owned array input.</param>
    [TestMethod]
    [DataRow("'{}'::integer[]")]
    [DataRow("'[2:3][-1:0]={{1,NULL},{3,4}}'::integer[]")]
    [DataRow("ARRAY[repeat('héllo 🐘',10000),NULL,''::text]")]
    [DataRow("ARRAY[ROW(1,repeat('owned',10000)),NULL]::pg_temp.poly_pair[]")]
    public async Task ArrayCopiesAndBuiltinCallsPreserveValues(string expression)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        Assert.IsTrue(await ScalarAsync<bool>(connection, $"SELECT datatype.poly_array_ownership({expression})::text = ({expression})::text", token));
        Assert.IsTrue(await ScalarAsync<bool>(connection, $"SELECT datatype.poly_array_json({expression})::text = array_to_json({expression})::text", token));
        Assert.AreEqual(await ScalarAsync<int>(connection, $"SELECT cardinality({expression})", token),
            await ScalarAsync<int>(connection, $"SELECT datatype.poly_array_count({expression})", token));
        await using var command = new NpgsqlCommand("SELECT datatype.poly_array_count(42)", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual(PostgresErrorCodes.DatatypeMismatch, error.SqlState);
        Assert.AreEqual(42, await ScalarAsync<int>(connection, "SELECT datatype.poly_identity(42)", token));
    }

    /// <summary>
    /// Validates type identity and native lifetime before returning a result, then recovers in the same backend.
    /// </summary>
    /// <param name="stale">Whether to return freed storage instead of the wrong type.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InvalidResultsFailWithoutCorruption(bool stale)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand($"SELECT datatype.poly_invalid(42,{(stale ? "true" : "false")})", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual(stale ? "38000" : "42804", error.SqlState);
        Assert.AreEqual("42|expired|exact", await ScalarAsync<string>(connection, "SELECT datatype.poly_ownership(42)", token));
        Assert.AreEqual(42, await ScalarAsync<int>(connection, "SELECT datatype.poly_identity(42)", token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Checks domain constraints when a nullable managed result resolves to a non-nullable PostgreSQL domain.
    /// </summary>
    [TestMethod]
    public async Task NullResultsRespectResolvedDomainConstraints()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await using var command = new NpgsqlCommand("CREATE DOMAIN pg_temp.required_integer AS integer NOT NULL", connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT datatype.poly_null(42::pg_temp.required_integer)";
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual(PostgresErrorCodes.NotNullViolation, error.SqlState);
        Assert.AreEqual(DBNull.Value, await ScalarAsync<object>(connection, "SELECT datatype.poly_null(42)", token));
        Assert.AreEqual(42, await ScalarAsync<int>(connection, "SELECT datatype.poly_identity(42::pg_temp.required_integer)", token));
    }

    /// <summary>
    /// Creates isolated catalog types with no managed mappings in this connection's temporary schema.
    /// </summary>
    private static async Task<NpgsqlConnection> OpenAsync(CancellationToken token)
    {
        NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        try
        {
            await using var command = new NpgsqlCommand("""
                CREATE TYPE pg_temp.poly_enum AS ENUM ('owned','other');
                CREATE DOMAIN pg_temp.poly_domain AS integer;
                CREATE TYPE pg_temp.poly_pair AS (number integer, text text);
                """, connection);
            await command.ExecuteNonQueryAsync(token);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Reads one exact SQL result from the current backend.
    /// </summary>
    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }
}
