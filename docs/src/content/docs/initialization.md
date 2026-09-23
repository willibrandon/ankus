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
