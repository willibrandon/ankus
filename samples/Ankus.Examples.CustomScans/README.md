# Tracing custom scans

This extension wraps PostgreSQL sequential, index and index-only scan paths in
a native custom scan. It retains the original child path, costs, ordering,
qualifications and parameters, then
delegates tuple execution to that child. EXPLAIN reports the custom provider and
its per-node row, call, rescan, mark and restore counters.

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

Parallel-aware trace nodes additionally report shared rows, executor calls,
worker attachments, worker shutdowns and the DSM execution generation in
EXPLAIN. `TraceScan.Parallel.cs` implements PostgreSQL's estimate, initialize,
reinitialize, worker-attach and shutdown callbacks with selected-header native
atomics. PostgreSQL owns both the segment and the child scan's work distribution.
Shutdown copies the shared observations before releasing the borrowed address.
For an early LIMIT, these are snapshots while workers may still be finishing,
not final totals. A rescan resets shared counters and increments the generation;
ordinary per-backend lifecycle counters retain their original meaning.

`TraceScan.cs` demonstrates field-named `[PgNativeCallback]` declarations,
backend-lived method tables, previous-hook chaining, copyable plan state, a
native state prefix, scan projection, rescan, EXPLAIN and context cleanup.
`TraceScan.Position.cs` delegates mark/restore to native index children whose
access methods support it. `Trace Marks` and `Trace Restores` in EXPLAIN observe
those callbacks during merge joins. Backward scanning is advertised only when
the actual child plan supports it; sequential children do not gain mark/restore.
`TraceScan.Parameters.cs` retains copyable outer-variable expressions, maps them
through partition ancestry, and places them in the final plan's expression list
for PostgreSQL's reference adjustments. EXPLAIN reports `Trace Parameters` and
`Trace Parameter Remaps` without evaluating the original clauses again.
Disabling the setting affects future planning; already prepared custom plans
continue using the registered methods until PostgreSQL discards them.
