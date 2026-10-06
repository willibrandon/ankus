using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes pgrx's datetime example through the independently published Native AOT library.
/// </summary>
/// <param name="context">The current test cancellation context.</param>
[TestClass]
[DoNotParallelize]
public sealed class DateTimeExampleTests(TestContext context)
{
    /// <summary>
    /// The independently required upstream clock column names in their exact order.
    /// </summary>
    private static readonly string[] s_clockNames = ["now", "transaction_timestamp", "statement_timestamp", "clock_timestamp"];

    /// <summary>
    /// Preserves native arithmetic bytes, timezone offsets, full-range values and strict SQL nulls.
    /// </summary>
    /// <param name="kind">The independently selected native input type.</param>
    /// <param name="send">The native result's binary sender.</param>
    /// <param name="value">The input value.</param>
    /// <param name="interval">The independent month, day and microsecond interval.</param>
    /// <param name="zone">The session timezone used by native calendar arithmetic.</param>
    [TestMethod]
    [DataRow("date", "timestamp_send", "2000-01-31", "1 month 2 days 03:04:05.000006", "UTC")]
    [DataRow("date", "timestamp_send", "2000-02-29", "1 year", "UTC")]
    [DataRow("date", "timestamp_send", "40000-02-28", "0", "UTC")]
    [DataRow("date", "timestamp_send", "0044-03-15 BC", "-2 days", "UTC")]
    [DataRow("date", "timestamp_send", "infinity", "1 month", "UTC")]
    [DataRow("date", "timestamp_send", "-infinity", "1 day", "UTC")]
    [DataRow("time", "time_send", "23:59:59.999999", "00:00:00.000002", "UTC")]
    [DataRow("time", "time_send", "24:00:00", "0", "UTC")]
    [DataRow("time", "time_send", "00:00:00", "-1 month -1 day -01:00:00", "UTC")]
    [DataRow("timetz", "timetz_send", "23:59:59.999999+05:45", "00:00:00.000002", "UTC")]
    [DataRow("timetz", "timetz_send", "24:00:00-03:30", "0", "UTC")]
    [DataRow("timetz", "timetz_send", "00:00:00+00:09:21", "-1 month -1 day -01:00:00", "UTC")]
    [DataRow("timestamp", "timestamp_send", "2000-01-31 23:59:59.999999", "1 month 00:00:00.000002", "UTC")]
    [DataRow("timestamp", "timestamp_send", "294000-01-01 00:00:00.000001", "0", "UTC")]
    [DataRow("timestamp", "timestamp_send", "0044-03-15 12:34:56.123456 BC", "1 day", "UTC")]
    [DataRow("timestamp", "timestamp_send", "infinity", "1 month", "UTC")]
    [DataRow("timestamp", "timestamp_send", "-infinity", "1 day", "UTC")]
    [DataRow("timestamptz", "timestamptz_send", "2024-03-09 12:00:00-05", "1 day", "America/New_York")]
    [DataRow("timestamptz", "timestamptz_send", "2024-03-09 12:00:00-05", "24 hours", "America/New_York")]
    [DataRow("timestamptz", "timestamptz_send", "2024-11-02 12:00:00-04", "1 day", "America/New_York")]
    [DataRow("timestamptz", "timestamptz_send", "2024-11-02 12:00:00-04", "24 hours", "America/New_York")]
    [DataRow("timestamptz", "timestamptz_send", "40000-02-28 00:00:00.000001+00", "0", "UTC")]
    [DataRow("timestamptz", "timestamptz_send", "infinity", "1 month", "UTC")]
    [DataRow("timestamptz", "timestamptz_send", "-infinity", "1 day", "UTC")]
    [DataRow("interval", "interval_send", "0", "0", "UTC")]
    [DataRow("interval", "interval_send", "1 month -30 days 00:00:00.000001", "-2 months 7 days -01:02:03.000004", "UTC")]
    [DataRow("interval", "interval_send", "2147483646 months", "1 month", "UTC")]
    [DataRow("interval", "interval_send", "-2147483647 days", "-1 day", "UTC")]
    public Task DateTimeSampleArithmeticPreservesNativeValues(string kind, string send, string value, string interval, string zone)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DateTimeSampleArithmeticPreservesNativeValues), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await SetZoneAsync(connection, transaction, zone, token);
            await using var command = new NpgsqlCommand($"""
                SELECT {send}($1::{kind} + $2::interval),
                    {send}(datetime_example.add_interval(v => $1::{kind}, i => $2::interval)),
                    {send}($1::{kind} - $2::interval),
                    {send}(datetime_example.subtract_interval(v => $1::{kind}, i => $2::interval)),
                    datetime_example.add_interval(NULL::{kind}, $2::interval) IS NULL,
                    datetime_example.add_interval($1::{kind}, NULL::interval) IS NULL,
                    datetime_example.subtract_interval(NULL::{kind}, $2::interval) IS NULL,
                    datetime_example.subtract_interval($1::{kind}, NULL::interval) IS NULL
                """, connection, transaction);
            command.Parameters.AddWithValue(value);
            command.Parameters.AddWithValue(interval);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreSequenceEqual(reader.GetFieldValue<byte[]>(0), reader.GetFieldValue<byte[]>(1));
            Assert.AreSequenceEqual(reader.GetFieldValue<byte[]>(2), reader.GetFieldValue<byte[]>(3));
            for (int column = 4; column < 8; column++)
            {
                Assert.IsTrue(reader.GetBoolean(column));
            }

            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Preserves PostgreSQL's interval component rounding for multiplication and division.
    /// </summary>
    /// <param name="value">The independent interval.</param>
    /// <param name="factor">The finite nonzero multiplier and divisor.</param>
    [TestMethod]
    [DataRow("0", 1.0)]
    [DataRow("1 month -2 days 01:02:03.000005", 0.5)]
    [DataRow("-1 month 2 days -01:02:03.000005", -0.5)]
    [DataRow("00:00:00.000001", 2.0)]
    [DataRow("2147483646 months", 1.0)]
    public Task DateTimeSampleScalingPreservesNativeComponents(string value, double factor)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DateTimeSampleScalingPreservesNativeComponents), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await using var command = new NpgsqlCommand("""
                SELECT interval_send($1::interval * $2::double precision),
                    interval_send(datetime_example.mul_interval(i => $1::interval, v => $2::double precision)),
                    interval_send($1::interval / $2::double precision),
                    interval_send(datetime_example.div_interval(i => $1::interval, v => $2::double precision))
                """, connection, transaction);
            command.Parameters.AddWithValue(value);
            command.Parameters.AddWithValue(factor);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreSequenceEqual(reader.GetFieldValue<byte[]>(0), reader.GetFieldValue<byte[]>(1));
            Assert.AreSequenceEqual(reader.GetFieldValue<byte[]>(2), reader.GetFieldValue<byte[]>(3));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Formats both ISO overloads independently of DateStyle without changing the session timezone.
    /// </summary>
    /// <param name="value">The independently specified instant.</param>
    /// <param name="sourceZone">The session timezone.</param>
    /// <param name="targetZone">The explicit formatting timezone.</param>
    [TestMethod]
    [DataRow("2024-03-10 07:00:00.123456+00", "America/New_York", "Asia/Kathmandu")]
    [DataRow("2024-11-03 06:00:00.123456+00", "Asia/Kathmandu", "America/New_York")]
    [DataRow("1890-01-01 12:34:56.123456+00", "UTC", "Europe/Paris")]
    [DataRow("0044-03-15 12:34:56+00 BC", "UTC", "UTC")]
    [DataRow("infinity", "America/New_York", "UTC")]
    [DataRow("-infinity", "UTC", "Asia/Kathmandu")]
    public Task DateTimeSampleIsoFormattingPreservesNativeZones(string value, string sourceZone, string targetZone)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DateTimeSampleIsoFormattingPreservesNativeZones), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await SetZoneAsync(connection, transaction, targetZone, token);
            string target = await FormatNativeAsync(connection, transaction, value, token);
            await SetZoneAsync(connection, transaction, sourceZone, token);
            string source = await FormatNativeAsync(connection, transaction, value, token);
            await ExecuteAsync(connection, transaction, "SET LOCAL DateStyle = 'SQL, DMY'", token);
            await using var command = new NpgsqlCommand("""
                SELECT datetime_example.to_iso_string(tsz => $1::timestamptz),
                    datetime_example.to_iso_string(tsz => $1::timestamptz, tz => $2),
                    current_setting('TimeZone')
                """, connection, transaction);
            command.Parameters.AddWithValue(value);
            command.Parameters.AddWithValue(targetZone);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(source, reader.GetString(0));
            Assert.AreEqual(target, reader.GetString(1));
            Assert.AreEqual(sourceZone, reader.GetString(2));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Retains full-range date composition, microseconds, infinities and 24:00 rollover.
    /// </summary>
    /// <param name="date">The independent native date.</param>
    /// <param name="time">The independent native time.</param>
    [TestMethod]
    [DataRow("2000-02-29", "23:59:59.999999")]
    [DataRow("2000-12-31", "24:00:00")]
    [DataRow("40000-02-28", "00:00:00.000001")]
    [DataRow("0044-03-15 BC", "12:34:56.123456")]
    [DataRow("infinity", "24:00:00")]
    [DataRow("-infinity", "00:00:00")]
    public Task DateTimeSampleCompositionPreservesNativeTimestamp(string date, string time)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DateTimeSampleCompositionPreservesNativeTimestamp), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await using var command = new NpgsqlCommand("""
                SELECT timestamp_send($1::date + $2::time),
                    timestamp_send(datetime_example.compose_timestamp(d => $1::date, t => $2::time)),
                    datetime_example.compose_timestamp(NULL::date, $2::time) IS NULL,
                    datetime_example.compose_timestamp($1::date, NULL::time) IS NULL
                """, connection, transaction);
            command.Parameters.AddWithValue(date);
            command.Parameters.AddWithValue(time);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreSequenceEqual(reader.GetFieldValue<byte[]>(0), reader.GetFieldValue<byte[]>(1));
            Assert.IsTrue(reader.GetBoolean(2));
            Assert.IsTrue(reader.GetBoolean(3));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Interprets the input in the session zone before projecting it into the explicit destination zone.
    /// </summary>
    /// <param name="value">The independent local timestamp.</param>
    /// <param name="sourceZone">The interpretation timezone.</param>
    /// <param name="targetZone">The destination timezone.</param>
    [TestMethod]
    [DataRow("2024-03-10 02:30:00.123456", "America/New_York", "Asia/Kathmandu")]
    [DataRow("2024-11-03 01:30:00.123456", "America/New_York", "UTC")]
    [DataRow("1890-01-01 12:34:56.123456", "Europe/Paris", "UTC")]
    [DataRow("40000-02-28 00:00:00.000001", "UTC", "UTC")]
    [DataRow("infinity", "UTC", "America/New_York")]
    [DataRow("-infinity", "America/New_York", "UTC")]
    public Task DateTimeSampleProjectionPreservesNativeInterpretation(string value, string sourceZone, string targetZone)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DateTimeSampleProjectionPreservesNativeInterpretation), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await SetZoneAsync(connection, transaction, sourceZone, token);
            await using var command = new NpgsqlCommand("""
                SELECT timestamp_send($1::timestamp::timestamptz AT TIME ZONE $2),
                    timestamp_send(datetime_example.set_timezone(ts => $1::timestamp, timezone => $2)),
                    current_setting('TimeZone')
                """, connection, transaction);
            command.Parameters.AddWithValue(value);
            command.Parameters.AddWithValue(targetZone);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreSequenceEqual(reader.GetFieldValue<byte[]>(0), reader.GetFieldValue<byte[]>(1));
            Assert.AreEqual(sourceZone, reader.GetString(2));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Confirms all four clocks have their native identities and advance at the correct boundary.
    /// </summary>
    [TestMethod]
    public Task DateTimeSampleClocksPreserveTransactionAndStatementBoundaries()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DateTimeSampleClocksPreserveTransactionAndStatementBoundaries), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            DateTime transactionStart = await ScalarAsync<DateTime>(connection, transaction, "SELECT transaction_timestamp()", token);
            await ExecuteAsync(connection, transaction, "SELECT pg_sleep(0.01)", token);
            await using var command = new NpgsqlCommand("""
                SELECT clocks.*, transaction_timestamp(), statement_timestamp()
                FROM datetime_example.all_times() AS clocks
                """, connection, transaction);
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
            {
                Assert.AreSequenceEqual(s_clockNames, Enumerable.Range(0, 4).Select(reader.GetName));
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(transactionStart, reader.GetDateTime(0));
                Assert.AreEqual(transactionStart, reader.GetDateTime(1));
                Assert.AreEqual(transactionStart, reader.GetDateTime(4));
                Assert.AreEqual(reader.GetDateTime(5), reader.GetDateTime(2));
                Assert.IsGreaterThan(transactionStart, reader.GetDateTime(2));
                Assert.IsGreaterThanOrEqualTo(reader.GetDateTime(2), reader.GetDateTime(3));
                Assert.IsFalse(await reader.ReadAsync(token));
            }

            Assert.AreEqual(transactionStart, await ScalarAsync<DateTime>(connection, transaction, "SELECT transaction_timestamp()", token));
        }, context.CancellationToken);

    /// <summary>
    /// Samples only valid values within pgrx's upper-exclusive random date/time ranges.
    /// </summary>
    [TestMethod]
    public Task DateTimeSampleRandomValuesPreserveUpstreamBounds()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DateTimeSampleRandomValuesPreserveUpstreamBounds), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await using var command = new NpgsqlCommand("""
                SELECT datetime_example.random_date(), datetime_example.random_time()
                FROM generate_series(1, 128)
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            HashSet<DateOnly> dates = [];
            HashSet<TimeOnly> times = [];
            int rows = 0;
            while (await reader.ReadAsync(token))
            {
                DateOnly date = reader.GetFieldValue<DateOnly>(0);
                TimeOnly time = reader.GetFieldValue<TimeOnly>(1);
                Assert.IsInRange(1978, 2022, date.Year);
                Assert.IsInRange(1, 11, date.Month);
                Assert.IsInRange(1, Math.Min(30, DateTime.DaysInMonth(date.Year, date.Month)), date.Day);
                Assert.IsInRange(0, 22, time.Hour);
                Assert.IsInRange(0, 58, time.Minute);
                Assert.IsInRange(0, 58, time.Second);
                Assert.AreEqual(0L, time.Ticks % TimeSpan.TicksPerSecond);
                dates.Add(date);
                times.Add(time);
                rows++;
            }

            Assert.AreEqual(128, rows);
            Assert.IsGreaterThan(1, dates.Count);
            Assert.IsGreaterThan(1, times.Count);
        }, context.CancellationToken);

    /// <summary>
    /// Preserves independent native error fields and same-backend recovery after explicit rollback.
    /// </summary>
    /// <param name="nativeSql">The independent PostgreSQL operation.</param>
    /// <param name="managedSql">The corresponding published sample call.</param>
    /// <param name="state">The independently required SQLSTATE.</param>
    [TestMethod]
    [DataRow("SELECT '1 day'::interval / 0.0::float8", "SELECT datetime_example.div_interval('1 day', 0.0)", "22012")]
    [DataRow("SELECT '2147483647 months'::interval + '1 month'::interval", "SELECT datetime_example.add_interval('2147483647 months'::interval, '1 month')", "22008")]
    [DataRow("SELECT '5874897-12-31'::date + '12:00'::time", "SELECT datetime_example.compose_timestamp('5874897-12-31', '12:00')", "22008")]
    [DataRow("SELECT '2000-01-01'::timestamp::timestamptz AT TIME ZONE 'Ankus/NoSuchZone'", "SELECT datetime_example.set_timezone('2000-01-01', 'Ankus/NoSuchZone')", "22023")]
    public Task DateTimeSampleFailuresPreserveNativeRecovery(string nativeSql, string managedSql, string state)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DateTimeSampleFailuresPreserveNativeRecovery), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            int backend = connection.ProcessID;
            await transaction.SaveAsync("datetime_sample_native", token);
            await using var command = new NpgsqlCommand(nativeSql, connection, transaction);
            PostgresException native = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual(state, native.SqlState);
            await transaction.RollbackAsync("datetime_sample_native", token);
            await transaction.ReleaseAsync("datetime_sample_native", token);
            await transaction.SaveAsync("datetime_sample_managed", token);
            command.CommandText = managedSql;
            PostgresException managed = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual(native.SqlState, managed.SqlState);
            Assert.AreEqual(native.MessageText, managed.MessageText);
            Assert.AreEqual(native.Detail, managed.Detail);
            Assert.AreEqual(native.Hint, managed.Hint);
            await transaction.RollbackAsync("datetime_sample_managed", token);
            await transaction.ReleaseAsync("datetime_sample_managed", token);
            Assert.AreEqual(backend, await ScalarAsync<int>(connection, transaction, "SELECT pg_backend_pid()", token));
            Assert.AreEqual("2000-02-29 12:34:56.123456", await ScalarAsync<string>(connection, transaction,
                "SELECT datetime_example.compose_timestamp('2000-02-29', '12:34:56.123456')::text", token));
        }, context.CancellationToken);

    /// <summary>
    /// Verifies upstream signatures and parameter names with PostgreSQL-correct execution flags.
    /// </summary>
    [TestMethod]
    public Task DateTimeSampleDeclarationsPreserveNativeContracts()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DateTimeSampleDeclarationsPreserveNativeContracts), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await using var command = new NpgsqlCommand("""
                WITH expected(signature, result, names, volatility, parallel) AS (VALUES
                    ('to_iso_string(timestamptz)', 'text', ARRAY['tsz'], 's', 's'),
                    ('to_iso_string(timestamptz,text)', 'text', ARRAY['tsz','tz'], 'i', 's'),
                    ('add_interval(date,interval)', 'timestamp', ARRAY['v','i'], 'i', 's'),
                    ('add_interval(time,interval)', 'time', ARRAY['v','i'], 'i', 's'),
                    ('add_interval(timetz,interval)', 'timetz', ARRAY['v','i'], 'i', 's'),
                    ('add_interval(timestamp,interval)', 'timestamp', ARRAY['v','i'], 'i', 's'),
                    ('add_interval(timestamptz,interval)', 'timestamptz', ARRAY['v','i'], 's', 's'),
                    ('add_interval(interval,interval)', 'interval', ARRAY['v','i'], 'i', 's'),
                    ('subtract_interval(date,interval)', 'timestamp', ARRAY['v','i'], 'i', 's'),
                    ('subtract_interval(time,interval)', 'time', ARRAY['v','i'], 'i', 's'),
                    ('subtract_interval(timetz,interval)', 'timetz', ARRAY['v','i'], 'i', 's'),
                    ('subtract_interval(timestamp,interval)', 'timestamp', ARRAY['v','i'], 'i', 's'),
                    ('subtract_interval(timestamptz,interval)', 'timestamptz', ARRAY['v','i'], 's', 's'),
                    ('subtract_interval(interval,interval)', 'interval', ARRAY['v','i'], 'i', 's'),
                    ('mul_interval(interval,float8)', 'interval', ARRAY['i','v'], 'i', 's'),
                    ('div_interval(interval,float8)', 'interval', ARRAY['i','v'], 'i', 's'),
                    ('compose_timestamp(date,time)', 'timestamp', ARRAY['d','t'], 'i', 's'),
                    ('set_timezone(timestamp,text)', 'timestamp', ARRAY['ts','timezone'], 's', 's'),
                    ('random_time()', 'time', NULL, 'v', 's'),
                    ('random_date()', 'date', NULL, 'v', 's'),
                    ('all_times()', 'record', ARRAY['now','transaction_timestamp','statement_timestamp','clock_timestamp'], 'v', 'u')
                )
                SELECT signature, p.oid IS NOT NULL,
                    p.prorettype = to_regtype(result), names IS NOT DISTINCT FROM p.proargnames,
                    p.provolatile::text = volatility, p.proparallel::text = parallel,
                    p.pronargs = 0 OR p.proisstrict,
                    p.proretset = (signature = 'all_times()')
                FROM expected LEFT JOIN pg_proc p ON p.oid = to_regprocedure('datetime_example.' || signature)
                ORDER BY signature
                """, connection, transaction);
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
            {
                int rows = 0;
                while (await reader.ReadAsync(token))
                {
                    string signature = reader.GetString(0);
                    for (int column = 1; column < 8; column++)
                    {
                        Assert.IsFalse(reader.IsDBNull(column), signature);
                        Assert.IsTrue(reader.GetBoolean(column), signature);
                    }

                    rows++;
                }

                Assert.AreEqual(21, rows);
            }

            Assert.IsTrue(await ScalarAsync<bool>(connection, transaction,
                "SELECT extrelocatable FROM pg_extension WHERE extname = 'ankus_datetime'", token));
            Assert.IsTrue(await ScalarAsync<bool>(connection, transaction, """
                SELECT datetime_example.to_iso_string(NULL::timestamptz) IS NULL
                    AND datetime_example.to_iso_string(NULL::timestamptz, 'UTC') IS NULL
                    AND datetime_example.to_iso_string(now(), NULL::text) IS NULL
                    AND datetime_example.mul_interval(NULL::interval, 1.0) IS NULL
                    AND datetime_example.mul_interval('1 day', NULL::float8) IS NULL
                    AND datetime_example.div_interval(NULL::interval, 1.0) IS NULL
                    AND datetime_example.div_interval('1 day', NULL::float8) IS NULL
                    AND datetime_example.set_timezone(NULL::timestamp, 'UTC') IS NULL
                    AND datetime_example.set_timezone('2000-01-01', NULL::text) IS NULL
                """, token));
        }, context.CancellationToken);

    /// <summary>
    /// Keeps an already prepared constant query correct after the session timezone changes.
    /// </summary>
    /// <param name="sample">The closed example expression in the cached query.</param>
    /// <param name="reference">The independent PostgreSQL expression for the current timezone.</param>
    [TestMethod]
    [DataRow("datetime_example.to_iso_string(timestamptz '2024-03-10 12:00:00-04')", "to_json(timestamptz '2024-03-10 12:00:00-04') #>> '{}'")]
    [DataRow("encode(timestamptz_send(datetime_example.add_interval(timestamptz '2024-03-09 12:00:00-05', interval '1 day')), 'hex')", "encode(timestamptz_send(timestamptz '2024-03-09 12:00:00-05' + interval '1 day'), 'hex')")]
    [DataRow("encode(timestamptz_send(datetime_example.subtract_interval(timestamptz '2024-03-10 12:00:00-04', interval '1 day')), 'hex')", "encode(timestamptz_send(timestamptz '2024-03-10 12:00:00-04' - interval '1 day'), 'hex')")]
    [DataRow("encode(timestamp_send(datetime_example.set_timezone(timestamp '2024-03-10 02:30:00', 'UTC')), 'hex')", "encode(timestamp_send(timestamp '2024-03-10 02:30:00'::timestamptz AT TIME ZONE 'UTC'), 'hex')")]
    public Task DateTimeSampleCachedPlansFollowSessionTimeZone(string sample, string reference)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DateTimeSampleCachedPlansFollowSessionTimeZone), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await SetZoneAsync(connection, transaction, "America/New_York", token);
            await ExecuteAsync(connection, transaction,
                "SET LOCAL plan_cache_mode = force_generic_plan; PREPARE datetime_zone_contract AS SELECT " + sample, token);
            try
            {
                string original = await ScalarAsync<string>(connection, transaction, "SELECT " + reference, token);
                Assert.AreEqual(original, await ScalarAsync<string>(connection, transaction, "EXECUTE datetime_zone_contract", token));
                await SetZoneAsync(connection, transaction, "UTC", token);
                string changed = await ScalarAsync<string>(connection, transaction, "SELECT " + reference, token);
                Assert.AreNotEqual(original, changed, "The independent PostgreSQL result must actually change between the selected zones.");
                Assert.AreEqual(changed, await ScalarAsync<string>(connection, transaction, "EXECUTE datetime_zone_contract", token));
                Assert.AreEqual(2L, await ScalarAsync<long>(connection, transaction,
                    "SELECT generic_plans FROM pg_prepared_statements WHERE name = 'datetime_zone_contract'", token));
                Assert.AreEqual(0L, await ScalarAsync<long>(connection, transaction,
                    "SELECT custom_plans FROM pg_prepared_statements WHERE name = 'datetime_zone_contract'", token));
            }
            finally
            {
                await ExecuteAsync(connection, transaction, "DEALLOCATE datetime_zone_contract", token);
            }
        }, context.CancellationToken);

    /// <summary>
    /// Installs the independently published example in a deliberately selected schema.
    /// </summary>
    /// <param name="connection">The isolated backend connection.</param>
    /// <param name="transaction">The rollback-isolated test transaction.</param>
    /// <param name="token">Cancels native work.</param>
    /// <returns>The completed native installation.</returns>
    private static Task InstallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
        => ExecuteAsync(connection, transaction, "CREATE SCHEMA datetime_example; CREATE EXTENSION ankus_datetime WITH SCHEMA datetime_example", token);

    /// <summary>
    /// Selects the exact transaction-local interpretation timezone without embedding its text in SQL.
    /// </summary>
    /// <param name="connection">The isolated backend connection.</param>
    /// <param name="transaction">The rollback-isolated test transaction.</param>
    /// <param name="zone">The exact PostgreSQL timezone name.</param>
    /// <param name="token">Cancels native work.</param>
    /// <returns>The completed setting change.</returns>
    private static async Task SetZoneAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string zone, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT set_config('TimeZone', $1, true)", connection, transaction);
        command.Parameters.AddWithValue(zone);
        Assert.AreEqual(zone, Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token)));
    }

    /// <summary>
    /// Uses PostgreSQL's independent JSON formatter for timestamp ISO text.
    /// </summary>
    /// <param name="connection">The isolated backend connection.</param>
    /// <param name="transaction">The rollback-isolated test transaction.</param>
    /// <param name="value">The independent instant text.</param>
    /// <param name="token">Cancels native work.</param>
    /// <returns>The independent native ISO representation.</returns>
    private static async Task<string> FormatNativeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string value, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT to_json($1::timestamptz) #>> '{}'", connection, transaction);
        command.Parameters.AddWithValue(value);
        return Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Executes an independently specified fixture statement.
    /// </summary>
    /// <param name="connection">The isolated backend connection.</param>
    /// <param name="transaction">The rollback-isolated test transaction.</param>
    /// <param name="sql">The independent fixture statement.</param>
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
    /// <typeparam name="T">The required client-side scalar type.</typeparam>
    /// <param name="connection">The isolated backend connection.</param>
    /// <param name="transaction">The rollback-isolated test transaction.</param>
    /// <param name="sql">The independent scalar statement.</param>
    /// <param name="token">Cancels native work.</param>
    /// <returns>The required scalar value.</returns>
    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }
}
