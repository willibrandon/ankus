---
title: Extension initialization
description: Initialize managed extension state when PostgreSQL loads the library.
---

Use `[PgInitialize]` on one accessible, synchronous, non-generic, parameterless
static `void` method per extension assembly:

```csharp
public static class Startup
{
    [PgInitialize]
    public static void Initialize()
    {
        PgLog.Write(PgLogLevel.Notice, "Extension initialized.");
    }
}
```

Ankus generates PostgreSQL's `_PG_init` entry point. PostgreSQL calls it before
the first extension function. A library with only an initializer can load with:

```sql
LOAD 'Ankus.Examples.Initialization';
```

Installing an extension does not load an initializer-only library.

## Native hook and provider registration

Use `[PgModuleLoad]` when native registration must happen as soon as PostgreSQL
loads the library, before a parallel worker's first executor entry. It uses the
same method requirements as `[PgInitialize]`. An assembly may declare one method
for each phase; a single method cannot declare both attributes.

```csharp
public static class Startup
{
    [PgModuleLoad]
    public static void Register()
    {
        PgLog.Write(PgLogLevel.Notice, "Extension module loaded.");
    }

    [PgInitialize]
    public static void Initialize()
    {
        PgLog.Write(PgLogLevel.Notice, "Extension initialized.");
    }
}
```

For native registration, see the complete
[executor hook example](/raw-values/#managed-native-callbacks-and-hooks).
Ankus registers configuration settings before calling `PgModuleLoad`, then calls
`PgInitialize`. On Windows PostgreSQL versions before 18, a parallel worker loads
libraries before restoring its settings and transaction state. `PgModuleLoad`
runs immediately, while `PgInitialize` waits until restoration finishes.
SQL is unavailable during that early registration phase, including in nested
native callbacks. Use `PgInitialize` for work that needs restored worker settings.

Each successful phase runs once. If `PgModuleLoad` fails, the next load retries it.
If `PgInitialize` fails after successful registration, registration remains in
place and only initialization retries. Installed native hooks and managed state
are not automatically rolled back; registration code owns their cleanup.

## Database access and failure

`Spi` works when PostgreSQL loads the library inside a transaction. It is not
available during postmaster startup. Transaction-dependent APIs throw
`InvalidOperationException` when no transaction exists. Logging and configuration
reads remain available.

Managed exceptions unwind `finally` blocks before Ankus raises a PostgreSQL
error. `PgException` preserves SQLSTATE, message, detail, and hint. Other managed
exceptions use SQLSTATE `38000`.

Initialization can run again after a failed load. Managed static changes are not
rolled back. SQL changes follow the surrounding transaction or savepoint. Loading
the same library recursively raises SQLSTATE `55000`.

## Preloading

Use `session_preload_libraries` to initialize separately in each backend:

```ini
session_preload_libraries = 'Ankus.Examples.Initialization'
```

Use `shared_preload_libraries` to initialize once before PostgreSQL creates its
backends:

```ini
shared_preload_libraries = 'Ankus.Examples.Initialization'
```

Each backend receives its own copy of the initialized managed state. Changes made
in one backend do not change another backend or the postmaster. Tasks, timers,
garbage collection, finalizers, and managed configuration hooks continue to work
after startup. Extension projects use this automatically; no host setup is needed.
