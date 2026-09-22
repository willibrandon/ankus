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
uses ties-to-even.

## Function constraints

Declare precision and scale on parameters and return values:

```csharp
[PgFunction]
[return: PgNumericPrecision(10, 2)]
public static PgNumeric AddTax([PgNumericPrecision(10, 2)] PgNumeric amount)
    => amount * 1.20m;
```

The input is rounded before the method runs. The return value is rounded after
the method returns, including after its `finally` blocks. Overflow raises a native
PostgreSQL range error. For `decimal` parameters, rescaling happens before checked
decimal conversion. Nullable parameters and results retain their SQL NULL behavior.

Precision must be 1–1000 and scale must be -1000–1000; omitted scale means zero.
Negative scales and scales above precision require PostgreSQL 15+. Invalid values
or an attribute on a nonnumeric type produce compiler diagnostic `ANKUS003`.

PostgreSQL discards type modifiers in function signatures. Ankus enforces these
constraints through the guarded dispatcher; they do not distinguish SQL overloads
and do not apply to direct C# method calls. Use `Rescale` for an explicit conversion
inside managed code.

Parsing and arithmetic require the active PostgreSQL backend thread. Native errors
become catchable `PgException` instances. Text access, comparison, hashing, and exact
.NET conversions also work outside PostgreSQL.

## .NET conversions and SPI

`FromDecimal` and `ToDecimal` preserve numeric value exactly. `FromBigInteger` and
`ToBigInteger` handle finite integers within PostgreSQL's range; fractional inputs
are rejected by `ToBigInteger`.

`FromInteger<T>` and `ToInteger<T>` support .NET binary integer types, including
unsigned values, `Int128`, `UInt128`, and `BigInteger`. Narrowing is exact and
checked: fractions, negative-to-unsigned conversions, and overflow throw
`OverflowException`. These operations work without a backend.

```csharp
PgNumeric large = ulong.MaxValue;        // exact implicit conversion
ulong original = large.ToInteger<ulong>();
PgNumeric scaled = 12.3400m;             // retains decimal scale
```

Integer and decimal operands can be mixed with `PgNumeric` arithmetic. The type
also implements the .NET arithmetic, comparison, and identity operator interfaces
for generic algorithms. `PgNumeric.Sum(values)` enumerates once, uses PostgreSQL
addition, and returns zero for an empty sequence.

`ToInt16`, `ToInt32`, and `ToInt64` use PostgreSQL casts: they round ties away from
zero and raise `PgException` for out-of-range or nonfinite input. Use `ToInteger<T>`
when rounding is not acceptable.

`FromSingle`/`ToSingle` and `FromDouble`/`ToDouble` use the server's floating-point
conversion rules and may lose precision. Explicit casts between `PgNumeric` and
`float` or `double` use the same routines. Floating-point conversions and server
integer casts require the backend.

Both `PgNumeric` and `decimal` work in typed SPI queries, prepared plans, sessions,
cursors, and local row edits:

```csharp
PgNumeric? total = Spi.ExecuteScalar<PgNumeric?>("SELECT sum(amount) FROM invoices");
decimal price = Spi.ExecuteScalar<decimal>(
    "SELECT $1::numeric(10,2)", SpiParameter.Create(12.345m));
```

Untyped numeric cells contain `PgNumeric`. Reading them with `row.Get<decimal>()`
performs the same exact conversion as a generated function adapter.

## JSON serialization

`PgNumeric` serializes as a JSON string, retaining scale and full precision even
when the consumer uses floating-point numbers. NaN and infinities use strings too.
The converter accepts either a string or an unquoted JSON number on input; number
tokens pass directly to PostgreSQL's parser without conversion through `double`
or `decimal`.

Use a source-generated `JsonSerializerContext` and explicit `JsonTypeInfo<T>`
metadata, as in the [JSON guide](/json-and-uuid/). Writing an owned numeric value
works outside PostgreSQL. Deserialization requires the active backend thread.
Invalid input throws `JsonException` with its property path, retaining a native
`PgException` as the inner exception when applicable. JSON null requires
`PgNumeric?`.
