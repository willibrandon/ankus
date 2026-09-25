---
title: Arrays
description: Use C# vectors, preserve PostgreSQL dimensions, and declare variadic functions.
---

Use `T[]` for one-dimensional arrays with the usual PostgreSQL lower bound of one.
Use `PgArray<T>` when dimensions or lower bounds matter. Both support the scalar
types listed in [Write a function](/getting-started/functions/#types).

Reusable scalar mappings declared with `[PgDatumType]` do not yet support typed
arrays. Read individual raw elements with
[`PgDatum.Read<T>()`](/raw-values/#reusable-scalar-mappings).

[Composite arrays](/composites/#arrays-sets-and-spi) use `PgHeapTuple?[]` or
`PgArray<PgHeapTuple?>`. `PgCompositeType` binds named function signatures;
`PgTupleDescriptor.CreateArray` retains an explicit element identity even for
empty and all-NULL arrays. `PgArray<T>.ElementTypeOid` exposes that identity.

## Vectors

```csharp
[PgFunction]
public static int?[] ReverseValues(int?[] values) => [.. values.Reverse()];
```

```sql
SELECT reverse_values(ARRAY[1, NULL, 3]); -- {3,NULL,1}
```

`byte[]` remains a single `bytea` value. Use `byte[][]` or `PgArray<byte[]>` for
`bytea[]`. Other jagged arrays and C# rectangular arrays are not SQL mappings;
represent a multidimensional PostgreSQL array with `PgArray<T>`.

An implicit conversion to `T[]` rejects multiple dimensions or a non-one lower
bound. It never silently flattens the value.

## Dimensions and indexing

`PgArray<T>` stores elements in row-major order: the last subscript varies fastest.
Its constructors copy the element sequence, dimension lengths, and lower bounds.

```csharp
[PgFunction]
public static PgArray<int?> Grid()
    => new([11, null, -7, 0, 5, 9], [2, 3], [-2, 4]);
```

```sql
SELECT grid();        -- [-2:-1][4:6]={{11,NULL,-7},{0,5,9}}
SELECT (grid())[-1][6]; -- 9
```

```csharp
PgArray<int?> grid = Grid();
int? flat = grid[2];           // -7; zero-based flat index
int? cell = grid.GetValue(-1, 6); // 9; PostgreSQL subscripts

int rank = grid.Rank;          // 2
int count = grid.Count;        // 6, including NULL elements
ReadOnlySpan<int> lengths = grid.Lengths;     // [2, 3]
ReadOnlySpan<int> bounds = grid.LowerBounds;  // [-2, 4]
```

Omitting lower bounds uses one for each dimension. PostgreSQL supports up to six
dimensions. Empty arrays normalize to rank zero, with empty lengths and bounds.

`ToVector()` copies only a rank-zero empty array or a one-dimensional array whose
lower bound is one. `ToArray()` explicitly flattens any shape into a new vector.
Enumeration follows the same row-major order.

## NULLs and element conversions

The array and its elements have separate nullability:

| C# declaration | Meaning |
| --- | --- |
| `int[]` | Required array; NULL elements raise a conversion error |
| `int?[]` | Required array; elements may be NULL |
| `int?[]?` | Array and elements may be NULL |
| `PgArray<int?>?` | Nullable array and elements, preserving shape |
| `string?[]?` | Nullable text array and reference elements |

Use nullable value types for NULL elements. Reference elements can receive null;
annotate them accordingly. An empty array is distinct from a null array.

The [numeric](/numeric/) and [temporal](/date-and-time/) conversion rules apply to
each element. For example, `decimal[]` rejects values requiring rounding, and
`DateOnly[]` rejects PostgreSQL infinity. Use `PgNumeric` and the full-range
temporal types to retain those values.

[Network](/network/) arrays preserve address families and prefixes.
`IPAddress[]` rejects subnet prefixes; use `PgInet[]` to retain them.

## SPI

Arrays work with queries, prepared statements, sessions, cursors, and local row edits:

```csharp
int?[] values = [1, null, 3];
int?[] copy = Spi.ExecuteScalar<int?[]>(
    "SELECT $1", SpiParameter.Create(values));

PgArray<int?> shaped = Spi.ExecuteScalar<PgArray<int?>>(
    "SELECT '[0:2]={1,NULL,3}'::int[]");

SpiParameter nullArray = SpiParameter.Create<int?[]?>(null);
```

Untyped row cells contain `PgArray<T>` with nullable value-type elements, such as
`PgArray<int?>`. Use `row.Get<T[]>()` or `row.Get<PgArray<T>>()` for an explicit
conversion. Domains use their underlying scalar conversion; `varchar[]` and
`char(n)[]` become text elements, preserving any stored padding.

Results own their managed storage and survive later SPI calls. Binary elements
are ordinary managed byte arrays. Copying an element sequence does not clone
those inner byte arrays.

## Variadic functions

C# `params T[]` declares SQL `VARIADIC`:

```csharp
[PgFunction]
public static int SumValues(params int?[] values)
    => values.Sum(static value => value ?? 0);
```

```sql
SELECT sum_values(2, NULL, 3);                     -- 5
SELECT sum_values(VARIADIC ARRAY[2, NULL, 3]);     -- 5
SELECT sum_values(VARIADIC ARRAY[]::integer[]);   -- 0
```

PostgreSQL requires the explicit `VARIADIC` array form for an empty list. A nullable
array declaration can also receive `VARIADIC NULL::integer[]`. A required array
uses the ordinary strict/null rules for [function parameters](/getting-started/functions/#sql-null).
`params byte[]` is rejected because `byte[]` maps to scalar `bytea`; use
`params byte[][]` for variadic binary values.

Enums declared with `[PgEnum]` also support vectors, shaped arrays, nullable
elements and variadic functions. Enum identity is preserved even for byte-backed
enums. See [enumerated types](/enums/).

## Arrays of any PostgreSQL type

Use `PgAnyArray` to declare an `anyarray` parameter. Its `ElementTypeOid`, `Rank`,
`GetLength`, and `GetLowerBound` retain the actual type and shape. Indexing and
enumeration return nullable `PgAnyElement` cells in row-major order. Each cell's
`Read<T>()` checks the requested managed type; `Datum.ToPostgresString()` works
with PostgreSQL types that have no C# mapping.

The array and cells belong to the function call or iterator. `CopyTo(context)`
gives them another owner; resetting or deleting that context invalidates them.
