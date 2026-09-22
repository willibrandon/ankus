namespace Ankus;

/// <summary>
/// Represents a PostgreSQL timestamp with time zone as a UTC instant, including infinities.
/// PostgreSQL does not retain the original offset or zone name.
/// </summary>
public readonly record struct PgTimestampTz
{
    /// <summary>
    /// Creates an instant from microseconds relative to 2000-01-01 UTC. Int64 extremes represent infinities.
    /// </summary>
    /// <param name="microsecondsSinceEpoch">The signed PostgreSQL timestamp offset.</param>
    /// <exception cref="ArgumentOutOfRangeException">The finite value is outside PostgreSQL's timestamp range.</exception>
    public PgTimestampTz(long microsecondsSinceEpoch)
    {
        PgTemporal.ValidateTimestamp(microsecondsSinceEpoch);
        MicrosecondsSinceEpoch = microsecondsSinceEpoch;
    }

    /// <summary>
    /// Gets the UTC microsecond offset, including PostgreSQL's infinity sentinels.
    /// </summary>
    public long MicrosecondsSinceEpoch { get; }

    /// <summary>
    /// Gets positive infinity.
    /// </summary>
    public static PgTimestampTz PositiveInfinity => new(long.MaxValue);

    /// <summary>
    /// Gets negative infinity.
    /// </summary>
    public static PgTimestampTz NegativeInfinity => new(long.MinValue);

    /// <summary>
    /// Gets whether this instant is finite.
    /// </summary>
    public bool IsFinite => MicrosecondsSinceEpoch is not (long.MinValue or long.MaxValue);

    /// <summary>
    /// Converts an offset timestamp to its UTC instant, rejecting sub-microsecond ticks.
    /// </summary>
    /// <param name="value">The .NET instant with an offset.</param>
    /// <returns>The PostgreSQL instant.</returns>
    /// <exception cref="ArgumentException">The value contains sub-microsecond ticks.</exception>
    public static PgTimestampTz FromDateTimeOffset(DateTimeOffset value)
        => new(PgTemporal.ToMicroseconds(value.UtcTicks - PgTemporal.EpochTicks));

    /// <summary>
    /// Converts a representable finite instant to a zero-offset DateTimeOffset; other values throw InvalidOperationException.
    /// </summary>
    /// <returns>The UTC .NET instant.</returns>
    /// <exception cref="InvalidOperationException">The instant is infinite or outside the DateTimeOffset range.</exception>
    public DateTimeOffset ToDateTimeOffset() => new(PgTemporal.ToDateTime(MicrosecondsSinceEpoch, DateTimeKind.Utc));
}
