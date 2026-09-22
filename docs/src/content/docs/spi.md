---
title: SPI queries
description: Query PostgreSQL from C# with typed parameters, sessions, plans, and cursors.
---

`Spi` executes SQL in the PostgreSQL backend running the extension function.
Calls participate in the caller's transaction and use that connection's role,
search path, and session settings.

## Parameters and commands

Bind values with positional placeholders and `SpiParameter.Create`:

```csharp
long inserted = Spi.Execute(
    "INSERT INTO messages (body) VALUES ($1)",
    SpiParameter.Create("Hello, PostgreSQL!"));
```

`Execute` returns the final statement's processed-row count. Parameter types are
inferred from the declared C# type. Use an explicit nullable type for SQL NULL:

```csharp
SpiParameter optionalCount = SpiParameter.Create<int?>(null);
SpiParameter optionalText = SpiParameter.Create<string?>(null);
```

Parameters accept `bool`, `sbyte`, `short`, `int`, `long`, `uint` (OID), `float`,
`double`, `string`, `byte[]`, `Guid`, `PgJson`, `PgJsonb`, and nullable forms. Parameter values
are sent separately from the SQL command. Each command call uses one internal
subtransaction and reports results from its final statement.

See [JSON and UUID values](/json-and-uuid/) for the distinction between JSON null
and SQL NULL and for source-generated JSON serialization.

## Scalar values

```csharp
int answer = Spi.ExecuteScalar<int>(
    "SELECT $1 + $2",
    SpiParameter.Create(40),
    SpiParameter.Create(2));
```

`ExecuteScalar<T>` reads the first column of the first row. SQL NULL or an absent
cell returns null when `T` is a reference or nullable value type. A non-nullable
value type throws `InvalidOperationException` instead of substituting a default.
An incompatible result type throws `InvalidCastException`; numeric values are not
implicitly widened or parsed from text.

Reading a scalar does not limit command execution. For example, an
`INSERT ... RETURNING` command completes all its writes even though only its first
returned cell is copied.

## Rows and metadata

```csharp
SpiResult result = Spi.Query("SELECT id, body FROM messages ORDER BY id");

foreach (SpiRow row in result)
{
    int id = row.Get<int>("id");
    string? body = row.Get<string?>(1);
}
```

Ordinals are zero-based. Column names are case-sensitive, and duplicate names
resolve to the first matching column. The row indexer returns a managed object or
null. `Columns` exposes each column's name and PostgreSQL type OID, including for
queries that return no rows. Domain columns retain their declared OID and use the
underlying base type's value conversion.

Results are managed copies. Rows, text, and binary buffers remain valid after
another SPI command or after the original SPI connection is released.

### Editing result rows

`SpiRow.Set<T>` replaces a cell in the managed result, using either an ordinal or
an exact column name:

```csharp
SpiRow row = result[0];
row.Set("body", "edited locally");
row.Set<int?>("id", null);

int ordinal = row.GetOrdinal("body");
string body = row.Get<string>(ordinal);
uint cellType = row.GetTypeOid("id"); // int4 OID, even though the cell is NULL
```

Edits belong to that row's managed copy. Database changes use SQL commands.
`Set<T>` accepts the supported datum types and permits replacing a cell with a
different type. `GetTypeOid` tracks each cell's current type; `result.Columns`
continues to describe the original query. A domain cell retains its domain OID
until replaced. Typed NULL retains the replacement type's OID. Invalid types,
names, or ordinals leave the row's value and type intact.

`Count` gives the number of cells. Both numeric and named indexers return the
managed object or null. Managed reference values, including byte arrays, follow
ordinary .NET reference semantics when assigned to a row. The owned rows can be
read or edited after returning from an extension callback, without a backend binding.

### Query options

For an explicit row limit or read-only SPI execution:

```csharp
SpiResult page = Spi.Query(
    "SELECT id FROM messages ORDER BY id",
    readOnly: true,
    limit: 100);
```

A limit of zero means unlimited. Read-only mode uses PostgreSQL's read-only SPI
snapshot and restrictions, including rejection of write commands.

## Quoting SQL fragments

Use PostgreSQL's identifier rules when assembling dynamic object names:

```csharp
string table = Spi.QuoteQualifiedIdentifier("my schema", "messages");
string column = Spi.QuoteIdentifier("Message Body");
Spi.Execute($"INSERT INTO {table} ({column}) VALUES ($1)", SpiParameter.Create("hello"));
```

`QuoteIdentifier` treats its input as one identifier, including any dots.
`QuoteQualifiedIdentifier` quotes its two components independently; a null
qualifier omits the prefix, while an empty qualifier represents an empty name.
These functions use the server's keyword table and `quote_all_identifiers` setting.

`Spi.QuoteLiteral(text)` produces a SQL text literal, escaping apostrophes and
backslashes. Its output is valid with either `standard_conforming_strings`
setting. Quoting uses server-encoding conversion and the active backend thread;
embedded zero characters and malformed UTF-16 are rejected before native calls.

## JSON query plans

```csharp
PgJson plan = Spi.Explain(
    "SELECT * FROM messages WHERE id = $1",
    SpiParameter.Create(42));

using JsonDocument document = plan.Parse();
JsonElement root = document.RootElement[0].GetProperty("Plan");
```

`Spi.Explain` and `SpiSession.Explain` run `EXPLAIN (FORMAT JSON)` and return an
owned JSON value. The server plans the supplied statement with its typed
parameters, using ordinary EXPLAIN rather than EXPLAIN ANALYZE. The command
accepts one SQL statement. Parse and planning errors throw `PgException`.

## Prepared statements

Prepare a statement once and bind new values on each execution:

```csharp
using SpiPreparedStatement statement = Spi.Prepare(
    "SELECT $1 + $2", typeof(int), typeof(int));

int first = statement.ExecuteScalar<int>(SpiParameter.Create(40), SpiParameter.Create(2));
int second = statement.ExecuteScalar<int>(SpiParameter.Create(10), SpiParameter.Create(5));
```

The declared CLR types determine the PostgreSQL parameter types. Nullable value
types map to the same SQL type as their underlying type. Bind SQL NULL with a
typed nullable parameter.
An incorrect parameter count or SQL type throws `ArgumentException` before the
plan executes.

`SpiPreparedStatement` provides `Execute`, `Query`, and `ExecuteScalar<T>` with the
same result semantics as `Spi`. `Query` also accepts `readOnly` and `limit` options.
Preparation supports multiple SQL commands; each execution's internal subtransaction
covers all commands, and results describe the final command.

Plans created by `Spi.Prepare` survive the SPI connection, the extension callback, and transaction commit
or rollback. They can be cached within a backend. PostgreSQL manages replanning
after schema or search-path changes. Result rows are independent managed copies
even when the same statement is executed again.

Execution and disposal require an extension callback on the owning backend thread.
Use `using` for local plans and explicitly dispose cached plans when replacing them.
Disposal is idempotent, and subsequent execution throws `ObjectDisposedException`.
Reentrant execution of independently retained plans is supported; disposal or retention during an active execution throws
`InvalidOperationException` to preserve the native plan's lifetime.

There is no finalizer that calls PostgreSQL: its APIs cannot run on the .NET
finalizer thread. An independently retained plan that is never explicitly disposed remains allocated in
the backend until the process exits.

## Scoped SPI sessions

`Spi.Connect` runs a synchronous callback using one native SPI connection:

```csharp
SpiResult rows = Spi.Connect(session =>
{
    session.Execute("INSERT INTO messages (body) VALUES ($1)", SpiParameter.Create("hello"));

    using SpiPreparedStatement statement = session.Prepare(
        "SELECT id, body FROM messages WHERE id >= $1 ORDER BY id", typeof(int));
    return statement.Query(SpiParameter.Create(100));
});

// The materialized rows remain valid after the native session closes.
foreach (SpiRow row in rows)
{
    int id = row.Get<int>("id");
}
```

The session offers `Execute`, `Query`, `ExecuteScalar<T>`, `Prepare`, and
`OpenCursor`, with the same parameter and owned-result conversions as the
standalone API. `Query` and `OpenCursor` accept explicit read-only execution mode.
Session operations run in individual internal subtransactions: a failed command
rolls back that command, and successful commands remain in the caller's
transaction. The callback's exit closes the connection, including when a managed
exception or PostgreSQL cancellation unwinds the callback.

A statement created by `session.Prepare` belongs to that session and is freed
automatically when the callback exits. Explicit disposal can release it earlier.
Use `Keep()` while the session is active to transfer ownership out of the scope:

```csharp
using SpiPreparedStatement statement = Spi.Connect(session =>
    session.Prepare("SELECT $1 + 2", typeof(int)).Keep());

int answer = statement.ExecuteScalar<int>(SpiParameter.Create(40));
```

`Keep()` returns the same statement instance. The retained statement survives
callback and transaction boundaries and requires explicit disposal. Both scoped
and retained statements participate in PostgreSQL plan invalidation after schema
changes. Cursors opened by a session or its plans have their normal portal
lifetimes and can be fetched after the session closes.

Sessions nest in stack order. While an inner session callback runs, operations on
an outer session or its scoped plans throw `InvalidOperationException`; access
resumes after the inner scope exits. A recursive extension callback must use an
independent session or the standalone SPI API. Escaped sessions and unretained
plans throw `ObjectDisposedException` when used after their scope ends. The
callback must remain synchronous and on the backend thread; asynchronous session
work is not supported.

## Cursors and batched results

Use a cursor to fetch a query incrementally:

```csharp
using SpiCursor cursor = Spi.OpenCursor(
    "SELECT id, body FROM messages WHERE id >= $1 ORDER BY id",
    readOnly: true,
    SpiParameter.Create(100));

while (true)
{
    SpiResult batch = cursor.Fetch(128);
    if (batch.Count == 0)
    {
        break;
    }

    foreach (SpiRow row in batch)
    {
        int id = row.Get<int>("id");
        string? body = row.Get<string?>("body");
    }
}
```

Each batch owns its managed rows and buffers and remains valid after later fetches
or cursor disposal. Empty batches retain column metadata. `SpiPreparedStatement.OpenCursor`
accepts the plan's typed parameters; the resulting cursor remains usable even after
the prepared statement is disposed.

`Fetch(count)` moves forward. `Fetch(count, forward: false)` moves backward when
the underlying PostgreSQL cursor supports scrolling. Counts must be nonnegative;
zero means fetch the current row, following PostgreSQL semantics, and can require
a scrollable cursor. `Spi.FindCursor` can attach to cursors declared using SQL,
including `SCROLL` and `WITH HOLD` cursors.

To continue a cursor in another extension callback within the same transaction,
detach its ownership and retain its portal name:

```csharp
string name;
using (SpiCursor cursor = Spi.OpenCursor("SELECT id FROM messages ORDER BY id"))
{
    name = cursor.Detach();
}

using SpiCursor resumed = Spi.FindCursor(name);
SpiResult nextBatch = resumed.Fetch(128);
```

`Detach` leaves the portal open and makes the original managed object unusable.
Disposal closes an owned portal and is idempotent. If several objects refer to
the same portal, closing one invalidates the others. Normal SPI-opened cursors
end with their transaction; a savepoint rollback also closes cursors created
inside that savepoint. Externally declared holdable cursors follow PostgreSQL's
hold semantics. Stale objects produce a managed `PgException` with SQLSTATE `34000`;
reusing a portal name never makes an old object refer to the replacement.

Cursor operations require the owning backend thread. A failed fetch can leave
the portal unusable; cursor position and recovery follow PostgreSQL's rules.
Independent SPI commands remain available after the error is caught, and the
cursor can still be disposed. No cursor cleanup is performed by a .NET finalizer.

## Errors and transactions

Each call runs in an internal subtransaction. Success retains its changes in the
enclosing transaction; a PostgreSQL error rolls back that call before throwing
`PgException`. Managed code can catch the exception and execute another SPI call:

```csharp
try
{
    Spi.Execute("INSERT INTO unique_values (value) VALUES ($1)", SpiParameter.Create(42));
}
catch (PgException exception) when (exception.SqlState == "23505")
{
    // Handle the duplicate value.
}
```

Managed `finally` blocks run normally for PostgreSQL errors and cancellation.
SPI calls are confined to the active backend thread; worker-thread calls throw
`InvalidOperationException` before accessing PostgreSQL state.

### Error diagnostics

`PgException` owns its diagnostic strings. They remain valid after recovery,
subsequent SPI calls, and the transaction ending. Available fields include:

| Properties | Meaning |
|---|---|
| `SqlState`, `Message`, `Detail`, `Hint` | SQLSTATE and primary/secondary diagnostics |
| `Context` | PostgreSQL execution context, including procedural and SQL frames |
| `SchemaName`, `TableName`, `ColumnName`, `DataTypeName`, `ConstraintName` | Server-supplied object names |
| `Position` | One-based character position in the client query; zero when absent |
| `InternalPosition`, `InternalQuery` | Character position and text of an internally executed query |
| `File`, `Line`, `Routine` | Original reporting source location |
| `DetailLog`, `Backtrace` | Server-only detail and native backtrace, when supplied |

Optional strings are null when absent; an explicitly empty diagnostic remains an
empty string. SPI typically reports parse positions through `InternalPosition`
and `InternalQuery`. Positions count PostgreSQL characters, not UTF-8 bytes or
UTF-16 code units.

Extension-authored errors can supply structured object/context fields:

```csharp
throw new PgException("22023", "The supplied value is invalid.", hint: "Use a positive value.")
{
    SchemaName = "app",
    TableName = "messages",
    ColumnName = "priority",
    Context = "Validating message priority",
};
```

Rethrowing a caught `PgException` preserves its source location and existing
context without duplicating PostgreSQL context callbacks. Diagnostic strings
are transported in full; if allocation or encoding fails during capture,
`DiagnosticsIncomplete` indicates a partial diagnostic and the primary message
has a bounded emergency fallback. Diagnostics use the same server-encoding
conversion rules as text values.
