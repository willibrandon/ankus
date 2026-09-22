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

Extension projects use the `Ankus.Sdk` NuGet project SDK:

```xml
<Project Sdk="Ankus.Sdk/1.0.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AnkusExtensionName>hello</AnkusExtensionName>
  </PropertyGroup>
</Project>
```

The SDK includes matching runtime and source-generator packages plus the native
build helper. See [package setup](docs/contributing/development.md#build-the-packages)
for the local NuGet feed; public publication is pending.

From the repository root:

```console
dotnet test
```

See [development and testing](docs/contributing/development.md) for prerequisites,
PostgreSQL discovery, and the integration harness.

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
| `decimal`, `PgNumeric` | `numeric` |
| `DateOnly`, `PgDate` | `date` |
| `TimeOnly`, `PgTime` | `time` |
| `PgTimeTz` | `timetz` |
| `DateTime`, `PgTimestamp` | `timestamp` |
| `DateTimeOffset`, `PgTimestampTz` | `timestamptz` |
| `TimeSpan`, `PgInterval` | `interval` |
| `void` result | `void` |

Nullable value types and nullable reference annotations accept SQL NULL. Methods
with only required parameters are declared `STRICT`. For mixed signatures, a NULL
required argument returns SQL NULL without invoking the method; nullable arguments
reach managed code. Nullable results become SQL NULL. Methods can share a SQL name
when their PostgreSQL argument types differ.

Temporal conversions preserve microseconds. `DateTime` requires `Kind.Unspecified`;
`DateTimeOffset` represents a UTC instant. Full-range `Pg*` types support PostgreSQL
infinities, BC dates, 24:00, second-resolution offsets, and separate calendar months
and days. See [date and time values](docs/src/content/docs/date-and-time.md).

Text supports server-encoding conversion and Unicode; binary data preserves zero
bytes. Native wrappers detoast compressed, external, and packed varlena inputs
before invoking managed code. Managed exceptions return completely to native code
before PostgreSQL raises ERROR. See [the native boundary design](docs/contributing/native-boundary.md)
for buffer ownership and error cleanup.

See [JSON and UUID values](docs/src/content/docs/json-and-uuid.md) for JSON text ownership, document
access, and source-generated serialization with Native AOT.

## Querying PostgreSQL

Use `Spi` inside an extension function to execute SQL in the calling backend:

```csharp
int answer = Spi.ExecuteScalar<int>("SELECT $1 + $2", SpiParameter.Create(40), SpiParameter.Create(2));
```

See [SPI queries](docs/src/content/docs/spi.md) for typed parameters, result rows, and error handling.
Use [logging and errors](docs/src/content/docs/logging.md) to send PostgreSQL notices and structured diagnostics.

## Publishing and installation

Use the `ankus` tool to register PostgreSQL, publish an extension, and install its files:

```console
ankus init --pg18 /path/to/postgresql/bin/pg_config
ankus publish --project samples/Ankus.Examples.Hello --output artifacts/hello
ankus install --from artifacts/hello
```

See [tool setup](docs/contributing/development.md#build-the-tool) for local package installation
and the [command reference](docs/src/content/docs/reference/cli.md) for options.

Publishing the sample produces a native library and these PostgreSQL installation files:

```text
Ankus.Examples.Hello.so                   # .dll on Windows, .dylib on macOS
Ankus.Examples.Hello.sql
ankus.extension.json
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

## Documentation site

The Astro/Starlight site lives in `docs/`. See [site maintenance](docs/contributing/site.md)
for local preview and build commands.

The [public API reference](docs/src/content/docs/api/index.md) is generated from C# XML
documentation. See [API reference generation](docs/contributing/api-reference.md).
