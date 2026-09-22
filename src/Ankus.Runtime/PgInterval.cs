namespace Ankus;

/// <summary>
/// Preserves PostgreSQL interval's independent month, day, and microsecond components, including mixed signs.
/// Equality compares storage components, rather than PostgreSQL's thirty-day-month comparison convention.
/// </summary>
public readonly record struct PgInterval
{
    private readonly int _infinity;

    private PgInterval(int infinity) => _infinity = infinity;

    /// <summary>
    /// Creates an interval without normalizing months to days or days to hours.
    /// </summary>
    /// <param name="months">The signed month component.</param>
    /// <param name="days">The signed day component.</param>
    /// <param name="microseconds">The signed time component.</param>
    public PgInterval(int months, int days, long microseconds)
    {
        Months = months;
        Days = days;
        Microseconds = microseconds;
    }

    /// <summary>
    /// Gets the month component, without assuming a fixed month length.
    /// </summary>
    public int Months { get; }

    /// <summary>
    /// Gets the calendar-day component, distinct from twenty-four elapsed hours across daylight-saving changes.
    /// </summary>
    public int Days { get; }

    /// <summary>
    /// Gets the time component in microseconds, which may exceed a day.
    /// </summary>
    public long Microseconds { get; }

    /// <summary>
    /// Gets positive infinity, supported by PostgreSQL 17 and later.
    /// Its finite component properties are zero.
    /// </summary>
    public static PgInterval PositiveInfinity => new(1);

    /// <summary>
    /// Gets negative infinity, supported by PostgreSQL 17 and later.
    /// Its finite component properties are zero.
    /// </summary>
    public static PgInterval NegativeInfinity => new(-1);

    /// <summary>
    /// Gets whether this interval has finite components. Infinity is distinct from any finite component combination.
    /// </summary>
    public bool IsFinite => _infinity == 0;

    internal int Infinity => _infinity;

    /// <summary>
    /// Converts a fixed duration to elapsed microseconds without adding calendar-day semantics.
    /// </summary>
    /// <param name="value">The elapsed duration at whole-microsecond precision.</param>
    /// <returns>An interval with zero months and days.</returns>
    /// <exception cref="ArgumentException">The duration contains sub-microsecond ticks.</exception>
    public static PgInterval FromTimeSpan(TimeSpan value) => new(0, 0, PgTemporal.ToMicroseconds(value.Ticks));

    /// <summary>
    /// Converts an interval containing only elapsed time. Calendar months or days require a reference timestamp.
    /// </summary>
    /// <returns>The fixed duration.</returns>
    /// <exception cref="InvalidOperationException">The interval is infinite or contains calendar months or days.</exception>
    /// <exception cref="OverflowException">The microseconds exceed TimeSpan's range.</exception>
    public TimeSpan ToTimeSpan()
    {
        if (!IsFinite || Months != 0 || Days != 0)
        {
            throw new InvalidOperationException("Only finite intervals without calendar months or days represent a fixed duration.");
        }

        return new TimeSpan(checked(Microseconds * TimeSpan.TicksPerMicrosecond));
    }
}
