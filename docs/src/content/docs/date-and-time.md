---
title: Date and time values
description: Use .NET date and time types or preserve PostgreSQL's full temporal range.
---

Use ordinary .NET types when their range fits your data:

```csharp
[PgFunction]
public static DateOnly NextDay(DateOnly date) => date.AddDays(1);

[PgFunction]
public static DateTimeOffset AddHour(DateTimeOffset instant) => instant.AddHours(1);
```

| .NET type | PostgreSQL type | Conversion |
| --- | --- | --- |
| `DateOnly` | `date` | Finite dates in years 1–9999 |
| `TimeOnly` | `time` | Midnight through 23:59:59.999999 |
| `DateTime` | `timestamp` | Requires `Kind.Unspecified` |
| `DateTimeOffset` | `timestamptz` | Converts to UTC; reads have offset zero |
| `TimeSpan` | `interval` | Elapsed microseconds, with no calendar months or days |

PostgreSQL stores temporal values at microsecond precision. Ankus rejects .NET
values containing sub-microsecond ticks rather than silently rounding them.
Unrepresentable values throw; .NET minimum and maximum dates are never treated
as infinity sentinels.

`timestamptz` stores an instant, not its original offset or a time-zone name.
The session's `TimeZone` affects how PostgreSQL displays it. Use `DateTimeOffset`
for instants and `DateTime` for timezone-free wall-clock values.

## Full-range values

The Ankus value types preserve PostgreSQL's full range and special values:

| Type | Stored value |
| --- | --- |
| [`PgDate`](/api/ankus.pgdate/) | Signed days since 2000-01-01, including BC dates and infinities |
| [`PgTime`](/api/ankus.pgtime/) | Microseconds since midnight, including the distinct value 24:00:00 |
| [`PgTimeTz`](/api/ankus.pgtimetz/) | Local time and a fixed offset in seconds east of UTC |
| [`PgTimestamp`](/api/ankus.pgtimestamp/) | Microseconds since 2000-01-01 without a timezone, including infinities |
| [`PgTimestampTz`](/api/ankus.pgtimestamptz/) | Microseconds since 2000-01-01 UTC, including infinities |
| [`PgInterval`](/api/ankus.pginterval/) | Independent month, day, and microsecond components |

```csharp
[PgFunction]
public static PgDate? KeepDate(PgDate? date) => date;

[PgFunction]
public static PgDate NoDeadline() => PgDate.PositiveInfinity;

[PgFunction]
public static PgTime ClosingTime() => PgTime.EndOfDay;
```

The default date and timestamp values are the PostgreSQL epoch, 2000-01-01.
The default time is midnight and the default interval is zero. Nullable wrappers
represent SQL NULL independently of these values and infinities.

Use `FromDateOnly`, `ToDateOnly`, and the corresponding conversion methods on
the other types for explicit .NET conversions. Their range, precision, and
timezone checks are the same as the generated function conversions.

### Raw values and calendar fields

The raw constructors validate PostgreSQL's finite range and preserve its infinity
sentinels. Use `PgDate.FromRawSaturating`, `PgTimestamp.FromRawSaturating`, or
`PgTimestampTz.FromRawSaturating` when an out-of-range encoding should instead
become the corresponding infinity. `IsPositiveInfinity` and `IsNegativeInfinity`
distinguish the two sentinels.

`PgTime.FromMicrosecondsWrapping` reduces a signed microsecond count modulo one
day. It accepts the complete `long` range; minus one becomes 23:59:59.999999, and
24 hours becomes midnight. This differs from the checked constructor, which
retains the distinct 24:00 value.

`PgTimeTz.FromRawWrapping` accepts PostgreSQL's raw pair: microseconds and seconds
**west** of UTC. It applies pgrx's Euclidean modulo rules to both fields, including
the offset modulo 57,600. For example, a raw west offset of -1 wraps to 57,599
seconds west, so the result's `OffsetSeconds` is -57,599. Use the ordinary
constructor with seconds **east** of UTC when constructing a meaningful signed
offset; it validates the offset without wrapping it.

Dates and timestamps expose `Year`, `Month`, `Day`, and `GetDateParts()`.
Negative years denote BC and there is no year zero. Times and timestamps expose
`Hour`, `Minute`, `Second`, `MicrosecondsWithinSecond`, `FractionalSecond`, and
`GetTimeParts()`:

```csharp
var time = new PgTime(45_296_123_456); // 12:34:56.123456
int second = time.Second; // 56
int fraction = time.MicrosecondsWithinSecond; // 123456
double seconds = time.FractionalSecond; // 56.123456
(int hour, int minute, int wholeSecond, int microseconds) = time.GetTimeParts();
```

The tuple's microseconds are within the second. PostgreSQL's
`EXTRACT(MICROSECONDS)` includes whole seconds: the same time yields 56,123,456
through `GetPart(PgDateTimePart.Microseconds)` or `Extract`. The explicitly named
fractional property avoids that ambiguity.

`PgDate.ToJulianDays`, `ToUnixEpochDays`, and `ToUnixTimeSeconds` return exact
finite epoch values across PostgreSQL's full date range. These methods and finite
calendar/clock accessors throw `InvalidOperationException` for infinity. Raw
storage properties still expose the sentinel, and `GetPart`/`Extract` retain SQL's
infinity and NULL behavior.

Date, time, fixed-offset time, and timezone-free timestamp fields work outside
PostgreSQL. `PgTimestampTz` fields require the backend because they follow the
session timezone at that instant. They use native extraction, including at
endpoints where conversion to a local `timestamp` would overflow. Diagnostic
`ToString()` remains usable outside the backend for every value, including
infinities.

## Calendar intervals

PostgreSQL keeps months, days, and elapsed time separate. A month has no fixed
duration, and a calendar day can be 23 or 25 hours across daylight-saving changes.

```csharp
var calendarDay = new PgInterval(months: 0, days: 1, microseconds: 0);
PgInterval elapsedDay = PgInterval.FromTimeSpan(TimeSpan.FromHours(24));
```

These can produce different timestamps when added to the same instant.
`ToTimeSpan()` therefore accepts only finite intervals whose month and day
components are zero. Mixed component signs are preserved. Managed equality
compares exact components; PostgreSQL's interval comparison instead treats a
month as thirty days.

Use `Add`, `Subtract`, or arithmetic operators for PostgreSQL calendar arithmetic:

```csharp
[PgFunction]
public static PgTimestamp NextMonth(PgTimestamp value)
    => value + PgInterval.FromMonths(1);
```

January 31 becomes the last day of February. For `PgTimestampTz`, calendar months
and days use the session's timezone. Adding elapsed hours can give a different
result across a daylight-saving transition.

Subtracting two timestamps returns an elapsed interval. `Age` returns a symbolic
calendar difference with years and months. Intervals also support `Multiply`,
`Divide`, `Negate`, `JustifyDays`, `JustifyHours`, and `Justify`. Use
`CompareInPostgres` when you want PostgreSQL's interval ordering.

`PgInterval.Create` accepts years, months, weeks, days, hours, minutes, and fractional
seconds. Named arguments make the calendar distinction explicit:

```csharp
PgInterval calendarDay = PgInterval.Create(days: 1);
PgInterval elapsedDay = PgInterval.FromHours(24);
```

`FromYears` through `FromMicroseconds` provide individual unit factories.
`FromMicroseconds` preserves the complete signed 64-bit value. `Abs` takes the
absolute value of each stored component, throwing on signed minimum values.
`Sign` and `ToComparisonMicroseconds` use PostgreSQL's thirty-day-month comparison
convention; they do not calculate elapsed time across a calendar.

`PgInterval.PositiveInfinity` and `NegativeInfinity` require PostgreSQL 17 or
later. Their finite component properties are zero; check `IsFinite` first.
On earlier servers, writing an infinite interval raises an unsupported-feature
error. Finite components that match a newer server's reserved infinity sentinel
raise a range error rather than changing meaning.

## Parsing and formatting

The six full-range types expose `Parse`, `TryParse`, and `ToPostgresString`:

```csharp
[PgFunction]
public static PgDate? ReadDate(string text)
    => PgDate.TryParse(text, out PgDate date) ? date : null;
```

Parsing accepts PostgreSQL input syntax, including special values. `DateStyle`
controls ambiguous date input and date/timestamp output. `IntervalStyle` controls
interval output. `TryParse` returns `false` and a default out value for invalid
input; backend-access and operational errors still throw.

Dates, times, and timestamps also expose `ToIsoString`, using PostgreSQL's ISO JSON
representation independently of `DateStyle`. A `PgTimestampTz` is formatted in the
session's timezone, with its offset. `ToPostgresString` and `ToIsoString` are
explicit server-formatting methods; the record struct's `ToString()` remains a
managed diagnostic representation.

`PgTimestampTz.ToIsoString(zone)` formats an instant in an explicit timezone. Its
offset comes from that instant, including historical offsets and daylight-saving
transitions. `PgTimeTz.ToIsoString(zone)` uses current-date rules because a time
with offset has no stored date.

## Timezones and fields

```csharp
[PgFunction]
public static PgTimestampTz NewYorkTime(PgTimestamp local)
    => local.AtTimeZone("America/New_York");

[PgFunction]
public static PgTimestampTz NewYorkDay(PgTimestampTz instant)
    => instant.Truncate(PgDateTimePart.Day, "America/New_York");
```

`PgTimestamp.AtTimeZone` interprets a wall-clock time in the named zone.
`PgTimestampTz.AtTimeZone` returns the local wall-clock timestamp for an instant.
Both follow PostgreSQL's rules for ambiguous and nonexistent times. Explicit-zone
truncation leaves the session timezone unchanged. A `PgTimeTz` has no date, so its
named-zone conversion uses PostgreSQL's current-date rules for daylight saving.

The three `AtTimeZone` methods also accept `PgInterval` for a fixed offset.
PostgreSQL rejects month/day components and infinite offsets for finite inputs;
it truncates offset fractions to whole seconds. Positive offsets mean east of
UTC. For a timestamp without a zone, the offset interprets the local clock; for
an instant or fixed-offset time, it shifts the local clock. A `timetz` result
outside `PgTimeTz`'s supported offset range is rejected during managed conversion.

`PgTimestampTz.ToUtc()` returns its timezone-free UTC timestamp, preserving
infinities. `PgTimeTz.ToUtc()` returns a time after offset adjustment and wrapping
at midnight; 24:00+00 becomes 00:00. Both work outside the backend.

[`PgTimeZone.GetOffset`](/api/ankus.pgtimezone/) resolves names, abbreviations,
and PostgreSQL POSIX timezone specifications using the server's timezone data:

```csharp
TimeSpan currentOffset = PgTimeZone.GetOffset("America/New_York");
TimeSpan historicalOffset = PgTimeZone.GetOffset("America/New_York", instant);
```

The first overload resolves at transaction start, matching pgrx's timezone-offset
helper. The second resolves at the supplied finite instant, including historical
second offsets and daylight-saving changes. Fixed abbreviations keep their fixed
offset; dynamic abbreviations use PostgreSQL's abbreviation table. Neither changes
the session timezone. Offset lookup can resolve a POSIX offset beyond `PgTimeTz`'s
range, such as `UTC+20`; constructing a `PgTimeTz` with that zone rejects the
unrepresentable offset.

`GetPart(PgDateTimePart)` follows `date_part` semantics and returns `double?`.
Undefined fields of infinite values return null; unsupported fields raise
`PgException`. Floating-point extraction can lose precision for large values.
On PostgreSQL 13, date extraction uses the server's timestamp conversion and its
narrower range. `Truncate` supports timestamps and intervals.

Use `Extract(PgDateTimePart)` for a [`PgNumeric`](/numeric/) result. PostgreSQL 14+
extracts directly to numeric, retaining exact fractional seconds even for
timestamps near the server's range limit. PostgreSQL 13 converts its floating-point
extraction result, matching that version's precision limits.

`Create` constructs dates, times, and timestamps from fields with PostgreSQL's
validation and fractional-second rounding. Negative years denote BC; year zero
is invalid. `PgTimestampTz.Create` accepts an optional explicit zone; otherwise
it uses the session timezone. `PgTimeTz.Create` accepts an explicit offset in
seconds or uses the session's current-date offset.

`PgTimeTz.Create(hour, minute, second, zone)` attaches the named zone's offset at
transaction start while retaining the supplied clock fields, including 24:00.
To shift a session-local clock to an interval offset, use
`PgTimeTz.Create(hour, minute, second).AtTimeZone(offset)`.
`OffsetHours` and `OffsetMinutes` expose signed components; `OffsetSeconds`
retains the complete offset, including seconds.

`PgDate.AtTime` combines a date with a time or fixed-offset time. Timestamp `ToDate`,
`ToTime`, and `PgTimestampTz.ToTimeTz` conversions follow server rules; the time
conversions return null for infinity.

`PgTimestampTz.TransactionTimestamp`, `StatementTimestamp`, and `ClockTimestamp`
read the three PostgreSQL clocks. `FromUnixTimeSeconds` accepts fractional seconds.

`PgTimestampTz.TimeOfDay` returns PostgreSQL's owned `timeofday()` text, including
six fractional-second digits and the session timezone abbreviation. It reads the
live wall clock, so it can change within a statement or transaction.

`PgDate.CurrentDate`, `PgTime.GetLocalTime`, `PgTimeTz.GetCurrentTime`,
`PgTimestamp.GetLocalTimestamp`, and `PgTimestampTz.GetCurrentTimestamp` match
SQL's current/local expressions. The methods accept a precision from zero through
six fractional-second digits and use the current transaction's start time.

Times and timestamps also expose `Round(precision)`, following PostgreSQL's type
modifiers. Rounding can reach 24:00 or carry into the next day. A timestamp that
rounds outside the finite range raises a `PgException` range error.

Server parsing, formatting, arithmetic, extraction, and timezone methods require
the active backend thread inside an Ankus callback. Their native errors become
catchable `PgException` instances. Stored-value access, exact .NET conversions,
and date/time/timestamp comparisons also work outside PostgreSQL. `CompareTo` and
relational operators preserve infinities and the distinct 24:00 time value.

## JSON serialization

The six full-range types have `System.Text.Json` converters. Include your containing
type in a source-generated context and pass its metadata:

```csharp
public sealed record Appointment(PgTimestampTz StartsAt, PgInterval Duration);

[JsonSerializable(typeof(Appointment))]
internal partial class AppointmentJsonContext : JsonSerializerContext;

// Inside an extension callback:
PgJson json = PgJson.Serialize(appointment, AppointmentJsonContext.Default.Appointment);
```

Dates, times, and timestamps serialize as PostgreSQL ISO strings; intervals use
the session's `IntervalStyle`. BC dates, infinities, 24:00, and second-resolution
offsets remain representable. Nullable types serialize as JSON null. Reading and
writing temporal JSON requires the backend thread, and parsing follows the same
rules as `Parse`. Invalid values throw `JsonException` with the property path and
the underlying `PgException` when PostgreSQL rejected the input.

## SPI

All temporal types work as typed parameters and results, including prepared
plans, sessions, cursors, and local row edits:

```csharp
PgDate date = Spi.ExecuteScalar<PgDate>("SELECT date 'infinity'");
DateOnly next = Spi.ExecuteScalar<DateOnly>(
    "SELECT $1 + 1", SpiParameter.Create(new DateOnly(2026, 9, 22)));
```

Untyped row cells contain the full-range `Pg*` value. `row.Get<DateOnly>(ordinal)`
and the other .NET readers perform checked conversions from that value.
