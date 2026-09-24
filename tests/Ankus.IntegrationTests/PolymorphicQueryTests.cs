using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Proves typed query and function-call results preserve their real types and native lifetimes.
/// </summary>
public sealed partial class PolymorphicTests
{
    /// <summary>
    /// Preserves nullable, unmapped, domain, record, and large values through every typed result entry point.
    /// </summary>
    /// <param name="mode">The scalar, tuple, plan, session, or catalog call entry point.</param>
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
    [DataRow(9)]
    [DataRow(10)]
    [DataRow(11)]
    public async Task TypedQueryResultsPreserveIdentityAfterExecutionOwnersEnd(int mode)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        string[] expressions = ["0::integer", "42::pg_temp.poly_domain", "'owned'::pg_temp.poly_enum",
            "repeat('héllo 🐘',10000)", "ROW(42,repeat('owned',10000))::pg_temp.poly_pair",
            "'[2:3][-1:0]={{1,NULL},{3,4}}'::integer[]", "NULL::text", "NULL::pg_temp.poly_domain"];
        foreach (string expression in expressions)
        {
            await using var command = new NpgsqlCommand($"""
                WITH input AS (SELECT {expression} AS value)
                SELECT datatype.poly_query_value(value, {mode})::text, value::text,
                    pg_typeof(datatype.poly_query_value(value, {mode}))::oid, pg_typeof(value)::oid
                FROM input
                """, connection);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(reader.GetValue(1), reader.GetValue(0), expression);
            Assert.AreEqual(reader.GetFieldValue<uint>(3), reader.GetFieldValue<uint>(2), expression);
        }
    }

    /// <summary>
    /// Preserves empty arrays, unusual bounds, element identities, NULLs, and eager cells after temporary owners close.
    /// </summary>
    /// <param name="mode">The typed result API.</param>
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
    [DataRow(9)]
    [DataRow(10)]
    [DataRow(11)]
    public async Task TypedArrayResultsPreserveShapeAndCellsAfterExecutionOwnersEnd(int mode)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await using (var setup = new NpgsqlCommand("CREATE DOMAIN pg_temp.array_domain AS integer[]", connection))
        {
            await setup.ExecuteNonQueryAsync(token);
        }

        string[] expressions = ["'{}'::integer[]", "'[2:3][-1:0]={{1,NULL},{3,4}}'::integer[]",
            "ARRAY['owned',NULL,'other']::pg_temp.poly_enum[]", "ARRAY[42,NULL]::pg_temp.poly_domain[]",
            "ARRAY[ROW(42,repeat('owned',10000)),NULL]::pg_temp.poly_pair[]",
            "ARRAY[42,NULL]::pg_temp.array_domain", "NULL::integer[]"];
        foreach (string expression in expressions)
        {
            await using var command = new NpgsqlCommand($"""
                WITH input AS (SELECT {expression} AS value)
                SELECT datatype.poly_array_shape(datatype.poly_query_array(value, {mode})),
                    datatype.poly_array_shape(value), pg_typeof(datatype.poly_query_array(value, {mode}))::oid,
                    pg_typeof(datatype.poly_array_identity(value))::oid
                FROM input
                """, connection);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(reader.GetValue(1), reader.GetValue(0), expression);
            Assert.AreEqual(reader.GetFieldValue<uint>(3), reader.GetFieldValue<uint>(2), expression);
        }

        await using var domainCommand = new NpgsqlCommand($"""
            SELECT datatype.poly_query_array_info(ARRAY[42,NULL]::pg_temp.array_domain,{mode}),
                'pg_temp.array_domain'::regtype::oid
            """, connection);
        await using NpgsqlDataReader domainReader = await domainCommand.ExecuteReaderAsync(token);
        Assert.IsTrue(await domainReader.ReadAsync(token));
        Assert.AreEqual(domainReader.GetFieldValue<uint>(1) + "|23|1|2|1:2|42;NULL", domainReader.GetString(0));
    }

    /// <summary>
    /// Query and catalog results remain live across iterator calls and early termination.
    /// </summary>
    /// <param name="mode">The query or catalog call result path.</param>
    /// <param name="materialize">Whether execution resets a temporary row context between advances.</param>
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(10, false)]
    [DataRow(0, true)]
    [DataRow(10, true)]
    public async Task TypedQueryResultsSurviveIteratorAdvances(int mode, bool materialize)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        string function = materialize ? "poly_query_materialized" : "poly_query_repeat";
        Assert.AreEqual(300000L, await ScalarAsync<long>(connection,
            $"SELECT sum(length(value)) FROM datatype.{function}(repeat('x',10000),{mode},30) value", token));
        Assert.AreEqual(10000, await ScalarAsync<int>(connection,
            $"SELECT length(value) FROM datatype.{function}(repeat('x',10000),{mode},100) value LIMIT 1", token));
        Assert.AreEqual(3L, await ScalarAsync<long>(connection,
            $"SELECT count(*) FROM datatype.{function}(NULL::text,{mode},3) value WHERE value IS NULL", token));
    }

    /// <summary>
    /// Shares raw result ownership, rejects disposed and reset values, and retains explicit copies and managed reads.
    /// </summary>
    /// <param name="cursor">Whether the owner is a fetched cursor batch.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TypedRawRowsRejectExpiredOwnersAndRetainCopies(bool cursor)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        string first = new('x', 10000);
        string expected = $"42|{first}|25|1|3|-2:3|{first};NULL;last";
        Assert.AreEqual(expected, await ScalarAsync<string>(connection,
            $"SELECT datatype.poly_query_ownership(('[-2:0]={{' || repeat('x',10000) || ',NULL,last}}')::text[], {cursor})", token));
        Assert.AreEqual(42, await ScalarAsync<int>(connection, "SELECT 40 + 2", token));
    }

    /// <summary>
    /// Preserves first-row NULL, empty, final-statement, missing-column, and exact ordinary-type contracts.
    /// </summary>
    /// <param name="sql">The query under test.</param>
    /// <param name="columns">The requested count.</param>
    /// <param name="expected">The value or exact failure.</param>
    [TestMethod]
    [DataRow("SELECT NULL::text", 1, "NULL")]
    [DataRow("SELECT 42 WHERE false", 1, "NULL")]
    [DataRow("SELECT FROM generate_series(1,1)", 1, "NULL")]
    [DataRow("SELECT 'first'::text; SELECT 'last'::text", 1, "last")]
    [DataRow("SELECT 42, 73 WHERE false", 2, "NULL")]
    [DataRow("SELECT NULL::integer, 73", 2, "NULL")]
    [DataRow("SELECT 42", 2, "The SPI result has 1 columns; column 2 was requested.")]
    [DataRow("SELECT 42, 'wrong'::text", 2, "InvalidCastException")]
    [DataRow("SELECT 73, 'owned'::text, 'owned'::pg_temp.poly_enum", 3, "owned")]
    [DataRow("SELECT 73, 'owned'::text, 42 WHERE false", 3, "NULL")]
    [DataRow("SELECT 73, 'owned'::text", 3, "The SPI result has 2 columns; column 3 was requested.")]
    [DataRow("SELECT 73, 'owned'::pg_temp.poly_enum", 4, "owned")]
    [DataRow("SELECT 73, ARRAY[1,2], 'owned'::text", 5, "{1,2}")]
    public async Task PolymorphicFirstRowsPreserveExistingScalarContracts(string sql, int columns, string expected)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await using var command = new NpgsqlCommand("SELECT datatype.poly_query_contract($1,$2)", connection);
        command.Parameters.AddWithValue(sql);
        command.Parameters.AddWithValue(columns);
        Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
        Assert.AreEqual(42, await ScalarAsync<int>(connection, "SELECT datatype.poly_identity(42)", token));
    }

    /// <summary>
    /// Capturing the first row must execute every write, including when concrete-column conversion fails afterward.
    /// </summary>
    [TestMethod]
    public async Task PolymorphicFirstRowsDoNotLimitWriteEffects()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await using var command = new NpgsqlCommand("CREATE TEMP TABLE poly_written(value integer)", connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT datatype.poly_query_contract($1,2)";
        command.Parameters.AddWithValue("INSERT INTO poly_written SELECT generate_series(1,7) RETURNING value, 73");
        Assert.AreEqual("1", await command.ExecuteScalarAsync(token));
        Assert.AreEqual(7L, await ScalarAsync<long>(connection, "SELECT count(*) FROM poly_written", token));
        command.Parameters[0].Value = "INSERT INTO poly_written SELECT generate_series(8,11) RETURNING value, 'wrong'::text";
        Assert.AreEqual("InvalidCastException", await command.ExecuteScalarAsync(token));
        Assert.AreEqual(11L, await ScalarAsync<long>(connection, "SELECT count(*) FROM poly_written", token));
    }

    /// <summary>
    /// Rejects wrong catalog results before side effects and preserves guarded rollback and same-backend recovery.
    /// </summary>
    [TestMethod]
    public async Task PolymorphicCallsValidateArrayTypesBeforeExecutionAndRecoverFromErrors()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await using var command = new NpgsqlCommand("""
            CREATE TEMP SEQUENCE poly_call_sequence;
            CREATE TEMP TABLE poly_call_changes(value integer);
            CREATE FUNCTION pg_temp.wrong_array() RETURNS integer LANGUAGE plpgsql AS
                $$ BEGIN PERFORM nextval('pg_temp.poly_call_sequence'); RETURN NULL; END $$;
            CREATE FUNCTION pg_temp.failed_array() RETURNS integer[] LANGUAGE plpgsql AS
                $$ BEGIN INSERT INTO poly_call_changes VALUES (1); RAISE EXCEPTION 'array failure' USING ERRCODE = 'P7911'; END $$;
            """, connection);
        await command.ExecuteNonQueryAsync(token);
        int pid = await ScalarAsync<int>(connection, "SELECT pg_backend_pid()", token);
        Assert.AreEqual("42804", await ScalarAsync<string>(connection, "SELECT datatype.poly_call_array_error('pg_temp.wrong_array')", token));
        Assert.IsFalse(await ScalarAsync<bool>(connection, "SELECT is_called FROM poly_call_sequence", token));
        Assert.AreEqual("P7911", await ScalarAsync<string>(connection, "SELECT datatype.poly_call_array_error('pg_temp.failed_array')", token));
        Assert.AreEqual(0L, await ScalarAsync<long>(connection, "SELECT count(*) FROM poly_call_changes", token));
        Assert.AreEqual(pid, await ScalarAsync<int>(connection, "SELECT pg_backend_pid()", token));
        Assert.AreEqual("{1,2}", await ScalarAsync<string>(connection, "SELECT datatype.poly_query_array(ARRAY[1,2],10)::text", token));
    }
}
