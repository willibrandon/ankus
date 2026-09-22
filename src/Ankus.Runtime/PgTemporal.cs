namespace Ankus;

/// <summary>
/// Shares exact PostgreSQL epoch, range, and precision checks among temporal values.
/// </summary>
internal static class PgTemporal
{
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
