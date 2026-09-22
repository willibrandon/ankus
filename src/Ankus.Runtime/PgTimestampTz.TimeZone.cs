namespace Ankus;

public readonly partial record struct PgTimestampTz
{
    /// <summary>
    /// Gets PostgreSQL's current wall-clock text in the session timezone, like SQL timeofday().
    /// </summary>
    /// <remarks>
    /// Requires the active backend thread. The owned text includes fractional seconds and the
    /// timezone abbreviation. It reads the live clock, which can change within a statement.
    /// </remarks>
    public static string TimeOfDay => PgTemporal.Call<string>(TemporalOperation.TimeOfDay);

    /// <summary>
    /// Converts this instant to a local timestamp at a fixed interval offset from UTC.
    /// </summary>
    /// <param name="offset">A finite interval without months or days; positive values mean east of UTC.</param>
    /// <returns>The local timestamp, preserving infinities according to PostgreSQL's rules.</returns>
    /// <remarks>
    /// Requires the active backend thread. Offset precision and invalid-interval handling follow
    /// PostgreSQL's interval form of AT TIME ZONE.
    /// </remarks>
    public PgTimestamp AtTimeZone(PgInterval offset)
        => PgTemporal.Call<PgTimestamp>(TemporalOperation.AtTimeZone, SpiParameter.Create(offset), SpiParameter.Create(this));
}
