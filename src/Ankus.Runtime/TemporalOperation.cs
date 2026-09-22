namespace Ankus;

/// <summary>
/// Selects an allowlisted PostgreSQL temporal operation in the native guard.
/// </summary>
internal enum TemporalOperation
{
    /// <summary>
    /// Parses text with the requested temporal type's native input routine.
    /// </summary>
    Parse,

    /// <summary>
    /// Formats a temporal value using the server's output settings.
    /// </summary>
    Format,

    /// <summary>
    /// Formats a temporal value with the bridge's ISO representation.
    /// </summary>
    FormatIso,

    /// <summary>
    /// Adds a supported temporal pair using PostgreSQL calendar semantics.
    /// </summary>
    Add,

    /// <summary>
    /// Subtracts a supported temporal pair, producing a temporal value, interval, or day count.
    /// </summary>
    Subtract,

    /// <summary>
    /// Scales an interval by a double-precision factor.
    /// </summary>
    Multiply,

    /// <summary>
    /// Divides an interval by a double-precision divisor.
    /// </summary>
    Divide,

    /// <summary>
    /// Negates an interval through PostgreSQL's native routine.
    /// </summary>
    Negate,

    /// <summary>
    /// Truncates a timestamp or interval to the requested field.
    /// </summary>
    Truncate,

    /// <summary>
    /// Computes a symbolic age retaining calendar years and months.
    /// </summary>
    Age,

    /// <summary>
    /// Applies a named timezone or interval offset to a supported time or timestamp value.
    /// </summary>
    AtTimeZone,

    /// <summary>
    /// Extracts a temporal field as a nullable double-precision result.
    /// </summary>
    Part,

    /// <summary>
    /// Converts 30-day groups of an interval into months.
    /// </summary>
    JustifyDays,

    /// <summary>
    /// Converts 24-hour groups of an interval into days.
    /// </summary>
    JustifyHours,

    /// <summary>
    /// Normalizes interval months, days, hours, and component signs.
    /// </summary>
    Justify,

    /// <summary>
    /// Compares supported temporal values using their native comparison routines.
    /// </summary>
    Compare,

    /// <summary>
    /// Reads the current transaction's start instant.
    /// </summary>
    TransactionTimestamp,

    /// <summary>
    /// Reads the current statement's start instant.
    /// </summary>
    StatementTimestamp,

    /// <summary>
    /// Reads the current wall-clock instant, which can change within a statement.
    /// </summary>
    ClockTimestamp,

    /// <summary>
    /// Converts double-precision seconds since the Unix epoch to a timestamptz.
    /// </summary>
    FromUnixTimeSeconds,

    /// <summary>
    /// Casts a supported timestamp to date using server timezone rules where applicable.
    /// </summary>
    ToDate,

    /// <summary>
    /// Casts a supported temporal value to time without timezone.
    /// </summary>
    ToTime,

    /// <summary>
    /// Casts a supported date or timezone-aware timestamp to timestamp without timezone.
    /// </summary>
    ToTimestamp,

    /// <summary>
    /// Casts a supported date or timestamp to timestamptz using the session timezone.
    /// </summary>
    ToTimestampTz,

    /// <summary>
    /// Constructs a date from year, month, and day components.
    /// </summary>
    MakeDate,

    /// <summary>
    /// Constructs a time from hour, minute, and fractional-second components.
    /// </summary>
    MakeTime,

    /// <summary>
    /// Extracts an exact numeric temporal field on PostgreSQL versions supporting numeric extraction.
    /// </summary>
    Extract,

    /// <summary>
    /// Constructs a timezone-free timestamp from calendar and clock components.
    /// </summary>
    MakeTimestamp,

    /// <summary>
    /// Constructs a timestamptz from calendar and clock components in a selected timezone.
    /// </summary>
    MakeTimestampTz,

    /// <summary>
    /// Constructs an interval from independent calendar and clock components.
    /// </summary>
    MakeInterval,

    /// <summary>
    /// Casts a time or timestamptz to timetz using the session timezone where needed.
    /// </summary>
    ToTimeTz,

    /// <summary>
    /// Applies a fractional-second type modifier while checking finite timestamp bounds.
    /// </summary>
    Round,

    /// <summary>
    /// Reads the transaction's current date in the session timezone.
    /// </summary>
    CurrentDate,

    /// <summary>
    /// Reads precision-rounded current time with timezone at transaction start.
    /// </summary>
    CurrentTime,

    /// <summary>
    /// Reads precision-rounded local time without timezone at transaction start.
    /// </summary>
    LocalTime,

    /// <summary>
    /// Reads the transaction-start timestamptz rounded to a requested precision.
    /// </summary>
    CurrentTimestamp,

    /// <summary>
    /// Reads precision-rounded transaction-start local timestamp without timezone.
    /// </summary>
    LocalTimestamp,

    /// <summary>
    /// Formats an instant as ISO text in an explicit named timezone, resolving its historical offset.
    /// </summary>
    FormatIsoZone,

    /// <summary>
    /// Resolves a named timezone's offset at a supplied finite instant or at transaction start.
    /// </summary>
    TimeZoneOffset,

    /// <summary>
    /// Reads PostgreSQL's live wall-clock text in the session timezone.
    /// </summary>
    TimeOfDay,
}
