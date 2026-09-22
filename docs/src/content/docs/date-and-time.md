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

`PgInterval.PositiveInfinity` and `NegativeInfinity` require PostgreSQL 17 or
later. Their finite component properties are zero; check `IsFinite` first.
On earlier servers, writing an infinite interval raises an unsupported-feature
error. Finite components that match a newer server's reserved infinity sentinel
raise a range error rather than changing meaning.

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
