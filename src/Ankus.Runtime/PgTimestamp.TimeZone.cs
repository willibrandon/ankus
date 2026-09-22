namespace Ankus;

public readonly partial record struct PgTimestamp
{
    /// <summary>
    /// Interprets this local timestamp at a fixed interval offset from UTC.
    /// </summary>
    /// <param name="offset">A finite interval without months or days; positive values mean east of UTC.</param>
    /// <returns>The resulting UTC instant, preserving infinities according to PostgreSQL's rules.</returns>
    /// <remarks>
    /// Requires the active backend thread. Offset precision and invalid-interval handling follow
    /// PostgreSQL's interval form of AT TIME ZONE.
    /// </remarks>
    public PgTimestampTz AtTimeZone(PgInterval offset)
        => PgTemporal.Call<PgTimestampTz>(TemporalOperation.AtTimeZone, SpiParameter.Create(offset), SpiParameter.Create(this));
}
