namespace Ankus;

public readonly partial record struct PgTimeTz
{
    /// <summary>
    /// Gets the local hour from zero through twenty-four, without adjusting for the offset.
    /// </summary>
    public int Hour => Time.Hour;

    /// <summary>
    /// Gets the local minute within the hour.
    /// </summary>
    public int Minute => Time.Minute;

    /// <summary>
    /// Gets the local whole second within the minute.
    /// </summary>
    public int Second => Time.Second;

    /// <summary>
    /// Gets the microseconds within the current local second, from zero through 999999.
    /// </summary>
    public int MicrosecondsWithinSecond => Time.MicrosecondsWithinSecond;

    /// <summary>
    /// Gets the local seconds within the minute, including the microsecond fraction.
    /// </summary>
    public double FractionalSecond => Time.FractionalSecond;

    /// <summary>
    /// Gets the signed whole-hour offset east of UTC, truncated toward zero like PostgreSQL's timezone_hour field.
    /// </summary>
    public int OffsetHours => OffsetSeconds / 3_600;

    /// <summary>
    /// Gets the signed remaining whole-minute offset east of UTC, like PostgreSQL's timezone_minute field.
    /// </summary>
    public int OffsetMinutes => OffsetSeconds / 60 % 60;

    /// <summary>
    /// Reads exact local wall-clock fields without requiring a backend or adjusting for the offset.
    /// </summary>
    /// <returns>The local hour, minute, whole second, and microseconds within that second.</returns>
    public (int Hour, int Minute, int Second, int Microseconds) GetTimeParts() => Time.GetTimeParts();

    /// <summary>
    /// Converts the fixed-offset local time to UTC, wrapping across midnight and normalizing 24:00 to midnight.
    /// </summary>
    /// <returns>The UTC time of day without requiring a backend.</returns>
    public PgTime ToUtc() => PgTime.FromMicrosecondsWrapping(Time.Microseconds - (long)OffsetSeconds * 1_000_000);

    /// <summary>
    /// Creates a time from PostgreSQL's raw storage convention, wrapping the clock and offset into their valid ranges.
    /// </summary>
    /// <param name="microseconds">The signed raw time; its nonnegative remainder modulo one day becomes the local clock.</param>
    /// <param name="secondsWestOfUtc">
    /// The raw PostgreSQL offset west of UTC. Its nonnegative remainder modulo 57600 is negated to obtain
    /// the public <see cref="OffsetSeconds"/> value east of UTC.
    /// </param>
    /// <returns>The wrapped clock and fixed offset without requiring a backend.</returns>
    public static PgTimeTz FromRawWrapping(long microseconds, int secondsWestOfUtc)
    {
        int westRemainder = secondsWestOfUtc % 57_600;
        return new(PgTime.FromMicrosecondsWrapping(microseconds), -(westRemainder < 0 ? westRemainder + 57_600 : westRemainder));
    }

    /// <summary>
    /// Formats stored diagnostic values without requiring a backend.
    /// </summary>
    /// <returns>The managed record diagnostic representation.</returns>
    public override string ToString() => $"PgTimeTz {{ Time = {Time}, OffsetSeconds = {OffsetSeconds}, Offset = {Offset} }}";
}
