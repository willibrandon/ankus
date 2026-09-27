---
title: Shared memory and locks
description: Share unmanaged values between PostgreSQL backends with native reader/writer locks.
---

Use `PgLwLock<T>` for a value shared by PostgreSQL processes. Keep the descriptor
in a static field, then register it from `[PgModuleLoad]`:

```csharp
public readonly record struct Counters(long Completed, long Failed);

public static class SharedCounters
{
    private static readonly PgLwLock<Counters> State = new("my_extension.counters");

    [PgModuleLoad]
    public static void Register()
    {
        PgSharedMemory.Initialize(State, static () => new Counters(0, 0));
    }

    [PgFunction]
    public static long Completed()
    {
        using PgLwLockShareGuard<Counters> guard = State.Share();
        return guard.Value.Completed;
    }

    [PgFunction]
    public static void RecordCompletion()
    {
        using PgLwLockExclusiveGuard<Counters> guard = State.Exclusive();
        Counters value = guard.Value;
        guard.Value = value with { Completed = checked(value.Completed + 1) };
    }
}
```

Add the published library to `shared_preload_libraries` and restart PostgreSQL:

```ini
shared_preload_libraries = 'MyExtension'
```

Registration reserves space during startup. The initializer runs when PostgreSQL
creates the shared value, after registration finishes. It can read configuration,
write log messages and use memory contexts; SQL is unavailable in the postmaster.
Factories run in registration order and may read values whose initialization has
already completed. Access to a value before its initialization completes fails.
`PgSharedMemory.Initialize(State)` uses the default zero value instead of a
factory. Registering the same descriptor again keeps its original initializer.

Shared-memory names are cluster-wide, case-sensitive UTF-8 strings. Include your
extension's name to avoid collisions. Names must be nonempty, contain no zero
byte, and fit PostgreSQL's shared-memory index. Names that exceed its limit are
rejected without truncation. Registering another descriptor with the same name
in one extension is rejected.

## Values and lifetime

`T` must be unmanaged. Primitive values and structs containing unmanaged fields
can be copied directly. Strings, arrays, delegates and other managed references
cannot be stored in shared memory. An unmanaged struct can still contain an
address: you are responsible for ensuring that address is valid in every process
that uses it. A pointer to a managed object or a process-local allocation does
not meet that contract.

Shared values live until PostgreSQL shuts down or recreates its shared memory
after a backend crash. They are not durable storage. Creating a replacement
shared segment runs the initializers again. Ordinary backend attachment preserves
existing values without rerunning their initializer or replacing their bytes.
Windows backends attach explicitly; this does not depend on inheriting
process-local pointers from the postmaster.

Each `Value` read returns a copy. Modify that copy and assign it back through an
exclusive guard to publish the update. Writes take effect immediately; a SQL
rollback or exception does not undo them. Readers in other backends see the
updated value after the writer releases its lock.

## Lock ownership and errors

`Share()` permits concurrent readers. `Exclusive()` waits for readers and writers
and permits one writer. These use PostgreSQL lightweight locks across processes.
Recursive acquisition in the same backend is rejected, including attempts to
upgrade a shared guard to exclusive access.

Use `using` to release each guard promptly. Callback exit also releases forgotten
guards. A guard cannot be used after disposal or callback exit, from another
thread, or through another extension's native capability. Keep guarded work
synchronous; do not retain a guard for another SQL call.

Managed exceptions unwind guards before being reported to PostgreSQL. Native
error recovery can release PostgreSQL locks before managed cleanup runs. Such a
guard no longer permits access, and disposing it cannot release a later
acquisition of the same lock. After recovery, acquire a new guard before accessing
the value. Shared-memory writes made before an error remain visible.

Atomics, bounded shared collections, spinlock conveniences and high-level
background-worker APIs are still being ported.
