---
title: Build settings
description: Configure extension names, versions, PostgreSQL headers, and installation paths.
---

Set extension properties in your project file:

```xml
<PropertyGroup>
  <AnkusExtensionName>hello</AnkusExtensionName>
  <AnkusExtensionVersion>0.1.0</AnkusExtensionVersion>
</PropertyGroup>
```

| Property | Default | Purpose |
| --- | --- | --- |
| `AnkusExtensionName` | Lowercase assembly name, with periods replaced by underscores | Names the control and SQL files |
| `AnkusExtensionVersion` | Project `Version` | Selects the versioned SQL filename and control-file version |
| `AnkusExtensionControlFile` | None | Adds author settings from a PostgreSQL control file; see [control settings](#extension-control-settings) |
| `EnableDefaultAnkusUpgradeScripts` | Enabled | Includes `sql/<extension>--<old>--<new>.sql` upgrade files; set to `false` for explicit items only |
| `AnkusPostgresMajor` | `18` | Selects the server headers used to compile the native wrapper |
| `AnkusPgConfigPath` | Registered or discovered installation | Selects an exact `pg_config`; the tool sets this automatically |
| `AnkusClangPath` | `clang` on Linux/macOS; `clang-cl.exe` on Windows | Selects LLVM Clang 20 or later for native declaration discovery |
| `AnkusLibClangPath` | Matching library from the selected Clang installation | Selects `libclang` when it is installed separately |
| `AnkusBindingCacheDirectory` | Ankus's directory in the current user's local application data | Selects shared generated sources, compiled companions and native objects |

## Extension control settings

Select a control file relative to your project, or use an absolute path:

```xml
<PropertyGroup>
  <AnkusExtensionControlFile>extension.control</AnkusExtensionControlFile>
</PropertyGroup>
```

```ini
comment = 'Search helpers'
schema = 'search_helpers'
requires = 'pg_trgm'
superuser = true
trusted = false
```

Publishing merges these settings into the generated control file. Installation
and packaging preserve the result. `requires` names extensions that PostgreSQL
must install first; `CREATE EXTENSION ... CASCADE` can install available
dependencies. Use double quotes inside the value for mixed-case dependency names,
for example `requires = '"My Dependency", pg_trgm'`.

`schema` fixes the installation schema and makes the extension non-relocatable.
Without it, Ankus derives relocatability from generated SQL. You can explicitly
set `relocatable = false`, but cannot set it to `true` when generated SQL requires
a fixed schema.

`superuser` and `trusted` retain PostgreSQL's defaults when omitted: `true` and
`false`, respectively. `trusted = true` with `superuser = true` lets database
users with `CREATE` privilege install the extension; PostgreSQL executes its
script as the bootstrap superuser. With `superuser = false`, the caller must
have the privileges required by each SQL statement, including C-language
function creation. Extension authors are responsible for deciding whether their
extension is suitable for trusted installation.

PostgreSQL 16 and later also accept `no_relocate`, a list of dependencies whose
schemas must stay fixed. See PostgreSQL's [extension control parameters](https://www.postgresql.org/docs/18/extend-extensions.html#EXTEND-EXTENSIONS-FILES)
for the server's dependency and privilege semantics.

Control files use ASCII. Quotes, backslashes and comments follow PostgreSQL's
configuration syntax; repeated assignments use the last value. Use SQL
`COMMENT ON EXTENSION` for non-ASCII comments. Include directives are unsupported.

Ankus owns `default_version`, `module_pathname` and `encoding`; if present, those
values must match the generated publication. Set the version through
`AnkusExtensionVersion`, the library name through `AssemblyName`, and retain
`UTF8` for generated SQL. Alternate SQL `directory` settings and secondary
version-specific control files are not supported yet.

## PostgreSQL compilation symbols

The SDK also defines one C# compilation symbol for the selected major:
`ANKUS_PG13` through `ANKUS_PG19`. It uses the final `AnkusPostgresMajor`
setting, including project or command-line overrides, and preserves existing
framework and consumer symbols. Use these symbols when a native declaration
changes between server versions:

```csharp
#if ANKUS_PG13 || ANKUS_PG14 || ANKUS_PG15
// Use the declaration supplied by PostgreSQL 13–15 headers.
#else
// Use the declaration supplied by PostgreSQL 16 and later headers.
#endif
```

Build a separate native library against each target major's headers. A runtime
version check cannot make references to absent fields or different native
signatures compile; select that source at compile time.

Binding reuse checks file contents and current compiler inputs. Native layout
and declaration checks still run against the selected installation. Each project
receives its own companion files, so cleaning one project does not remove another
project's outputs. The companion restore follows the consuming project's NuGet
feeds, configuration, source mappings and package directory.
On Windows, choose a short cache path so MSVC can open its native objects.

When a selected-header function is supplied by your own native code, include its
object file or library through the ordinary `NativeLibrary` item:

```xml
<ItemGroup>
  <NativeLibrary Include="native/libprovider.a" />
</ItemGroup>
```

Use an object or archive compiled for the extension's target, such as a `.lib`
archive on Windows. PostgreSQL and the native linker resolve the function's
actual definition; its generated `NativeMethods` entry retains the native error
guard.

`NativeMethods` includes only declarations present in the selected headers.
For a function owned by your native library whose declaration is absent from
those headers, use an explicit .NET `LibraryImport` with its matching
`DirectPInvoke` item and `NativeLibrary` input. A direct import does not add an
Ankus error guard: any PostgreSQL operation that can raise `ERROR` must be
guarded entirely in native code before control returns to managed code.

## Project SDK

An extension uses `Ankus.Sdk` as its project SDK. The SDK sets `PublishAot` and
`IsAotCompatible` to `true`, `NativeLib` to `Shared`, and enables unsafe code for
generated native entry points. The output type is `Library`.

Use `AnkusUpgradeScript` items to select SQL upgrades. See
[upgrading an extension](/getting-started/publishing/#upgrade-an-existing-extension)
for file naming, tokens, and native library versioning.

Pin the SDK version in the project (`Ankus.Sdk/0.1.0`) or centrally in `global.json`:

```json
{
  "msbuild-sdks": {
    "Ankus.Sdk": "0.1.0"
  }
}
```

With a central SDK version, use `<Project Sdk="Ankus.Sdk">`. Runtime and generator
package versions follow the SDK, including in projects using
`Directory.Packages.props`.

The .NET SDK, project target framework and embedded Native AOT runtime are
separate selections. See [.NET support](/reference/dotnet-support/) before
retargeting or updating a deployed extension's runtime.
Use `TargetFramework=net10.0` and let Ankus select the matching compiler, patched
runtime and framework libraries. A conflicting `RuntimeFrameworkVersion`
override is rejected.

## Embedded installation metadata

Published libraries also contain their original installation SQL and publication
identity. Use [`ankus schema`](/getting-started/publishing/#inspect-installation-sql)
to extract the complete script. With the matching `Ankus.PgConfig` package, read this metadata without
loading the library or needing its adjacent JSON, control, or SQL files:

```csharp
using Ankus.PgConfig;

ExtensionSchema schema = ExtensionSchema.Read("publish/MyExtension.so");
Console.WriteLine(schema.Name);
Console.WriteLine(schema.Sql);
```

Use `.dll` on Windows or `.dylib` on macOS. The reader supports Linux x64,
Windows x64, and macOS x64/ARM64 libraries. For a universal macOS library,
pass `"osx-x64"` or `"osx-arm64"` as the second argument to select its slice.
For a thin library, that optional argument checks the native target instead.
`Artifacts` reports the original native filename, PostgreSQL major, runtime
identifier, and installation filenames even if the library was renamed.

`Sql` preserves the complete installation script, including PostgreSQL's
`MODULE_PATHNAME` substitution marker. It is intended for extension installation;
direct SQL replay must resolve that marker to the library's location first.
Missing, malformed, incompatible, or oversized metadata raises `FormatException`.
The native schema section is limited to 64 MiB. This metadata does not replace
the control and SQL files PostgreSQL needs for `CREATE EXTENSION`.

## Installation directories

| Artifact | Standard location |
| --- | --- |
| Native library | `pg_config --pkglibdir` |
| Control and versioned SQL files | `extension/` under `pg_config --sharedir` |

The control file resolves the library through `dynamic_library_path`, normally
`$libdir`. PostgreSQL 18's `extension_control_path` also allows a separate
directory for extension files.
