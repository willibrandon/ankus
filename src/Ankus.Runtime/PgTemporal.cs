namespace Ankus;

/// <summary>
/// Shares exact PostgreSQL epoch, range, and precision checks among temporal values.
/// </summary>
internal static class PgTemporal
{
    /// <summary>
    /// Calls a PostgreSQL temporal routine on the active backend thread.
    /// </summary>
    internal static T Call<T>(TemporalOperation operation, params ReadOnlySpan<SpiParameter> parameters)
        => NativeBackend.Temporal<T>(operation, parameters);

    /// <summary>
    /// Validates a fractional-second precision before passing it as a native type modifier.
    /// </summary>
    internal static SpiParameter Precision(int precision)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(precision);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(precision, 6);
        return SpiParameter.Create(precision);
    }

    /// <summary>
    /// Parses temporal text, catching input data errors while preserving backend-access and operational failures.
    /// </summary>
    internal static bool TryParse<T>(string? text, out T value) where T : struct
    {
        value = default;
        if (text is null || text.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            value = Call<T>(TemporalOperation.Parse, Text(text));
            return true;
        }
        catch (PgException error) when (error.SqlState is "22007" or "22008" or "22009" or "22015" or "22023")
        {
            return false;
        }
        catch (System.Text.EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Validates text that will be passed to a PostgreSQL input or timezone routine.
    /// </summary>
    internal static SpiParameter Text(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("PostgreSQL temporal text cannot contain a zero character.", nameof(value));
        }

        return SpiParameter.Create(value);
    }

    /// <summary>
    /// Maps public field names to PostgreSQL's spellings without culture-sensitive enum formatting.
    /// </summary>
    internal static SpiParameter Part(PgDateTimePart part) => SpiParameter.Create(part switch
    {
        PgDateTimePart.Century => "century",
        PgDateTimePart.Day => "day",
        PgDateTimePart.Decade => "decade",
        PgDateTimePart.DayOfWeek => "dow",
        PgDateTimePart.DayOfYear => "doy",
        PgDateTimePart.Epoch => "epoch",
        PgDateTimePart.Hour => "hour",
        PgDateTimePart.IsoDayOfWeek => "isodow",
        PgDateTimePart.IsoYear => "isoyear",
        PgDateTimePart.Julian => "julian",
        PgDateTimePart.Microseconds => "microseconds",
        PgDateTimePart.Millennium => "millennium",
        PgDateTimePart.Milliseconds => "milliseconds",
        PgDateTimePart.Minute => "minute",
        PgDateTimePart.Month => "month",
        PgDateTimePart.Quarter => "quarter",
        PgDateTimePart.Second => "second",
        PgDateTimePart.TimeZone => "timezone",
        PgDateTimePart.TimeZoneHour => "timezone_hour",
        PgDateTimePart.TimeZoneMinute => "timezone_minute",
        PgDateTimePart.Week => "week",
        PgDateTimePart.Year => "year",
        _ => throw new ArgumentOutOfRangeException(nameof(part)),
    });

    internal const long EpochTicks = 630_822_816_000_000_000;
    internal const int EpochDayNumber = 730_119;
    internal const long MicrosecondsPerDay = 86_400_000_000;
    internal const long MinTimestamp = -211_813_488_000_000_000;
    internal const long EndTimestamp = 9_223_371_331_200_000_000;

    /// <summary>
    /// Rejects finite timestamp values outside PostgreSQL's supported range.
    /// </summary>
    internal static void ValidateTimestamp(long value)
    {
        if (value is not (long.MinValue or long.MaxValue) && (value < MinTimestamp || value >= EndTimestamp))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "The value is outside PostgreSQL's timestamp range.");
        }
    }

    /// <summary>
    /// Converts ticks without silently truncating sub-microsecond precision.
    /// </summary>
    internal static long ToMicroseconds(long ticks)
    {
        if (ticks % TimeSpan.TicksPerMicrosecond != 0)
        {
            throw new ArgumentException("PostgreSQL temporal values require whole microseconds.", nameof(ticks));
        }

        return ticks / TimeSpan.TicksPerMicrosecond;
    }

    /// <summary>
    /// Converts a PostgreSQL timestamp to a representable .NET date without substituting infinity sentinels.
    /// </summary>
    internal static DateTime ToDateTime(long microseconds, DateTimeKind kind)
    {
        Int128 ticks = (Int128)microseconds * TimeSpan.TicksPerMicrosecond + EpochTicks;
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            throw new InvalidOperationException("This PostgreSQL timestamp cannot be represented by DateTime.");
        }

        return new DateTime((long)ticks, kind);
    }
}
