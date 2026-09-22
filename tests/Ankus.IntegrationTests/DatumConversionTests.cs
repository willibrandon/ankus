using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes generated type conversions inside PostgreSQL, including nullable values, TOAST, and character encodings.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class DatumConversionTests(TestContext context)
{
    /// <summary>
    /// Verifies exact scalar boundaries and distinct integer/bigint SQL overloads.
    /// </summary>
    /// <param name="expression">The SQL expression exercising a generated native function.</param>
    /// <param name="expected">The exact PostgreSQL textual representation.</param>
    [TestMethod]
    [DataRow("datatype.echo_boolean(false)", "false")]
    [DataRow("datatype.echo_boolean(true)", "true")]
    [DataRow("datatype.echo_char((-128)::\"char\")::integer", "-128")]
    [DataRow("datatype.echo_char(127::\"char\")::integer", "127")]
    [DataRow("datatype.echo_small_int((-32768)::smallint)", "-32768")]
    [DataRow("datatype.echo_small_int(32767::smallint)", "32767")]
    [DataRow("datatype.echo((-2147483648)::integer)", "-2147483648")]
    [DataRow("datatype.echo(2147483647::integer)", "2147483647")]
    [DataRow("datatype.echo((-9223372036854775808)::bigint)", "-9223372036854775808")]
    [DataRow("datatype.echo(9223372036854775807::bigint)", "9223372036854775807")]
    [DataRow("datatype.echo_oid(0::oid)", "0")]
    [DataRow("datatype.echo_oid(4294967295::oid)", "4294967295")]
    [DataRow("datatype.echo_real('-Infinity'::real)", "-Infinity")]
    [DataRow("datatype.echo_real('Infinity'::real)", "Infinity")]
    [DataRow("datatype.echo_real('NaN'::real)", "NaN")]
    [DataRow("datatype.echo_double('-Infinity'::float8)", "-Infinity")]
    [DataRow("datatype.echo_double('Infinity'::float8)", "Infinity")]
    [DataRow("datatype.echo_double('NaN'::float8)", "NaN")]
    [DataRow("encode(float4send(datatype.echo_real('-0'::real)), 'hex')", "80000000")]
    [DataRow("encode(float8send(datatype.echo_double('-0'::float8)), 'hex')", "8000000000000000")]
    [DataRow("datatype.echo_real(1.25::real)", "1.25")]
    [DataRow("datatype.echo_double(-1234.125::float8)", "-1234.125")]
    [DataRow("datatype.nothing() IS NULL", "false")]
    public Task ScalarValuesPreservePostgresRepresentation(string expression, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ScalarValuesPreservePostgresRepresentation),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand($"SELECT ({expression})::text", connection, transaction);
                Assert.AreEqual(expected, Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token)));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies single-precision transport preserves subnormal values, signed zero, and NaN payload bits without widening.
    /// </summary>
    /// <param name="bits">The IEEE-754 input bit pattern.</param>
    [TestMethod]
    [DataRow(1)]
    [DataRow(0x007FFFFF)]
    [DataRow(0x7F7FFFFF)]
    [DataRow(int.MinValue)]
    [DataRow(0x7FC01234)]
    [DataRow(0x7F801234)]
    public Task RealPreservesBitPattern(int bits)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RealPreservesBitPattern), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT datatype.echo_real($1)", connection, transaction);
            command.Parameters.AddWithValue(BitConverter.Int32BitsToSingle(bits));
            float result = Assert.IsInstanceOfType<float>(await command.ExecuteScalarAsync(token));
            Assert.AreEqual(bits, BitConverter.SingleToInt32Bits(result));
        }, context.CancellationToken);

    /// <summary>
    /// Verifies double-precision transport preserves subnormal values, signed zero, and NaN payload bits.
    /// </summary>
    /// <param name="bits">The IEEE-754 input bit pattern.</param>
    [TestMethod]
    [DataRow(1L)]
    [DataRow(0x000FFFFFFFFFFFFFL)]
    [DataRow(0x7FEFFFFFFFFFFFFFL)]
    [DataRow(long.MinValue)]
    [DataRow(0x7FF8123456789ABCL)]
    [DataRow(0x7FF0123456789ABCL)]
    public Task DoublePreservesBitPattern(long bits)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DoublePreservesBitPattern), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT datatype.echo_double($1)", connection, transaction);
            command.Parameters.AddWithValue(BitConverter.Int64BitsToDouble(bits));
            double result = Assert.IsInstanceOfType<double>(await command.ExecuteScalarAsync(token));
            Assert.AreEqual(bits, BitConverter.DoubleToInt64Bits(result));
        }, context.CancellationToken);

    /// <summary>
    /// Verifies nullable parameters reach managed code and non-nullable parameters propagate SQL NULL even in mixed signatures.
    /// </summary>
    /// <param name="expression">The nullable SQL expression.</param>
    /// <param name="expected">The exact text result, or null for SQL NULL.</param>
    [TestMethod]
    [DataRow("datatype.echo_optional(NULL)", null)]
    [DataRow("datatype.echo_optional(0)", "0")]
    [DataRow("datatype.echo_optional(-2147483648)", "-2147483648")]
    [DataRow("datatype.coalesce(NULL, 42)", "42")]
    [DataRow("datatype.coalesce(7, 42)", "7")]
    [DataRow("datatype.coalesce(7, NULL)", null)]
    [DataRow("datatype.coalesce(NULL, NULL)", null)]
    [DataRow("datatype.echo_optional_text(NULL)", null)]
    [DataRow("datatype.echo_optional_text('')", "")]
    [DataRow("datatype.text_or_default(NULL)", "default")]
    [DataRow("datatype.text_or_default('')", "")]
    [DataRow("datatype.concatenate(NULL, '🐘')", "<null>🐘")]
    [DataRow("datatype.concatenate('café', '🐘')", "café🐘")]
    [DataRow("datatype.concatenate('prefix', NULL)", null)]
    [DataRow("datatype.echo_optional_bytes(NULL)", null)]
    [DataRow("encode(datatype.echo_optional_bytes(''::bytea), 'hex')", "")]
    public Task NullableContractsPreserveNullAndEmpty(string expression, string? expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NullableContractsPreserveNullAndEmpty),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand($"SELECT ({expression})::text", connection, transaction);
                Assert.AreEqual((object?)expected ?? DBNull.Value, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies UTF-8 conversion and length-aware output for empty, ASCII, combining, and supplementary characters.
    /// </summary>
    /// <param name="name">The supplied SQL text.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("PostgreSQL")]
    [DataRow("café e\u0301 日本語 🐘")]
    public Task GreetingUsesGeneratedTextConversion(string name)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(GreetingUsesGeneratedTextConversion),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT public.greet($1)", connection, transaction);
                command.Parameters.AddWithValue(name);
                Assert.AreEqual($"Hello, {name}!", Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token)));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies binary zero bytes and all byte values survive a bytea round trip, including empty buffers.
    /// </summary>
    /// <param name="length">The input buffer length.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(256)]
    [DataRow(65536)]
    public Task ByteaPreservesAllBytes(int length)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ByteaPreservesAllBytes),
            async (connection, transaction, token) =>
            {
                byte[] bytes = Enumerable.Range(0, length).Select(static value => (byte)(value % 256)).ToArray();
                await using var command = new NpgsqlCommand("SELECT datatype.echo_bytes($1)", connection, transaction);
                command.Parameters.AddWithValue(bytes);
                byte[] result = Assert.IsInstanceOfType<byte[]>(await command.ExecuteScalarAsync(token));
                Assert.AreSequenceEqual(bytes, result);
            }, context.CancellationToken);

    /// <summary>
    /// Verifies one-byte varlena headers in stored short text and bytea values are handled without assuming four-byte alignment.
    /// </summary>
    [TestMethod]
    public Task PackedShortValuesRoundTrip()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(PackedShortValuesRoundTrip),
            async (connection, transaction, token) =>
            {
                const string sql = """
                    CREATE TEMP TABLE packed_values (value text, bytes bytea);
                    INSERT INTO packed_values VALUES ('🐘', decode('00ff', 'hex'));
                    SELECT pg_column_size(value), pg_column_size(bytes), datatype.echo_text(value), datatype.echo_bytes(bytes)
                    FROM packed_values
                    """;
                await using var command = new NpgsqlCommand(sql, connection, transaction);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(5, reader.GetInt32(0));
                Assert.AreEqual(3, reader.GetInt32(1));
                Assert.AreEqual("🐘", reader.GetString(2));
                Assert.AreSequenceEqual(new byte[] { 0, 255 }, reader.GetFieldValue<byte[]>(3));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies actual stored compressed and external TOAST inputs are detoasted before crossing into managed code.
    /// </summary>
    /// <param name="storage">The PostgreSQL storage mode to exercise.</param>
    [TestMethod]
    [DataRow("EXTENDED")]
    [DataRow("EXTERNAL")]
    public Task ToastedValuesAreDecodedAndCopied(string storage)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ToastedValuesAreDecodedAndCopied),
            async (connection, transaction, token) =>
            {
                string text = string.Concat(Enumerable.Repeat("PostgreSQL 🐘 café ", 4000));
                byte[] bytes = Enumerable.Range(0, 65536).Select(static value => (byte)(value % 256)).ToArray();
                string setup = $"""
                    CREATE TEMP TABLE toast_values (value text, bytes bytea);
                    ALTER TABLE toast_values ALTER COLUMN value SET STORAGE {storage};
                    ALTER TABLE toast_values ALTER COLUMN bytes SET STORAGE {storage};
                    ALTER TABLE toast_values ALTER COLUMN value SET COMPRESSION pglz;
                    ALTER TABLE toast_values ALTER COLUMN bytes SET COMPRESSION pglz;
                    """;
                await using (var configure = new NpgsqlCommand(setup, connection, transaction))
                {
                    await configure.ExecuteNonQueryAsync(token);
                }

                await using (var insert = new NpgsqlCommand("INSERT INTO toast_values VALUES ($1, $2)", connection, transaction))
                {
                    insert.Parameters.AddWithValue(text);
                    insert.Parameters.AddWithValue(bytes);
                    await insert.ExecuteNonQueryAsync(token);
                }

                string storageCheck = storage == "EXTENDED"
                    ? "SELECT pg_column_compression(value) = 'pglz' AND pg_column_compression(bytes) = 'pglz' FROM toast_values"
                    : "SELECT pg_relation_size(reltoastrelid) > 0 FROM pg_class WHERE oid = 'pg_temp.toast_values'::regclass";
                await using (var check = new NpgsqlCommand(storageCheck, connection, transaction))
                {
                    Assert.IsTrue(Assert.IsInstanceOfType<bool>(await check.ExecuteScalarAsync(token)));
                }

                await using var command = new NpgsqlCommand(
                    "SELECT datatype.echo_text(value), datatype.echo_bytes(bytes) FROM toast_values", connection, transaction);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(text, reader.GetString(0));
                Assert.AreSequenceEqual(bytes, reader.GetFieldValue<byte[]>(1));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies invalid managed text is reported as a SQL error while the same backend stays usable.
    /// </summary>
    /// <param name="function">The test function producing invalid text.</param>
    /// <param name="message">A distinctive part of the managed error.</param>
    [TestMethod]
    [DataRow("invalid_text", "zero character")]
    [DataRow("invalid_surrogate", "D800")]
    public async Task InvalidManagedTextDoesNotTerminateBackend(string function, string message)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand($"SELECT datatype.{function}()", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(
            () => command.ExecuteScalarAsync(context.CancellationToken));
        Assert.AreEqual(PostgresErrorCodes.ExternalRoutineException, error.SqlState);
        Assert.Contains(message, error.MessageText);

        await using var succeeding = new NpgsqlCommand("SELECT datatype.echo_text('still working')", connection);
        Assert.AreEqual("still working", await succeeding.ExecuteScalarAsync(context.CancellationToken));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Verifies server encoding conversion and a native PostgreSQL error after a managed output buffer has been allocated.
    /// </summary>
    [TestMethod]
    public async Task Latin1ConversionAndNativeErrorPreserveBackend()
    {
        string database = "latin1_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection administrator = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using (var create = new NpgsqlCommand(
            $"CREATE DATABASE {database} TEMPLATE template0 ENCODING 'LATIN1' LC_COLLATE 'C' LC_CTYPE 'C'", administrator))
        {
            await create.ExecuteNonQueryAsync(context.CancellationToken);
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(administrator.ConnectionString) { Database = database };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(context.CancellationToken);
            int backend = connection.ProcessID;
            await using (var install = new NpgsqlCommand("CREATE EXTENSION ankus_test", connection))
            {
                await install.ExecuteNonQueryAsync(context.CancellationToken);
            }

            await using (var roundtrip = new NpgsqlCommand("SELECT echo_text($1)", connection))
            {
                roundtrip.Parameters.AddWithValue("café");
                Assert.AreEqual("café", await roundtrip.ExecuteScalarAsync(context.CancellationToken));
            }

            await using (var invalid = new NpgsqlCommand("SELECT unicode_text()", connection))
            {
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(
                    () => invalid.ExecuteScalarAsync(context.CancellationToken));
                Assert.AreEqual(PostgresErrorCodes.UntranslatableCharacter, error.SqlState);
            }

            await using (var reported = new NpgsqlCommand("SELECT report_error('22023', 'café', 'détail', 'réessayer')", connection))
            {
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(
                    () => reported.ExecuteScalarAsync(context.CancellationToken));
                Assert.AreEqual("22023", error.SqlState);
                Assert.AreEqual("café", error.MessageText);
                Assert.AreEqual("détail", error.Detail);
                Assert.AreEqual("réessayer", error.Hint);
            }

            await using var succeeding = new NpgsqlCommand("SELECT echo_text('recovered')", connection);
            Assert.AreEqual("recovered", await succeeding.ExecuteScalarAsync(context.CancellationToken));
            Assert.AreEqual(backend, connection.ProcessID);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", administrator);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
