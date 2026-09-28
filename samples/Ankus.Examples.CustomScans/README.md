# Tracing custom scans

This extension wraps PostgreSQL sequential scan paths in a native custom scan.
It retains the original child path, costs, qualifications and parameters, then
delegates tuple execution to that child. EXPLAIN reports the custom provider and
its per-node row, call and rescan counters.

After publishing and installing `ankus_trace_scan`, load it in the session that
will plan queries:

```sql
CREATE EXTENSION ankus_trace_scan;
SELECT trace_scan_reset();
SET ankus_trace_scan.enabled = on;
EXPLAIN (ANALYZE, FORMAT JSON) SELECT * FROM your_table;
SELECT trace_scan_counts();
```

In a later connection, call `trace_scan_reset()` to load the library before
enabling tracing. The eight backend-local counters report planning, state
creation, begin, execution, end, rescan, managed unwind and native query-context
cleanup, in that order. Returned arrays are snapshots. Counters in parallel
workers belong to those workers; they are not summed into the leader's counters.

`TraceScan.cs` demonstrates field-named `[PgNativeCallback]` declarations,
backend-lived method tables, previous-hook chaining, copyable plan state, a
native state prefix, scan projection, rescan, EXPLAIN and context cleanup.
Disabling the setting affects future planning; already prepared custom plans
continue using the registered methods until PostgreSQL discards them.
