using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes every pgrx numeric example through its published Native AOT library.
/// </summary>
/// <param name="context">The current test cancellation context.</param>
[TestClass]
[DoNotParallelize]
public sealed class NumericExampleTests(TestContext context)
{
    /// <summary>
    /// Adds before rounding, preserving large inputs, display scale and away-from-zero ties.
    /// </summary>
    /// <param name="left">The first independently specified numeric input.</param>
    /// <param name="right">The second independently specified numeric input.</param>
    [TestMethod]
    [DataRow("0", "0")]
    [DataRow("1.2300", "2.45000")]
    [DataRow("-1.2300", "2.45000")]
    [DataRow("4e-34", "4e-34")]
    [DataRow("-4e-34", "-4e-34")]
    [DataRow("5e-34", "0")]
    [DataRow("-5e-34", "0")]
    [DataRow("999999999999999999999999999999999999999999999999999999999999", "1")]
    [DataRow("NaN", "1")]
    public Task NumericSampleAddsBeforeRescaling(string left, string right)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NumericSampleAddsBeforeRescaling), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await using var command = new NpgsqlCommand("""
                SELECT numeric_send(($1::numeric + $2::numeric)::numeric(1000,33)),
                    numeric_send(numeric_example.add_numeric($1::numeric,$2::numeric)),
                    numeric_example.add_numeric($1::numeric,$2::numeric)::text,
                    pg_typeof(numeric_example.add_numeric($1::numeric,$2::numeric))::text
                """, connection, transaction);
            command.Parameters.AddWithValue(left);
            command.Parameters.AddWithValue(right);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreSequenceEqual(reader.GetFieldValue<byte[]>(0), reader.GetFieldValue<byte[]>(1));
            Assert.AreEqual("numeric", reader.GetString(3));
            if (left == "4e-34")
            {
                Assert.AreEqual("0.000000000000000000000000000000001", reader.GetString(2));
            }
            else if (left == "-4e-34")
            {
                Assert.AreEqual("-0.000000000000000000000000000000001", reader.GetString(2));
            }

            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Accepts the largest precision-1000 input and rejects overflow before or after rounding and addition.
    /// </summary>
    /// <param name="scenario">Selects the independently constructed precision boundary.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public Task NumericSampleChecksExactPrecisionBoundaries(int scenario)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NumericSampleChecksExactPrecisionBoundaries), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            string integral = new('9', 967);
            (string left, string right) = scenario switch
            {
                0 => (integral + "." + new string('9', 33), "0"),
                1 => ("1" + new string('0', 967), "0"),
                2 => (integral + "." + new string('9', 33) + "5", "0"),
                3 => (integral, integral),
                _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
            };
            await using var command = new NpgsqlCommand(
                "SELECT numeric_example.add_numeric($1::numeric,$2::numeric)::text", connection, transaction);
            command.Parameters.AddWithValue(left);
            command.Parameters.AddWithValue(right);
            if (scenario == 0)
            {
                Assert.AreEqual(left, Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token)));
                return;
            }

            await AssertMatchingFailureAsync(command, transaction,
                "SELECT ($1::numeric + $2::numeric)::numeric(1000,33)", "22003", token);
        }, context.CancellationToken);

    /// <summary>
    /// Parses finite and special input without losing PostgreSQL's numeric representation.
    /// </summary>
    /// <param name="text">The independently specified native input text.</param>
    [TestMethod]
    [DataRow("0")]
    [DataRow("-0.0000")]
    [DataRow("1.230000")]
    [DataRow("  +1.2300e2  ")]
    [DataRow("-9e-1000")]
    [DataRow("170141183460469231731687303715884105727")]
    [DataRow("-170141183460469231731687303715884105728")]
    [DataRow("9999999999999999999999999999999999999999999999999999999999999999.00001")]
    [DataRow("NaN")]
    [DataRow("Infinity")]
    [DataRow("-Infinity")]
    public Task NumericSampleParsingPreservesNativeValues(string text)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NumericSampleParsingPreservesNativeValues), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await using var command = new NpgsqlCommand("""
                SELECT numeric_send($1::numeric), numeric_send(numeric_example.numeric_from_string($1)),
                    $1::numeric::text, numeric_example.numeric_from_string($1)::text
                """, connection, transaction);
            command.Parameters.AddWithValue(text);
            if (text.EndsWith("Infinity", StringComparison.Ordinal) && PostgresFixture.Cluster.Installation.Version.Major < 14)
            {
                command.CommandText = "SELECT numeric_example.numeric_from_string($1)";
                await AssertMatchingFailureAsync(command, transaction, "SELECT $1::numeric", "22P02", token);
                return;
            }

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreSequenceEqual(reader.GetFieldValue<byte[]>(0), reader.GetFieldValue<byte[]>(1));
            Assert.AreEqual(reader.GetString(2), reader.GetString(3));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Invalid text and out-of-range numeric input retain native diagnostics and same-backend recovery.
    /// </summary>
    /// <param name="text">The invalid or out-of-range native input.</param>
    /// <param name="state">The independently specified PostgreSQL error code.</param>
    [TestMethod]
    [DataRow("", "22P02")]
    [DataRow("not a numeric", "22P02")]
    [DataRow("1e131072", "22003")]
    [DataRow("1e-100000", "22003")]
    public Task NumericSampleFailuresPreserveNativeRecovery(string text, string state)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NumericSampleFailuresPreserveNativeRecovery), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await using var command = new NpgsqlCommand(
                "SELECT numeric_example.numeric_from_string($1)", connection, transaction);
            command.Parameters.AddWithValue(text);
            await AssertMatchingFailureAsync(command, transaction, "SELECT $1::numeric", state, token);
        }, context.CancellationToken);

    /// <summary>
    /// Compares the complete arithmetic chain, including both final divisions, with independent native SQL.
    /// </summary>
    [TestMethod]
    public Task NumericSampleMathPreservesTheCompleteUpstreamChain()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NumericSampleMathPreservesTheCompleteUpstreamChain), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await using var command = new NpgsqlCommand("""
                WITH initial AS (
                    SELECT (((((5::numeric * 5) - 2.234::double precision::numeric) / 4) % 19)
                        + 99.42::double precision::numeric) AS value),
                integer_operations AS (
                    SELECT (((value * 42) / 42) + 42) - 42 AS value FROM initial),
                single_operations AS (
                    SELECT (((value * 42::real::numeric) / 42::real::numeric)
                        + 42::real::numeric) - 42::real::numeric AS value FROM integer_operations),
                double_operations AS (
                    SELECT (((value * 42::double precision::numeric) / 42::double precision::numeric)
                        + 42::double precision::numeric) - 42::double precision::numeric AS value FROM single_operations),
                expected AS (
                    SELECT ((value / 42::numeric) / 42::numeric(1000,33))::numeric(10,3) AS value FROM double_operations)
                SELECT value::text, numeric_example.math()::text,
                    numeric_send(value), numeric_send(numeric_example.math()) FROM expected
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual("0.060", reader.GetString(0));
            Assert.AreEqual(reader.GetString(0), reader.GetString(1));
            Assert.AreSequenceEqual(reader.GetFieldValue<byte[]>(2), reader.GetFieldValue<byte[]>(3));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Retains forty-two's exact value and all thirty-three fractional display positions.
    /// </summary>
    [TestMethod]
    public Task NumericSampleFortyTwoRetainsItsDisplayScale()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NumericSampleFortyTwoRetainsItsDisplayScale), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await using var command = new NpgsqlCommand("""
                SELECT numeric_example.forty_twooooooo()::text,
                    numeric_send(42::numeric(1000,33)), numeric_send(numeric_example.forty_twooooooo()),
                    scale(numeric_example.forty_twooooooo())
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual("42." + new string('0', 33), reader.GetString(0));
            Assert.AreSequenceEqual(reader.GetFieldValue<byte[]>(1), reader.GetFieldValue<byte[]>(2));
            Assert.AreEqual(33, reader.GetInt32(3));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Each random result is a finite scale-zero integer within the full signed Int128 contract.
    /// </summary>
    [TestMethod]
    public Task NumericSampleRandomValuesRetainTheSignedIntegerContract()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NumericSampleRandomValuesRetainTheSignedIntegerContract), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await using var command = new NpgsqlCommand("""
                WITH samples AS (SELECT numeric_example.random_numeric() AS value FROM generate_series(1,16))
                SELECT count(*), bool_and(value >= '-170141183460469231731687303715884105728'::numeric
                        AND value <= '170141183460469231731687303715884105727'::numeric
                        AND value = trunc(value) AND scale(value) = 0) FROM samples
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(16L, reader.GetInt64(0));
            Assert.IsTrue(reader.GetBoolean(1));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Keeps upstream names, SQL numeric identity, strict inputs and default execution flags.
    /// </summary>
    [TestMethod]
    public Task NumericSampleDeclarationsPreserveUpstreamContracts()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NumericSampleDeclarationsPreserveUpstreamContracts), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            Assert.AreEqual(5L, await ScalarAsync<long>(connection, transaction, """
                SELECT count(*) FROM pg_proc JOIN pg_namespace ON pg_namespace.oid = pronamespace
                WHERE nspname = 'numeric_example'
                    AND proname IN ('add_numeric','random_numeric','numeric_from_string','math','forty_twooooooo')
                    AND prorettype = 'numeric'::regtype AND provolatile = 'v' AND proparallel = 'u'
                """, token));
            Assert.IsTrue(await ScalarAsync<bool>(connection, transaction, """
                SELECT numeric_example.add_numeric(NULL::numeric,1) IS NULL
                    AND numeric_example.add_numeric(1,NULL::numeric) IS NULL
                    AND numeric_example.numeric_from_string(NULL::text) IS NULL
                """, token));
            Assert.IsFalse(await ScalarAsync<bool>(connection, transaction,
                "SELECT extrelocatable FROM pg_extension WHERE extname = 'ankus_numeric'", token));
        }, context.CancellationToken);

    /// <summary>
    /// Stores detached numeric output that remains exact after the input table has been deleted.
    /// </summary>
    [TestMethod]
    public Task NumericSampleOutputsSurviveSourceDeletion()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NumericSampleOutputsSurviveSourceDeletion), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await ExecuteAsync(connection, transaction, """
                CREATE TEMP TABLE numeric_sample_source(value text);
                INSERT INTO numeric_sample_source VALUES ('123456789012345678901234567890.00000100');
                CREATE TEMP TABLE numeric_sample_result AS
                    SELECT numeric_example.numeric_from_string(value) AS value FROM numeric_sample_source;
                DROP TABLE numeric_sample_source;
                """, token);
            Assert.AreEqual("123456789012345678901234567890.00000100", await ScalarAsync<string>(connection, transaction,
                "SELECT value::text FROM numeric_sample_result", token));
            Assert.AreEqual(connection.ProcessID, await ScalarAsync<int>(connection, transaction, "SELECT pg_backend_pid()", token));
            Assert.AreEqual(42, await ScalarAsync<int>(connection, transaction, "SELECT 42", token));
        }, context.CancellationToken);

    /// <summary>
    /// Compares native and managed failure fields after explicit rollback and then proves same-session recovery.
    /// </summary>
    /// <param name="command">The prepared managed failure statement and input parameters.</param>
    /// <param name="transaction">The isolated enclosing transaction.</param>
    /// <param name="nativeSql">The independent PostgreSQL operation.</param>
    /// <param name="expectedState">The independently required SQLSTATE.</param>
    /// <param name="token">Cancels native work.</param>
    private static async Task AssertMatchingFailureAsync(NpgsqlCommand command, NpgsqlTransaction transaction,
        string nativeSql, string expectedState, CancellationToken token)
    {
        NpgsqlConnection? connection = command.Connection;
        Assert.IsNotNull(connection);
        int backend = connection.ProcessID;
        string managedSql = command.CommandText;
        await transaction.SaveAsync("numeric_sample_native", token);
        command.CommandText = nativeSql;
        PostgresException native = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual(expectedState, native.SqlState);
        await transaction.RollbackAsync("numeric_sample_native", token);
        await transaction.ReleaseAsync("numeric_sample_native", token);
        await transaction.SaveAsync("numeric_sample_managed", token);
        command.CommandText = managedSql;
        PostgresException managed = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual(native.SqlState, managed.SqlState);
        Assert.AreEqual(native.MessageText, managed.MessageText);
        Assert.AreEqual(native.Detail, managed.Detail);
        Assert.AreEqual(native.Hint, managed.Hint);
        await transaction.RollbackAsync("numeric_sample_managed", token);
        await transaction.ReleaseAsync("numeric_sample_managed", token);
        command.Parameters.Clear();
        command.CommandText = "SELECT numeric_example.numeric_from_string('42')::text, pg_backend_pid()";
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        Assert.IsTrue(await reader.ReadAsync(token));
        Assert.AreEqual("42", reader.GetString(0));
        Assert.AreEqual(backend, reader.GetInt32(1));
        Assert.IsFalse(await reader.ReadAsync(token));
    }

    /// <summary>
    /// Installs the independently published numeric example in a deliberately chosen schema.
    /// </summary>
    /// <param name="connection">The isolated backend connection.</param>
    /// <param name="transaction">The rollback-isolated test transaction.</param>
    /// <param name="token">Cancels native work.</param>
    /// <returns>The completed installation.</returns>
    private static Task InstallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
        => ExecuteAsync(connection, transaction, "CREATE SCHEMA numeric_example; CREATE EXTENSION ankus_numeric WITH SCHEMA numeric_example", token);

    /// <summary>
    /// Executes an independently specified fixture statement.
    /// </summary>
    /// <param name="connection">The isolated backend connection.</param>
    /// <param name="transaction">The rollback-isolated test transaction.</param>
    /// <param name="sql">The fixture statement.</param>
    /// <param name="token">Cancels native work.</param>
    /// <returns>The completed native statement.</returns>
    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(token);
    }

    /// <summary>
    /// Reads a required detached scalar with an explicit type assertion.
    /// </summary>
    /// <typeparam name="T">The expected client-side type.</typeparam>
    /// <param name="connection">The isolated backend connection.</param>
    /// <param name="transaction">The rollback-isolated test transaction.</param>
    /// <param name="sql">The independent scalar query.</param>
    /// <param name="token">Cancels native work.</param>
    /// <returns>The required scalar value.</returns>
    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }
}
