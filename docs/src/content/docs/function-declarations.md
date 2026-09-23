---
title: Function declarations
description: Name arguments, provide defaults, choose schemas, and declare PostgreSQL execution options.
---

## Named arguments and defaults

C# parameter names become snake-case SQL argument names. Optional constants
become PostgreSQL defaults, so callers can omit arguments or name them:

```csharp
[PgFunction]
public static string Greet(string firstName = "world", int repeatCount = 1)
    => string.Join(" ", Enumerable.Repeat($"Hello, {firstName}!", repeatCount));
```

```sql
SELECT greet();
SELECT greet(repeat_count => 2, first_name => 'Ada');
```

Use `PgParameter` to override a name or supply a SQL expression:

```csharp
[PgFunction]
public static PgDate ReportDate(
    [PgParameter(Name = "as_of", Default = "current_date")] PgDate date) => date;
```

PostgreSQL evaluates `current_date` when the function is called. A SQL default
does not make the argument optional in direct C# calls. When both are present,
`PgParameter.Default` overrides the C# default for SQL calls only.

Every argument following a defaulted argument must also have a default. Ankus
reports `ANKUS004` for invalid declaration options, names, or default ordering;
PostgreSQL validates SQL expressions during installation. Expressions are
trusted extension source, just like handwritten installation SQL.

Constants retain decimal scale, floating-point signed zero and special values,
escaped text, and integer bounds. Supported value-type `default` arguments use
their actual value: `default(PgDate)` is 2000-01-01, while `default(DateOnly)` is
0001-01-01. Nullable defaults become SQL NULL.

Variadic arguments can have an explicit SQL default too:

```csharp
[PgFunction]
public static int Total(
    [PgParameter(Default = "ARRAY[]::integer[]")] params int[] values) => values.Sum();
```

This allows both `total()` and `total(1, 2, 3)`. See [arrays](/arrays/) for
variadic calls without a default.

## Injected memory contexts

Add a `PgMemoryContext` parameter to receive the callable's native allocation
context. It adds no SQL argument:

```csharp
[PgFunction]
public static int CopyValue(PgMemoryContext context, int value)
{
    using PgAllocation storage = context.Allocate<int>(1);
    storage.Write(value);
    return storage.Read<int>();
}
```

Call this as `SELECT copy_value(42)`. Context parameters can appear before,
between, or after SQL parameters, and multiple context parameters are allowed.
SQL names, defaults, strictness, overload identity, and the 100-argument limit
count only SQL parameters. Operators and casts follow the same rule.

The injected handle is borrowed and always non-null. Nullable annotations and
optional null defaults affect direct C# calls only. A `PgParameter` attribute on
the context reports `ANKUS004` because there is no corresponding SQL name or
default. Context arrays, context results, and `ref`, `in`, or `out` parameters
are unsupported.

Scalar functions receive the current native context; set functions receive
their multi-call context. The handle preserves that owner across temporary
context switches. See [memory contexts](/memory-contexts/) and
[set lifetime](/sets-and-tables/#iterator-lifetime-and-errors) for its lifetime.

## Schemas

Without a schema declaration, functions use the schema selected by
`CREATE EXTENSION`. Such an extension can be relocated with
`ALTER EXTENSION ... SET SCHEMA`.

Apply `PgSchema` to a class to create an extension-owned schema and place its
functions there:

```csharp
[PgSchema("reporting")]
public static class Reports
{
    [PgFunction]
    public static int Double(int value) => checked(value * 2);
}
```

Call it as `reporting.double(21)`. Nested classes inherit the nearest
`PgSchema` declaration. An empty attributed class can declare a schema without
any functions.

Schema creation happens before function declarations. PostgreSQL records the
new schema as an extension member and removes it with `DROP EXTENSION`.
Installation fails if an unrelated object already owns that schema name.
To use an existing schema without adopting it, set `Create = false`:

```csharp
[PgSchema("public", Create = false)]
public static class SharedFunctions
{
    [PgFunction]
    public static int Increment(int value) => checked(value + 1);
}
```

A function's `Schema` option overrides its containing class and refers to an
existing schema. Declare that schema separately with `PgSchema` if the extension
should create it. Schema and argument identifiers are quoted exactly and must
fit PostgreSQL's 63-byte UTF-8 limit.

Any fixed schema makes the extension non-relocatable. Ankus records this in
the generated control file, including when all fixed schemas already exist.

Use `Id` and `Requires` to order schemas and functions alongside
[custom installation SQL](/custom-sql/). A function default that calls a
SQL-created routine should require the block that creates it.

## Execution options

Declare PostgreSQL's planner and execution contracts on `PgFunction`:

```csharp
[PgFunction(Volatility = PgVolatility.Immutable,
    ParallelSafety = PgParallelSafety.Safe, Cost = 2.5)]
public static int Maximum(int left, int right) => Math.Max(left, right);
```

| Option | Default | Meaning |
| --- | --- | --- |
| `Volatility` | `Volatile` | `Stable` promises statement-stable results; `Immutable` promises equal results for equal arguments forever. Both prohibit database writes. |
| `ParallelSafety` | `Unsafe` | `Restricted` runs in the parallel leader; `Safe` also permits parallel workers. |
| `NullInput` | `Inferred` | Infers strictness from SQL parameter nullability. `Strict` skips any NULL call; `CalledOnNull` requires all SQL parameters to be nullable. |
| `SecurityDefiner` | `false` | Uses the function owner's privileges when true. Otherwise uses the caller's. |
| `Leakproof` | `false` | Claims the function reveals no argument information except through its result. Installation requires a superuser. |
| `Cost` | `1` | Positive, finite planner cost in `cpu_operator_cost` units. |
| `CreateOrReplace` | `false` | Emits `CREATE OR REPLACE FUNCTION`, retaining compatible function identities and dependencies. |
| `SearchPath` | `null` | An ordered list of schema names scoped to the call. An empty array clears the path; null preserves the caller's setting. |
| `SupportFunction` | `null` | An existing planner support routine, optionally schema-qualified. PostgreSQL validates its signature during installation. |
| `Rows` | `1000` | Positive finite row estimate for an `IEnumerable<T>` return. |
| `SetMode` | `Auto` | Prefer one row per call, or require `ValuePerCall`/`Materialize` for a set return. |

These options are promises to PostgreSQL. Choose them to match the method's
behavior; the generator does not infer purity, privilege requirements, or
parallel safety from its body.

For owner-privileged functions, pin name resolution to trusted schemas and put
`pg_temp` last:

```csharp
[PgFunction(SecurityDefiner = true,
    SearchPath = ["pg_catalog", "reporting", "pg_temp"])]
public static long ReportCount()
    => Spi.ExecuteScalar<long>("SELECT count(*) FROM reporting.reports");
```

The owner's privileges and function-local search path end when the call
returns or throws. Native error guards and managed exception unwinding apply
to every execution mode.

`PgOperator` and `PgCast` also expose static methods as functions. Add `PgFunction`
alongside them to select the options described here. See [operators and casts](/operators-and-casts/).

`IEnumerable<T>` returns a set; named tuple elements declare TABLE output columns.
See [sets and tables](/sets-and-tables/) for column names, NULL rows, execution modes,
iterator disposal and cancellation.
