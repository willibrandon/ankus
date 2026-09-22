---
title: Operators and casts
description: Declare PostgreSQL operators and casts with ordinary C# methods.
---

`PgOperator` and `PgCast` generate a backing SQL function plus an operator or cast
that calls it. Both work on the same supported types as `PgFunction`, including
generated enums and arrays. They use the same NULL handling, Native AOT conversion,
and managed exception boundary.

## Operators

Apply `PgOperator` to a static method with two parameters for a binary operator,
or one parameter for a prefix operator:

```csharp
[PgOperator("@+")]
[PgFunction(Volatility = PgVolatility.Immutable,
    ParallelSafety = PgParallelSafety.Safe)]
public static int CheckedAdd(int left, int right) => checked(left + right);

[PgOperator("@-")]
public static int Negate(int value) => checked(-value);
```

```sql
SELECT 20 @+ 22;  -- 42
SELECT @- 7;     -- -7
```

`PgFunction` is optional. Use it to set the backing function's SQL name, schema,
volatility, parallel mode, strictness, or other [execution options](/function-declarations/).
The operator belongs to that function's schema, including inherited `PgSchema`
declarations. Without a fixed schema, both belong to the extension's installation
schema and move together during relocation.

To call an operator in a specific schema, use PostgreSQL's `OPERATOR` syntax:

```sql
SELECT 20 OPERATOR(calculations.@+) 22;
SELECT OPERATOR(calculations.@-) 7;
```

Names contain PostgreSQL operator punctuation and occupy at most 63 bytes. They
cannot contain comment starts (`--` or `/*`). A name longer than one character
can end in `+` or `-` only if it contains a nonstandard operator character such
as `@` or `?`. PostgreSQL treats `!=` as `<>`; Ankus uses the same normalization.
The exact token `=>` is reserved. Operators cannot be variadic or return `void`.

Nullable parameters follow the normal function policy. An operator with required
parameters is strict by default; nullable parameters let the method handle NULL.
Postfix unary operators are not generated.

## Planner options

```csharp
[PgOperator("===", Commutator = "===", Negator = "!==",
    RestrictionEstimator = "pg_catalog.eqsel",
    JoinEstimator = "pg_catalog.eqjoinsel", Hashes = true, Merges = true)]
[PgFunction(Volatility = PgVolatility.Immutable,
    ParallelSafety = PgParallelSafety.Safe)]
public static bool Equal(int left, int right) => left == right;
```

| Option | PostgreSQL contract |
| --- | --- |
| `Commutator` | Names the operator with reversed operands. Only binary operators support this. |
| `Negator` | Names the boolean complement with the same operand types. |
| `RestrictionEstimator` | Names a restriction selectivity function (`RESTRICT`). Requires a boolean result. |
| `JoinEstimator` | Names a join selectivity function (`JOIN`). Requires a binary boolean operator. |
| `Hashes`, `Merges` | Declare hash/merge join support. Require a binary boolean operator. |

Unqualified commutator and negator names use the operator's schema. A name such
as `comparison.!==` explicitly selects another schema. Estimator names can be
unqualified or prefixed with one schema. PostgreSQL validates referenced routines
at installation.

Commutator/negator pairs may refer to each other: PostgreSQL creates a temporary
shell entry and fills it when the companion operator is declared. A self-commutator
is supported; an operator cannot be its own negator. These links are not
installation dependency cycles.

Planner options are semantic promises. A commutator must actually reverse the
operands, a negator must complement the result, and equality/hash/sort behavior
must agree. `Hashes` and `Merges` do not create operator classes or support
functions. Declare compatible operator families using [custom SQL](/custom-sql/)
when PostgreSQL needs them for joins or indexes.

## Casts

Apply `PgCast` to a static conversion method:

```csharp
[PgEnum]
public enum Priority { Low = 10, Normal = 20, High = 30 }

public static class PriorityFunctions
{
    [PgCast]
    [PgFunction(Volatility = PgVolatility.Immutable)]
    public static int Score(Priority value) => (int)value;
}
```

```sql
SELECT 'High'::priority::integer; -- 30
```

The first parameter determines the source SQL type, and the return type determines
the target. C# enum numeric values are converted by the method; PostgreSQL enum
OIDs never become the integer result accidentally.

| Context | Declaration | Where PostgreSQL may apply it |
| --- | --- | --- |
| Explicit | `[PgCast]` or `[PgCast(PgCastContext.Explicit)]` | `CAST(value AS type)` and `value::type` |
| Assignment | `[PgCast(PgCastContext.Assignment)]` | Explicit conversions and assignments to columns |
| Implicit | `[PgCast(PgCastContext.Implicit)]` | Explicit conversions, assignments, and expression/function resolution |

Use implicit casts when the conversion is unambiguous and preserves the intended
meaning. PostgreSQL chooses among all casts and overloads in the database; the
attribute does not change its resolution rules.

PostgreSQL can also supply a second `int` parameter containing the target type
modifier and a third `bool` parameter indicating whether the conversion was
explicit. Both must be non-nullable. Type modifiers are PostgreSQL's encoded
values; `-1` means no modifier was specified. A one-parameter cast must connect
different SQL types. Two- or three-parameter methods can implement same-type
length coercion where PostgreSQL supports it.

Nullable first parameters and results use the usual function NULL policy.
Variadic casts and `void` results are rejected. CLR aliases such as `decimal` and
`PgNumeric` share a SQL type, so they do not define distinct cast signatures.

## Installation dependencies and ownership

Operators and casts are separate SQL graph entries, ordered after their backing
functions and argument/result type declarations. Their `Id` and `Requires`
properties refer to the operator or cast; `PgFunction.Id` refers to the function.
For example, a custom SQL view using an operator can require its `PgOperator.Id`.

The generator reports `ANKUS007` for invalid operator/cast declarations and
`ANKUS005` for duplicate SQL signatures or invalid dependencies. PostgreSQL
validates database-dependent contracts during installation. An existing cast for
the same source/target pair or an existing fully defined operator is an installation
error, even when the backing function uses `CreateOrReplace`.

PostgreSQL owns the generated objects as extension members. `DROP EXTENSION`
removes them, and a relocatable extension can move its enum, functions and
operators while retaining its casts. See the complete
`samples/Ankus.Examples.Operators` sample in the repository.
