using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies full PostgreSQL diagnostics through native capture, managed ownership, and native rethrow.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class PgDiagnosticTests(TestContext context)
{
    /// <summary>
    /// Verifies object and source fields survive recovery, later callbacks, and a transaction boundary.
    /// </summary>
    [TestMethod]
    public async Task NativeObjectDiagnosticsSurviveCatchAndRethrow()
    {
        const string sql = """
            DO $$ BEGIN
                RAISE EXCEPTION USING ERRCODE = '23514', MESSAGE = 'bad café', DETAIL = 'détail', HINT = 'retry 🐘',
                    SCHEMA = 'schéma', TABLE = 'table 🐘', COLUMN = 'cölumn', DATATYPE = 'custom_type', CONSTRAINT = 'check_value';
            END $$
            """;
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var direct = new NpgsqlCommand(sql, connection);
        PostgresException original = await Assert.ThrowsExactlyAsync<PostgresException>(() => direct.ExecuteNonQueryAsync(token));
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await using var cache = new NpgsqlCommand("SELECT datatype.diagnostic_cache($1)", connection, transaction);
            cache.Parameters.AddWithValue(sql);
            Assert.AreEqual("23514", await cache.ExecuteScalarAsync(token));
            await transaction.CommitAsync(token);
        }

        await AssertFieldsAsync(connection, token,
            ("message", "bad café"), ("detail", "détail"), ("hint", "retry 🐘"), ("schema", "schéma"),
            ("table", "table 🐘"), ("column", "cölumn"), ("datatype", "custom_type"), ("constraint", "check_value"),
            ("file", original.File), ("line", original.Line), ("routine", original.Routine), ("incomplete", "false"));
        string capturedContext = Assert.IsInstanceOfType<string>(await ReadFieldAsync(connection, "context", token));
        Assert.Contains("inline_code_block line 2 at RAISE", capturedContext);
        direct.CommandText = "SELECT datatype.diagnostic_rethrow()";
        PostgresException rethrown = await Assert.ThrowsExactlyAsync<PostgresException>(() => direct.ExecuteNonQueryAsync(token));
        AssertMetadataEqual(original, rethrown);
        Assert.AreEqual(capturedContext, rethrown.Where);
        direct.CommandText = "SELECT datatype.spi_int(42)";
        Assert.AreEqual(42, await direct.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Verifies SPI positions identify characters in the internal query rather than bytes or UTF-16 code units.
    /// </summary>
    [TestMethod]
    public async Task SyntaxPositionPreservesUnicodeQueryAndOriginalLocation()
    {
        const string sql = "SELECT '🐘é', missing_column";
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand(sql, connection);
        PostgresException original = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual(14, original.Position);
        command.CommandText = "SELECT datatype.diagnostic_cache($1)";
        command.Parameters.AddWithValue(sql);
        Assert.AreEqual(PostgresErrorCodes.UndefinedColumn, await command.ExecuteScalarAsync(token));
        await AssertFieldsAsync(connection, token, ("position", "0"), ("internalPosition", "14"), ("query", sql),
            ("file", original.File), ("routine", original.Routine), ("line", original.Line));
        command.Parameters.Clear();
        command.CommandText = "SELECT datatype.diagnostic_rethrow()";
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual(PostgresErrorCodes.UndefinedColumn, error.SqlState);
        Assert.AreEqual(0, error.Position);
        Assert.AreEqual(14, error.InternalPosition);
        Assert.AreEqual(sql, error.InternalQuery);
        Assert.AreEqual(original.File, error.File);
        Assert.AreEqual(original.Line, error.Line);
        Assert.AreEqual(original.Routine, error.Routine);
    }

    /// <summary>
    /// Verifies the actual server constraint diagnostics, including nullable object fields, reach managed code.
    /// </summary>
    [TestMethod]
    public async Task ConstraintViolationPreservesCatalogMetadata()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand("""
            CREATE TEMP TABLE diagnostic_values (value int CONSTRAINT diagnostic_unique UNIQUE);
            INSERT INTO diagnostic_values VALUES (1)
            """, connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "INSERT INTO diagnostic_values VALUES (1)";
        PostgresException original = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        command.CommandText = "SELECT datatype.diagnostic_cache('INSERT INTO diagnostic_values VALUES (1)')";
        Assert.AreEqual(PostgresErrorCodes.UniqueViolation, await command.ExecuteScalarAsync(token));
        await AssertFieldsAsync(connection, token, ("schema", original.SchemaName), ("table", "diagnostic_values"),
            ("constraint", "diagnostic_unique"), ("column", null), ("datatype", null), ("hint", null),
            ("detail", "Key (value)=(1) already exists."), ("routine", original.Routine));
        command.CommandText = "SELECT datatype.diagnostic_rethrow()";
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        AssertMetadataEqual(original, error);
        command.CommandText = "SELECT array_agg(value ORDER BY value) FROM diagnostic_values";
        Assert.AreSequenceEqual<int>([1], Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token)));
    }

    /// <summary>
    /// Verifies managed errors deliver every client diagnostic without truncating long multibyte strings.
    /// </summary>
    /// <param name="repetitions">The number of multibyte text fragments.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2000)]
    public async Task ManagedDiagnosticsReachClientWithoutTruncation(int repetitions)
    {
        string text = string.Concat(Enumerable.Repeat("é🐘", repetitions));
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand("SELECT datatype.diagnostic_report($1)", connection);
        command.Parameters.AddWithValue(text);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual("22023", error.SqlState);
        Assert.AreEqual(text, error.MessageText);
        Assert.AreEqual("detail " + text, error.Detail);
        Assert.AreEqual("hint " + text, error.Hint);
        Assert.AreEqual("context " + text, error.Where);
        Assert.AreEqual("schéma", error.SchemaName);
        Assert.AreEqual("table 🐘", error.TableName);
        Assert.AreEqual("cölumn", error.ColumnName);
        Assert.AreEqual("custom_type", error.DataTypeName);
        Assert.AreEqual("check_value", error.ConstraintName);
        Assert.AreEqual(7, error.Position);
        Assert.AreEqual(5, error.InternalPosition);
        Assert.AreEqual("SELECT value", error.InternalQuery);
        Assert.AreEqual("diagnostic.cs", error.File);
        Assert.AreEqual("42", error.Line);
        Assert.AreEqual("ReportDiagnostic", error.Routine);
    }

    /// <summary>
    /// Verifies native messages, details, and hints larger than the emergency buffer retain all their Unicode text.
    /// </summary>
    [TestMethod]
    public async Task LongNativeDiagnosticsAreNotTruncated()
    {
        string text = string.Concat(Enumerable.Repeat("é🐘", 2000));
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand("SELECT datatype.diagnostic_cache($1)", connection);
        command.Parameters.AddWithValue("""
            DO $$ BEGIN RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = repeat('é🐘', 2000),
                DETAIL = 'detail ' || repeat('é🐘', 2000), HINT = 'hint ' || repeat('é🐘', 2000); END $$
            """);
        Assert.AreEqual("22023", await command.ExecuteScalarAsync(token));
        await AssertFieldsAsync(connection, token, ("message", text), ("detail", "detail " + text),
            ("hint", "hint " + text), ("incomplete", "false"));
        command.Parameters.Clear();
        command.CommandText = "SELECT datatype.diagnostic_rethrow()";
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual(text, error.MessageText);
        Assert.AreEqual("detail " + text, error.Detail);
        Assert.AreEqual("hint " + text, error.Hint);
    }

    /// <summary>
    /// Verifies server-only detail and backtrace remain separate from client detail through recursive dispatch.
    /// </summary>
    [TestMethod]
    public async Task ServerOnlyDiagnosticsRemainSeparateFromClientDetail()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand(
            "SELECT datatype.diagnostic_cache('SELECT datatype.diagnostic_report(''café 🐘'')')", connection);
        Assert.AreEqual("22023", await command.ExecuteScalarAsync(token));
        await AssertFieldsAsync(connection, token, ("detail", "detail café 🐘"), ("detailLog", "server only café 🐘"),
            ("backtrace", "native backtrace café 🐘"), ("file", "diagnostic.cs"), ("line", "42"), ("routine", "ReportDiagnostic"));
        command.CommandText = "SELECT datatype.diagnostic_rethrow()";
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual("detail café 🐘", error.Detail);
        Assert.AreEqual("diagnostic.cs", error.File);
        Assert.AreEqual("42", error.Line);
    }

    /// <summary>
    /// Verifies an outer procedural context appears once even when an error crosses several native/managed guards.
    /// </summary>
    [TestMethod]
    public async Task RecursiveRethrowDoesNotDuplicateContextFrames()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand("""
            CREATE FUNCTION pg_temp.diagnostic_outer() RETURNS void LANGUAGE plpgsql AS $body$
            BEGIN
                PERFORM datatype.execute_sql('SELECT datatype.execute_sql(''SELECT 1 / 0'')');
            END
            $body$
            """, connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT pg_temp.diagnostic_outer()";
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual(PostgresErrorCodes.DivisionByZero, error.SqlState);
        Assert.IsNotNull(error.Where);
        Assert.ContainsSingle(error.Where.Split('\n').Where(static line => line.Contains("diagnostic_outer()", StringComparison.Ordinal)));
        Assert.AreEqual("int4div", error.Routine);
        Assert.AreEqual("int.c", error.File);
    }

    /// <summary>
    /// Verifies repeated recovery frees temporary PostgreSQL diagnostic contexts before returning to the caller.
    /// </summary>
    [TestMethod]
    public async Task RepeatedFailuresReleaseDiagnosticContexts()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand("SELECT datatype.diagnostic_repeat(1000)", connection);
        Assert.AreEqual(1000, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT datatype.spi_int(42)";
        Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Verifies explicitly empty native diagnostics do not become absent during capture or rethrow.
    /// </summary>
    [TestMethod]
    public async Task EmptyNativeDiagnosticsRemainPresent()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand("SELECT datatype.diagnostic_cache($1)", connection);
        command.Parameters.AddWithValue("DO $$ BEGIN RAISE EXCEPTION USING MESSAGE = 'message', DETAIL = '', HINT = ''; END $$");
        Assert.AreEqual("P0001", await command.ExecuteScalarAsync(token));
        await AssertFieldsAsync(connection, token, ("detail", ""), ("hint", ""), ("schema", null), ("constraint", null));
        command.Parameters.Clear();
        command.CommandText = "SELECT datatype.diagnostic_rethrow()";
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual(string.Empty, error.Detail);
        Assert.AreEqual(string.Empty, error.Hint);
        Assert.IsNull(error.SchemaName);
    }

    /// <summary>
    /// Verifies every diagnostic crosses a non-UTF-8 server boundary and failed context conversion releases managed buffers.
    /// </summary>
    [TestMethod]
    public async Task Latin1DiagnosticsAndEncodingFailurePreserveBackend()
    {
        CancellationToken token = context.CancellationToken;
        string database = "diagnostic_latin1_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection administrator = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using (var create = new NpgsqlCommand(
            $"CREATE DATABASE {database} TEMPLATE template0 ENCODING 'LATIN1' LC_COLLATE 'C' LC_CTYPE 'C'", administrator))
        {
            await create.ExecuteNonQueryAsync(token);
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(administrator.ConnectionString) { Database = database };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(token);
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand("CREATE EXTENSION ankus_test", connection);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT diagnostic_cache('SELECT diagnostic_encoding(false)')";
            Assert.AreEqual("22023", await command.ExecuteScalarAsync(token));
            command.CommandText = """
                SELECT diagnostic_read('schema') || '|' || diagnostic_read('table') || '|' || diagnostic_read('column') ||
                    '|' || diagnostic_read('datatype') || '|' || diagnostic_read('constraint')
                """;
            Assert.AreEqual("schéma|tablë|cölumn|typé|consträint", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT diagnostic_rethrow()";
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual("message café", error.MessageText);
            Assert.AreEqual("détail", error.Detail);
            Assert.AreEqual("réessayer", error.Hint);
            Assert.StartsWith("context café", error.Where);
            Assert.AreEqual("schéma", error.SchemaName);
            Assert.AreEqual("tablë", error.TableName);
            Assert.AreEqual("cölumn", error.ColumnName);
            Assert.AreEqual("typé", error.DataTypeName);
            Assert.AreEqual("consträint", error.ConstraintName);
            Assert.AreEqual("encoding.cs", error.File);
            Assert.AreEqual("73", error.Line);
            Assert.AreEqual("ReportEncoding", error.Routine);

            command.CommandText = "SELECT diagnostic_encoding(true)";
            for (int index = 0; index < 20; index++)
            {
                error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
                Assert.AreEqual(PostgresErrorCodes.UntranslatableCharacter, error.SqlState);
            }

            command.CommandText = "SELECT spi_int(42)";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", administrator);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static async Task<object?> ReadFieldAsync(NpgsqlConnection connection, string field, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT datatype.diagnostic_read($1)", connection);
        command.Parameters.AddWithValue(field);
        return await command.ExecuteScalarAsync(token);
    }

    private static async Task AssertFieldsAsync(
        NpgsqlConnection connection, CancellationToken token, params (string Field, string? Value)[] fields)
    {
        foreach ((string field, string? value) in fields)
        {
            Assert.AreEqual((object?)value ?? DBNull.Value, await ReadFieldAsync(connection, field, token), field);
        }
    }

    private static void AssertMetadataEqual(PostgresException expected, PostgresException actual)
    {
        Assert.AreEqual(expected.SqlState, actual.SqlState);
        Assert.AreEqual(expected.MessageText, actual.MessageText);
        Assert.AreEqual(expected.Detail, actual.Detail);
        Assert.AreEqual(expected.Hint, actual.Hint);
        Assert.AreEqual(expected.SchemaName, actual.SchemaName);
        Assert.AreEqual(expected.TableName, actual.TableName);
        Assert.AreEqual(expected.ColumnName, actual.ColumnName);
        Assert.AreEqual(expected.DataTypeName, actual.DataTypeName);
        Assert.AreEqual(expected.ConstraintName, actual.ConstraintName);
        Assert.AreEqual(expected.File, actual.File);
        Assert.AreEqual(expected.Line, actual.Line);
        Assert.AreEqual(expected.Routine, actual.Routine);
    }
}
