namespace Ankus;

/// <summary>
/// Represents PostgreSQL timestamp without time zone, including BC values and infinities.
/// The default value is 2000-01-01 00:00:00.
/// </summary>
public readonly record struct PgTimestamp
{
    /// <summary>
    /// Creates a timestamp from microseconds relative to 2000-01-01. Int64 extremes represent infinities.
    /// </summary>
    /// <param name="microsecondsSinceEpoch">The signed PostgreSQL timestamp offset.</param>
    /// <exception cref="ArgumentOutOfRangeException">The finite value is outside PostgreSQL's timestamp range.</exception>
    public PgTimestamp(long microsecondsSinceEpoch)
    {
        PgTemporal.ValidateTimestamp(microsecondsSinceEpoch);
        MicrosecondsSinceEpoch = microsecondsSinceEpoch;
    }

    /// <summary>
    /// Gets the PostgreSQL microsecond offset, including its infinity sentinels.
    /// </summary>
    public long MicrosecondsSinceEpoch { get; }

    /// <summary>
    /// Gets positive infinity.
    /// </summary>
    public static PgTimestamp PositiveInfinity => new(long.MaxValue);

    /// <summary>
    /// Gets negative infinity.
    /// </summary>
    public static PgTimestamp NegativeInfinity => new(long.MinValue);

    /// <summary>
    /// Gets whether this timestamp is finite.
    /// </summary>
    public bool IsFinite => MicrosecondsSinceEpoch is not (long.MinValue or long.MaxValue);

    /// <summary>
    /// Converts an unspecified DateTime, rejecting UTC/local kinds and sub-microsecond ticks.
    /// </summary>
    /// <param name="value">A timezone-free .NET timestamp.</param>
    /// <returns>The PostgreSQL timestamp.</returns>
    /// <exception cref="ArgumentException">The kind is not Unspecified or the value contains sub-microsecond ticks.</exception>
    public static PgTimestamp FromDateTime(DateTime value)
    {
        if (value.Kind != DateTimeKind.Unspecified)
        {
            throw new ArgumentException("A timestamp without time zone requires DateTimeKind.Unspecified.", nameof(value));
        }

        return new(PgTemporal.ToMicroseconds(value.Ticks - PgTemporal.EpochTicks));
    }

    /// <summary>
    /// Converts a representable finite value to DateTime with Kind Unspecified; other values throw InvalidOperationException.
    /// </summary>
    /// <returns>The timezone-free .NET timestamp.</returns>
    /// <exception cref="InvalidOperationException">The timestamp is infinite or outside the DateTime range.</exception>
    public DateTime ToDateTime() => PgTemporal.ToDateTime(MicrosecondsSinceEpoch, DateTimeKind.Unspecified);
}
