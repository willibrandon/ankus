using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies typed SPI parameters, result ownership, metadata, scalar semantics, and command modes in PostgreSQL.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class SpiQueryTests(TestContext context)
{
    /// <summary>
    /// Verifies exact primitive values and NULL survive both directions of parameterized SPI conversion.
    /// </summary>
    /// <param name="expression">The native-backed expression.</param>
    /// <param name="expected">Its exact SQL text representation, or null.</param>
    [TestMethod]
    [DataRow("datatype.spi_boolean(false)", "false")]
    [DataRow("datatype.spi_boolean(NULL)", null)]
    [DataRow("datatype.spi_char((-128)::\"char\")::integer", "-128")]
    [DataRow("datatype.spi_small_int((-32768)::smallint)", "-32768")]
    [DataRow("datatype.spi_int(-2147483648)", "-2147483648")]
    [DataRow("datatype.spi_int(NULL)", null)]
    [DataRow("datatype.spi_big_int(9223372036854775807)", "9223372036854775807")]
    [DataRow("datatype.spi_oid(4294967295::oid)", "4294967295")]
    [DataRow("datatype.spi_real(1.25::real)", "1.25")]
    [DataRow("datatype.spi_double(-1234.125)", "-1234.125")]
    [DataRow("datatype.spi_real('NaN'::real)", "NaN")]
    [DataRow("datatype.spi_double('-Infinity'::float8)", "-Infinity")]
    [DataRow("encode(float4send(datatype.spi_real('-0'::real)), 'hex')", "80000000")]
    [DataRow("encode(float8send(datatype.spi_double('-0'::float8)), 'hex')", "8000000000000000")]
    [DataRow("datatype.spi_text('café 🐘')", "café 🐘")]
    [DataRow("datatype.spi_text('')", "")]
    [DataRow("datatype.spi_text(NULL)", null)]
    [DataRow("encode(datatype.spi_bytes(decode('00ff007f', 'hex')), 'hex')", "00ff007f")]
    [DataRow("encode(datatype.spi_bytes(''::bytea), 'hex')", "")]
    [DataRow("datatype.spi_bytes(NULL)", null)]
    [DataRow("datatype.spi_parameter_types()", "boolean,\"char\",smallint,integer,bigint,oid,real,double precision,text,bytea")]
    public Task ParametersPreserveValuesAndDeclaredTypes(string expression, string? expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ParametersPreserveValuesAndDeclaredTypes),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand($"SELECT ({expression})::text", connection, transaction);
                Assert.AreEqual((object?)expected ?? DBNull.Value, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies copied rows preserve order and SQL NULL after subsequent SPI activity.
    /// </summary>
    [TestMethod]
    public Task MaterializedRowsSurviveSubsequentSpiCalls()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(MaterializedRowsSurviveSubsequentSpiCalls),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.spi_text_rows($1)", connection, transaction);
                command.Parameters.AddWithValue("SELECT value FROM (VALUES (1, 'café 🐘'), (2, NULL), (3, '')) AS v(n, value) ORDER BY n");
                Assert.AreEqual("café 🐘|<null>|", await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies empty results retain column metadata and preserve quoted identifier names.
    /// </summary>
    [TestMethod]
    public Task EmptyResultsRetainMetadata()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(EmptyResultsRetainMetadata),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.spi_metadata($1)", connection, transaction);
                command.Parameters.AddWithValue("SELECT 1 AS \"Mixed Case\", 'x'::text AS \"café\", 2::bigint AS number WHERE false");
                Assert.AreEqual("Mixed Case:23|café:25|number:20", await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies column-name lookup uses the first exact name when PostgreSQL returns duplicates.
    /// </summary>
    [TestMethod]
    public Task DuplicateNamesResolveToFirstColumn()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DuplicateNamesResolveToFirstColumn),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.spi_named_value($1, 'Value')", connection, transaction);
                command.Parameters.AddWithValue("SELECT 7 AS \"Value\", 9 AS \"Value\", 11 AS value");
                Assert.AreEqual(7, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies row limits and an empty scalar result are distinct from a numeric default.
    /// </summary>
    /// <param name="expression">The result operation.</param>
    /// <param name="expected">The expected integer or SQL NULL.</param>
    [TestMethod]
    [DataRow("datatype.spi_row_count('SELECT generate_series(1, 5)', true, 2)", 2)]
    [DataRow("datatype.spi_row_count('SELECT generate_series(1, 5)', false, 0)", 5)]
    [DataRow("datatype.spi_scalar('SELECT 0')", 0)]
    [DataRow("datatype.spi_scalar('SELECT 7 WHERE false')", null)]
    [DataRow("datatype.spi_scalar('SELECT NULL::integer')", null)]
    [DataRow("datatype.spi_scalar('SELECT 7, ARRAY[1,2,3]')", 7)]
    [DataRow("datatype.spi_required_scalar('SELECT 7')", 7)]
    [DataRow("datatype.spi_scalar('SELECT FROM generate_series(1, 2)')", null)]
    [DataRow("datatype.spi_row_count('SELECT FROM generate_series(1, 2)', false, 0)", 2)]
    public Task ScalarAndRowLimitSemanticsArePreserved(string expression, int? expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ScalarAndRowLimitSemanticsArePreserved),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand($"SELECT {expression}", connection, transaction);
                Assert.AreEqual((object?)expected ?? DBNull.Value, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies scalar reads do not limit the write effects of INSERT RETURNING.
    /// </summary>
    [TestMethod]
    public Task ExecuteScalarCompletesAllWriteEffects()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ExecuteScalarCompletesAllWriteEffects),
            async (connection, transaction, token) =>
            {
                await using (var setup = new NpgsqlCommand("CREATE TEMP TABLE spi_scalar_writes (value integer)", connection, transaction))
                {
                    await setup.ExecuteNonQueryAsync(token);
                }

                await using (var command = new NpgsqlCommand("SELECT datatype.spi_scalar($1)", connection, transaction))
                {
                    command.Parameters.AddWithValue("INSERT INTO spi_scalar_writes SELECT generate_series(1, 5) RETURNING value");
                    Assert.AreEqual(1, await command.ExecuteScalarAsync(token));
                }

                await using var count = new NpgsqlCommand("SELECT count(*) FROM spi_scalar_writes", connection, transaction);
                Assert.AreEqual(5L, await count.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies bound text and binary values remain data, including quotes, SQL syntax, Unicode, and zero bytes.
    /// </summary>
    [TestMethod]
    public Task BoundParametersRemainData()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(BoundParametersRemainData),
            async (connection, transaction, token) =>
            {
                const string text = "'); DROP TABLE spi_parameters; -- café 🐘";
                byte[] bytes = [0, 255, 127, 0];
                await using (var setup = new NpgsqlCommand(
                    "CREATE TEMP TABLE spi_parameters (value text, bytes bytea)", connection, transaction))
                {
                    await setup.ExecuteNonQueryAsync(token);
                }

                await using (var insert = new NpgsqlCommand("SELECT datatype.spi_insert($1, $2)", connection, transaction))
                {
                    insert.Parameters.AddWithValue(text);
                    insert.Parameters.AddWithValue(bytes);
                    Assert.AreEqual(1L, await insert.ExecuteScalarAsync(token));
                }

                await using var query = new NpgsqlCommand("SELECT value, bytes FROM spi_parameters", connection, transaction);
                await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(text, reader.GetString(0));
                Assert.AreSequenceEqual(bytes, reader.GetFieldValue<byte[]>(1));
                Assert.IsFalse(await reader.ReadAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies domains use their base conversion while retaining the declared domain OID in column metadata.
    /// </summary>
    [TestMethod]
    public Task DomainResultsPreserveDeclaredType()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DomainResultsPreserveDeclaredType),
            async (connection, transaction, token) =>
            {
                const string sql = """
                    CREATE DOMAIN pg_temp.spi_positive AS integer CHECK (VALUE > 0);
                    SELECT datatype.spi_scalar('SELECT 42::pg_temp.spi_positive'),
                        datatype.spi_metadata('SELECT 42::pg_temp.spi_positive AS item'),
                        'pg_temp.spi_positive'::regtype::oid::text
                    """;
                await using var command = new NpgsqlCommand(sql, connection, transaction);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(42, reader.GetInt32(0));
                Assert.AreEqual("item:" + reader.GetString(2), reader.GetString(1));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies errors partway through native result copying roll back writes and allow another managed SPI call.
    /// </summary>
    [TestMethod]
    public Task UnregisteredResultTypeRollsBackCommand()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(UnregisteredResultTypeRollsBackCommand),
            async (connection, transaction, token) =>
            {
                const string setupSql = """
                    CREATE TYPE pg_temp.unregistered_spi AS ENUM ('value');
                    CREATE TEMP TABLE spi_result_rollback (value text)
                    """;
                await using (var setup = new NpgsqlCommand(setupSql, connection, transaction))
                {
                    await setup.ExecuteNonQueryAsync(token);
                }

                await using (var command = new NpgsqlCommand("SELECT datatype.spi_recover_result_error($1)", connection, transaction))
                {
                    command.Parameters.AddWithValue("""
                        INSERT INTO spi_result_rollback VALUES (repeat('buffer', 1000))
                        RETURNING value, 'value'::pg_temp.unregistered_spi
                        """);
                    Assert.AreEqual("0A000:42", await command.ExecuteScalarAsync(token));
                }

                await using var count = new NpgsqlCommand("SELECT count(*) FROM spi_result_rollback", connection, transaction);
                Assert.AreEqual(0L, await count.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies native query errors and managed result-contract errors propagate with the expected SQLSTATE.
    /// </summary>
    /// <param name="expression">The failing managed query operation.</param>
    /// <param name="state">The expected SQLSTATE.</param>
    /// <param name="message">The distinctive error diagnostic.</param>
    [TestMethod]
    [DataRow("datatype.spi_required_scalar('SELECT NULL::integer')", "38000", "SQL NULL cannot be read")]
    [DataRow("datatype.spi_required_scalar('SELECT 1::bigint')", "38000", "cannot be read as 'System.Int32'")]
    [DataRow("datatype.spi_named_value('SELECT 1 AS name', 'Name')", "38000", "no column named 'Name'")]
    [DataRow("datatype.spi_row_count('CREATE TEMP TABLE forbidden_spi (n int)', true, 0)", "0A000", "not allowed")]
    public async Task QueryErrorsPreserveBackend(string expression, string state, string message)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand($"SELECT {expression}", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(
            () => command.ExecuteScalarAsync(context.CancellationToken));
        Assert.AreEqual(state, error.SqlState);
        Assert.Contains(message, error.MessageText);

        await using var recovery = new NpgsqlCommand("SELECT datatype.spi_int(42)", connection);
        Assert.AreEqual(42, await recovery.ExecuteScalarAsync(context.CancellationToken));
        Assert.AreEqual(backend, connection.ProcessID);
    }
}
