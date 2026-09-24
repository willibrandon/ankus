---
title: Polymorphic values
description: Write functions that preserve the caller's PostgreSQL type.
---

Use `PgAnyElement` when a function should accept different PostgreSQL types:

```csharp
[PgFunction]
public static PgAnyElement? Identity(PgAnyElement? value) => value;
```

```sql
SELECT identity(42);              -- integer
SELECT identity('hello'::text);   -- text
SELECT identity(NULL::integer);   -- NULL of type integer
```

Ankus declares this as `anyelement → anyelement`. PostgreSQL resolves the actual
type from the input. Domains, enums, composites, and types without a C# mapping
retain their identity. A polymorphic result needs a polymorphic input so
PostgreSQL can determine its type.

`TypeOid` exposes the resolved type. `Read<T>()` copies a supported managed value
and checks its type. `Datum.ToPostgresString()` uses PostgreSQL's output function.
Returning an incompatible type raises a PostgreSQL error.

Use nullable wrappers to receive or return SQL NULL. A non-null wrapper always
contains a present value; zero is distinct from NULL.

## Arrays and sets

`PgAnyArray` maps to `anyarray`. It preserves dimensions, lower bounds, and the
actual element type, including unregistered enums and domains:

```csharp
[PgFunction]
public static IEnumerable<PgAnyElement?> Elements(PgAnyArray values)
{
    foreach (PgAnyElement? value in values)
    {
        yield return value;
    }
}
```

```sql
SELECT * FROM elements(ARRAY[1, NULL, 3]); -- integer rows
SELECT * FROM elements(ARRAY['a', 'b']);  -- text rows
```

`Count` includes NULL cells. Indexing and enumeration use flattened row-major
order. `Rank`, `GetLength(dimension)`, and `GetLowerBound(dimension)` expose the
shape; dimension numbers start at zero. Empty arrays have rank zero.
`ElementTypeOid` retains the declared element type.

Polymorphic values also work in named TABLE columns and materialized sets.
Pass either wrapper to `SpiParameter.Create` or `PgFunctionArgument.Create`
to bind its actual type in a query or function call.

## Lifetime

Values belong to the current function call or iterator. `CopyTo(context)` keeps
a value under another memory owner. Resetting or deleting that owner invalidates
the value and its array cells; later native access throws. Managed values read
with `Read<T>()` remain independent.

To wrap a raw result, construct `PgAnyElement` or `PgAnyArray` from its `PgDatum`.
The array constructor checks that the datum is an array. Both require a present
value; represent SQL NULL with a null wrapper.
