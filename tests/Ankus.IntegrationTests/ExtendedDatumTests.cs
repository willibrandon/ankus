using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies generated UUID and JSON boundaries against PostgreSQL's binary, storage, encoding, and SPI semantics.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class ExtendedDatumTests(TestContext context)
{
    /// <summary>
    /// Verifies UUIDs, SQL NULL, exact JSON text, and normalized JSONB across all SPI ownership paths.
    /// </summary>
    /// <param name="mode">The direct, scalar, prepared-plan, session, or cursor conversion path.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public Task ExtendedTypesSurviveEverySpiPath(int mode)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ExtendedTypesSurviveEverySpiPath),
            async (connection, transaction, token) =>
            {
                const string json = " { \"b\":1, \"a\":123456789012345678901234567890.00100, \"b\":\"café 🐘\" } ";
                Guid uuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
                await using var command = new NpgsqlCommand("""
                    SELECT datatype.exchange_uuid($1, $3), datatype.exchange_json($2::json, $3)::text,
                           datatype.exchange_jsonb($2::jsonb, $3)::text,
                           datatype.exchange_uuid(NULL, $3) IS NULL, datatype.exchange_json(NULL, $3) IS NULL,
                           datatype.exchange_jsonb(NULL, $3) IS NULL,
                           datatype.exchange_json('null'::json, $3)::text,
                           datatype.exchange_jsonb('null'::jsonb, $3)::text
                    """, connection, transaction);
                command.Parameters.AddWithValue(uuid);
                command.Parameters.AddWithValue(json);
                command.Parameters.AddWithValue(mode);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(uuid, reader.GetGuid(0));
                Assert.AreEqual(json, reader.GetString(1));
                Assert.AreEqual("{\"a\": 123456789012345678901234567890.00100, \"b\": \"café 🐘\"}", reader.GetString(2));
                Assert.IsTrue(reader.GetBoolean(3));
                Assert.IsTrue(reader.GetBoolean(4));
                Assert.IsTrue(reader.GetBoolean(5));
                Assert.AreEqual("null", reader.GetString(6));
                Assert.AreEqual("null", reader.GetString(7));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies UUID byte order independently in both directions, avoiding a symmetric round-trip masking the error.
    /// </summary>
    /// <param name="text">The UUID's canonical spelling.</param>
    [TestMethod]
    [DataRow("00112233-4455-6677-8899-aabbccddeeff")]
    [DataRow("00000000-0000-0000-0000-000000000000")]
    [DataRow("ffffffff-ffff-ffff-ffff-ffffffffffff")]
    public Task UuidUsesNetworkByteOrder(string text)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(UuidUsesNetworkByteOrder),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("""
                    SELECT datatype.uuid_text($1::uuid), encode(uuid_send(datatype.uuid_from_text($1)), 'hex')
                    """, connection, transaction);
                command.Parameters.AddWithValue(text);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(text, reader.GetString(0));
                Assert.AreEqual(text.Replace("-", string.Empty, StringComparison.Ordinal), reader.GetString(1));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies scalar JSON values, JSON null, unbounded numeric text, and nullable/strict function contracts.
    /// </summary>
    /// <param name="expression">The SQL expression.</param>
    /// <param name="expected">The expected text or SQL NULL.</param>
    [TestMethod]
    [DataRow("datatype.json_default()", "null")]
    [DataRow("datatype.jsonb_default()", "null")]
    [DataRow("datatype.uuid_text(NULL)", null)]
    [DataRow("datatype.json_from_text(NULL)", null)]
    [DataRow("datatype.jsonb_from_text(NULL)", null)]
    [DataRow("datatype.json_from_text('1e1000000')", "1e1000000")]
    [DataRow("datatype.json_from_text('\"\\u0000\"')", "\"\\u0000\"")]
    [DataRow("datatype.exchange_json('[]', 1)", "[]")]
    [DataRow("datatype.exchange_jsonb('{}', 2)", "{}")]
    [DataRow("datatype.exchange_jsonb('false', 3)", "false")]
    [DataRow("datatype.exchange_jsonb('\"café\"', 4)", "\"café\"")]
    public Task JsonValuesPreserveTypeSemantics(string expression, string? expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(JsonValuesPreserveTypeSemantics),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand($"SELECT ({expression})::text", connection, transaction);
                Assert.AreEqual((object?)expected ?? DBNull.Value, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies AOT source-generated serialization embeds JSON fields rather than quoting their contents or writing wrapper objects.
    /// </summary>
    [TestMethod]
    public Task SourceGeneratedJsonContractsRunInsideNativeAot()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SourceGeneratedJsonContractsRunInsideNativeAot),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("""
                    SELECT datatype.json_envelope_transform(
                        '{"Id":"00000000-0000-0000-0000-000000000000","Value":[1,"café",null],"Metadata":{"ok":true}}') =
                        '{"Id":"00112233-4455-6677-8899-aabbccddeeff","Value":[1,"café",null],"Metadata":{"ok":true}}'::jsonb
                    """, connection, transaction);
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
                command.CommandText = """
                    SELECT datatype.json_envelope_roundtrip(
                        '{"Id":"00112233-4455-6677-8899-aabbccddeeff","Value":null,"Metadata":[1,2]}')::jsonb =
                        '{"Id":"00112233-4455-6677-8899-aabbccddeeff","Value":null,"Metadata":[1,2]}'::jsonb
                    """;
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies packed short headers for both JSON formats are expanded safely before accessing native payloads.
    /// </summary>
    [TestMethod]
    public Task PackedJsonValuesUseCorrectVarlenaLayout()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(PackedJsonValuesUseCorrectVarlenaLayout),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("""
                    CREATE TEMP TABLE packed_json (j json, b jsonb);
                    INSERT INTO packed_json VALUES ('"a"', '"a"');
                    SELECT pg_column_size(j), pg_column_size(b),
                           datatype.exchange_json(j, 1)::text, datatype.exchange_jsonb(b, 1)::text FROM packed_json
                    """, connection, transaction);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(4, reader.GetInt32(0));
                Assert.AreEqual(10, reader.GetInt32(1));
                Assert.AreEqual("\"a\"", reader.GetString(2));
                Assert.AreEqual("\"a\"", reader.GetString(3));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies domain metadata resolves to the new converters and materialized cells remain owned after later SPI work.
    /// </summary>
    [TestMethod]
    public Task DomainResultsResolveBaseTypesAndOwnTheirValues()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DomainResultsResolveBaseTypesAndOwnTheirValues),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("""
                    CREATE DOMAIN pg_temp.uuid_domain AS uuid;
                    CREATE DOMAIN pg_temp.json_domain AS json;
                    CREATE DOMAIN pg_temp.jsonb_domain AS jsonb;
                    CREATE TEMP TABLE extended_domain_values (u pg_temp.uuid_domain, j pg_temp.json_domain, b pg_temp.jsonb_domain);
                    INSERT INTO extended_domain_values VALUES ('00112233-4455-6677-8899-aabbccddeeff', '[1, 2]', '{"a":1}');
                    SELECT datatype.extended_domain_values('SELECT * FROM extended_domain_values')
                    """, connection, transaction);
                Assert.AreEqual("00112233-4455-6677-8899-aabbccddeeff|[1, 2]|{\"a\": 1}",
                    await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies compressed and external stored JSON/JSONB are detoasted before managed conversion and cursor materialization.
    /// </summary>
    /// <param name="storage">The native storage mode.</param>
    [TestMethod]
    [DataRow("EXTENDED")]
    [DataRow("EXTERNAL")]
    public Task ToastedJsonValuesRetainExactContent(string storage)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ToastedJsonValuesRetainExactContent),
            async (connection, transaction, token) =>
            {
                string text = "\"" + string.Concat(Enumerable.Repeat("café 🐘 ", 8000)) + "\"";
                await using var command = new NpgsqlCommand($"""
                    CREATE TEMP TABLE json_toast (j json, b jsonb);
                    ALTER TABLE json_toast ALTER COLUMN j SET STORAGE {storage};
                    ALTER TABLE json_toast ALTER COLUMN b SET STORAGE {storage};
                    ALTER TABLE json_toast ALTER COLUMN j SET COMPRESSION pglz;
                    ALTER TABLE json_toast ALTER COLUMN b SET COMPRESSION pglz;
                    """, connection, transaction);
                await command.ExecuteNonQueryAsync(token);
                command.CommandText = "INSERT INTO json_toast VALUES ($1::json, $1::jsonb)";
                command.Parameters.AddWithValue(text);
                await command.ExecuteNonQueryAsync(token);
                command.Parameters.Clear();
                command.CommandText = storage == "EXTENDED"
                    ? "SELECT pg_column_compression(j) = 'pglz' AND pg_column_compression(b) = 'pglz' FROM json_toast"
                    : "SELECT pg_relation_size(reltoastrelid) > 0 FROM pg_class WHERE oid = 'pg_temp.json_toast'::regclass";
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
                command.CommandText = "SELECT datatype.exchange_json(j, 4)::text, datatype.exchange_jsonb(b, 4)::text FROM json_toast";
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(text, reader.GetString(0));
                Assert.AreEqual(text, reader.GetString(1));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies deep PostgreSQL JSON survives the managed parser's input path beyond its normal default depth.
    /// </summary>
    [TestMethod]
    public Task DeepJsonSurvivesNativeAndManagedBoundaries()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DeepJsonSurvivesNativeAndManagedBoundaries),
            async (connection, transaction, token) =>
            {
                string text = new string('[', 128) + "42" + new string(']', 128);
                await using var command = new NpgsqlCommand(
                    "SELECT datatype.exchange_json($1::json, 2)::text, datatype.exchange_jsonb($1::jsonb, 2)::text",
                    connection, transaction);
                command.Parameters.AddWithValue(text);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(text, reader.GetString(0));
                Assert.AreEqual(text, reader.GetString(1));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies errors in managed validation or native jsonb construction unwind cleanly and retain a usable backend.
    /// </summary>
    /// <param name="text">The invalid JSON or jsonb input.</param>
    /// <param name="state">The expected SQLSTATE.</param>
    [TestMethod]
    [DataRow("[1,]", "38000")]
    [DataRow("\"\\u0000\"", "22P05")]
    [DataRow("1e1000000", "22003")]
    [DataRow("\"\\ud800\"", "22P02")]
    public async Task JsonFailuresPreserveTheBackend(string text, string state)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand("SELECT datatype.jsonb_from_text($1)", connection);
        command.Parameters.AddWithValue(text);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual(state, error.SqlState);
        command.Parameters.Clear();
        command.CommandText = "SELECT datatype.exchange_jsonb('{\"recovered\":true}', 3)::text";
        Assert.AreEqual("{\"recovered\": true}", await command.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Verifies parameter-conversion failures remain inside the native guard and preserve successful session work.
    /// </summary>
    /// <param name="text">Syntactically valid JSON rejected by PostgreSQL jsonb.</param>
    /// <param name="expected">The diagnostic and surviving row count.</param>
    [TestMethod]
    [DataRow("\"\\u0000\"", "22P05:1")]
    [DataRow("1e1000000", "22003:1")]
    public Task JsonbParameterErrorsRecoverWithinSession(string text, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(JsonbParameterErrorsRecoverWithinSession),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.jsonb_parameter_recovery($1)", connection, transaction);
                command.Parameters.AddWithValue(text);
                Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies Latin-1 input/output conversion and PostgreSQL's stricter jsonb Unicode handling after managed return.
    /// </summary>
    [TestMethod]
    public async Task Latin1JsonConversionPreservesTextAndNativeErrors()
    {
        CancellationToken token = context.CancellationToken;
        string database = "json_latin1_" + Guid.NewGuid().ToString("N");
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
            command.CommandText = "SELECT exchange_json('\"café\"', 2)::text, exchange_jsonb('\"café\"', 4)::text";
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
            {
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual("\"café\"", reader.GetString(0));
                Assert.AreEqual("\"café\"", reader.GetString(1));
            }

            command.CommandText = "SELECT json_from_text('\"\\ud83d\\udc18\"')::text";
            Assert.AreEqual("\"\\ud83d\\udc18\"", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT jsonb_from_text('\"\\ud83d\\udc18\"')";
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual(PostgresErrorCodes.UntranslatableCharacter, error.SqlState);
            command.CommandText = "SELECT exchange_jsonb('\"récupéré\"', 3)::text";
            Assert.AreEqual("\"récupéré\"", await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", administrator);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
