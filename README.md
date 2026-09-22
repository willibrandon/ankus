# Ankus

Ankus ports [pgrx](https://github.com/pgcentralfoundation/pgrx) to .NET Native AOT.
Write ordinary C# functions and publish them as a native PostgreSQL extension library.

```csharp
[PgFunction]
public static int Add(int left, int right) => checked(left + right);

[PgFunction]
public static string Greet(string name) => $"Hello, {name}!";
```

See the [minimal sample](samples/Ankus.Examples.Hello/Hello.cs). Ankus generates
PostgreSQL module magic, exports, argument conversion, SQL declarations, and the
managed-to-native error boundary.

## Develop and test

Prerequisites:

- A stable .NET SDK compatible with `global.json`.
- The platform's [.NET Native AOT toolchain](https://learn.microsoft.com/dotnet/core/deploying/native-aot/).
- PostgreSQL 18, including `pg_config`, server executables, and server development
  headers. On Windows, the server import library is also needed.

From the repository root, run:

```console
dotnet test
```

Tests use MSTest with Microsoft.Testing.Platform.
The integration fixture publishes the native sample, creates an isolated local
PostgreSQL cluster, installs with `CREATE EXTENSION`, executes SQL tests, rolls back
test transactions, and shuts down the cluster. Logs are written to `artifacts/test-logs`.

PostgreSQL discovery checks:

1. `~/.ankus/config.json` (the current user's home directory on every platform).
2. `~/.ankus/postgres/*/bin/pg_config` (`pg_config.exe` on Windows).
3. `PATH` and conventional PostgreSQL installation directories for the host OS.

For a nonstandard installation, an optional configuration entry supplies its path:

```json
{
  "pg18": "/path/to/postgresql/bin/pg_config"
}
```

## Function types

Declare functions as synchronous static methods. The generator uses these type mappings:

| C# | PostgreSQL |
|---|---|
| `bool` | `boolean` |
| `sbyte` | `"char"` (PostgreSQL's internal signed byte) |
| `short`, `int`, `long` | `smallint`, `integer`, `bigint` |
| `uint` | `oid` |
| `float`, `double` | `real`, `double precision` |
| `string` | `text` |
| `byte[]` | `bytea` |
| `Guid` | `uuid` |
| `PgJson`, `PgJsonb` | `json`, `jsonb` |
| `void` result | `void` |

Nullable value types and nullable reference annotations accept SQL NULL. Methods
with only required parameters are declared `STRICT`. For mixed signatures, a NULL
required argument returns SQL NULL without invoking the method; nullable arguments
reach managed code. Nullable results become SQL NULL. Methods can share a SQL name
when their PostgreSQL argument types differ.

Text supports server-encoding conversion and Unicode; binary data preserves zero
bytes. Native wrappers detoast compressed, external, and packed varlena inputs
before invoking managed code. Managed exceptions return completely to native code
before PostgreSQL raises ERROR. See [the native boundary design](docs/native-boundary.md)
for buffer ownership and error cleanup.

See [JSON and UUID values](docs/json-and-uuid.md) for JSON text ownership, document
access, and source-generated serialization with Native AOT.

## Querying PostgreSQL

Use `Spi` inside an extension function to execute SQL in the calling backend:

```csharp
int answer = Spi.ExecuteScalar<int>("SELECT $1 + $2", SpiParameter.Create(40), SpiParameter.Create(2));
```

See [SPI queries](docs/spi.md) for typed parameters, result rows, and error handling.

## Publishing and installation

Publishing the sample produces a native library and these PostgreSQL installation files:

```text
Ankus.Examples.Hello.so                   # .dll on Windows, .dylib on macOS
Ankus.Examples.Hello.sql
extension/
    ankus_hello.control
    ankus_hello--1.0.0.sql
```

`AnkusExtensionName` selects the extension name; its default is the assembly name
lowercased with periods replaced by underscores. `AnkusExtensionVersion` defaults
to the project's `Version`. The control file resolves the native library through
PostgreSQL's `dynamic_library_path`, whose default is `$libdir`.

For a standard server installation, the native library belongs in
`pg_config --pkglibdir`, and the control and versioned SQL files belong in the
`extension` subdirectory of `pg_config --sharedir`. Then PostgreSQL can run:

```sql
CREATE EXTENSION ankus_hello;
SELECT public.add(40, 2); -- 42
SELECT public.greet('PostgreSQL'); -- Hello, PostgreSQL!
```

See [PROGRESS.md](PROGRESS.md) for implementation status and platform validation.
