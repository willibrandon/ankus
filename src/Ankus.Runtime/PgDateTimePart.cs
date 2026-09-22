namespace Ankus;

/// <summary>
/// Selects a PostgreSQL date_part or date_trunc field. Support depends on the temporal type and server version.
/// </summary>
public enum PgDateTimePart
{
    /// <summary>Calendar century.</summary>
    Century,
    /// <summary>Day of month, or the interval day component.</summary>
    Day,
    /// <summary>Calendar decade.</summary>
    Decade,
    /// <summary>Sunday-based day of week, zero through six.</summary>
    DayOfWeek,
    /// <summary>Day of year, starting at one.</summary>
    DayOfYear,
    /// <summary>Seconds relative to the Unix epoch, or interval duration under PostgreSQL's comparison rules.</summary>
    Epoch,
    /// <summary>Hour component.</summary>
    Hour,
    /// <summary>Monday-based ISO day of week, one through seven.</summary>
    IsoDayOfWeek,
    /// <summary>ISO week-numbering year.</summary>
    IsoYear,
    /// <summary>Julian date.</summary>
    Julian,
    /// <summary>Seconds including the fractional part, multiplied by one million.</summary>
    Microseconds,
    /// <summary>Calendar millennium.</summary>
    Millennium,
    /// <summary>Seconds including the fractional part, multiplied by one thousand.</summary>
    Milliseconds,
    /// <summary>Minute component.</summary>
    Minute,
    /// <summary>Month of year, or the interval month component modulo twelve.</summary>
    Month,
    /// <summary>Quarter of year.</summary>
    Quarter,
    /// <summary>Seconds including the fractional part.</summary>
    Second,
    /// <summary>Seconds east of UTC.</summary>
    TimeZone,
    /// <summary>Hour component of the UTC offset.</summary>
    TimeZoneHour,
    /// <summary>Minute component of the UTC offset.</summary>
    TimeZoneMinute,
    /// <summary>ISO week number.</summary>
    Week,
    /// <summary>Year, with negative BC years and no year zero.</summary>
    Year,
}
