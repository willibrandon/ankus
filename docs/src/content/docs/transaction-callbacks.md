---
title: Transaction callbacks
description: Run managed work when PostgreSQL transactions and savepoints change state.
---

`PgTransaction` registers callbacks for the current PostgreSQL transaction. An
implicit transaction ends with the current statement; use `BEGIN` when later
statements must share the registration.

```csharp
_ = PgTransaction.RegisterCallback(PgTransactionEvent.PreCommit, () =>
{
    Spi.Execute("INSERT INTO audit_log(message) VALUES ('committing')");
});

PgTransactionCallback cleanup = PgTransaction.RegisterCallback(
    PgTransactionEvent.Abort,
    static () => PgLog.Write(PgLogLevel.Notice, "Transaction rolled back."));

// Cancel the callback if cleanup is no longer needed.
cleanup.Dispose();
```

Outer-transaction callbacks run once, in registration order. PostgreSQL keeps
the callback alive even when its returned registration is discarded. Dispose
the registration to cancel a callback that has not run.

| Event | Timing | SPI |
|---|---|---|
| `PreCommit` | Before commit | Yes |
| `Commit` | After commit | No |
| `Abort` | After rollback | No |
| `PrePrepare` | Before `PREPARE TRANSACTION` | Yes |
| `Prepare` | After preparation | No |
| `ParallelPreCommit` | Before a parallel worker commits | No |
| `ParallelCommit` | After a parallel worker commits | No |
| `ParallelAbort` | After a parallel worker rolls back | No |

`PreCommit` and `PrePrepare` may reject the operation by throwing. Events after
commit, rollback, or preparation are for cleanup and logging. An unhandled
exception in those events causes PostgreSQL to disconnect all sessions and run
crash recovery, as with pgrx. Committed changes remain committed. Use `PreCommit`
to reject a transaction safely; handle expected failures inside cleanup callbacks.

## Savepoints and subtransactions

Subtransaction callbacks repeat for every matching savepoint or internal
subtransaction in the current outer transaction:

```csharp
PgSubtransactionCallback registration = PgTransaction.RegisterSubtransactionCallback(
    PgSubtransactionEvent.Start,
    static (id, parentId) =>
        PgLog.Write(PgLogLevel.Debug1, $"Started subtransaction {id} under {parentId}."));
```

| Event | Timing | SPI |
|---|---|---|
| `Start` | After the subtransaction starts | Yes |
| `PreCommit` | Before it commits | Yes |
| `Commit` | After it commits | No |
| `Abort` | After it rolls back | No |

The callback receives `PgSubtransactionId` values for the current and parent IDs.
Dispose its registration to stop future calls. Ankus's private error guards do
not appear as consumer subtransaction events.

## Errors

An uncaught managed exception in a reversible phase becomes a PostgreSQL error
after managed `finally` blocks finish. A PostgreSQL error raised by SPI also aborts
that phase, even when callback code catches the managed `PgException`; continuing
would leave PostgreSQL in a failed transaction state. Nested callback dispatch is
supported when callback SQL creates another subtransaction.
