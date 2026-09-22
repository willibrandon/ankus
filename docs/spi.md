# SPI queries

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
`double`, `string`, `byte[]`, and nullable forms. Text and binary parameter values
are sent separately from the SQL command. Parameterized commands contain a single
SQL statement, as required by `SPI_execute_with_args`.

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

For an explicit row limit or read-only SPI execution:

```csharp
SpiResult page = Spi.Query(
    "SELECT id FROM messages ORDER BY id",
    readOnly: true,
    limit: 100);
```

A limit of zero means unlimited. Read-only mode uses PostgreSQL's read-only SPI
snapshot and restrictions, including rejection of write commands.

## Prepared statements

Prepare a statement once and bind new values on each execution:

```csharp
using SpiPreparedStatement statement = Spi.Prepare(
    "SELECT $1 + $2", typeof(int), typeof(int));

int first = statement.ExecuteScalar<int>(SpiParameter.Create(40), SpiParameter.Create(2));
int second = statement.ExecuteScalar<int>(SpiParameter.Create(10), SpiParameter.Create(5));
```

The declared CLR types determine the PostgreSQL parameter types without inspecting
members or generating code at runtime. Nullable value types map to the same SQL
type as their underlying type. Bind SQL NULL with a typed nullable parameter.
An incorrect parameter count or SQL type throws `ArgumentException` before the
plan executes.

`SpiPreparedStatement` provides `Execute`, `Query`, and `ExecuteScalar<T>` with the
same result semantics as `Spi`. `Query` also accepts `readOnly` and `limit` options.
Preparation supports multiple SQL commands; each execution's internal subtransaction
covers all commands, and results describe the final command.

Plans survive the SPI connection, the extension callback, and transaction commit
or rollback. They can be cached within a backend. PostgreSQL manages replanning
after schema or search-path changes. Result rows are independent managed copies
even when the same statement is executed again.

Execution and disposal require an extension callback on the owning backend thread.
Use `using` for local plans and explicitly dispose cached plans when replacing them.
Disposal is idempotent, and subsequent execution throws `ObjectDisposedException`.
Reentrant execution is supported; disposal during an active execution throws
`InvalidOperationException` to preserve the native plan's lifetime.

There is no finalizer that calls PostgreSQL: its APIs cannot run on the .NET
finalizer thread. A plan that is never explicitly disposed remains allocated in
the backend until the process exits.

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
