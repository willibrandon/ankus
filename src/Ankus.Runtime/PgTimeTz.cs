namespace Ankus;

/// <summary>
/// Represents PostgreSQL time with time zone as a local time and a fixed offset, without a date or zone name.
/// </summary>
public readonly record struct PgTimeTz
{
    /// <summary>
    /// Creates a time with a signed offset east of UTC, retaining second-resolution historical offsets.
    /// </summary>
    /// <param name="time">The local time, including 24:00:00.</param>
    /// <param name="offsetSeconds">The offset east of UTC, strictly between -57,600 and 57,600 seconds.</param>
    /// <exception cref="ArgumentOutOfRangeException">The offset is outside PostgreSQL's supported range.</exception>
    public PgTimeTz(PgTime time, int offsetSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(offsetSeconds, -57_600);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offsetSeconds, 57_600);
        Time = time;
        OffsetSeconds = offsetSeconds;
    }

    /// <summary>
    /// Gets the local wall-clock time.
    /// </summary>
    public PgTime Time { get; }

    /// <summary>
    /// Gets the offset east of UTC in seconds; positive values correspond to SQL offsets such as +05:30.
    /// </summary>
    public int OffsetSeconds { get; }

    /// <summary>
    /// Gets the fixed UTC offset as a TimeSpan.
    /// </summary>
    public TimeSpan Offset => TimeSpan.FromSeconds(OffsetSeconds);
}
