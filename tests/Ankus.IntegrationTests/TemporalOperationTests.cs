using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Compares managed temporal APIs with independently evaluated PostgreSQL SQL expressions.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class TemporalOperationTests(TestContext context)
{
    /// <summary>
    /// Verifies calendar, timezone, field, and arithmetic behavior using PostgreSQL as the independent oracle.
    /// </summary>
    /// <param name="type">The temporal type.</param>
    /// <param name="operation">The managed operation.</param>
    /// <param name="left">The primary operand.</param>
    /// <param name="right">The secondary operand.</param>
    /// <param name="expectedSql">The independently written native SQL expression.</param>
    [TestMethod]
    [DataRow("date", "days", "2024-02-28", "2", "date '2024-02-28' + 2")]
    [DataRow("date", "days", "infinity", "2", "date 'infinity' + 2")]
    [DataRow("date", "add", "2024-01-31", "1 month", "date '2024-01-31' + interval '1 month'")]
    [DataRow("date", "subtract", "2024-03-31", "1 month", "date '2024-03-31' - interval '1 month'")]
    [DataRow("date", "difference", "0001-01-01", "0001-12-31 BC", "date '0001-01-01' - date '0001-12-31 BC'")]
    [DataRow("date", "time", "2024-02-29", "24:00", "date '2024-02-29' + time '24:00'")]
    [DataRow("date", "timetz", "2024-02-29", "12:00+05:30:17", "date '2024-02-29' + timetz '12:00+05:30:17'")]
    [DataRow("date", "timestamp", "0001-01-01 BC", "", "date '0001-01-01 BC'::timestamp")]
    [DataRow("date", "timestamptz", "2024-03-10", "", "date '2024-03-10'::timestamptz")]
    [DataRow("date", "part", "5874897-12-31", "Year", "extract(year FROM date '5874897-12-31')")]
    [DataRow("date", "part", "2021-01-01", "IsoYear", "extract(isoyear FROM date '2021-01-01')")]
    [DataRow("date", "part", "infinity", "Month", "extract(month FROM date 'infinity')")]
    [DataRow("time", "add", "23:00", "2 hours", "time '23:00' + interval '2 hours'")]
    [DataRow("time", "subtract", "00:00", "1 microsecond", "time '00:00' - interval '1 microsecond'")]
    [DataRow("time", "difference", "01:00", "23:00", "time '01:00' - time '23:00'")]
    [DataRow("time", "part", "12:34:56.123456", "Microseconds", "date_part('microseconds', time '12:34:56.123456')")]
    [DataRow("timetz", "add", "23:00+05:30:17", "2 hours", "timetz '23:00+05:30:17' + interval '2 hours'")]
    [DataRow("timetz", "subtract", "00:00-02", "1 second", "timetz '00:00-02' - interval '1 second'")]
    [DataRow("timetz", "zone", "12:00+05:30:17", "UTC", "timetz '12:00+05:30:17' AT TIME ZONE 'UTC'")]
    [DataRow("timetz", "time", "12:00+05:30:17", "", "timetz '12:00+05:30:17'::time")]
    [DataRow("timetz", "part", "12:00-05:30:17", "TimeZone", "date_part('timezone', timetz '12:00-05:30:17')")]
    [DataRow("timestamp", "add", "2024-01-31 12:34:56", "1 month", "timestamp '2024-01-31 12:34:56' + interval '1 month'")]
    [DataRow("timestamp", "subtract", "2024-03-31", "1 month", "timestamp '2024-03-31' - interval '1 month'")]
    [DataRow("timestamp", "difference", "2024-03-10", "2024-03-08 23:00", "timestamp '2024-03-10' - timestamp '2024-03-08 23:00'")]
    [DataRow("timestamp", "age", "2024-03-31", "2024-02-29", "age(timestamp '2024-03-31', timestamp '2024-02-29')")]
    [DataRow("timestamp", "truncate", "2024-03-31 12:34:56.123456", "Month", "date_trunc('month', timestamp '2024-03-31 12:34:56.123456')")]
    [DataRow("timestamp", "zone", "2024-03-10 02:30", "America/New_York", "timestamp '2024-03-10 02:30' AT TIME ZONE 'America/New_York'")]
    [DataRow("timestamp", "zone", "2024-11-03 01:30", "America/New_York", "timestamp '2024-11-03 01:30' AT TIME ZONE 'America/New_York'")]
    [DataRow("timestamp", "timestamptz", "2024-03-10 12:00", "", "timestamp '2024-03-10 12:00'::timestamptz")]
    [DataRow("timestamp", "date", "0001-01-01 12:34:56 BC", "", "timestamp '0001-01-01 12:34:56 BC'::date")]
    [DataRow("timestamp", "time", "2000-01-01 12:34:56.123456", "", "timestamp '2000-01-01 12:34:56.123456'::time")]
    [DataRow("timestamp", "time", "infinity", "", "timestamp 'infinity'::time")]
    [DataRow("timestamp", "part", "2021-01-01", "IsoYear", "date_part('isoyear', timestamp '2021-01-01')")]
    [DataRow("timestamp", "part", "-infinity", "Epoch", "date_part('epoch', timestamp '-infinity')")]
    [DataRow("timestamptz", "add", "2024-03-09 12:00-05", "1 day", "timestamptz '2024-03-09 12:00-05' + interval '1 day'")]
    [DataRow("timestamptz", "add", "2024-03-09 12:00-05", "24 hours", "timestamptz '2024-03-09 12:00-05' + interval '24 hours'")]
    [DataRow("timestamptz", "subtract", "2024-03-10 12:00-04", "1 day", "timestamptz '2024-03-10 12:00-04' - interval '1 day'")]
    [DataRow("timestamptz", "difference", "2024-03-10 12:00-04", "2024-03-09 12:00-05",
        "timestamptz '2024-03-10 12:00-04' - timestamptz '2024-03-09 12:00-05'")]
    [DataRow("timestamptz", "age", "2024-03-10 12:00-04", "2024-03-09 12:00-05",
        "age(timestamptz '2024-03-10 12:00-04', timestamptz '2024-03-09 12:00-05')")]
    [DataRow("timestamptz", "truncate", "2024-03-10 12:00-04", "Day", "date_trunc('day', timestamptz '2024-03-10 12:00-04')")]
    [DataRow("timestamptz", "zone", "2024-03-10 07:30+00", "America/New_York", "timestamptz '2024-03-10 07:30+00' AT TIME ZONE 'America/New_York'")]
    [DataRow("timestamptz", "timestamp", "2024-03-10 07:30+00", "", "timestamptz '2024-03-10 07:30+00'::timestamp")]
    [DataRow("timestamptz", "date", "2024-03-10 01:00+00", "", "timestamptz '2024-03-10 01:00+00'::date")]
    [DataRow("timestamptz", "time", "2024-03-10 01:00+00", "", "timestamptz '2024-03-10 01:00+00'::time")]
    [DataRow("timestamptz", "time", "-infinity", "", "timestamptz '-infinity'::time")]
    [DataRow("timestamptz", "part", "2024-03-10 12:00-04", "TimeZone", "date_part('timezone', timestamptz '2024-03-10 12:00-04')")]
    [DataRow("interval", "add", "1 month -2 days", "3 days 4 hours", "interval '1 month -2 days' + interval '3 days 4 hours'")]
    [DataRow("interval", "subtract", "1 month -2 days", "3 days", "interval '1 month -2 days' - interval '3 days'")]
    [DataRow("interval", "multiply", "1 month 1 day 1 second", "0.5", "interval '1 month 1 day 1 second' * 0.5")]
    [DataRow("interval", "divide", "1 month 1 day 1 second", "2", "interval '1 month 1 day 1 second' / 2")]
    [DataRow("interval", "negate", "1 month -2 days", "", "-interval '1 month -2 days'")]
    [DataRow("interval", "days", "31 days", "", "justify_days(interval '31 days')")]
    [DataRow("interval", "hours", "49 hours", "", "justify_hours(interval '49 hours')")]
    [DataRow("interval", "justify", "1 month -1 hour", "", "justify_interval(interval '1 month -1 hour')")]
    [DataRow("interval", "truncate", "1 year 3 months 4 days 05:06:07.123456", "Day",
        "date_trunc('day', interval '1 year 3 months 4 days 05:06:07.123456')")]
    [DataRow("interval", "compare", "1 month", "30 days", "interval_cmp(interval '1 month', interval '30 days')")]
    [DataRow("interval", "part", "2 years 3 months", "Month", "date_part('month', interval '2 years 3 months')")]
    [DataRow("interval", "part", "infinity", "Day", "date_part('day', interval 'infinity')")]
    [DataRow("date", "compare", "-infinity", "0001-01-01 BC", "date_cmp('-infinity', '0001-01-01 BC')")]
    [DataRow("time", "compare", "24:00", "00:00", "time_cmp('24:00', '00:00')")]
    [DataRow("timetz", "compare", "12:00+02", "10:00+00", "timetz_cmp('12:00+02', '10:00+00')")]
    [DataRow("timetz", "compare", "00:00+15:59:59", "23:59:59-15:59:59", "timetz_cmp('00:00+15:59:59', '23:59:59-15:59:59')")]
    [DataRow("timetz", "compare", "24:00+00", "00:00+00", "timetz_cmp('24:00+00', '00:00+00')")]
    [DataRow("timestamp", "compare", "294276-12-31 23:59:59.999999", "infinity", "timestamp_cmp('294276-12-31 23:59:59.999999', 'infinity')")]
    [DataRow("timestamptz", "compare", "2024-03-10 12:00-04", "2024-03-10 16:00+00", "timestamptz_cmp('2024-03-10 12:00-04', '2024-03-10 16:00+00')")]
    [DataRow("interval", "compare", "infinity", "1000 years", "interval_cmp('infinity', '1000 years')")]
    public Task TemporalMethodsMatchServerSemantics(string type, string operation, string left, string right, string expectedSql)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(TemporalMethodsMatchServerSemantics),
            async (connection, transaction, token) =>
            {
                await using var settings = new NpgsqlCommand("SET LOCAL TIME ZONE 'America/New_York'", connection, transaction);
                await settings.ExecuteNonQueryAsync(token);
                await using var command = new NpgsqlCommand($"""
                    SELECT ({expectedSql})::text, datatype.temporal_operation($1, $2, $3, $4)
                    """, connection, transaction);
                command.Parameters.AddWithValue(type);
                command.Parameters.AddWithValue(operation);
                command.Parameters.AddWithValue(left);
                command.Parameters.AddWithValue(right);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(reader.GetValue(0), reader.GetValue(1));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies native calendar constructors, explicit-zone truncation, clock semantics, and SPI ownership preservation.
    /// </summary>
    [TestMethod]
    public Task ConstructorsClocksAndSessionsUseNativeSemantics()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ConstructorsClocksAndSessionsUseNativeSemantics),
            async (connection, transaction, token) =>
            {
                await using var settings = new NpgsqlCommand("SET LOCAL TIME ZONE 'UTC'", connection, transaction);
                await settings.ExecuteNonQueryAsync(token);
                await using var command = new NpgsqlCommand("""
                    SELECT datatype.make_pg_date(-1, 2, 29)::text, datatype.make_pg_time(24, 0, 0)::text,
                           datatype.truncate_in_zone('2024-03-10 16:00+00', 'Day', 'America/New_York')::text,
                           datatype.temporal_clocks(), datatype.temporal_operations_preserve_session()
                    """, connection, transaction);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual("0001-02-29 BC", reader.GetString(0));
                Assert.AreEqual("24:00:00", reader.GetString(1));
                Assert.AreEqual("2024-03-10 05:00:00+00", reader.GetString(2));
                Assert.AreEqual("True:True:True:1970-01-01T00:00:00.000001+00:00", reader.GetString(3));
                Assert.AreEqual("0:0:42", reader.GetString(4));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies exact text formats under session settings and DateStyle-independent ISO output.
    /// </summary>
    /// <param name="type">The temporal type.</param>
    /// <param name="input">The PostgreSQL input.</param>
    /// <param name="settings">The local display settings.</param>
    /// <param name="expected">The expected native output.</param>
    /// <param name="iso">The expected ISO output, or null for intervals.</param>
    [TestMethod]
    [DataRow("date", "03/04/2024", "SET LOCAL DateStyle = 'SQL, DMY'", "03/04/2024", "2024-04-03")]
    [DataRow("date", "03/04/2024", "SET LOCAL DateStyle = 'SQL, MDY'", "03/04/2024", "2024-03-04")]
    [DataRow("date", "0001-02-29 BC", "SET LOCAL DateStyle = 'German'", "29.02.0001 BC", "0001-02-29 BC")]
    [DataRow("date", "infinity", "SET LOCAL DateStyle = 'German'", "infinity", "infinity")]
    [DataRow("time", "24:00", "SET LOCAL DateStyle = 'ISO'", "24:00:00", "24:00:00")]
    [DataRow("time", "12:34:56.123456", "SET LOCAL DateStyle = 'ISO'", "12:34:56.123456", "12:34:56.123456")]
    [DataRow("timetz", "12:34:56.123456+05:30:17", "SET LOCAL DateStyle = 'ISO'", "12:34:56.123456+05:30:17", "12:34:56.123456+05:30:17")]
    [DataRow("timestamp", "2024-04-03 12:34:56.123456", "SET LOCAL DateStyle = 'German'", "03.04.2024 12:34:56.123456", "2024-04-03T12:34:56.123456")]
    [DataRow("timestamp", "-infinity", "SET LOCAL DateStyle = 'SQL'", "-infinity", "-infinity")]
    [DataRow("timestamptz", "2024-03-10 07:30+00", "SET LOCAL DateStyle = 'ISO'; SET LOCAL TIME ZONE 'America/New_York'", "2024-03-10 03:30:00-04", "2024-03-10T03:30:00-04:00")]
    [DataRow("timestamptz", "2000-01-01+00", "SET LOCAL DateStyle = 'ISO'; SET LOCAL TIME ZONE 'Asia/Kathmandu'", "2000-01-01 05:45:00+05:45", "2000-01-01T05:45:00+05:45")]
    [DataRow("interval", "P1Y2M3DT4H5M6.123456S", "SET LOCAL IntervalStyle = 'postgres'", "1 year 2 mons 3 days 04:05:06.123456", null)]
    [DataRow("interval", "1 month -2 days 3 microseconds", "SET LOCAL IntervalStyle = 'iso_8601'", "P1M-2DT0.000003S", null)]
    [DataRow("interval", "-infinity", "SET LOCAL IntervalStyle = 'sql_standard'", "-infinity", null)]
    public Task ParsingAndFormattingHonorServerSettings(string type, string input, string settings, string expected, string? iso)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ParsingAndFormattingHonorServerSettings),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand(settings, connection, transaction);
                await command.ExecuteNonQueryAsync(token);
                command.CommandText = "SELECT datatype.temporal_operation($1, 'format', $2, '')";
                command.Parameters.AddWithValue(type);
                command.Parameters.AddWithValue(input);
                Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
                if (iso is not null)
                {
                    command.CommandText = "SELECT datatype.temporal_operation($1, 'iso', $2, '')";
                    Assert.AreEqual(iso, await command.ExecuteScalarAsync(token));
                }
            }, context.CancellationToken);

    /// <summary>
    /// Verifies invalid syntax, overflow, unsupported units, and unknown zones recover inside the managed callback.
    /// </summary>
    /// <param name="type">The temporal type.</param>
    /// <param name="operation">The failing operation.</param>
    /// <param name="left">The primary operand.</param>
    /// <param name="right">The secondary operand.</param>
    /// <param name="sqlState">The required native error code.</param>
    [TestMethod]
    [DataRow("date", "format", "not a date", "", "22007")]
    [DataRow("date", "format", "2023-02-29", "", "22008")]
    [DataRow("date", "format", "5874898-01-01", "", "22008")]
    [DataRow("date", "days", "5874897-12-31", "1", "22008")]
    [DataRow("date", "timestamp", "5874897-12-31", "", "22008")]
    [DataRow("date", "difference", "infinity", "infinity", "22008")]
    [DataRow("time", "format", "25:00", "", "22008")]
    [DataRow("time", "part", "12:00", "Year", "0A000")]
    [DataRow("timetz", "format", "12:00+16", "", "22009")]
    [DataRow("timestamp", "zone", "2024-03-10 12:00", "Ankus/Unknown_Zone", "22023")]
    [DataRow("timestamp", "truncate", "2024-03-10 12:00", "Epoch", "22023")]
    [DataRow("timestamp", "add", "294276-12-31 23:59:59.999999", "1 microsecond", "22008")]
    [DataRow("timestamptz", "format", "2024-03-10 Ankus/Unknown_Zone", "", "22023")]
    [DataRow("timestamptz", "difference", "infinity", "infinity", "22008")]
    [DataRow("interval", "divide", "1 month", "0", "22012")]
    [DataRow("interval", "format", "2147483648 months", "", "22015")]
    [DataRow("interval", "multiply", "2147483647 months", "2", "22008")]
    [DataRow("interval", "subtract", "infinity", "infinity", "22008")]
    public Task TemporalErrorsPreserveWritesAndManagedUnwinding(string type, string operation, string left, string right, string sqlState)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(TemporalErrorsPreserveWritesAndManagedUnwinding),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.temporal_operation_recovery($1, $2, $3, $4)", connection, transaction);
                command.Parameters.AddWithValue(type);
                command.Parameters.AddWithValue(operation);
                command.Parameters.AddWithValue(left);
                command.Parameters.AddWithValue(right);
                Assert.AreEqual($"{sqlState}:True:1:2", await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies TryParse returns a default out value for bad input and an exact value for valid input.
    /// </summary>
    /// <param name="type">The temporal type.</param>
    /// <param name="input">The candidate text.</param>
    /// <param name="expected">The success flag and out value.</param>
    [TestMethod]
    [DataRow("date", "2023-02-29", "False:2000-01-01")]
    [DataRow("date", "infinity", "True:infinity")]
    [DataRow("date", null, "False:2000-01-01")]
    [DataRow("time", "", "False:00:00:00")]
    [DataRow("time", "24:00", "True:24:00:00")]
    [DataRow("timetz", "12:00+16", "False:00:00:00+00")]
    [DataRow("timetz", "12:00+05:30:17", "True:12:00:00+05:30:17")]
    [DataRow("timestamp", "294277-01-01", "False:2000-01-01 00:00:00")]
    [DataRow("timestamp", "0001-01-01 BC", "True:0001-01-01 00:00:00 BC")]
    [DataRow("timestamptz", "invalid", "False:2000-01-01 00:00:00+00")]
    [DataRow("timestamptz", "2024-03-10 12:00-04", "True:2024-03-10 16:00:00+00")]
    [DataRow("interval", "2147483648 months", "False:00:00:00")]
    [DataRow("interval", "1 month -2 days", "True:1 mon -2 days")]
    public Task TryParseDistinguishesInvalidInputFromValidValues(string type, string? input, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(TryParseDistinguishesInvalidInputFromValidValues),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SET LOCAL TIME ZONE 'UTC'", connection, transaction);
                await command.ExecuteNonQueryAsync(token);
                command.CommandText = "SELECT datatype.try_parse_temporal($1, $2)";
                command.Parameters.AddWithValue(type);
                command.Parameters.Add(new NpgsqlParameter<string?> { TypedValue = input });
                Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies invalid UTF-16 becomes a failed parse rather than reaching the PostgreSQL input routine.
    /// </summary>
    [TestMethod]
    public Task TryParseRejectsInvalidManagedEncoding()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(TryParseRejectsInvalidManagedEncoding),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.try_parse_invalid_utf16()", connection, transaction);
                Assert.AreEqual("False:2000-01-01", await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);
}
