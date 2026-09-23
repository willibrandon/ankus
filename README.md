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

With the Ankus tool installed from your configured feed:

```console
ankus new Hello
cd Hello
dotnet test
```

This creates an extension and MSTest project. Tests call managed methods directly
and load the published Native AOT library into an isolated PostgreSQL 18+ cluster.

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
| `[PgEnum]` C# enums | Generated PostgreSQL enum types |
| `PgHeapTuple` | `record`, or a named type using `[PgCompositeType]` |
| `void` result | `void` |

Nullable value types and nullable reference annotations accept SQL NULL. Methods
with only required parameters are declared `STRICT`. For mixed signatures, a NULL
required argument returns SQL NULL without invoking the method; nullable arguments
reach managed code. Nullable results become SQL NULL. Methods can share a SQL name
when their PostgreSQL argument types differ.

Temporal conversions preserve microseconds. `DateTime` requires `Kind.Unspecified`;
`DateTimeOffset` represents a UTC instant. Full-range `Pg*` types support PostgreSQL
infinities, BC dates, 24:00, second-resolution offsets, and separate calendar months
and days. They expose exact calendar fields and raw-value factories;
`PgTimeZone` resolves server timezone offsets at transaction start or a supplied instant.
See [date and time values](docs/src/content/docs/date-and-time.md).

Text supports server-encoding conversion and Unicode; binary data preserves zero
bytes. Native wrappers detoast compressed, external, and packed varlena inputs
before invoking managed code. Managed exceptions return completely to native code
before PostgreSQL raises ERROR. See [the native boundary design](docs/contributing/native-boundary.md)
for buffer ownership and error cleanup.

See [JSON and UUID values](docs/src/content/docs/json-and-uuid.md) for JSON text ownership, document
access, and source-generated serialization with Native AOT.

Use `[PgEnum]` and optional `[PgEnumLabel]` attributes for PostgreSQL enums,
including nullable values, arrays, typed SPI queries and schema dependencies.
See [enumerated types](docs/src/content/docs/enums.md) and the
[enum sample](samples/Ankus.Examples.Enums/DeliveryFunctions.cs).

Use `[PgOperator]` for binary or prefix operators and `[PgCast]` for explicit,
assignment, or implicit conversions. Both generate backing functions and
dependency-ordered SQL. See [operators and casts](docs/src/content/docs/operators-and-casts.md)
and the [operator sample](samples/Ankus.Examples.Operators/PriorityFunctions.cs).

## Querying PostgreSQL

Return `IEnumerable<T>` from `[PgFunction]` for `SETOF T`, or named tuple elements
for `RETURNS TABLE`. Ordinary C# iterators support streaming, PostgreSQL-backed
materialization, and cleanup when a query stops early. See [sets and tables](docs/src/content/docs/sets-and-tables.md)
and the [sets sample](samples/Ankus.Examples.Sets/SetFunctions.cs).

`PgHeapTuple` owns composite cells and immutable descriptor metadata, including
physical dropped slots, type modifiers, and domain identity. Named bindings,
anonymous records, nested arrays, and composite sets share the guarded native
conversion. See [composite values](docs/src/content/docs/composites.md) and the
[composite sample](samples/Ankus.Examples.Composites/CompositeFunctions.cs).

Use `[PgTrigger]` with `PgTriggerContext` for row and statement triggers.
Owned OLD/NEW tuples, trigger arguments, and transition-table SPI queries preserve
PostgreSQL's before/after/instead-of semantics. See [triggers](docs/src/content/docs/triggers.md)
and the [trigger sample](samples/Ankus.Examples.Triggers/PetTriggers.cs).

Use `[PgEventTrigger]` with `PgEventTriggerContext` for DDL, dropped-object,
table-rewrite and login callbacks. Metadata snapshots remain owned after callback
return. See [event triggers](docs/src/content/docs/event-triggers.md) and the
[event trigger sample](samples/Ankus.Examples.EventTriggers/DdlEvents.cs).

Use `[PgAggregate]` with typed static support methods for grouped, parallel,
moving-window, and ordered-set aggregation. `PgAggregateState<T>` owns managed
state through PostgreSQL's group and query lifetimes. See
[aggregates](docs/src/content/docs/aggregates.md) and the
[average sample](samples/Ankus.Examples.Aggregates/IntegerAverage.cs).

Use `Spi` inside an extension function to execute SQL in the calling backend:

```csharp
int answer = Spi.ExecuteScalar<int>("SELECT $1 + $2", SpiParameter.Create(40), SpiParameter.Create(2));
```

See [SPI queries](docs/src/content/docs/spi.md) for typed parameters, result rows, and error handling.
Use [logging and errors](docs/src/content/docs/logging.md) to send PostgreSQL notices and structured diagnostics.

Use `PgMemoryContext` and `PgAllocation` for PostgreSQL-owned native storage,
temporary current-context scopes, checked byte access, and deterministic cleanup.
Reset and transaction cleanup invalidate managed handles before they can access
freed memory. `RegisterResetCallback` roots one-shot managed cleanup until the
native context resets or is deleted; its disposable registration supports cancellation.
See [memory contexts](docs/src/content/docs/memory-contexts.md).

Use `[PgInitialize]` on one static method to initialize the extension when its
library loads in a backend. Initialization supports guarded database access,
owned error diagnostics, and retries after failure. See
[extension initialization](docs/src/content/docs/initialization.md) for loading,
transaction behavior, and the Native AOT restriction on shared preload.

Declare PostgreSQL settings with `[PgGucBool]`, `[PgGucInt]`, `[PgGucReal]`,
`[PgGucString]`, or `[PgGucEnum]` on static partial getters. PostgreSQL owns their
storage, startup source priority, permissions, SET/RESET behavior, and transaction restoration. See
[configuration settings](docs/src/content/docs/configuration.md) for typed hooks,
units, owned extra data, and native-only shared preload. An assembly `PgGucPrefix`
attribute checks unknown settings after registration. Hooks can use `PgLog` during reload,
rollback, and client reporting. Parallel workers restore typed values and regenerate
hook extra data in their own managed runtime.

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
