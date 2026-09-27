---
title: Background workers
description: Run PostgreSQL background-worker processes with managed entries, transactions and shared state.
---

A background worker is a PostgreSQL process with its own managed state. Declare
its entry with `[PgBackgroundWorker]` on an accessible synchronous static method
returning `void` and accepting one `nuint` argument:

```csharp
public static class Workers
{
    [PgBackgroundWorker]
    public static void Run(nuint argument)
    {
        PgBackgroundWorker.Connect("postgres");
        while (PgBackgroundWorker.Wait(TimeSpan.FromSeconds(10)))
        {
            if ((PgBackgroundWorker.ConsumeSignals(PgBackgroundWorkerSignals.Reload)
                & PgBackgroundWorkerSignals.Reload) != 0)
            {
                PgBackgroundWorker.ReloadConfiguration();
            }

            PgBackgroundWorker.RunTransaction(() =>
            {
                long count = Spi.ExecuteScalar<long>("SELECT count(*) FROM pg_database");
                PgLog.Write(PgLogLevel.Log, $"Worker {argument}: {count} databases");
            });
        }
    }
}
```

The native symbol defaults to the method name. Set `EntryPoint` on the attribute
to choose another symbol. The generator rejects invalid signatures, conflicting
SQL attributes and duplicate worker symbols. Worker entries do not create SQL
functions. A library can declare worker entries without a `[PgModuleLoad]` or
`[PgInitialize]` method.

Start with a complete worker project and tests:

```console
ankus new MyExtension --background-worker
cd MyExtension
dotnet test
```

The generated backend fixture preloads the extension and verifies a worker's
committed query result from another PostgreSQL process. The
[database observer sample](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.BackgroundWorkers)
also publishes shared counters for SQL sessions.

## Register during preload

Register a static worker from `[PgModuleLoad]`:

```csharp
[PgModuleLoad]
public static void Register()
{
    PgBackgroundWorker.Register(new("Database observer", "MyExtension", nameof(Run))
    {
        DatabaseAccess = true,
        Argument = 42,
        RestartDelay = TimeSpan.FromSeconds(5),
    });
}
```

Here `MyExtension` is the published library name. Add it to PostgreSQL's
configuration and restart the server:

```ini
shared_preload_libraries = 'MyExtension'
```

Static registration requires shared preload. Windows backend replay attaches
the extension's state without registering another static worker. Set
`max_worker_processes` high enough for your extensions and PostgreSQL's other
workers.

`DatabaseAccess` permits database initialization and defaults the startup phase
to `RecoveryFinished`. Workers without database access default to
`PostmasterStart`. `ConsistentState` is also available; database workers cannot
start at `PostmasterStart`.

`RestartDelay` accepts whole seconds. `null` disables restart, zero requests an
immediate restart after failure, and a positive value delays it. Successful
return from the entry does not restart the worker. Explicit termination also
prevents subsequent restarts.

## Start dynamically

A backend callback can register a worker at runtime, after shared preload has
completed:

```csharp
var options = new PgBackgroundWorkerOptions("Database observer", "MyExtension", "Run")
{
    DatabaseAccess = true,
    Argument = 42,
    NotifyProcessId = Environment.ProcessId,
};
if (!PgBackgroundWorker.TryStart(options, out PgBackgroundWorkerHandle? worker))
{
    throw new InvalidOperationException("No PostgreSQL worker slot is available.");
}

using (worker)
{
    PgBackgroundWorkerState startup = worker.WaitForStartup();
    if (startup.Status == PgBackgroundWorkerStatus.Started)
    {
        PgLog.Write(PgLogLevel.Notice, $"Worker PID: {startup.ProcessId}");
    }
}
```

`TryStart` returning true means registration succeeded. Startup may still fail,
for example when the library or entry cannot be loaded. `GetState` polls the
registration. `WaitForStartup` waits for a startup outcome; a `Started` snapshot
contains a process ID, while the other states have no process ID. After a
restart, query the handle again to obtain the new process ID.

PostgreSQL registers a dynamic worker before allocating its observation handle.
If that allocation fails, `TryStart` throws a `PgException`, but the registered
worker can still start. Ankus releases its local observation storage; it cannot
undo that registration without a handle. Check your worker's shared state or
other application-level completion signal before retrying if duplicate work
would be a problem.

Set `NotifyProcessId` to the registering backend's PID to use startup and
shutdown waits. They return `Untracked` if that backend is not the notifier.
Polling and termination remain available without notifications. Static workers
require a notification PID of zero.

The handle belongs to the current backend callback and thread. Disposing it,
or returning from the callback, releases observation storage; the worker keeps
running. Call `Terminate()` to request shutdown, then `WaitForShutdown()` to
observe it before disposing the handle. Keep the handle within its originating
callback; it cannot be retained for a later SQL invocation.

## Database work and errors

Call `Connect` once in a database-enabled worker, using database and optional
role names or their OIDs. A null database name or zero database OID requests no
selected database. A null role name or zero role OID selects PostgreSQL's
bootstrap superuser. Choose an explicit role when the worker should use that
role's database privileges. The role's login permission, database `CONNECT`
permission and table privileges apply. An empty name requests a database whose
name is empty. The same null-versus-empty distinction applies to role names.
PostgreSQL can terminate the worker if connection initialization fails, including
a missing database or role, a role without login permission, or denied database
access. Observe the stopped worker and server diagnostic, then start another
worker to retry.

Use `RunTransaction` for synchronous database work after connecting. The callback
can use `Spi` and return an owned managed result. Commit completes before the
method returns. A managed exception aborts the transaction and is rethrown with
its identity preserved. PostgreSQL errors, including commit failures, become
owned `PgException` diagnostics after native cleanup. The worker may then start
another transaction.

Worker transactions cannot nest. Do not retain transaction-owned native views
or return asynchronous work from a transaction callback. Keep PostgreSQL calls
on the worker's owning thread; a task or timer callback is not a backend entry.

## Signals and shared values

Generated entries install native reload and termination handlers before calling
managed code. `Wait` sleeps on PostgreSQL's latch, wakes for a signal or timeout,
and returns false for termination or postmaster death. Its optional timeout
accepts whole milliseconds; null waits indefinitely and zero polls immediately.
It consumes a pending termination request. `CanContinue` checks postmaster
liveness and an unconsumed termination request without waiting.

`ConsumeSignals` returns and clears only the selected observations. To observe
interrupt or child signals, first add them with `AttachSignalHandlers`. Signal
handlers record flags and wake the latch; managed code processes them after
waking. These observations are flags: multiple deliveries before consumption
can coalesce. Consuming one selection leaves other pending observations intact;
consuming the same selection again returns no flags unless another signal arrived.
Reloading configuration is explicit through `ReloadConfiguration()`.

`Name`, `Type` and `Extra` return owned strings from the current registration.
`Type` defaults to `Name`. Registration text uses strict UTF-8 and must fit the
selected PostgreSQL version's fixed fields, including its terminating zero.
Oversized text and embedded zero characters are rejected without truncation.
PostgreSQL sanitizes dynamic-worker names, types, library paths and entry names.
`TryStart` rejects characters it would replace: these four fields accept ASCII
characters from space through DEL, plus tab, carriage return and newline.
The extra payload preserves UTF-8, including non-ASCII characters. Static
registration retains its original UTF-8 metadata.

`Argument` preserves one native-sized unsigned value. Pass an identifier or
other by-value data, never a process-local address. Managed static fields are
independent in each worker. For values shared with other processes, register
`PgAtomic<T>`, `PgShared<T>` or `PgLwLock<T>` descriptors during preload; see
[shared memory, locks and atomics](/shared-memory/).
