namespace Ankus;

/// <summary>
/// Represents PostgreSQL time without time zone at microsecond precision, including 24:00:00.
/// </summary>
public readonly record struct PgTime
{
    /// <summary>
    /// Creates a time from microseconds since midnight, between zero and 86,400,000,000 inclusive.
    /// </summary>
    /// <param name="microseconds">The time of day in microseconds.</param>
    /// <exception cref="ArgumentOutOfRangeException">The offset is negative or later than 24:00:00.</exception>
    public PgTime(long microseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(microseconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(microseconds, PgTemporal.MicrosecondsPerDay);
        Microseconds = microseconds;
    }

    /// <summary>
    /// Gets the microseconds since midnight. The end-of-day value is distinct from midnight.
    /// </summary>
    public long Microseconds { get; }

    /// <summary>
    /// Gets PostgreSQL's 24:00:00 value.
    /// </summary>
    public static PgTime EndOfDay => new(PgTemporal.MicrosecondsPerDay);

    /// <summary>
    /// Converts a .NET time, rejecting sub-microsecond ticks.
    /// </summary>
    /// <param name="value">The .NET time.</param>
    /// <returns>The PostgreSQL time.</returns>
    /// <exception cref="ArgumentException">The value contains sub-microsecond ticks.</exception>
    public static PgTime FromTimeOnly(TimeOnly value) => new(PgTemporal.ToMicroseconds(value.Ticks));

    /// <summary>
    /// Converts to TimeOnly, rejecting 24:00:00 rather than wrapping to midnight.
    /// </summary>
    /// <returns>The .NET time.</returns>
    /// <exception cref="InvalidOperationException">The value is 24:00:00.</exception>
    public TimeOnly ToTimeOnly()
        => Microseconds == PgTemporal.MicrosecondsPerDay
            ? throw new InvalidOperationException("TimeOnly cannot represent 24:00:00.")
            : new TimeOnly(Microseconds * TimeSpan.TicksPerMicrosecond);
}
