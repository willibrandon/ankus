using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Checks temporal datum transport against PostgreSQL's own binary protocols, epoch, and calendar semantics.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class TemporalDatumTests(TestContext context)
{
    /// <summary>
    /// Verifies exact storage, infinities, BC dates, full finite boundaries, offsets, and NULL through every SPI owner.
    /// </summary>
    /// <param name="mode">The direct, scalar, prepared, session, cursor, session-plan, retained-plan, or edited-row path.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    public Task TemporalStorageSurvivesEveryPath(int mode)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(TemporalStorageSurvivesEveryPath),
            async (connection, transaction, token) =>
            {
                (string type, string values)[] cases =
                [
                    ("date", "NULL, '2000-01-01', '1999-12-31', '0001-01-01 BC', '4714-11-24 BC', " +
                        "'5874897-12-31', 'infinity', '-infinity'"),
                    ("time", "NULL, '00:00:00', '00:00:00.000001', '23:59:59.999999', '24:00:00'"),
                    ("timetz", "NULL, '00:00:00+00', '24:00:00+15:59:59', '12:34:56.123456-15:59:59', '01:02:03.000001+05:30:17'"),
                    ("timestamp", "NULL, '2000-01-01', '1999-12-31 23:59:59.999999', '4714-11-24 BC', " +
                        "'294276-12-31 23:59:59.999999', 'infinity', '-infinity'"),
                    ("timestamptz", "NULL, '2000-01-01+00', '1999-12-31 23:59:59.999999+00', '4714-11-24+00 BC', " +
                        "'294276-12-31 23:59:59.999999+00', '2024-03-10 01:59:59.999999-05', 'infinity', '-infinity'"),
                    ("interval", "NULL, '0', '1 mon -2 days 03:04:05.123456', '-1 microsecond', '25 hours', " +
                        "'2147483647 months', '-2147483648 days', 'infinity', '-infinity'"),
                ];
                foreach ((string type, string values) in cases)
                {
                    await using var command = new NpgsqlCommand($"""
                        SELECT value::text, {type}_send(value), {type}_send(datatype.exchange_{type}(value, $1))
                        FROM unnest(ARRAY[{values}]::{type}[]) AS value
                        """, connection, transaction);
                    command.Parameters.AddWithValue(mode);
                    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                    int rows = 0;
                    while (await reader.ReadAsync(token))
                    {
                        rows++;
                        if (reader.IsDBNull(1))
                        {
                            Assert.IsTrue(reader.IsDBNull(2), $"{type} NULL, path {mode}");
                        }
                        else
                        {
                            Assert.AreSequenceEqual(reader.GetFieldValue<byte[]>(1), reader.GetFieldValue<byte[]>(2),
                                $"{type} {reader.GetString(0)}, path {mode}");
                        }
                    }

                    Assert.AreEqual(values.Split(", ", StringSplitOptions.None).Length, rows, type);
                }
            }, context.CancellationToken);

    /// <summary>
    /// Verifies ordinary .NET types work in generated methods, typed NULLs, and SPI's generic readers.
    /// </summary>
    /// <param name="mode">The ownership path.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    public Task DotnetTemporalTypesWorkInsideNativeAot(int mode)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DotnetTemporalTypesWorkInsideNativeAot),
            async (connection, transaction, token) =>
            {
                (string type, string function, string literal)[] cases =
                [
                    ("date", "date_only", "0001-01-01"),
                    ("date", "date_only", "9999-12-31"),
                    ("time", "time_only", "23:59:59.999999"),
                    ("timestamp", "date_time", "1999-12-31 23:59:59.999999"),
                    ("timestamptz", "date_time_offset", "2024-03-10 01:59:59.999999-05"),
                    ("interval", "time_span", "-49:00:00.000001"),
                ];
                foreach ((string type, string function, string literal) in cases)
                {
                    await using var command = new NpgsqlCommand($"""
                        SELECT {type}_send($1::{type}), {type}_send(datatype.exchange_{function}($1::{type}, $2)),
                               datatype.exchange_{function}(NULL, $2) IS NULL
                        """, connection, transaction);
                    command.Parameters.AddWithValue(literal);
                    command.Parameters.AddWithValue(mode);
                    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                    Assert.IsTrue(await reader.ReadAsync(token));
                    Assert.AreSequenceEqual(reader.GetFieldValue<byte[]>(0), reader.GetFieldValue<byte[]>(1), function);
                    Assert.IsTrue(reader.GetBoolean(2), function + " SQL NULL");
                }
            }, context.CancellationToken);

    /// <summary>
    /// Verifies read-side epoch, offset direction, and interval field ordering against independent numeric expectations.
    /// </summary>
    [TestMethod]
    public Task NativeInputUsesPostgresEpochAndIndependentFields()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NativeInputUsesPostgresEpochAndIndependentFields),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("""
                    SELECT datatype.temporal_parts('1999-12-31', '00:00:00.000001', '00:00:00.000002+05:30:17',
                        '1999-12-31 23:59:59.999997', '2000-01-01 05:30:00.000004+05:30', '1 mon -2 days 3 microseconds')
                    """, connection, transaction);
                Assert.AreEqual("-1|1|2|19817|-3|4|1|-2|3", await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies write-side conversions independently using PostgreSQL binary storage, rather than SQL interval equality.
    /// </summary>
    /// <param name="expression">The managed construction.</param>
    /// <param name="expected">The PostgreSQL value.</param>
    /// <param name="type">The binary protocol function prefix.</param>
    [TestMethod]
    [DataRow("date_from_days(-1)", "1999-12-31", "date")]
    [DataRow("date_from_days(2147483647)", "infinity", "date")]
    [DataRow("time_from_micros(86400000000)", "24:00:00", "time")]
    [DataRow("timetz_from_parts(2, 19817)", "00:00:00.000002+05:30:17", "timetz")]
    [DataRow("timestamp_from_micros(-3)", "1999-12-31 23:59:59.999997", "timestamp")]
    [DataRow("timestamptz_from_micros(4)", "2000-01-01 00:00:00.000004+00", "timestamptz")]
    [DataRow("interval_from_parts(1, -2, 3)", "1 mon -2 days 3 microseconds", "interval")]
    [DataRow("interval_infinity(false)", "infinity", "interval")]
    [DataRow("interval_infinity(true)", "-infinity", "interval")]
    public Task ManagedConstructionMatchesNativeStorage(string expression, string expected, string type)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ManagedConstructionMatchesNativeStorage),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand(
                    $"SELECT {type}_send($1::{type}), {type}_send(datatype.{expression})", connection, transaction);
                command.Parameters.AddWithValue(expected);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreSequenceEqual(reader.GetFieldValue<byte[]>(0), reader.GetFieldValue<byte[]>(1));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies infinity has a distinct managed representation from finite interval components.
    /// </summary>
    [TestMethod]
    public Task IntervalInfinityIsExplicitAndNativeErrorsRecover()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(IntervalInfinityIsExplicitAndNativeErrorsRecover),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("""
                    SELECT datatype.interval_infinity_kind('-infinity'), datatype.interval_infinity_kind('infinity'),
                           datatype.interval_infinity_kind('0'), datatype.interval_write_recovery()
                    """, connection, transaction);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(-1, reader.GetInt32(0));
                Assert.AreEqual(1, reader.GetInt32(1));
                Assert.AreEqual(0, reader.GetInt32(2));
                Assert.AreEqual("22008:1:3", reader.GetString(3));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies domain datums use their base conversion and do not borrow backend storage.
    /// </summary>
    [TestMethod]
    public Task TemporalDomainsRemainOwnedAfterSpiCleanup()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(TemporalDomainsRemainOwnedAfterSpiCleanup),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("""
                    CREATE DOMAIN pg_temp.d AS date;
                    CREATE DOMAIN pg_temp.t AS time;
                    CREATE DOMAIN pg_temp.z AS timetz;
                    CREATE DOMAIN pg_temp.s AS timestamp;
                    CREATE DOMAIN pg_temp.i AS timestamptz;
                    CREATE DOMAIN pg_temp.v AS interval;
                    SELECT datatype.temporal_domain_parts($query$SELECT '1999-12-31'::pg_temp.d,
                        '00:00:00.000001'::pg_temp.t, '00:00:00.000002+05:30:17'::pg_temp.z,
                        '1999-12-31 23:59:59.999997'::pg_temp.s, '2000-01-01 00:00:00.000004+00'::pg_temp.i,
                        '1 mon -2 days 3 microseconds'::pg_temp.v$query$)
                    """, connection, transaction);
                Assert.AreEqual("-1|1|2|19817|-3|4|1|-2|3", await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies loss of full-range, end-of-day, or calendar information raises a managed error safely.
    /// </summary>
    /// <param name="expression">The unsupported conversion or invalid constructor.</param>
    [TestMethod]
    [DataRow("exchange_date_only('infinity', 0)")]
    [DataRow("exchange_date_only('0001-01-01 BC', 0)")]
    [DataRow("exchange_time_only('24:00:00', 0)")]
    [DataRow("exchange_date_time('infinity', 0)")]
    [DataRow("exchange_date_time_offset('10000-01-01+00', 0)")]
    [DataRow("exchange_time_span('1 day', 0)")]
    [DataRow("exchange_time_span('1 month', 0)")]
    [DataRow("exchange_time_span('infinity', 0)")]
    [DataRow("date_from_days(-2451546)")]
    [DataRow("time_from_micros(86400000001)")]
    [DataRow("timetz_from_parts(0, 57600)")]
    [DataRow("timestamp_from_micros(9223371331200000000)")]
    [DataRow("sub_microsecond_time()")]
    [DataRow("utc_wall_clock()")]
    public Task UnrepresentableTemporalValuesUnwindSafely(string expression)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(UnrepresentableTemporalValuesUnwindSafely),
            async (connection, transaction, token) =>
            {
                await transaction.SaveAsync("temporal_error", token);
                await using var command = new NpgsqlCommand($"SELECT datatype.{expression}", connection, transaction);
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
                Assert.AreEqual("38000", error.SqlState);
                await transaction.RollbackAsync("temporal_error", token);
                command.CommandText = "SELECT datatype.date_from_days(0)::text";
                Assert.AreEqual("2000-01-01", await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies timezone and interval settings change formatting but not exact temporal storage.
    /// </summary>
    /// <param name="zone">The session timezone.</param>
    /// <param name="style">The interval display style.</param>
    [TestMethod]
    [DataRow("America/New_York", "postgres")]
    [DataRow("Asia/Kathmandu", "iso_8601")]
    [DataRow("Australia/Lord_Howe", "sql_standard")]
    public Task SessionSettingsDoNotChangeStoredTemporalValues(string zone, string style)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SessionSettingsDoNotChangeStoredTemporalValues),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand($"SET LOCAL TIME ZONE '{zone}'; SET LOCAL intervalstyle = '{style}'",
                    connection, transaction);
                await command.ExecuteNonQueryAsync(token);
                command.CommandText = """
                    SELECT extract(epoch FROM datatype.exchange_date_time_offset('2024-03-10 06:59:59.123456+00', 3)),
                           encode(interval_send(datatype.exchange_interval('1 mon -2 days 3 microseconds', 3)), 'hex'),
                           datatype.temporal_read_recovery(), datatype.temporal_context_growth()
                    """;
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(1710053999.123456m, reader.GetDecimal(0));
                Assert.AreEqual("0000000000000003fffffffe00000001", reader.GetString(1));
                Assert.AreEqual("3:1:Unspecified", reader.GetString(2));
                Assert.AreEqual(0L, reader.GetInt64(3));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies a calendar day remains different from 24 elapsed hours across a daylight-saving transition.
    /// </summary>
    [TestMethod]
    public Task CalendarDaysRemainDistinctFromElapsedHours()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(CalendarDaysRemainDistinctFromElapsedHours),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("""
                    SET LOCAL TIME ZONE 'America/New_York';
                    SELECT ('2024-03-09 12:00:00-05'::timestamptz + datatype.interval_from_parts(0, 1, 0))::text,
                           ('2024-03-09 12:00:00-05'::timestamptz + datatype.exchange_time_span('24 hours', 3))::text
                    """, connection, transaction);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual("2024-03-10 12:00:00-04", reader.GetString(0));
                Assert.AreEqual("2024-03-10 13:00:00-04", reader.GetString(1));
            }, context.CancellationToken);
}
