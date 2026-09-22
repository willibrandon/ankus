---
title: Numeric values
description: Use decimal or PostgreSQL's full-range numeric values without silent precision loss.
---

Use `decimal` when its range and precision fit your data:

```csharp
[PgFunction]
public static decimal AddTax(decimal amount) => amount * 1.20m;
```

Ankus maps `decimal` to PostgreSQL `numeric`. Reads must be exact: values that
would overflow, round, or underflow a decimal throw `OverflowException`. This
includes NaN and infinities. SQL NULL maps to `decimal?`.

## Full-range numeric

[`PgNumeric`](/api/ankus.pgnumeric/) retains PostgreSQL's full numeric range and
display scale, including NaN and both infinities:

```csharp
[PgFunction]
public static PgNumeric Multiply(PgNumeric left, PgNumeric right) => left * right;

[PgFunction]
public static PgNumeric? ReadNumeric(string text)
    => PgNumeric.TryParse(text, out PgNumeric value) ? value : null;
```

The default value is zero. `Parse` accepts PostgreSQL numeric syntax, including
exponents. `Text` and `ToString()` return owned, culture-independent output text;
`ToNormalizedString()` removes insignificant fractional zeroes. `Scale` reports
display scale and returns null for special values.

Equality ignores display scale: `1.2300` equals `1.23` and their hashes agree.
As in PostgreSQL, NaN equals NaN and sorts above all other numeric values.
`IsFinite`, `IsNaN`, and the nullable `Sign` property distinguish special values.
Numeric infinities require PostgreSQL 14 or later.

## Arithmetic and precision

Operators `+`, `-`, `*`, `/`, `%`, and unary `-` use PostgreSQL's numeric routines.
Methods include `Abs`, `Round`, `Truncate`, `Ceiling`, `Floor`, `Sqrt`, `Exp`,
`Log`, `Pow`, `GreatestCommonDivisor`, and `LeastCommonMultiple`.

```csharp
[PgFunction]
public static PgNumeric Price(PgNumeric value) => value.Rescale(precision: 10, scale: 2);
```

`Rescale` applies PostgreSQL's declared precision and scale, including its rounding
and range checks. PostgreSQL 15+ supports negative scales and scales larger than
precision. `Round` breaks ties away from zero; .NET's default decimal rounding
uses ties-to-even. Explicit rescaling is useful when returning a constrained value
from a function, whose SQL signature otherwise uses unconstrained `numeric`.

Parsing and arithmetic require the active PostgreSQL backend thread. Native errors
become catchable `PgException` instances. Text access, comparison, hashing, and exact
.NET conversions also work outside PostgreSQL.

## .NET conversions and SPI

`FromDecimal` and `ToDecimal` preserve numeric value exactly. `FromBigInteger` and
`ToBigInteger` handle finite integers within PostgreSQL's range; fractional inputs
are rejected by `ToBigInteger`. `FromDouble` and `ToDouble` use the server's
floating-point conversion rules and may lose precision.

Both `PgNumeric` and `decimal` work in typed SPI queries, prepared plans, sessions,
cursors, and local row edits:

```csharp
PgNumeric? total = Spi.ExecuteScalar<PgNumeric?>("SELECT sum(amount) FROM invoices");
decimal price = Spi.ExecuteScalar<decimal>(
    "SELECT $1::numeric(10,2)", SpiParameter.Create(12.345m));
```

Untyped numeric cells contain `PgNumeric`. Reading them with `row.Get<decimal>()`
performs the same exact conversion as a generated function adapter.
