---
title: Ranges
description: Represent PostgreSQL range bounds and use native canonicalization and range operations.
---

`PgRange<T>` owns a PostgreSQL range. Its bound type selects the SQL type:

| Bound type | PostgreSQL |
| --- | --- |
| `int` | `int4range` |
| `long` | `int8range` |
| `PgNumeric`, `decimal` | `numrange` |
| `PgDate`, `DateOnly` | `daterange` |
| `PgTimestamp`, `DateTime` | `tsrange` |
| `PgTimestampTz`, `DateTimeOffset` | `tstzrange` |

The ordinary .NET types use the same checked conversions as [scalar numeric](/numeric/)
and [temporal values](/date-and-time/). Use the `Pg*` types for values outside .NET's
range, explicit temporal infinities, and full-precision numeric bounds.

## Declare a function

```csharp
[PgFunction]
public static PgRange<int> IntegerWindow(int start, int end) => new(start, end);

[PgFunction]
public static bool Includes(PgRange<DateOnly> dates, DateOnly date)
    => dates.Contains(date);
```

```sql
SELECT integer_window(1, 5); -- [1,5)
SELECT includes('[2024-01-01,2024-02-01)', '2024-01-15'); -- true
```

## Bounds and empty values

Construction and inspection work without a backend:

```csharp
var finite = PgRange.Create(1, 5);                 // [1,5)
var closed = PgRange.Create(1, 5, upperInclusive: true);
var from = new PgRange<int>(1, null);              // [1,)
var until = new PgRange<int>(null, 5);             // (,5)
var all = PgRange.Unbounded<int>();               // (,)
var empty = PgRange.Empty<int>();                 // empty
PgRange<int>? missing = null;                     // SQL NULL
```

- `Lower` and `Upper` are nullable bounds. A null bound means an unbounded end.
- `LowerInclusive` and `UpperInclusive` are false for unbounded ends.
- `IsEmpty` distinguishes an empty range from an interval.
- `IsUnbounded` identifies a nonempty range without either bound.
- A null **range reference** represents SQL NULL.

An explicit temporal infinity is still a bound. For example,
`new PgRange<PgDate>(null, PgDate.PositiveInfinity)` has an unbounded lower end
and a bounded upper end that excludes the `infinity` date value.

The parameterless constructor creates an empty range. No constructor calls
PostgreSQL. It retains finite bounds and inclusion flags even if PostgreSQL will
later canonicalize the interval to a different representation or reject it.

`PgRange.FromRange(2..^2, length: 10)` resolves a C# index range to `[2,8)`.
The length is required to resolve from-end indices. A zero-length slice becomes
an empty range.

## Canonicalization and equality

PostgreSQL validates and canonicalizes ranges whenever they cross into SQL:

```csharp
var requested = new PgRange<int>(1, 5, lowerInclusive: false, upperInclusive: true);
PgRange<int> canonical = requested.Canonicalize(); // [2,6)
```

Integer, bigint, and date ranges use discrete canonical forms. Continuous numeric
and timestamp ranges retain inclusion flags where the bounds allow it. Equal
exclusive ends become empty; reversed bounds and overflowing discrete successors
raise `PgException`.

`Equals` and `GetHashCode` compare the stored bounds and flags without a backend.
Canonicalize first when comparing independently constructed ranges for PostgreSQL
semantic equality.

## Parsing and operations

These methods use PostgreSQL on the active backend:

```csharp
PgRange<int> first = PgRange.Parse<int>("[1,5)");
PgRange<int> second = PgRange.Parse<int>("[4,9)");

bool contains = first.Contains(3);
bool overlaps = first.Overlaps(second);
PgRange<int> intersection = first.Intersect(second); // [4,5)
PgRange<int> union = first.Union(second);             // [1,9)
PgRange<int> difference = first.Except(second);       // [1,4)
```

`Contains(range)` tests range containment, and `IsAdjacentTo(range)` tests
adjacency. `Merge(range)` returns the smallest interval spanning both operands,
including a gap. `Union` and `Except` raise `PgException` when the result would
require two separate intervals.

`PgRange.TryParse<T>` returns false for malformed text, reversed bounds, and
PostgreSQL subtype range errors. Backend-access errors and checked .NET narrowing
failures propagate.

`ToPostgresString()` returns canonical SQL input text using the session's
`DateStyle` and `TimeZone`. `ToString()` is a detached diagnostic representation
of the stored managed bounds; it is not a SQL serialization contract.

## Arrays and SPI

Ranges support all typed SPI paths and [arrays](/arrays/):

```csharp
PgRange<decimal> value = Spi.ExecuteScalar<PgRange<decimal>>(
    "SELECT $1", SpiParameter.Create(PgRange.Create(1.2300m, 2.450m)));

PgRange<int>?[] ranges = Spi.ExecuteScalar<PgRange<int>?[]>(
    "SELECT ARRAY['[1,5)'::int4range, 'empty', NULL]");
```

Use `PgArray<PgRange<T>?>` to preserve dimensions and lower bounds. NULL array
elements, empty ranges, and unbounded ranges remain distinct. Returned values
own their bounds and survive result, cursor, or session disposal.
