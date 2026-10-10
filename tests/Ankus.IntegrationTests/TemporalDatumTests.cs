using System.Buffers.Binary;
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
                    bool rejectsInfinity = type == "interval" && PostgresFixture.Cluster.Installation.Version.Major < 17;
                    string inputs = rejectsInfinity ? values[..values.IndexOf(", 'infinity'", StringComparison.Ordinal)] : values;
                    await using var command = new NpgsqlCommand($"""
                        SELECT value::text, {type}_send(value), {type}_send(datatype.exchange_{type}(value, $1))
                        FROM unnest(ARRAY[{inputs}]::{type}[]) AS value
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

                    Assert.AreEqual(inputs.Split(", ", StringSplitOptions.None).Length, rows, type);
                    await reader.DisposeAsync();
                    if (rejectsInfinity)
                    {
                        string[] unsupported = ["infinity", "-infinity"];
                        foreach (string input in unsupported)
                        {
                            command.CommandText = "SELECT datatype.exchange_interval($1::interval, $2)";
                            command.Parameters.Clear();
                            command.Parameters.AddWithValue(input);
                            command.Parameters.AddWithValue(mode);
                            await AssertIntervalFailureAsync(command, transaction, "22007",
                                $"invalid input syntax for type interval: \"{input}\"", token);
                        }
                    }
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
    /// Seeded random raw values survive every exchange path and a text-literal round trip with exact native storage, as
    /// pgrx's temporal property tests require. Half of the values cover the whole raw range, saturated to infinity or
    /// wrapped as pgrx does; the other half stay finite.
    /// </summary>
    /// <param name="type">The temporal type.</param>
    [TestMethod]
    [DataRow("date")]
    [DataRow("time")]
    [DataRow("timetz")]
    [DataRow("timestamp")]
    [DataRow("timestamptz")]
    public Task SeededTemporalValuesRoundTripExactly(string type)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SeededTemporalValuesRoundTripExactly),
            async (connection, transaction, token) =>
            {
                const int count = 256;
                var random = new Random(20261009);
                long[] first = new long[count];
                int[] second = new int[count];
                string[] expected = new string[count];
                for (int index = 0; index < count; index++)
                {
                    bool wide = index % 2 == 0;
                    byte[] bytes;
                    switch (type)
                    {
                        case "date":
                            long days = wide ? random.NextInt64(int.MinValue, (long)int.MaxValue + 1) : random.NextInt64(-2_451_545, 2_145_031_949);
                            int stored = days < -2_451_545 ? int.MinValue : days >= 2_145_031_949 ? int.MaxValue : (int)days;
                            first[index] = stored;
                            bytes = new byte[4];
                            BinaryPrimitives.WriteInt32BigEndian(bytes, stored);
                            break;
                        case "time":
                        case "timetz":
                            long micros = wide ? random.NextInt64(long.MinValue, long.MaxValue) % 86_400_000_001 : random.NextInt64(0, 86_400_000_001);
                            first[index] = micros < 0 ? micros + 86_400_000_001 : micros;
                            second[index] = random.Next(-57_599, 57_600);
                            bytes = new byte[type == "time" ? 8 : 12];
                            BinaryPrimitives.WriteInt64BigEndian(bytes, first[index]);
                            if (type == "timetz")
                            {
                                BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8), -second[index]);
                            }

                            break;
                        default:
                            const long minimum = -211_813_488_000_000_000;
                            const long end = 9_223_371_331_200_000_000;
                            long instant = wide ? random.NextInt64(long.MinValue, long.MaxValue) : random.NextInt64(minimum, end);
                            first[index] = instant < minimum ? long.MinValue : instant >= end ? long.MaxValue : instant;
                            bytes = new byte[8];
                            BinaryPrimitives.WriteInt64BigEndian(bytes, first[index]);
                            break;
                    }

                    expected[index] = Convert.ToHexStringLower(bytes);
                }

                string construct = type switch
                {
                    "date" => "datatype.date_from_days(first::integer)",
                    "time" => "datatype.time_from_micros(first)",
                    "timetz" => "datatype.timetz_from_parts(first, second)",
                    _ => $"datatype.{type}_from_micros(first)",
                };
                await using var command = new NpgsqlCommand($"""
                    SELECT count(*) FILTER (WHERE encode({type}_send(value), 'hex') = expected
                               AND encode({type}_send((value::text)::{type}), 'hex') = expected
                               AND (SELECT bool_and(encode({type}_send(datatype.exchange_{type}(value, mode)), 'hex') = expected)
                                      FROM generate_series(0, 6) mode))
                      FROM unnest($1::bigint[], $2::integer[], $3::text[]) AS input(first, second, expected),
                           LATERAL (SELECT {construct} AS value) constructed
                    """, connection, transaction);
                command.Parameters.AddWithValue(first);
                command.Parameters.AddWithValue(second);
                command.Parameters.AddWithValue(expected);
                Assert.AreEqual((long)count, await command.ExecuteScalarAsync(token));
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
                if (type == "interval" && PostgresFixture.Cluster.Installation.Version.Major < 17 &&
                    expected is "infinity" or "-infinity")
                {
                    command.CommandText = $"SELECT datatype.{expression}";
                    command.Parameters.Clear();
                    await AssertIntervalFailureAsync(command, transaction, "0A000", "interval infinity requires PostgreSQL 17", token);
                    return;
                }

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
                if (PostgresFixture.Cluster.Installation.Version.Major < 17)
                {
                    command.CommandText = """
                        SELECT datatype.interval_infinity_kind(datatype.interval_from_parts(2147483647, 2147483647, 9223372036854775807)),
                            datatype.interval_infinity_kind(datatype.interval_from_parts((-2147483648)::integer, (-2147483648)::integer, (-9223372036854775808)::bigint)),
                            datatype.interval_infinity_kind('0'), datatype.interval_write_recovery()
                        """;
                }

                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                bool supportsInfinity = PostgresFixture.Cluster.Installation.Version.Major >= 17;
                Assert.AreEqual(supportsInfinity ? -1 : 0, reader.GetInt32(0));
                Assert.AreEqual(supportsInfinity ? 1 : 0, reader.GetInt32(1));
                Assert.AreEqual(0, reader.GetInt32(2));
                Assert.AreEqual(supportsInfinity ? "22008:1:3" : "finite:2147483647:2147483647:9223372036854775807:2:141:3", reader.GetString(3));
                Assert.IsFalse(await reader.ReadAsync(token));
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
                await using var command = new NpgsqlCommand($"SELECT datatype.{expression}", connection, transaction);
                if (PostgresFixture.Cluster.Installation.Version.Major < 17 && expression == "exchange_time_span('infinity', 0)")
                {
                    await AssertIntervalFailureAsync(command, transaction, "22007", "invalid input syntax for type interval: \"infinity\"", token);
                    return;
                }

                await transaction.SaveAsync("temporal_error", token);
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
                    SELECT encode(timestamptz_send(datatype.exchange_date_time_offset('2024-03-10 06:59:59.123456+00', 3)), 'hex'),
                           encode(interval_send(datatype.exchange_interval('1 mon -2 days 3 microseconds', 3)), 'hex'),
                           datatype.temporal_read_recovery(), datatype.temporal_context_growth()
                    """;
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual("0002b647bdf23c00", reader.GetString(0));
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

    /// <summary>
    /// Checks exact interval rejection and successful construction in the same backend after rollback.
    /// </summary>
    private static async Task AssertIntervalFailureAsync(NpgsqlCommand command, NpgsqlTransaction transaction,
        string expectedState, string expectedMessage, CancellationToken token)
    {
        NpgsqlConnection? connection = command.Connection;
        Assert.IsNotNull(connection);
        int backend = connection.ProcessID;
        await transaction.SaveAsync("interval_error", token);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual(expectedState, error.SqlState);
        Assert.AreEqual(expectedMessage, error.MessageText);
        Assert.IsNull(error.Detail);
        Assert.IsNull(error.Hint);
        await transaction.RollbackAsync("interval_error", token);
        await transaction.ReleaseAsync("interval_error", token);

        command.Parameters.Clear();
        command.CommandText = "SELECT encode(interval_send(datatype.interval_from_parts(1, -2, 3)), 'hex'), pg_backend_pid()";
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        Assert.IsTrue(await reader.ReadAsync(token));
        Assert.AreEqual("0000000000000003fffffffe00000001", reader.GetString(0));
        Assert.AreEqual(backend, reader.GetInt32(1));
        Assert.IsFalse(await reader.ReadAsync(token));
    }

    /// <summary>
    /// Generic .NET code parses PostgreSQL values through <see cref="IParsable{TSelf}"/>, which runs each type's input
    /// function, and invalid text is rejected without failing the transaction.
    /// </summary>
    [TestMethod]
    public Task ValueTypesParseThroughIParsable()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ValueTypesParseThroughIParsable), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT datatype.parse_through_interfaces(), ('2026-10-10'::date - '2000-01-01'::date)::text", connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual("12.50|192.0.2.1/24|(1.5,-2)|P1DT2H|" + reader.GetString(1) + "|rejected|accepted", reader.GetString(0));
        }, context.CancellationToken);
}
