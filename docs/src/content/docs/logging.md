---
title: Logging and errors
description: Report PostgreSQL notices, structured diagnostics, and errors from C#.
---

`PgLog` sends messages through PostgreSQL's reporting system:

```csharp
PgLog.Write(PgLogLevel.Notice, "Refresh complete.");
```

Calls require the active PostgreSQL backend thread. `Console.WriteLine` does not
produce a PostgreSQL notice.

## Levels and filtering

`Debug5` through `Debug1` provide decreasing detail. `Log` reports operational
messages, and `ServerOnly` keeps them out of the client connection. `Info` always
reaches clients. `Notice` and `Warning` report events without stopping execution.

PostgreSQL applies `client_min_messages` and `log_min_messages` separately. In the
server log, `Log` ranks above `Error`; it is not an ordinary numeric threshold.

Check the current settings before doing expensive formatting:

```csharp
if (PgLog.IsEnabled(PgLogLevel.Debug1))
{
    PgLog.Write(PgLogLevel.Debug1, $"Prepared {items.Count} items.");
}
```

Messages are literal text. Percent signs have no special meaning.

## Structured messages

```csharp
PgLog.Write(PgLogLevel.Warning, new PgDiagnostic("Entry has expired.")
{
    SqlState = "01000",
    Detail = "The entry was last refreshed yesterday.",
    Hint = "Refresh it before the next query.",
    TableName = "cached_entries",
});
```

`PgDiagnostic` also accepts context, object names, query positions, and source
locations. `DetailLog` replaces `Detail` in the server log and is never sent to
the client. Messages retain their full text across the native boundary.

Without an explicit SQLSTATE, warnings use `01000`, errors use `XX000`, and lower
levels use `00000`. An explicit code has five uppercase ASCII letters or digits;
error levels cannot use `00000`.

## Errors

`PgLog.Write(PgLogLevel.Error, ...)` throws `PgException`. Extension code can catch
it and continue. An unhandled exception becomes PostgreSQL `ERROR` after the
managed method and its `finally` blocks have finished.

Throwing `PgException` directly is also supported:

```csharp
throw new PgException("22023", "Count must be positive.", hint: "Pass a count greater than zero.");
```

`Fatal` and `Panic` also unwind managed code before reporting at the native
boundary. Let these exceptions propagate. `Fatal` ends the backend connection.
`Panic` aborts the backend and causes PostgreSQL to terminate peer backends and
perform crash recovery.
