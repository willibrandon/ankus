---
title: Custom scan providers
description: Use selected-header native methods and managed callbacks to plan and execute custom scans.
---

A custom scan provider participates in PostgreSQL's planner and executor. Use
the generated `Ankus.Postgres` declarations and `[PgNativeCallback]` for the
native method tables. Their fields and callback signatures come from the server
headers selected when the extension is built.

The [trace-scan sample](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.CustomScans)
wraps sequential scan paths and delegates to their real child plans. It keeps
the original path costs, qualifications, output values and parameters, and adds
per-node EXPLAIN observations. After installing the published extension:

```sql
CREATE EXTENSION ankus_trace_scan;
SELECT trace_scan_reset();
SET ankus_trace_scan.enabled = on;
EXPLAIN (ANALYZE, FORMAT JSON) SELECT * FROM your_table;
SELECT trace_scan_counts();
```

Load the library in each session before planning queries. Calling
`trace_scan_reset()` loads it and clears that backend's counters. An existing
cached plan continues to use its registered methods even if tracing is disabled
afterwards. PostgreSQL can still invalidate the plan; replanning uses the current
setting.

## Registering the native contracts

There are three method tables:

| Table | Responsibility |
|---|---|
| `CustomPathMethods` | Convert a selected custom path and its child plans into a `CustomScan` |
| `CustomScanMethods` | Allocate a `CustomScanState` with the correct node tag and executor methods |
| `CustomExecMethods` | Begin, execute, end and rescan; optionally explain, mark/restore or coordinate parallel execution |

Declare a managed handler using the name derived from its native field:

```csharp
[PgNativeCallback(nameof(Execute))]
private static partial CustomExecMethods_ExecCustomScanCallback Executor { get; }
```

The handler must exactly match the generated type's `Invoke` signature. Assign
that property to `CustomExecMethods.ExecCustomScan`. Callback addresses remain
valid for the loaded library's lifetime; copying an address does not extend the
lifetime of any state it uses.

Register `CustomScanMethods` from `[PgModuleLoad]` with
`NativeMethods.RegisterCustomScanMethods`. PostgreSQL retains the supplied
pointer; it does not copy the table or provide an unregister operation. The
table, its name and all method tables referenced by cached plans need storage
that survives individual queries and transactions. The sample allocates them in
a child of `TopMemoryContext`. Keep previous planner hooks and invoke them when chaining.

## Planning and execution

The relation path hook runs after PostgreSQL creates its ordinary access paths.
A provider can retain those paths as `CustomPath.custom_paths`. PostgreSQL
converts the children to plans before calling `PlanCustomPath`.

Create an exact `CustomScan` value. Its private plan data must be native nodes
that PostgreSQL can copy and serialize. Put expressions that need planner
reference adjustment in the appropriate expression fields. A
`custom_scan_tlist` describes the tuples the scan will produce; its shape must
agree with the executor's slots and projection.

`CreateCustomScanState` may allocate a larger native value with `CustomScanState`
as its first member. Initially set the node tag and method table and leave the
standard fields zeroed. PostgreSQL initializes those fields before
`BeginCustomScan`. Publish initialized child states in `custom_ps` so ordinary
executor traversal can find them.

The sample calls guarded `NativeMethods.ExecScan` with generated access and
recheck callbacks. The access callback runs the actual child using
`NativeMethods.ExecProcNode`, then copies its tuple into the custom scan's own
slot with `NativeMethods.ExecCopySlot`. This preserves the slot operations
PostgreSQL expects from the custom node, including fully deformed virtual
tuples. PostgreSQL applies scan projection and preserves SQL NULL and the native
tuple representation. Rescan propagates changed
parameters to the child; end invokes its ordinary executor cleanup.

## Ownership, errors and optional capabilities

Native state belongs to the relevant PostgreSQL memory context. `EndCustomScan`
is an ordinary completion callback and is not guaranteed after an error. Put
abort cleanup under the query context's lifetime. The sample uses a context
reset callback to observe reclamation, including error exits.

Use guarded `NativeMethods` calls and generated callback values for operations
that can raise a PostgreSQL error. Native errors become `PgException` while
managed frames unwind; PostgreSQL receives ERROR only after the managed frames
have returned. A raw unmanaged call must not let longjmp cross managed code.

Advertise only capabilities the provider implements. Backward scanning,
mark/restore, reparameterization and dynamic shared-memory callbacks have
different contracts. The trace sample delegates sequential reads and direction
changes; it does not implement mark/restore or its own shared-memory protocol.
Its counters are local to each backend or worker, so worker observations are not
automatically combined with leader counters. A provider with shared work must
implement the matching parallel lifecycle and use process-independent shared
data.

The complete PostgreSQL version and platform validation matrix remains in
progress. Build against the server headers that will load the extension.
