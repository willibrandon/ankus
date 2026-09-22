using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Compares temporal accessor and timezone contracts with PostgreSQL's independent native operations.
/// </summary>
/// <param name="context">The test context.</param>
[TestClass]
public sealed class TemporalParityTests(TestContext context)
{
    /// <summary>
    /// Full-range date fields and epochs retain PostgreSQL's BC and Gregorian calendar rules.
    /// </summary>
    /// <param name="value">The date input.</param>
    [TestMethod]
    [DataRow("4714-11-24 BC")]
    [DataRow("5874897-12-31")]
    [DataRow("0001-02-29 BC")]
    [DataRow("0001-12-31 BC")]
    [DataRow("0001-01-01")]
    [DataRow("0401-02-29 BC")]
    [DataRow("0101-03-01 BC")]
    [DataRow("1900-03-01")]
    [DataRow("2000-02-29")]
    [DataRow("2100-03-01")]
    [DataRow("1969-12-31")]
    [DataRow("1970-01-01")]
    [DataRow("1999-12-31")]
    [DataRow("2000-01-01")]
    public Task DateFieldsMatchPostgresAcrossFullRange(string value)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DateFieldsMatchPostgresAcrossFullRange), async (connection, transaction, token) =>
        {
            string parts = DatePartsSql;
            await using var command = new NpgsqlCommand($"""
                WITH input AS (SELECT $1::date AS value)
                SELECT ARRAY[{parts},{parts}, extract(julian FROM value)::bigint,
                    (value - date '1970-01-01')::bigint, extract(epoch FROM value)::bigint],
                    datatype.parity_date_fields(value) FROM input
                """, connection, transaction);
            command.Parameters.AddWithValue(value);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreSequenceEqual(reader.GetFieldValue<long[]>(0), reader.GetFieldValue<long[]>(1));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Detached timestamp access uses floor division at negative microseconds and retains full finite range.
    /// </summary>
    /// <param name="value">The timestamp.</param>
    [TestMethod]
    [DataRow("1999-12-31 23:59:59.999999")]
    [DataRow("1999-12-30 23:59:59.999999")]
    [DataRow("2000-01-01 00:00:00.000001")]
    [DataRow("4714-11-24 00:00:00 BC")]
    [DataRow("294276-12-31 23:59:59.999999")]
    [DataRow("0001-02-29 12:34:56.123456 BC")]
    [DataRow("1900-03-01 00:00:00")]
    public Task TimestampFieldsMatchPostgres(string value)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(TimestampFieldsMatchPostgres), async (connection, transaction, token) =>
        {
            string parts = DatePartsSql + "," + TimePartsSql;
            await using var command = new NpgsqlCommand($"WITH input AS (SELECT $1::timestamp AS value) SELECT ARRAY[{parts},{parts}], datatype.parity_timestamp_fields(value) FROM input", connection, transaction);
            command.Parameters.AddWithValue(value);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreSequenceEqual(reader.GetFieldValue<long[]>(0), reader.GetFieldValue<long[]>(1));
        }, context.CancellationToken);

    /// <summary>
    /// Instant fields follow session-zone extraction even when local timestamp conversion is out of range.
    /// </summary>
    /// <param name="value">The finite UTC instant.</param>
    /// <param name="zone">The session timezone.</param>
    /// <param name="castFails">Whether converting to a local timestamp must reject the rotated endpoint.</param>
    [TestMethod]
    [DataRow("4714-11-24 00:00:00+00 BC", "Etc/GMT+12", true)]
    [DataRow("294276-12-31 23:59:59.999999+00", "Pacific/Kiritimati", true)]
    [DataRow("1999-12-31 23:59:59.999999+00", "UTC", false)]
    [DataRow("2024-03-10 06:59:59.999999+00", "America/New_York", false)]
    [DataRow("2024-03-10 07:00:00+00", "America/New_York", false)]
    [DataRow("2024-11-03 05:30:00+00", "America/New_York", false)]
    [DataRow("2024-11-03 06:30:00+00", "America/New_York", false)]
    [DataRow("1880-01-01 12:34:56.123456+00", "America/New_York", false)]
    [DataRow("0001-02-29 12:34:56.123456+00 BC", "Asia/Kathmandu", false)]
    public Task TimestampFieldsFollowSessionZoneAtFiniteEndpoints(string value, string zone, bool castFails)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(TimestampFieldsFollowSessionZoneAtFiniteEndpoints), async (connection, transaction, token) =>
        {
            await SetZoneAsync(connection, transaction, zone, token);
            string parts = DatePartsSql + "," + TimePartsSql;
            await using (var command = new NpgsqlCommand($"WITH input AS (SELECT $1::timestamptz AS value) SELECT ARRAY[{parts},{parts}], datatype.parity_instant_fields(value), current_setting('TimeZone') FROM input", connection, transaction))
            {
                command.Parameters.AddWithValue(value);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreSequenceEqual(reader.GetFieldValue<long[]>(0), reader.GetFieldValue<long[]>(1));
                Assert.AreEqual(zone, reader.GetString(2));
            }

            if (castFails)
            {
                await using var savepoint = new NpgsqlCommand("SAVEPOINT parity_fields", connection, transaction);
                await savepoint.ExecuteNonQueryAsync(token);
                await using var cast = new NpgsqlCommand("SELECT ($1::timestamptz)::timestamp", connection, transaction);
                cast.Parameters.AddWithValue(value);
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(async () => await cast.ExecuteScalarAsync(token));
                Assert.AreEqual("22008", error.SqlState);
                await using var recover = new NpgsqlCommand("ROLLBACK TO SAVEPOINT parity_fields; SELECT 42", connection, transaction);
                Assert.AreEqual(42, await recover.ExecuteScalarAsync(token));
            }
        }, context.CancellationToken);

    /// <summary>
    /// Time and timetz expose fractional-only microseconds, complete fractional seconds and signed offset components.
    /// </summary>
    /// <param name="type">The PostgreSQL type.</param>
    /// <param name="value">The temporal input.</param>
    [TestMethod]
    [DataRow("time", "00:00:00")]
    [DataRow("time", "24:00:00")]
    [DataRow("time", "12:34:56.123456")]
    [DataRow("time", "23:59:59.999999")]
    [DataRow("timetz", "24:00:00+00")]
    [DataRow("timetz", "12:34:56.123456+05:30:17")]
    [DataRow("timetz", "12:34:56.123456-05:30:17")]
    [DataRow("timetz", "00:00:00-00:00:59")]
    [DataRow("timetz", "00:00:00+00:59:59")]
    [DataRow("timetz", "23:59:59.999999-15:59:59")]
    public Task TimeFieldsMatchPostgresIncludingEndOfDay(string type, string value)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(TimeFieldsMatchPostgresIncludingEndOfDay), async (connection, transaction, token) =>
        {
            string offset = type == "timetz" ? ",extract(timezone_hour FROM value)::bigint,extract(timezone_minute FROM value)::bigint,extract(timezone FROM value)::bigint" : "";
            string function = type == "timetz" ? "parity_time_zone_fields" : "parity_time_fields";
            await using var command = new NpgsqlCommand($"""
                WITH input AS (SELECT $1::{type} AS value)
                SELECT ARRAY[{TimePartsSql},{TimePartsSql}{offset}], datatype.{function}(value),
                    extract(second FROM value)::double precision, datatype.parity_fractional_second($2,$1),
                    extract(microseconds FROM value)::bigint FROM input
                """, connection, transaction);
            command.Parameters.AddWithValue(value);
            command.Parameters.AddWithValue(type);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            long[] actual = reader.GetFieldValue<long[]>(1);
            Assert.AreSequenceEqual(reader.GetFieldValue<long[]>(0), actual);
            Assert.AreEqual(reader.GetDouble(2), reader.GetDouble(3), 0.000000000001);
            Assert.AreEqual(reader.GetInt64(4), actual[2] * 1_000_000 + actual[3]);
        }, context.CancellationToken);

    /// <summary>
    /// Fractional seconds on both timestamp types match EXTRACT rather than dropping or duplicating whole seconds.
    /// </summary>
    /// <param name="type">The timestamp type.</param>
    /// <param name="value">The timestamp input.</param>
    [TestMethod]
    [DataRow("timestamp", "1999-12-31 23:59:59.999999")]
    [DataRow("timestamp", "294276-12-31 12:34:56.123456")]
    [DataRow("timestamptz", "1880-01-01 12:34:56.123456+00")]
    [DataRow("timestamptz", "2024-03-10 06:59:59.999999+00")]
    public Task TimestampFractionalSecondsMatchPostgres(string type, string value)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(TimestampFractionalSecondsMatchPostgres), async (connection, transaction, token) =>
        {
            await SetZoneAsync(connection, transaction, "America/New_York", token);
            await using var command = new NpgsqlCommand($"SELECT extract(second FROM $1::{type})::double precision, datatype.parity_fractional_second($2,$1)", connection, transaction);
            command.Parameters.AddWithValue(value);
            command.Parameters.AddWithValue(type);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(reader.GetDouble(0), reader.GetDouble(1), 0.000000000001);
        }, context.CancellationToken);

    /// <summary>
    /// Explicit instant offset lookup honors seasons, overlap instants, historical seconds, abbreviation precedence and POSIX signs.
    /// </summary>
    /// <param name="zone">The named, abbreviated or POSIX zone.</param>
    /// <param name="instant">The instant to resolve.</param>
    /// <param name="offset">The independently known offset east of UTC.</param>
    [TestMethod]
    [DataRow("America/New_York", "2024-01-01 12:00+00", -18000L)]
    [DataRow("america/new_york", "2024-07-01 12:00+00", -14400L)]
    [DataRow("America/New_York", "2024-11-03 05:30+00", -14400L)]
    [DataRow("America/New_York", "2024-11-03 06:30+00", -18000L)]
    [DataRow("America/New_York", "1880-01-01 12:00+00", -17762L)]
    [DataRow("Asia/Kathmandu", "2024-01-01 12:00+00", 20700L)]
    [DataRow("UTC", "0001-02-29 12:00+00 BC", 0L)]
    [DataRow("EST", "2024-07-01 12:00+00", -18000L)]
    [DataRow("EDT", "2024-01-01 12:00+00", -14400L)]
    [DataRow("MSK", "2012-01-01 12:00+00", 14400L)]
    [DataRow("MSK", "2024-01-01 12:00+00", 10800L)]
    [DataRow("Etc/GMT+5", "2024-01-01 12:00+00", -18000L)]
    [DataRow("UTC-05:30:17", "2024-01-01 12:00+00", 19817L)]
    [DataRow("UTC+20", "2024-01-01 12:00+00", -72000L)]
    public Task NamedZoneOffsetsUseSpecifiedInstant(string zone, string instant, long offset)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NamedZoneOffsetsUseSpecifiedInstant), async (connection, transaction, token) =>
        {
            await SetZoneAsync(connection, transaction, "Pacific/Auckland", token);
            await using var command = new NpgsqlCommand("""
                SELECT extract(epoch FROM (($2::timestamptz AT TIME ZONE $1) - ($2::timestamptz AT TIME ZONE 'UTC')))::bigint,
                    datatype.parity_zone_offset($1,$2::timestamptz), current_setting('TimeZone'), current_setting('DateStyle')
                """, connection, transaction);
            command.Parameters.AddWithValue(zone);
            command.Parameters.AddWithValue(instant);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(offset, reader.GetInt64(0));
            Assert.AreEqual(offset, reader.GetInt64(1));
            Assert.AreEqual("Pacific/Auckland", reader.GetString(2));
            Assert.AreEqual("ISO, YMD", reader.GetString(3));
        }, context.CancellationToken);

    /// <summary>
    /// Default offset lookup is the native transaction-start operation, independent of the session display zone.
    /// </summary>
    /// <param name="zone">The requested timezone.</param>
    [TestMethod]
    [DataRow("America/New_York")]
    [DataRow("Asia/Kathmandu")]
    [DataRow("EST")]
    [DataRow("MSK")]
    [DataRow("UTC-05:30:17")]
    public Task NamedZoneOffsetsUseTransactionStart(string zone)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NamedZoneOffsetsUseTransactionStart), async (connection, transaction, token) =>
        {
            await SetZoneAsync(connection, transaction, "Pacific/Auckland", token);
            await using var command = new NpgsqlCommand("""
                SELECT extract(timezone FROM timetz '00:00+00' AT TIME ZONE $1)::bigint,
                    datatype.parity_zone_offset($1,NULL), datatype.parity_zone_offset($1,transaction_timestamp()),
                    current_setting('TimeZone')
                """, connection, transaction);
            command.Parameters.AddWithValue(zone);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(reader.GetInt64(0), reader.GetInt64(1));
            Assert.AreEqual(reader.GetInt64(0), reader.GetInt64(2));
            Assert.AreEqual("Pacific/Auckland", reader.GetString(3));
        }, context.CancellationToken);

    /// <summary>
    /// Explicit offset lookup supports finite endpoints without constructing an out-of-range local timestamp.
    /// </summary>
    /// <param name="instant">The endpoint instant.</param>
    /// <param name="zone">The outward-rotating timezone.</param>
    /// <param name="offset">The exact timezone offset.</param>
    [TestMethod]
    [DataRow("4714-11-24 00:00+00 BC", "Etc/GMT+12", -43200L)]
    [DataRow("294276-12-31 23:59:59.999999+00", "Pacific/Kiritimati", 50400L)]
    public Task NamedZoneOffsetsSupportFiniteEndpoints(string instant, string zone, long offset)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NamedZoneOffsetsSupportFiniteEndpoints), async (connection, transaction, token) =>
        {
            await SetZoneAsync(connection, transaction, zone, token);
            await using var command = new NpgsqlCommand("SELECT extract(timezone FROM $1::timestamptz)::bigint, datatype.parity_zone_offset($2,$1::timestamptz),current_setting('TimeZone')", connection, transaction);
            command.Parameters.AddWithValue(instant);
            command.Parameters.AddWithValue(zone);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(offset, reader.GetInt64(0));
            Assert.AreEqual(offset, reader.GetInt64(1));
            Assert.AreEqual(zone, reader.GetString(2));
        }, context.CancellationToken);

    /// <summary>
    /// Named-zone construction assigns an offset while preserving requested wall time and end of day.
    /// </summary>
    /// <param name="hour">The hour.</param>
    /// <param name="minute">The minute.</param>
    /// <param name="second">The seconds and fraction.</param>
    /// <param name="zone">The named timezone.</param>
    [TestMethod]
    [DataRow(12, 34, 56.123456, "America/New_York")]
    [DataRow(12, 34, 56.123456, "Asia/Kathmandu")]
    [DataRow(24, 0, 0d, "UTC-05:30:17")]
    [DataRow(0, 0, 0d, "EST")]
    public Task NamedZoneTimeConstructionPreservesWallClock(int hour, int minute, double second, string zone)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NamedZoneTimeConstructionPreservesWallClock), async (connection, transaction, token) =>
        {
            await SetZoneAsync(connection, transaction, "Pacific/Auckland", token);
            await using var command = new NpgsqlCommand("""
                WITH actual AS MATERIALIZED (SELECT datatype.parity_named_time($1,$2,$3,$4) AS value)
                SELECT encode(time_send(make_time($1,$2,$3)),'hex'), encode(time_send(value::time),'hex'),
                    extract(timezone FROM timetz '00:00+00' AT TIME ZONE $4)::bigint,
                    extract(timezone FROM value)::bigint, current_setting('TimeZone') FROM actual
                """, connection, transaction);
            command.Parameters.AddWithValue(hour);
            command.Parameters.AddWithValue(minute);
            command.Parameters.AddWithValue(second);
            command.Parameters.AddWithValue(zone);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(reader.GetString(0), reader.GetString(1));
            Assert.AreEqual(reader.GetInt64(2), reader.GetInt64(3));
            Assert.AreEqual("Pacific/Auckland", reader.GetString(4));
        }, context.CancellationToken);

    /// <summary>
    /// UTC helpers preserve timestamp bits/infinities and normalize timetz across either midnight boundary.
    /// </summary>
    /// <param name="type">The input type.</param>
    /// <param name="value">The temporal input.</param>
    [TestMethod]
    [DataRow("timestamptz", "1999-12-31 23:59:59.999999+00")]
    [DataRow("timestamptz", "4714-11-24 00:00+00 BC")]
    [DataRow("timestamptz", "294276-12-31 23:59:59.999999+00")]
    [DataRow("timestamptz", "infinity")]
    [DataRow("timestamptz", "-infinity")]
    [DataRow("timetz", "24:00+00")]
    [DataRow("timetz", "00:00+05:30:17")]
    [DataRow("timetz", "23:59:59.999999-05:30:17")]
    [DataRow("timetz", "12:34:56.123456+00")]
    public Task UtcConveniencesMatchNativeValues(string type, string value)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(UtcConveniencesMatchNativeValues), async (connection, transaction, token) =>
        {
            await SetZoneAsync(connection, transaction, "Asia/Kathmandu", token);
            string expected = type == "timetz" ? "($1::timetz AT TIME ZONE 'UTC')::time" : "$1::timestamptz AT TIME ZONE 'UTC'";
            string actual = type == "timetz" ? "datatype.parity_utc_time($1::timetz)" : "datatype.parity_utc_instant($1::timestamptz)";
            string send = type == "timetz" ? "time_send" : "timestamp_send";
            await using var command = new NpgsqlCommand($"SELECT encode({send}({expected}),'hex'), encode({send}({actual}),'hex')", connection, transaction);
            command.Parameters.AddWithValue(value);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(reader.GetString(0), reader.GetString(1));
        }, context.CancellationToken);

    /// <summary>
    /// Interval timezone overloads preserve native signs, second truncation, calendar crossing and infinity ordering.
    /// </summary>
    /// <param name="type">The input type.</param>
    /// <param name="value">The input value.</param>
    /// <param name="offset">The interval timezone.</param>
    [TestMethod]
    [DataRow("timestamp", "2024-03-10 02:30:00.123456", "05:30:17")]
    [DataRow("timestamp", "0001-01-01 00:00:00", "-05:30:17.999999")]
    [DataRow("timestamp", "infinity", "1 month")]
    [DataRow("timestamptz", "2024-11-03 05:30+00", "-05:30:17")]
    [DataRow("timestamptz", "2024-01-01 00:00+00", "05:30:17.999999")]
    [DataRow("timestamptz", "-infinity", "infinity")]
    [DataRow("timetz", "24:00+00", "00:00")]
    [DataRow("timetz", "23:59:59.999999-05:30:17", "05:45")]
    [DataRow("timetz", "00:00+05:30:17", "-05:30:17.999999")]
    [DataRow("timetz", "00:00+00", "15:59:59")]
    [DataRow("timetz", "00:00+00", "-15:59:59")]
    public Task IntervalZoneConversionsMatchPostgres(string type, string value, string offset)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(IntervalZoneConversionsMatchPostgres), async (connection, transaction, token) =>
        {
            await SetZoneAsync(connection, transaction, "America/New_York", token);
            await using var command = new NpgsqlCommand($"SELECT ($1::{type} AT TIME ZONE $2::interval)::text, datatype.parity_interval_zone($3,$1,$2::interval), current_setting('TimeZone')", connection, transaction);
            command.Parameters.AddWithValue(value);
            command.Parameters.AddWithValue(offset);
            command.Parameters.AddWithValue(type);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(reader.GetString(0), reader.GetString(1));
            Assert.AreEqual("America/New_York", reader.GetString(2));
        }, context.CancellationToken);

    /// <summary>
    /// Live owned text parses inside independently sampled clock bounds and uses the session timezone.
    /// </summary>
    /// <param name="zone">The display timezone.</param>
    /// <param name="suffix">The expected stable zone suffix.</param>
    [TestMethod]
    [DataRow("UTC", "UTC")]
    [DataRow("Etc/GMT+5", "-05")]
    [DataRow("Asia/Kathmandu", "+0545")]
    public Task TimeOfDayReturnsOwnedServerClockText(string zone, string suffix)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(TimeOfDayReturnsOwnedServerClockText), async (connection, transaction, token) =>
        {
            await SetZoneAsync(connection, transaction, zone, token);
            await using (var style = new NpgsqlCommand("SET LOCAL DateStyle='German'", connection, transaction))
            {
                await style.ExecuteNonQueryAsync(token);
            }

            await using var command = new NpgsqlCommand("""
                WITH before AS MATERIALIZED (SELECT clock_timestamp() AS lower),
                owned AS MATERIALIZED (SELECT lower, datatype.parity_owned_time_of_day() AS value FROM before),
                after AS MATERIALIZED (SELECT lower, value, clock_timestamp() AS upper FROM owned)
                SELECT value, value::timestamptz >= lower, value::timestamptz <= upper,
                    current_setting('TimeZone'), current_setting('DateStyle') FROM after
                """, connection, transaction);
            for (int invocation = 0; invocation < 2; invocation++)
            {
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.MatchesRegex("^(Mon|Tue|Wed|Thu|Fri|Sat|Sun) (Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) [ 0-9][0-9] [0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{6} [0-9]{4} ", reader.GetString(0));
                Assert.EndsWith(" " + suffix, reader.GetString(0));
                Assert.IsTrue(reader.GetBoolean(1));
                Assert.IsTrue(reader.GetBoolean(2));
                Assert.AreEqual(zone, reader.GetString(3));
                Assert.AreEqual("German, DMY", reader.GetString(4));
            }
        }, context.CancellationToken);

    /// <summary>
    /// Raw saturation and Euclidean wrapping match independently specified PostgreSQL binary values.
    /// </summary>
    /// <param name="type">The temporal type.</param>
    /// <param name="raw">Raw days or microseconds.</param>
    /// <param name="west">Raw PostgreSQL seconds west of UTC.</param>
    /// <param name="expected">The independent PostgreSQL literal.</param>
    [TestMethod]
    [DataRow("date", -2451546L, 0, "-infinity")]
    [DataRow("date", -2451545L, 0, "4714-11-24 BC")]
    [DataRow("date", -2451544L, 0, "4714-11-25 BC")]
    [DataRow("date", 2145031947L, 0, "5874897-12-30")]
    [DataRow("date", 2145031948L, 0, "5874897-12-31")]
    [DataRow("date", 2145031949L, 0, "infinity")]
    [DataRow("date", -2147483648L, 0, "-infinity")]
    [DataRow("date", 2147483647L, 0, "infinity")]
    [DataRow("timestamp", -211813488000000001L, 0, "-infinity")]
    [DataRow("timestamp", -211813488000000000L, 0, "4714-11-24 00:00:00 BC")]
    [DataRow("timestamp", -211813487999999999L, 0, "4714-11-24 00:00:00.000001 BC")]
    [DataRow("timestamp", 9223371331199999999L, 0, "294276-12-31 23:59:59.999999")]
    [DataRow("timestamp", 9223371331200000000L, 0, "infinity")]
    [DataRow("timestamp", long.MinValue, 0, "-infinity")]
    [DataRow("timestamp", long.MaxValue, 0, "infinity")]
    [DataRow("timestamptz", -211813488000000001L, 0, "-infinity")]
    [DataRow("timestamptz", -211813488000000000L, 0, "4714-11-24 00:00:00+00 BC")]
    [DataRow("timestamptz", -211813487999999999L, 0, "4714-11-24 00:00:00.000001+00 BC")]
    [DataRow("timestamptz", 9223371331199999999L, 0, "294276-12-31 23:59:59.999999+00")]
    [DataRow("timestamptz", 9223371331200000000L, 0, "infinity")]
    [DataRow("timestamptz", long.MinValue, 0, "-infinity")]
    [DataRow("timestamptz", long.MaxValue, 0, "infinity")]
    [DataRow("time", long.MinValue, 0, "19:59:05.224192")]
    [DataRow("time", long.MaxValue, 0, "04:00:54.775807")]
    [DataRow("time", -1L, 0, "23:59:59.999999")]
    [DataRow("time", -86400000000L, 0, "00:00")]
    [DataRow("time", 86400000000L, 0, "00:00")]
    [DataRow("time", 86400000001L, 0, "00:00:00.000001")]
    [DataRow("timetz", -1L, -1, "23:59:59.999999-15:59:59")]
    [DataRow("timetz", 86400000000L, -57600, "00:00+00")]
    [DataRow("timetz", 86400000001L, 57601, "00:00:00.000001-00:00:01")]
    [DataRow("timetz", long.MinValue, int.MinValue, "19:59:05.224192-04:45:52")]
    [DataRow("timetz", long.MaxValue, int.MaxValue, "04:00:54.775807-11:14:07")]
    public Task RawTemporalFactoriesMatchNativeBinaryValues(string type, long raw, int west, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RawTemporalFactoriesMatchNativeBinaryValues), async (connection, transaction, token) =>
        {
            await SetZoneAsync(connection, transaction, "UTC", token);
            await using var command = new NpgsqlCommand($"SELECT encode({type}_send($4::{type}),'hex'), encode({type}_send(datatype.parity_raw_temporal($1,$2,$3)::{type}),'hex')", connection, transaction);
            command.Parameters.AddWithValue(type);
            command.Parameters.AddWithValue(raw);
            command.Parameters.AddWithValue(west);
            command.Parameters.AddWithValue(expected);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(reader.GetString(0), reader.GetString(1));
        }, context.CancellationToken);

    /// <summary>
    /// New native errors leave prior writes, prepared plans, finally execution and memory context counts intact.
    /// </summary>
    /// <param name="operation">The invalid operation.</param>
    /// <param name="code">Its native SQLSTATE or exact managed representability exception.</param>
    [TestMethod]
    [DataRow("zone", "22023")]
    [DataRow("zone-empty", "22023")]
    [DataRow("zone-space", "22023")]
    [DataRow("zone-nul", "ArgumentException")]
    [DataRow("zone-encoding", "EncoderFallbackException")]
    [DataRow("zone-instant", "22023")]
    [DataRow("zone-positive-infinity", "ArgumentOutOfRangeException")]
    [DataRow("zone-negative-infinity", "ArgumentOutOfRangeException")]
    [DataRow("named-time", "22023")]
    [DataRow("named-time-offset", "ArgumentOutOfRangeException")]
    [DataRow("timestamp-month", "22023")]
    [DataRow("timestamp-day", "22023")]
    [DataRow("timestamp-infinite", "22023")]
    [DataRow("instant-month", "22023")]
    [DataRow("instant-day", "22023")]
    [DataRow("instant-infinite", "22023")]
    [DataRow("time-month", "22023")]
    [DataRow("time-day", "22023")]
    [DataRow("time-infinite", "22023")]
    [DataRow("time-offset", "ArgumentOutOfRangeException")]
    [DataRow("time-offset-negative", "ArgumentOutOfRangeException")]
    [DataRow("timestamp-range", "22008")]
    [DataRow("instant-range", "22008")]
    public Task TemporalParityErrorsPreserveState(string operation, string code)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(TemporalParityErrorsPreserveState), async (connection, transaction, token) =>
        {
            await SetZoneAsync(connection, transaction, "Asia/Kathmandu", token);
            await using var command = new NpgsqlCommand("SELECT datatype.parity_temporal_recovery($1)", connection, transaction);
            command.Parameters.AddWithValue(operation);
            Assert.AreEqual($"{code}:50:50:50:0:2:True", await command.ExecuteScalarAsync(token));
            await using var recover = new NpgsqlCommand("SELECT 42, current_setting('TimeZone')", connection, transaction);
            await using NpgsqlDataReader reader = await recover.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(42, reader.GetInt32(0));
            Assert.AreEqual("Asia/Kathmandu", reader.GetString(1));
        }, context.CancellationToken);

    /// <summary>
    /// Gets independent integer date extraction expressions.
    /// </summary>
    private const string DatePartsSql = "extract(year FROM value)::bigint,extract(month FROM value)::bigint,extract(day FROM value)::bigint";

    /// <summary>
    /// Gets integer clock fields with fractional-only microseconds, avoiding numeric-to-integer rounding.
    /// </summary>
    private const string TimePartsSql = "extract(hour FROM value)::bigint,extract(minute FROM value)::bigint,trunc(extract(second FROM value))::bigint,mod(extract(microseconds FROM value),1000000)::bigint";

    /// <summary>
    /// Sets transaction-local display rules without interpolating timezone input into SQL.
    /// </summary>
    private static async Task SetZoneAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string zone, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT set_config('TimeZone',$1,true),set_config('DateStyle','ISO, YMD',true)", connection, transaction);
        command.Parameters.AddWithValue(zone);
        await command.ExecuteNonQueryAsync(token);
    }
}
