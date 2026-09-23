---
title: Extension initialization
description: Initialize managed extension state when PostgreSQL loads the library in a backend.
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

Ankus generates PostgreSQL's native `_PG_init` entry point and a guarded managed
callback. PostgreSQL invokes it when a backend first loads the native library,
before invoking any function from that library. A successful initialization runs
once per backend process. Ordinary managed static fields belong to that backend;
they are not shared with other connections.

See the [initialization sample](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Initialization).
It has no SQL function declarations and loads explicitly:

```sql
LOAD 'Ankus.Examples.Initialization';
```

An extension with SQL functions also initializes when PostgreSQL first loads its
library to validate or invoke a function. Installing an extension that contains
only an initializer does not itself load the library; use `LOAD` or backend preload.

## Database access and failure

When PostgreSQL loads the library inside a transaction, the initializer can use
guarded `Spi` queries and other backend APIs. `session_preload_libraries` loads
libraries during the backend's initial transaction, so database access is also
available there. If a native caller loads the library without an active
transaction, transaction-dependent APIs throw `InvalidOperationException` before
entering PostgreSQL.

Managed exceptions unwind `finally` blocks before Ankus raises a PostgreSQL
error. `PgException` preserves SQLSTATE, message, detail, and hint. Other managed
exceptions use SQLSTATE `38000`.

Initialization can run again after a failed load. Design failed attempts so that
retry is valid: managed static mutations and external side effects are not rolled
back. SQL changes follow the surrounding PostgreSQL transaction or savepoint.
Loading the same library recursively during its own initialization raises
SQLSTATE `55000`; the backend can recover and retry. An initializer may load a
different extension through guarded SPI.

## Preloading

Use `session_preload_libraries` to initialize the extension in each new backend:

```ini
session_preload_libraries = 'Ankus.Examples.Initialization'
```

Managed `[PgInitialize]` callbacks cannot run through `shared_preload_libraries`
in the forking postmaster. Native AOT starts runtime threads on its first managed
call; those threads and their runtime state cannot safely be inherited by
PostgreSQL's forked backends. The generated native entry point rejects this load
before invoking managed code. This restriction applies to managed initialization
and managed configuration hooks. Native-only [configuration declarations](/configuration/)
can preload before PostgreSQL forks its backends.
