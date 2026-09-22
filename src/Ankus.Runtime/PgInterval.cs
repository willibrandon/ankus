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

    /// <summary>Parses PostgreSQL interval syntax on the active backend thread.</summary>
    /// <param name="text">The interval text.</param>
    /// <returns>The interval.</returns>
    public static PgInterval Parse(string text) => PgTemporal.Call<PgInterval>(TemporalOperation.Parse, PgTemporal.Text(text));

    /// <summary>Tries to parse PostgreSQL interval syntax on the active backend thread.</summary>
    /// <param name="text">The interval text.</param>
    /// <param name="value">The parsed interval, or the default value on invalid input.</param>
    /// <returns>Whether the input is valid. Backend-access and operational errors still throw.</returns>
    public static bool TryParse(string? text, out PgInterval value) => PgTemporal.TryParse(text, out value);

    /// <summary>Formats the interval using the session's IntervalStyle.</summary>
    /// <returns>The PostgreSQL interval text.</returns>
    public string ToPostgresString() => PgTemporal.Call<string>(TemporalOperation.Format, SpiParameter.Create(this));

    /// <summary>Adds interval components using PostgreSQL's overflow and infinity rules.</summary>
    /// <param name="other">The interval to add.</param>
    /// <returns>The resulting interval.</returns>
    public PgInterval Add(PgInterval other)
        => PgTemporal.Call<PgInterval>(TemporalOperation.Add, SpiParameter.Create(this), SpiParameter.Create(other));

    /// <summary>Subtracts interval components using PostgreSQL's overflow and infinity rules.</summary>
    /// <param name="other">The interval to subtract.</param>
    /// <returns>The resulting interval.</returns>
    public PgInterval Subtract(PgInterval other)
        => PgTemporal.Call<PgInterval>(TemporalOperation.Subtract, SpiParameter.Create(this), SpiParameter.Create(other));

    /// <summary>Scales the interval with PostgreSQL's fractional-month and fractional-day rules.</summary>
    /// <param name="factor">The multiplier.</param>
    /// <returns>The scaled interval.</returns>
    public PgInterval Multiply(double factor)
        => PgTemporal.Call<PgInterval>(TemporalOperation.Multiply, SpiParameter.Create(this), SpiParameter.Create(factor));

    /// <summary>Divides the interval with PostgreSQL's fractional-month and fractional-day rules.</summary>
    /// <param name="divisor">The divisor.</param>
    /// <returns>The divided interval.</returns>
    public PgInterval Divide(double divisor)
        => PgTemporal.Call<PgInterval>(TemporalOperation.Divide, SpiParameter.Create(this), SpiParameter.Create(divisor));

    /// <summary>Negates all interval components with PostgreSQL overflow and infinity handling.</summary>
    /// <returns>The negated interval.</returns>
    public PgInterval Negate() => PgTemporal.Call<PgInterval>(TemporalOperation.Negate, SpiParameter.Create(this));

    /// <summary>Normalizes thirty-day groups into months using PostgreSQL justify_days.</summary>
    /// <returns>The normalized interval.</returns>
    public PgInterval JustifyDays() => PgTemporal.Call<PgInterval>(TemporalOperation.JustifyDays, SpiParameter.Create(this));

    /// <summary>Normalizes twenty-four-hour groups into calendar days using PostgreSQL justify_hours.</summary>
    /// <returns>The normalized interval.</returns>
    public PgInterval JustifyHours() => PgTemporal.Call<PgInterval>(TemporalOperation.JustifyHours, SpiParameter.Create(this));

    /// <summary>Normalizes months, days, hours, and mixed component signs using PostgreSQL justify_interval.</summary>
    /// <returns>The normalized interval.</returns>
    public PgInterval Justify() => PgTemporal.Call<PgInterval>(TemporalOperation.Justify, SpiParameter.Create(this));

    /// <summary>Truncates an interval to a PostgreSQL field.</summary>
    /// <param name="part">The truncation field.</param>
    /// <returns>The truncated interval.</returns>
    public PgInterval Truncate(PgDateTimePart part)
        => PgTemporal.Call<PgInterval>(TemporalOperation.Truncate, PgTemporal.Part(part), SpiParameter.Create(this));

    /// <summary>Reads a floating-point field using PostgreSQL date_part semantics.</summary>
    /// <param name="part">The field.</param>
    /// <returns>The field, or null for an undefined field of an infinite value.</returns>
    public double? GetPart(PgDateTimePart part)
        => PgTemporal.Call<double?>(TemporalOperation.Part, PgTemporal.Part(part), SpiParameter.Create(this));

    /// <summary>Extracts a numeric field exactly on PostgreSQL 14+; PostgreSQL 13 converts its floating-point result.</summary>
    /// <param name="part">The field to extract.</param>
    /// <returns>The numeric field, or null for an undefined field of infinity.</returns>
    public PgNumeric? Extract(PgDateTimePart part)
        => PgTemporal.Call<PgNumeric?>(TemporalOperation.Extract, PgTemporal.Part(part), SpiParameter.Create(this));

    /// <summary>Compares with PostgreSQL's thirty-day-month convention, separately from exact managed component equality.</summary>
    /// <param name="other">The interval to compare.</param>
    /// <returns>A negative value, zero, or a positive value when this interval sorts before, equals, or sorts after the other.</returns>
    public int CompareInPostgres(PgInterval other)
        => PgTemporal.Call<int>(TemporalOperation.Compare, SpiParameter.Create(this), SpiParameter.Create(other));

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
