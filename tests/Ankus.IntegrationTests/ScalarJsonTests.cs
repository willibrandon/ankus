using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Checks source-generated scalar JSON contracts inside the Native AOT extension.
/// </summary>
/// <param name="context">The test context.</param>
[TestClass]
public sealed class ScalarJsonTests(TestContext context)
{
    /// <summary>
    /// Checks serialized text and independent PostgreSQL binary values without a decimal or DateTime intermediary.
    /// </summary>
    /// <param name="type">The scalar type.</param>
    /// <param name="input">The PostgreSQL input.</param>
    /// <param name="expectedText">The exact converter output string.</param>
    [TestMethod]
    [DataRow("date", "0001-02-29 BC", "0001-02-29 BC")]
    [DataRow("date", "5874897-12-31", "5874897-12-31")]
    [DataRow("date", "infinity", "infinity")]
    [DataRow("time", "24:00", "24:00:00")]
    [DataRow("time", "23:59:59.999999", "23:59:59.999999")]
    [DataRow("timetz", "12:34:56.123456-05:30:17", "12:34:56.123456-05:30:17")]
    [DataRow("timestamp", "294276-12-31 23:59:59.999999", "294276-12-31T23:59:59.999999")]
    [DataRow("timestamp", "0001-02-29 12:34:56 BC", "0001-02-29T12:34:56 BC")]
    [DataRow("timestamp", "-infinity", "-infinity")]
    [DataRow("timestamptz", "2024-11-03 06:30+00", "2024-11-03T01:30:00-05:00")]
    [DataRow("timestamptz", "infinity", "infinity")]
    [DataRow("interval", "1 month -2 days 3 microseconds", "P1M-2DT0.000003S")]
    [DataRow("interval", "-1 day -02:00:00", "P-1DT-2H")]
    [DataRow("interval", "-1 year -1 month +1 day -00:00:00.5", "P-1Y-1M1DT-0.5S")]
    [DataRow("interval", "-infinity", "-infinity")]
    [DataRow("numeric", "123456789012345678901234567890.1234567890123456789000", "123456789012345678901234567890.1234567890123456789000")]
    [DataRow("numeric", "NaN", "NaN")]
    [DataRow("numeric", "Infinity", "Infinity")]
    [DataRow("numeric", "-Infinity", "-Infinity")]
    public Task ScalarJsonPreservesFullRangeAndScale(string type, string input, string expectedText)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ScalarJsonPreservesFullRangeAndScale), async (connection, transaction, token) =>
        {
            await using var settings = new NpgsqlCommand("SET LOCAL TIME ZONE 'America/New_York'; SET LOCAL DateStyle = 'German'; SET LOCAL IntervalStyle = 'postgres'", connection, transaction);
            await settings.ExecuteNonQueryAsync(token);
            string send = type == "date" ? "date_send" : type + "_send";
            await using var command = new NpgsqlCommand($"""
                SELECT payload->>'Value', json_typeof(payload->'Value'),
                    {send}($1::{type}), {send}((payload->>'Value')::{type})
                FROM (SELECT datatype.scalar_json($2, json_build_object('Value', $1::text)) AS payload) AS result
                """, connection, transaction);
            command.Parameters.AddWithValue(input);
            command.Parameters.AddWithValue(type);
            int major = PostgresFixture.Cluster.Installation.Version.Major;
            if ((major < 14 && type == "numeric" && input is "Infinity" or "-Infinity") ||
                (major < 17 && type == "interval" && input == "-infinity"))
            {
                int backend = connection.ProcessID;
                await transaction.SaveAsync("json_native_error", token);
                command.CommandText = $"SELECT $1::{type}";
                PostgresException native = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
                Assert.AreEqual(type == "numeric" ? "22P02" : "22007", native.SqlState);
                Assert.AreEqual($"invalid input syntax for type {type}: \"{input}\"", native.MessageText);
                await transaction.RollbackAsync("json_native_error", token);
                await transaction.ReleaseAsync("json_native_error", token);

                await transaction.SaveAsync("json_managed_error", token);
                command.CommandText = "SELECT datatype.scalar_json($2, json_build_object('Value', $1::text))";
                PostgresException managed = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
                Assert.AreEqual("38000", managed.SqlState);
                Assert.AreEqual("Invalid PostgreSQL value.", managed.MessageText);
                await transaction.RollbackAsync("json_managed_error", token);
                await transaction.ReleaseAsync("json_managed_error", token);

                command.CommandText = "SELECT datatype.scalar_json_recovery($2, json_build_object('Value', $1::text))";
                Assert.AreEqual($"$.Value:{native.SqlState}:50:0:2", await command.ExecuteScalarAsync(token));
                command.Parameters.Clear();
                command.CommandText = "SELECT datatype.scalar_json('numeric', '{\"Value\":\"1.2300\"}')->>'Value', pg_backend_pid()";
                await using NpgsqlDataReader recovery = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await recovery.ReadAsync(token));
                Assert.AreEqual("1.2300", recovery.GetString(0));
                Assert.AreEqual(backend, recovery.GetInt32(1));
                Assert.IsFalse(await recovery.ReadAsync(token));
                return;
            }

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(expectedText, reader.GetString(0));
            Assert.AreEqual("string", reader.GetString(1));
            Assert.AreSequenceEqual(reader.GetFieldValue<byte[]>(2), reader.GetFieldValue<byte[]>(3));
        }, context.CancellationToken);

    /// <summary>
    /// Checks exact unquoted number tokens, exponent normalization, and independently nullable scalar properties.
    /// </summary>
    [TestMethod]
    public Task ScalarJsonNumbersAndNullsUseExactContracts()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ScalarJsonNumbersAndNullsUseExactContracts), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                SELECT datatype.scalar_json('numeric', '{"Value":123456789012345678901234567890.1234567890123456789000}')::text,
                    datatype.scalar_json('numeric', '{"Value":1.2300e50}')::text,
                    datatype.scalar_json('nullable', '{"Date":null,"Time":null,"TimeTz":null,"Timestamp":null,"TimestampTz":null,"Interval":null,"Numeric":null}')::text
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual("{\"Value\":\"123456789012345678901234567890.1234567890123456789000\"}", reader.GetString(0));
            Assert.AreEqual("{\"Value\":\"123" + new string('0', 48) + "\"}", reader.GetString(1));
            Assert.AreEqual("{\"Date\":null,\"Time\":null,\"TimeTz\":null,\"Timestamp\":null,\"TimestampTz\":null,\"Interval\":null,\"Numeric\":null}", reader.GetString(2));
        }, context.CancellationToken);

    /// <summary>
    /// Reads PostgreSQL's style-dependent JSON and always writes the iso_8601 text, which every style parses identically.
    /// </summary>
    /// <param name="style">The session interval style.</param>
    [TestMethod]
    [DataRow("postgres")]
    [DataRow("postgres_verbose")]
    [DataRow("sql_standard")]
    [DataRow("iso_8601")]
    public Task IntervalJsonIgnoresStyleAndRetainsComponents(string style)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(IntervalJsonIgnoresStyleAndRetainsComponents), async (connection, transaction, token) =>
        {
            await using var settings = new NpgsqlCommand($"SET LOCAL IntervalStyle = '{style}'", connection, transaction);
            await settings.ExecuteNonQueryAsync(token);
            await using var command = new NpgsqlCommand("""
                SELECT interval_send(value), interval_send((payload->>'Value')::interval), payload->>'Value',
                    interval_send((datatype.scalar_json('interval', payload)->>'Value')::interval)
                FROM (SELECT value, datatype.scalar_json('interval', json_build_object('Value', value)) AS payload
                    FROM (SELECT interval '1 month -2 days 123456789 microseconds' AS value) AS input) AS result
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreSequenceEqual(reader.GetFieldValue<byte[]>(0), reader.GetFieldValue<byte[]>(1));
            Assert.AreEqual("P1M-2DT2M3.456789S", reader.GetString(2));
            Assert.AreSequenceEqual(reader.GetFieldValue<byte[]>(0), reader.GetFieldValue<byte[]>(3));
        }, context.CancellationToken);

    /// <summary>
    /// Checks JsonException paths and native causes while finally executes, writes survive and native contexts are released.
    /// </summary>
    /// <param name="type">The scalar type.</param>
    /// <param name="json">The invalid property.</param>
    /// <param name="code">The expected native cause or JSON token failure.</param>
    [TestMethod]
    [DataRow("date", "{\"Value\":\"2024-02-30\"}", "22008")]
    [DataRow("time", "{\"Value\":\"25:00\"}", "22008")]
    [DataRow("timetz", "{\"Value\":\"12:00+16\"}", "22009")]
    [DataRow("timestamp", "{\"Value\":\"no timestamp\"}", "22007")]
    [DataRow("timestamptz", "{\"Value\":\"2024-01-01 Unknown/Zone\"}", "22023")]
    [DataRow("interval", "{\"Value\":\"2147483648 months\"}", "22015")]
    [DataRow("numeric", "{\"Value\":1e131072}", "22003")]
    [DataRow("numeric", "{\"Value\":\"not numeric\"}", "22P02")]
    [DataRow("numeric", "{\"Value\":true}", "JSON")]
    [DataRow("numeric", "{\"Value\":null}", "JSON")]
    [DataRow("date", "{\"Value\":123}", "JSON")]
    [DataRow("time", "{\"Value\":[]}", "JSON")]
    [DataRow("timestamp", "{\"Value\":{}}", "JSON")]
    [DataRow("interval", "{\"Value\":null}", "JSON")]
    [DataRow("numeric", "{\"Value\":\"1\\u0000\"}", "JSON")]
    [DataRow("numeric", "{\"Value\":\"1\\ud800\"}", "JSON")]
    public Task ScalarJsonFailuresPreservePathsAndBackend(string type, string json, string code)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ScalarJsonFailuresPreservePathsAndBackend), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT datatype.scalar_json_recovery($1, $2::json)", connection, transaction);
            command.Parameters.AddWithValue(type);
            command.Parameters.AddWithValue(json);
            Assert.AreEqual($"$.Value:{code}:50:0:2", await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);
}
