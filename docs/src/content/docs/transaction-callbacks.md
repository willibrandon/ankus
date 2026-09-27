---
title: Transaction callbacks
description: Run managed work when PostgreSQL transactions and savepoints change state.
---

`PgTransaction` registers callbacks for the current PostgreSQL transaction. An
implicit transaction ends with the current statement; use `BEGIN` when later
statements must share the registration.

## Recoverable work

Use `PgTransaction.RunInSubtransaction` to group synchronous backend work in a
recovery scope. Success retains its changes in the enclosing transaction. An
exception rolls back the scope before it reaches your `catch` block:

```csharp
try
{
    PgTransaction.RunInSubtransaction(() =>
    {
        Spi.Execute("INSERT INTO audit_log(message) VALUES ('attempt')");
        Spi.Execute("SELECT perform_work()");
    });
}
catch (PgException error)
{
    // Both commands have rolled back. Backend operations are usable again.
    PgLog.Write(PgLogLevel.Notice, error.Message);
}
```

The generic overload returns the callback's result after successful release.
Nested scopes recover independently: catch an inner scope's exception outside
its callback to continue the outer scope. A managed exception also rolls back
the scope and retains its original exception type and instance.

Catch raw native errors outside the scope. Catching one inside the callback
does not make the scope successful: additional SQL and raw calls are rejected,
and the scope rolls back with the original error. Raw calls outside an explicit
recovery scope retain PostgreSQL's native transaction and cleanup requirements.

The callback must stay synchronous on the backend thread and must not perform
transaction control. Recovery scopes are unavailable during transaction
callbacks and abort cleanup. Their internal subtransactions do not appear as
consumer subtransaction events. Native results retain their memory-context
lifetimes; rollback invalidates allocations and resources owned by that scope.

## Register a callback

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

Preparation ends these registrations too. `PREPARE TRANSACTION` runs
`PrePrepare`, then `Prepare`, and releases unused callbacks and their captured
objects. A later `COMMIT PREPARED` or `ROLLBACK PREPARED` does not invoke the
original backend's callbacks, even when issued by that same backend. Writes made
by `PrePrepare` belong to the prepared transaction and become visible only if it
is committed.

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

`PreCommit`, `PrePrepare` and `ParallelPreCommit` may reject the operation by
throwing. Events after
commit, rollback, or preparation are for cleanup and logging. An unhandled
exception in those events causes PostgreSQL to disconnect all sessions and run
crash recovery, as with pgrx. Committed changes remain committed; a transaction
that reached `Prepare` remains prepared and can still be committed or rolled
back after recovery. Use a reversible phase to reject work safely; handle
expected failures inside cleanup callbacks.

Registrations made inside a parallel worker belong to that worker's transaction.
Successful completion runs `ParallelPreCommit` and `ParallelCommit`; failure
runs `ParallelAbort`. A `ParallelPreCommit` error fails the leader's query.
The leader's registrations remain separate, and SQL is unavailable in all three
parallel callback phases. After the failed query is rolled back, another query
can launch fresh workers.

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
