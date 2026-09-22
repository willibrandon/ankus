namespace Ankus;

/// <summary>
/// Represents PostgreSQL date, including BC dates, its full finite range, and both infinities.
/// The default value is 2000-01-01.
/// </summary>
public readonly record struct PgDate
{
    /// <summary>
    /// Creates a date from days relative to 2000-01-01. Int32 extremes represent infinities.
    /// </summary>
    /// <param name="daysSinceEpoch">The signed PostgreSQL day offset.</param>
    /// <exception cref="ArgumentOutOfRangeException">The finite offset is outside PostgreSQL's date range.</exception>
    public PgDate(int daysSinceEpoch)
    {
        if (daysSinceEpoch is not (int.MinValue or int.MaxValue) && daysSinceEpoch is < -2_451_545 or >= 2_145_031_949)
        {
            throw new ArgumentOutOfRangeException(nameof(daysSinceEpoch));
        }

        DaysSinceEpoch = daysSinceEpoch;
    }

    /// <summary>
    /// Gets the PostgreSQL day offset, including its infinity sentinels.
    /// </summary>
    public int DaysSinceEpoch { get; }

    /// <summary>
    /// Gets positive infinity.
    /// </summary>
    public static PgDate PositiveInfinity => new(int.MaxValue);

    /// <summary>
    /// Gets negative infinity.
    /// </summary>
    public static PgDate NegativeInfinity => new(int.MinValue);

    /// <summary>
    /// Gets whether this date is finite.
    /// </summary>
    public bool IsFinite => DaysSinceEpoch is not (int.MinValue or int.MaxValue);

    /// <summary>
    /// Converts a .NET date exactly; its minimum and maximum dates remain finite.
    /// </summary>
    /// <param name="value">The .NET date.</param>
    /// <returns>The PostgreSQL date.</returns>
    public static PgDate FromDateOnly(DateOnly value) => new(value.DayNumber - PgTemporal.EpochDayNumber);

    /// <summary>
    /// Converts a finite date in the .NET range to DateOnly; other values throw InvalidOperationException.
    /// </summary>
    /// <returns>The .NET date.</returns>
    /// <exception cref="InvalidOperationException">The date is infinite or outside years 1–9999.</exception>
    public DateOnly ToDateOnly()
    {
        long day = (long)DaysSinceEpoch + PgTemporal.EpochDayNumber;
        if (day < DateOnly.MinValue.DayNumber || day > DateOnly.MaxValue.DayNumber)
        {
            throw new InvalidOperationException("This PostgreSQL date cannot be represented by DateOnly.");
        }

        return DateOnly.FromDayNumber((int)day);
    }
}
