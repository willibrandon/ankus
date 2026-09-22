namespace Ankus;

public readonly partial record struct PgTime
{
    /// <summary>
    /// Gets the hour from zero through twenty-four; twenty-four occurs only for the end-of-day value.
    /// </summary>
    public int Hour => GetTimeParts().Hour;

    /// <summary>
    /// Gets the minute within the hour.
    /// </summary>
    public int Minute => GetTimeParts().Minute;

    /// <summary>
    /// Gets the whole second within the minute.
    /// </summary>
    public int Second => GetTimeParts().Second;

    /// <summary>
    /// Gets the microseconds within the current second, from zero through 999999.
    /// </summary>
    public int MicrosecondsWithinSecond => GetTimeParts().Microseconds;

    /// <summary>
    /// Gets the seconds within the minute, including the microsecond fraction, as a floating-point value.
    /// </summary>
    public double FractionalSecond => Second + MicrosecondsWithinSecond / 1_000_000d;

    /// <summary>
    /// Reads exact wall-clock fields without requiring a backend, preserving 24:00 and microseconds.
    /// </summary>
    /// <returns>The hour, minute, whole second, and microseconds within that second.</returns>
    public (int Hour, int Minute, int Second, int Microseconds) GetTimeParts() => PgTemporal.GetTimeParts(Microseconds);

    /// <summary>
    /// Creates a time by wrapping a signed microsecond count into one day using a nonnegative remainder.
    /// </summary>
    /// <param name="microseconds">The raw signed microseconds; whole days, including 24:00, wrap to midnight.</param>
    /// <returns>A time from midnight through one microsecond before midnight, without requiring a backend.</returns>
    public static PgTime FromMicrosecondsWrapping(long microseconds) => new(PgTemporal.WrapTime(microseconds));

    /// <summary>
    /// Formats stored diagnostic values without requiring a backend.
    /// </summary>
    /// <returns>The managed record diagnostic representation.</returns>
    public override string ToString() => $"PgTime {{ Microseconds = {Microseconds} }}";
}
