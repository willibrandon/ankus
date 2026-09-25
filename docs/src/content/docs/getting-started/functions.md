---
title: Write a function
description: Declare C# methods, map PostgreSQL types, and handle SQL NULL.
---

## Create a project

Install `Ankus.Tool` from your configured NuGet feed, then create a solution:

```console
dotnet tool install --global Ankus.Tool --version 1.0.0
ankus new Hello
cd Hello
```

Use the Ankus version available from your feed. Packages are currently built
locally; a public release is pending. The solution includes an extension project,
managed tests, and tests that load the native extension into PostgreSQL.

The generated `src/Hello/Hello.csproj` uses `Ankus.Sdk`:

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

The SDK enables Native AOT and includes matching runtime and source-generator
packages. It works with ordinary NuGet configuration and Central Package Management.

## Add a function

Edit `src/Hello/Functions.cs` and mark a synchronous static method with <code>[<span class="csharp-type">PgFunction</span>]</code>:

```csharp
using Ankus;

public static class Functions
{
    [PgFunction]
    public static string Greet(string name) => $"Hello, {name}!";
}
```

The method is exposed as `greet(text)`. Names use snake case by default.
Ankus generates the native entry point and SQL declaration during publishing.

C# parameter names also use snake case in SQL, and optional arguments become
SQL defaults. See [function declarations](/function-declarations/) for named
calls, schema placement, and PostgreSQL execution options.

See [enumerated types](/enums/) for custom C# enums, labels and type dependencies.

## Types

| C# | PostgreSQL |
| --- | --- |
| `bool` | `boolean` |
| `sbyte` | `"char"` (the internal signed byte type) |
| `short`, `int`, `long` | `smallint`, `integer`, `bigint` |
| `uint` | `oid` |
| `PgItemPointer` | `tid` ([tuple location](/item-pointers/)) |
| `float`, `double` | `real`, `double precision` |
| `string` | `text` |
| `byte[]` | `bytea` |
| `Guid` | `uuid` |
| `PgInet`, `IPAddress` | `inet` |
| `PgCidr`, `IPNetwork` | `cidr` |
| `PgPoint`, `PgLine`, `PgLineSegment` | `point`, `line`, `lseg` |
| `PgBox`, `PgCircle` | `box`, `circle` |
| `PgPath`, `PgPolygon` | `path`, `polygon` |
| `PgRange<T>` | Built-in range selected by the bound type |
| `PgJson`, `PgJsonb` | `json`, `jsonb` |
| `decimal`, `PgNumeric` | `numeric` |
| `DateOnly`, `PgDate` | `date` |
| `TimeOnly`, `PgTime` | `time` |
| `PgTimeTz` | `timetz` |
| `DateTime`, `PgTimestamp` | `timestamp` |
| `DateTimeOffset`, `PgTimestampTz` | `timestamptz` |
| `TimeSpan`, `PgInterval` | `interval` |
| `T[]`, `PgArray<T>` | Array of the corresponding scalar SQL type |
| `PgAnyElement`, `PgAnyArray` | `anyelement`, `anyarray` |
| `[PgEnum]` C# enums | Generated PostgreSQL enum types |
| `[PgDatumType]` classes, structs and enums | The converter's declared existing SQL type, also usable as an array element |
| `void` result | `void` |

Text and binary inputs are managed copies. They remain valid after PostgreSQL
releases the original storage.

See [date and time values](/date-and-time/) for precision, time zones, and
full-range PostgreSQL values.

See [network values](/network/) for IPv4/IPv6 prefixes and checked `System.Net`
conversions. `IPAddress` requires a full-width host prefix.

See [geometric values](/geometry/) for coordinates, owned vertex collections,
and PostgreSQL input validation.

See [ranges](/ranges/) for empty values, bound inclusion, canonicalization, and
checked .NET bound types.

See [arrays](/arrays/) for dimensions, lower bounds, nullable elements, and
variadic functions. `byte[]` is scalar `bytea`; `byte[][]` is `bytea[]`.

Use [reusable datum mappings](/raw-values/#reusable-scalar-mappings) for an existing
SQL representation. Function inputs need a reader; outputs need a writer. Array
signatures use the same element converter and type provider.

## SQL NULL

Use nullable C# types to receive SQL NULL:

```csharp
[PgFunction]
public static string GreetOrDefault(string? name) => $"Hello, {name ?? "world"}!";
```

If every parameter is required, the generated SQL function is `STRICT`:
PostgreSQL returns NULL without calling the method when any argument is NULL.

For mixed signatures, a NULL required argument still returns NULL without
entering the method. Nullable arguments reach your code. A null result becomes
SQL NULL.

## Errors

An unhandled managed exception becomes PostgreSQL ERROR. The managed stack
unwinds first, so `finally` blocks and `using` scopes run normally.

Use [`PgException`](/logging/#errors) when the error needs a particular SQLSTATE,
detail, or hint.
