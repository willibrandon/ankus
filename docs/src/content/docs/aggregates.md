---
title: Aggregates
description: Define PostgreSQL aggregates with typed transitions, owned managed state, moving windows, and parallel transport.
---

Mark a class or struct with `[PgAggregate]` and provide a static `Transition`
method. Its first SQL parameter and return value are the state; the remaining
parameters are the aggregate's inputs. PostgreSQL manages grouping, filtering,
NULL handling, and invocation order.

```csharp
[PgAggregate(Name = "integer_total", InitialCondition = "0")]
public static class IntegerTotal
{
    public static long Transition(long state, int value) => checked(state + value);
}
```

This defines `integer_total(integer)` with a `bigint` state. Without a final
method, the state is also the result. A state-only transition defines a
zero-argument aggregate invoked as `count_rows(*)`. Ordinary variadic aggregates
use a final `params T[]` input.

Use SQL normally:

```sql
SELECT category, integer_total(amount) FILTER (WHERE amount > 0)
FROM entries
GROUP BY category;
```

## Support methods and declaration options

The generator discovers these conventional method names. The matching attribute
property accepts a `nameof(...)` override when a different name is useful.

| Method | Managed parameters after an optional leading context | Result |
|---|---|---|
| `Transition` | State, aggregated inputs | State |
| `Final` | State, direct arguments, optional extra input slots | SQL result |
| `Combine` | Destination state, partial state | Destination state |
| `Serialize` | Managed state | `byte[]` |
| `Deserialize` | `byte[]` | New managed state |
| `MovingTransition` | Moving state, aggregated inputs | Moving state |
| `MovingInverse` | Moving state, departing inputs | Moving state or NULL to restart |
| `MovingFinal` | Moving state, direct arguments, optional extra input slots | Same SQL result type as ordinary execution |

All methods are accessible synchronous static methods. Add `[PgFunction]` to a
support method for its SQL name, schema, volatility, parallel safety, search
path, NULL policy, or other ordinary function settings. Support functions are
aggregate entry points; invoke them through an aggregate in SQL. Their C# bodies
remain callable directly for managed tests.

`PgFunction.Sql` can replace a support function's SQL while retaining its native
callback. `GenerateSql = false` omits that helper's SQL, so ordered custom SQL
must provide it if the aggregate still references it. These options apply to the
helper independently; they do not suppress `CREATE AGGREGATE`. Shared helpers
are emitted once. See [function SQL controls](/custom-sql/#replace-function-sql).

`PgAggregateAttribute` configures the aggregate's name, schema, dependency ID,
`Requires`/`Before` edges, `ParallelSafety`, `StateSize`, and `MovingStateSize`.
`InitialCondition` and `MovingInitialCondition` are PostgreSQL input strings for
the declared state type. Omitted, empty text, and the string `NULL` are distinct.
The generator quotes strings and orders support functions before their aggregate.
Aggregate `Requires` dependencies also precede its support functions, so a custom
state type can be declared once as an aggregate dependency. Generated enum types
and declared schemas participate in this ordering automatically.
See [custom SQL](/custom-sql/) for dependencies on custom types and operators.

The inferred support-function NULL policy follows parameter nullability. A strict
transition skips any row with a NULL input. Without an initial condition,
PostgreSQL can seed a strict state from the first compatible input without
calling the transition for that row. Use nullable state and input parameters
when the method needs to observe NULLs or recover from a NULL state.

## Owned managed state

Use `PgAggregateState<T>` for state that is not an ordinary SQL datum. It maps to
PostgreSQL's `internal` type. The payload can be a class or value type; a nullable
wrapper represents SQL NULL. Return an ordinary SQL value from `Final`.

```csharp
[PgAggregate(Name = "collect_count")]
public static class CollectCount
{
    public static PgAggregateState<List<int>> Transition(
        PgAggregateState<List<int>>? state, int? value)
    {
        state ??= new([]);
        if (value is { } number)
        {
            state.Value.Add(number);
        }

        return state;
    }

    public static int Final(PgAggregateState<List<int>>? state) => state?.Value.Count ?? 0;
}
```

Returned state is rooted until its owning PostgreSQL memory context resets.
That includes group completion, rescans, moving-window restarts, cancellation,
and errors. If the payload implements `IDisposable`, cleanup invalidates the
wrapper and releases its root before calling `Dispose` once. Cleanup failures
produce a PostgreSQL warning; other states still receive cleanup. During cleanup,
backend access is limited to releasing owned plans and cursors.

Finalization does not dispose state: PostgreSQL can call a final method repeatedly
or share state across several final methods. Retained wrappers reject payload
access after release, and attached wrappers belong to their backend thread.
Ordinary managed values copied from inputs use Ankus's owned datum representations.

Use `PgInternal` when the same state also passes through ordinary backend support
functions or native callbacks. `PgInternal.Create(value)` selects the aggregate
owner inside aggregate callbacks; `Get<T>()` retrieves the exact retained type.
Its payload is released at context reset or deletion. If its `Dispose` throws,
PostgreSQL reports an error and stops that context's cleanup until cleanup is
retried. The released payload cannot be read or passed again.

## Parallel aggregation

Set the aggregate's `ParallelSafety` and implement `Combine`. Managed internal
state also needs both `Serialize` and `Deserialize`. Serialize values into a
process-independent format; a managed or native pointer is not a portable state.
The generated deserializer has PostgreSQL's mandatory `(bytea, internal)` SQL
signature; its dummy `internal` argument is not passed to C#.

A deserialized state has a temporary owner. `Combine` must return state owned by
its destination context. When the destination is NULL, allocate a fresh wrapper
and copy the partial values. Returning the borrowed partial wrapper is rejected.
Choose an initial condition that is an identity for both transition and combine,
because PostgreSQL applies it to partial and combined states.

The `Ankus.Examples.Aggregates` sample's `IntegerAverage` implements checked sum
and count, a versioned binary transport, and a combine method that copies values
into its destination. It returns NULL for empty and all-null input.

## Moving windows and final state modification

`MovingTransition` and `MovingInverse` maintain a window as rows enter and leave.
They can use a different state type from ordinary execution. Supply
`MovingFinal` when conversion from that state to the aggregate result is needed.
A NULL inverse result asks PostgreSQL to recompute the frame. A moving forward
transition must return a present state.

`FinalModify` and `MovingFinalModify` describe whether finalization is read-only,
shareable, or read/write. They affect state sharing and window eligibility;
declare the behavior the method actually implements. The average example reads
its state without consuming it and supports bounded moving frames.

`FinalExtra` and `MovingFinalExtra` add typed NULL input slots after the state and
direct arguments. They convey type information, not the last row's values, and
require a non-strict final method.

## Polymorphic values

Use `PgAnyElement` to accept different PostgreSQL types, or `PgAnyArray` for
arrays. PostgreSQL resolves state and result types from the aggregate inputs:

```csharp
[PgAggregate]
public static class FirstValue
{
    public static PgAnyElement Transition(PgAnyElement state, PgAnyElement value)
        => state;
}
```

This strict transition keeps the first non-NULL value. PostgreSQL seeds the
state from that row; empty and all-NULL inputs return NULL. Types, array bounds,
and values retain their PostgreSQL representation.

For managed state, copy retained inputs into `context.MemoryContext`. Inputs
otherwise expire when the support callback ends. `FinalExtra` lets PostgreSQL
resolve a polymorphic result when the state itself is `internal`:

```csharp
[PgAggregate(FinalExtra = true)]
public static class FirstStoredValue
{
    public static PgAggregateState<PgAnyElement>? Transition(
        PgAggregateContext context, PgAggregateState<PgAnyElement>? state,
        PgAnyElement? value)
        => state ?? (value is null ? null : new(value.CopyTo(context.MemoryContext)));

    public static PgAnyElement? Final(
        PgAggregateState<PgAnyElement>? state, PgAnyElement? typeWitness)
        => state?.Value;
}
```

The extra `typeWitness` argument is always NULL. Polymorphic values also work
with moving states, ordered-set comparisons, and parallel combine methods.

## Ordered and hypothetical sets

Set `Kind = PgAggregateKind.OrderedSet` for `WITHIN GROUP` syntax. Parameters
after the final method's state, excluding extra NULL input slots, are direct
arguments. They are evaluated once per group and are not transition inputs.

PostgreSQL does not sort ordered-set input for the transition method. Accept a
leading `PgAggregateContext` to inspect `SortKeys` and compare values with
`context.Compare(left, right, sortKey)`. The native comparator honors the key's
operator, collation, direction, and NULL placement. Sort copies when declaring a
read-only final method.

Use the `SpiParameter` overload when NULL operands need an explicit named type.
For a named composite, load its descriptor and bind both operands with it:

```csharp
PgTupleDescriptor descriptor = PgTupleDescriptor.Load("app.item");
int order = context.Compare(
    SpiParameter.Create(left, descriptor),
    SpiParameter.Create(right, descriptor));
```

`SpiParameter.CreateArray` supplies the element identity for composite arrays.
The generic overload uses the same type inference as ordinary SPI parameters;
a NULL tuple alone carries no named descriptor.

The sample's `DiscretePercentile` uses that comparison and ceiling-ranked
selection. For `[10,20,30]`, fraction `0.4` selects `20`, in agreement with
PostgreSQL's `percentile_disc`.

```sql
SELECT integer_percentile(0.4) WITHIN GROUP (ORDER BY value DESC)
FROM (VALUES (30), (10), (20)) AS inputs(value);
```

`HypotheticalSet` adds PostgreSQL's hypothetical argument matching rules. The
implementation still supplies the rank/distribution logic; PostgreSQL does not
insert the hypothetical row. Ordered-set aggregates cannot be used as windows.

## Context and errors

`PgAggregateContext` exposes state storage, aggregate/window kind, state sharing,
input collation, an optional aggregate OID, and owned sort metadata. Window callbacks have no
`Aggref`, so their aggregate OID is null. A shared transition can represent more
than one aggregate; its reported OID must not be used to select final behavior.
Metadata remains readable after callback exit. Storage lookup and comparisons
require the current aggregate context on its backend thread. Nested scalar calls
retain that aggregate owner; nested aggregates use their own.

Throw `PgException` for a deliberate SQLSTATE and diagnostics. Native comparison,
datum conversion, and backend calls use guarded boundaries so PostgreSQL ERROR
does not cross managed frames. State cleanup still runs when a query fails.
