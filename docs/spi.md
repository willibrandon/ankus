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
