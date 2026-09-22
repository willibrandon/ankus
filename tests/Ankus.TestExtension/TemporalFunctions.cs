namespace Ankus.TestExtension;

/// <summary>
/// Exercises full-range and .NET temporal values inside Native AOT PostgreSQL callbacks.
/// </summary>
public static class TemporalFunctions
{
    /// <summary>
    /// Returns a date through the requested ownership path.
    /// </summary>
    /// <param name="value">The date or SQL NULL.</param>
    /// <param name="mode">The SPI path.</param>
    /// <returns>The owned date.</returns>
    [PgFunction]
    public static PgDate? ExchangeDate(PgDate? value, int mode) => Exchange(value, mode);

    /// <summary>
    /// Returns a time through the requested ownership path.
    /// </summary>
    /// <param name="value">The time or SQL NULL.</param>
    /// <param name="mode">The SPI path.</param>
    /// <returns>The owned time.</returns>
    [PgFunction]
    public static PgTime? ExchangeTime(PgTime? value, int mode) => Exchange(value, mode);

    /// <summary>
    /// Returns a time and offset through the requested ownership path.
    /// </summary>
    /// <param name="value">The time or SQL NULL.</param>
    /// <param name="mode">The SPI path.</param>
    /// <returns>The owned time.</returns>
    [PgFunction]
    public static PgTimeTz? ExchangeTimetz(PgTimeTz? value, int mode) => Exchange(value, mode);

    /// <summary>
    /// Returns a wall-clock timestamp through the requested ownership path.
    /// </summary>
    /// <param name="value">The timestamp or SQL NULL.</param>
    /// <param name="mode">The SPI path.</param>
    /// <returns>The owned timestamp.</returns>
    [PgFunction]
    public static PgTimestamp? ExchangeTimestamp(PgTimestamp? value, int mode) => Exchange(value, mode);

    /// <summary>
    /// Returns an instant through the requested ownership path.
    /// </summary>
    /// <param name="value">The instant or SQL NULL.</param>
    /// <param name="mode">The SPI path.</param>
    /// <returns>The owned instant.</returns>
    [PgFunction]
    public static PgTimestampTz? ExchangeTimestamptz(PgTimestampTz? value, int mode) => Exchange(value, mode);

    /// <summary>
    /// Returns an interval through the requested ownership path.
    /// </summary>
    /// <param name="value">The interval or SQL NULL.</param>
    /// <param name="mode">The SPI path.</param>
    /// <returns>The owned interval.</returns>
    [PgFunction]
    public static PgInterval? ExchangeInterval(PgInterval? value, int mode) => Exchange(value, mode);

    /// <summary>
    /// Returns a DateOnly through the requested ownership path.
    /// </summary>
    /// <param name="value">The date or SQL NULL.</param>
    /// <param name="mode">The SPI path.</param>
    /// <returns>The .NET date.</returns>
    [PgFunction]
    public static DateOnly? ExchangeDateOnly(DateOnly? value, int mode) => Exchange(value, mode);

    /// <summary>
    /// Returns a TimeOnly through the requested ownership path.
    /// </summary>
    /// <param name="value">The time or SQL NULL.</param>
    /// <param name="mode">The SPI path.</param>
    /// <returns>The .NET time.</returns>
    [PgFunction]
    public static TimeOnly? ExchangeTimeOnly(TimeOnly? value, int mode) => Exchange(value, mode);

    /// <summary>
    /// Returns an unspecified DateTime through the requested ownership path.
    /// </summary>
    /// <param name="value">The timestamp or SQL NULL.</param>
    /// <param name="mode">The SPI path.</param>
    /// <returns>The .NET timestamp.</returns>
    [PgFunction]
    public static DateTime? ExchangeDateTime(DateTime? value, int mode) => Exchange(value, mode);

    /// <summary>
    /// Returns a DateTimeOffset through the requested ownership path.
    /// </summary>
    /// <param name="value">The instant or SQL NULL.</param>
    /// <param name="mode">The SPI path.</param>
    /// <returns>The .NET instant.</returns>
    [PgFunction]
    public static DateTimeOffset? ExchangeDateTimeOffset(DateTimeOffset? value, int mode) => Exchange(value, mode);

    /// <summary>
    /// Returns an elapsed TimeSpan through the requested ownership path.
    /// </summary>
    /// <param name="value">The duration or SQL NULL.</param>
    /// <param name="mode">The SPI path.</param>
    /// <returns>The .NET duration.</returns>
    [PgFunction]
    public static TimeSpan? ExchangeTimeSpan(TimeSpan? value, int mode) => Exchange(value, mode);

    /// <summary>
    /// Reads all temporal fields independently of the native output converter.
    /// </summary>
    /// <param name="date">The date.</param>
    /// <param name="time">The time.</param>
    /// <param name="offset">The time and offset.</param>
    /// <param name="stamp">The wall-clock timestamp.</param>
    /// <param name="instant">The UTC instant.</param>
    /// <param name="span">The interval.</param>
    /// <returns>The independent numeric fields.</returns>
    [PgFunction]
    public static string TemporalParts(PgDate date, PgTime time, PgTimeTz offset, PgTimestamp stamp,
        PgTimestampTz instant, PgInterval span)
        => FormattableString.Invariant($"{date.DaysSinceEpoch}|{time.Microseconds}|{offset.Time.Microseconds}|{offset.OffsetSeconds}|") +
            FormattableString.Invariant(
                $"{stamp.MicrosecondsSinceEpoch}|{instant.MicrosecondsSinceEpoch}|{span.Months}|{span.Days}|{span.Microseconds}");

    /// <summary>
    /// Constructs a date without the native input converter.
    /// </summary>
    /// <param name="days">The day offset.</param>
    /// <returns>The date.</returns>
    [PgFunction]
    public static PgDate DateFromDays(int days) => new(days);

    /// <summary>
    /// Constructs a time without the native input converter.
    /// </summary>
    /// <param name="micros">The time in microseconds.</param>
    /// <returns>The time.</returns>
    [PgFunction]
    public static PgTime TimeFromMicros(long micros) => new(micros);

    /// <summary>
    /// Constructs a time with second-resolution offset.
    /// </summary>
    /// <param name="micros">The time in microseconds.</param>
    /// <param name="offset">The seconds east of UTC.</param>
    /// <returns>The time with offset.</returns>
    [PgFunction]
    public static PgTimeTz TimetzFromParts(long micros, int offset) => new(new PgTime(micros), offset);

    /// <summary>
    /// Constructs a wall-clock timestamp without the native input converter.
    /// </summary>
    /// <param name="micros">The epoch-relative microseconds.</param>
    /// <returns>The timestamp.</returns>
    [PgFunction]
    public static PgTimestamp TimestampFromMicros(long micros) => new(micros);

    /// <summary>
    /// Constructs an instant without the native input converter.
    /// </summary>
    /// <param name="micros">The epoch-relative UTC microseconds.</param>
    /// <returns>The instant.</returns>
    [PgFunction]
    public static PgTimestampTz TimestamptzFromMicros(long micros) => new(micros);

    /// <summary>
    /// Constructs an interval without the native input converter.
    /// </summary>
    /// <param name="months">The month component.</param>
    /// <param name="days">The day component.</param>
    /// <param name="micros">The elapsed-time component.</param>
    /// <returns>The interval.</returns>
    [PgFunction]
    public static PgInterval IntervalFromParts(int months, int days, long micros) => new(months, days, micros);

    /// <summary>
    /// Returns interval infinity through a parameterized session query.
    /// </summary>
    /// <param name="negative">Whether to return negative infinity.</param>
    /// <returns>The infinite interval.</returns>
    [PgFunction]
    public static PgInterval IntervalInfinity(bool negative)
        => Exchange(negative ? PgInterval.NegativeInfinity : PgInterval.PositiveInfinity, 3);

    /// <summary>
    /// Checks that native interval infinities become explicit managed values.
    /// </summary>
    /// <param name="value">The interval.</param>
    /// <returns>Zero for finite values, one for positive infinity, or negative one for negative infinity.</returns>
    [PgFunction]
    public static int IntervalInfinityKind(PgInterval value)
        => value.IsFinite ? 0 : value == PgInterval.PositiveInfinity ? 1 : -1;

    /// <summary>
    /// Materializes domain values and verifies that their storage survives later SPI calls.
    /// </summary>
    /// <param name="sql">A query returning the six temporal types in order.</param>
    /// <returns>The copied fields.</returns>
    [PgFunction]
    public static string TemporalDomainParts(string sql)
    {
        SpiRow row = Spi.Query(sql)[0];
        Spi.Execute("SELECT repeat('overwrite', 10000)");
        return TemporalParts(row.Get<PgDate>(0), row.Get<PgTime>(1), row.Get<PgTimeTz>(2),
            row.Get<PgTimestamp>(3), row.Get<PgTimestampTz>(4), row.Get<PgInterval>(5));
    }

    /// <summary>
    /// Recovers from a PostgreSQL interval-sentinel collision inside a session subtransaction.
    /// </summary>
    /// <returns>The SQLSTATE, preserved row count, and successful follow-up interval.</returns>
    [PgFunction]
    public static string IntervalWriteRecovery()
        => Spi.Connect(session =>
        {
            session.Execute("CREATE TEMP TABLE interval_recovery (value int); INSERT INTO interval_recovery VALUES (42)");
            try
            {
                session.Execute("INSERT INTO interval_recovery SELECT 99 WHERE $1 IS NOT NULL",
                    SpiParameter.Create(new PgInterval(int.MaxValue, int.MaxValue, long.MaxValue)));
                return "unexpected success";
            }
            catch (PgException error)
            {
                return error.SqlState + ":" + session.ExecuteScalar<long>("SELECT count(*) FROM interval_recovery") + ":" +
                    session.ExecuteScalar<PgInterval>("SELECT interval '3 microseconds'").Microseconds;
            }
        });

    /// <summary>
    /// Exercises rejected sub-microsecond .NET results at the generated output boundary.
    /// </summary>
    /// <returns>An unrepresentable time.</returns>
    [PgFunction]
    public static TimeOnly SubMicrosecondTime() => new(1);

    /// <summary>
    /// Exercises rejected UTC DateTime results at the timezone-free output boundary.
    /// </summary>
    /// <returns>A timestamp with an incompatible kind.</returns>
    [PgFunction]
    public static DateTime UtcWallClock() => new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Checks managed SPI conversion failures leave a session usable and do not lose preceding writes.
    /// </summary>
    /// <returns>The failure count and successfully read timestamp.</returns>
    [PgFunction]
    public static string TemporalReadRecovery()
        => Spi.Connect(session =>
        {
            int failures = 0;
            session.Execute("CREATE TEMP TABLE temporal_recovery (value int); INSERT INTO temporal_recovery VALUES (42)");
            try
            {
                session.ExecuteScalar<TimeOnly>("SELECT time '24:00:00'");
            }
            catch (InvalidOperationException)
            {
                failures++;
            }

            try
            {
                session.ExecuteScalar<DateOnly>("SELECT date 'infinity'");
            }
            catch (InvalidOperationException)
            {
                failures++;
            }

            try
            {
                session.Execute("INSERT INTO temporal_recovery SELECT 99 WHERE $1 IS NOT NULL",
                    SpiParameter.Create(new TimeSpan(1)));
            }
            catch (ArgumentException)
            {
                failures++;
            }

            return failures + ":" + session.ExecuteScalar<long>("SELECT count(*) FROM temporal_recovery") + ":" +
                session.ExecuteScalar<DateTime>("SELECT timestamp '2000-01-01'").Kind;
        });

    /// <summary>
    /// Counts native context retention from repeated interval and timetz parameter construction.
    /// </summary>
    /// <returns>The number of extra subtransaction contexts.</returns>
    [PgFunction]
    public static long TemporalContextGrowth()
        => Spi.Connect(session =>
        {
            const string countSql = "SELECT count(*) FROM pg_backend_memory_contexts WHERE name = 'CurTransactionContext'";
            long before = session.ExecuteScalar<long>(countSql);
            for (int index = 0; index < 100; index++)
            {
                session.Execute("SELECT $1, $2", SpiParameter.Create(new PgInterval(1, -2, index)),
                    SpiParameter.Create(new PgTimeTz(new PgTime(index), 19817)));
            }

            return session.ExecuteScalar<long>(countSql) - before;
        });

    private static T Exchange<T>(T value, int mode)
    {
        const string sql = "SELECT $1";
        switch (mode)
        {
            case 0:
                return value;
            case 1:
                return Spi.ExecuteScalar<T>(sql, SpiParameter.Create(value));
            case 2:
                using (SpiPreparedStatement plan = Spi.Prepare(sql, typeof(T)))
                {
                    return plan.ExecuteScalar<T>(SpiParameter.Create(value));
                }

            case 3:
                return Spi.Connect(session => session.ExecuteScalar<T>(sql, SpiParameter.Create(value)));
            case 4:
                using (SpiCursor cursor = Spi.OpenCursor(sql, SpiParameter.Create(value)))
                {
                    SpiRow row = cursor.Fetch(1)[0];
                    cursor.Fetch(1);
                    return row.Get<T>(0);
                }

            case 5:
                return Spi.Connect(session =>
                {
                    using SpiPreparedStatement plan = session.Prepare(sql, typeof(T));
                    return plan.ExecuteScalar<T>(SpiParameter.Create(value));
                });
            case 6:
                using (SpiPreparedStatement plan = Spi.Connect(session => session.Prepare(sql, typeof(T)).Keep()))
                {
                    return plan.ExecuteScalar<T>(SpiParameter.Create(value));
                }

            case 7:
                SpiRow edited = Spi.Query("SELECT 42 AS value")[0];
                edited.Set("value", value);
                return Spi.ExecuteScalar<T>(sql, SpiParameter.Create(edited.Get<T>(0)));
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
}
