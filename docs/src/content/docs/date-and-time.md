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

## Calendar intervals

PostgreSQL keeps months, days, and elapsed time separate. A month has no fixed
duration, and a calendar day can be 23 or 25 hours across daylight-saving changes.

```csharp
var calendarDay = new PgInterval(months: 0, days: 1, microseconds: 0);
var elapsedDay = PgInterval.FromTimeSpan(TimeSpan.FromHours(24));
```

These can produce different timestamps when added to the same instant.
`ToTimeSpan()` therefore accepts only finite intervals whose month and day
components are zero. Mixed component signs are preserved. Managed equality
compares exact components; PostgreSQL's interval comparison instead treats a
month as thirty days.

Use `Add` and `Subtract` for PostgreSQL calendar arithmetic:

```csharp
[PgFunction]
public static PgTimestamp NextMonth(PgTimestamp value)
    => value.Add(new PgInterval(months: 1, days: 0, microseconds: 0));
```

January 31 becomes the last day of February. For `PgTimestampTz`, calendar months
and days use the session's timezone. Adding elapsed hours can give a different
result across a daylight-saving transition.

Subtracting two timestamps returns an elapsed interval. `Age` returns a symbolic
calendar difference with years and months. Intervals also support `Multiply`,
`Divide`, `Negate`, `JustifyDays`, `JustifyHours`, and `Justify`. Use
`CompareInPostgres` when you want PostgreSQL's interval ordering.

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

`GetPart(PgDateTimePart)` follows `date_part` semantics and returns `double?`.
Undefined fields of infinite values return null; unsupported fields raise
`PgException`. Floating-point extraction can lose precision for large values.
On PostgreSQL 13, date extraction uses the server's timestamp conversion and its
narrower range. `Truncate` supports timestamps and intervals.

`PgDate.Create` and `PgTime.Create` validate calendar fields in PostgreSQL. Negative
years denote BC; year zero is invalid. `PgDate.AtTime` combines a date with a time
or fixed-offset time. Timestamp `ToDate` and `ToTime` conversions follow server
rules; `ToTime` returns null for infinity.

`PgTimestampTz.TransactionTimestamp`, `StatementTimestamp`, and `ClockTimestamp`
read the three PostgreSQL clocks. `FromUnixTimeSeconds` accepts fractional seconds.

Server parsing, formatting, arithmetic, extraction, and timezone methods require
the active backend thread inside an Ankus callback. Their native errors become
catchable `PgException` instances. Stored-value access, exact .NET conversions,
and date/time/timestamp comparisons also work outside PostgreSQL. `CompareTo` and
relational operators preserve infinities and the distinct 24:00 time value.

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
