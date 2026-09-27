---
title: Build settings
description: Configure extension names, versions, PostgreSQL headers, and installation paths.
---

Set extension properties in your project file:

```xml
<PropertyGroup>
  <AnkusExtensionName>hello</AnkusExtensionName>
  <AnkusExtensionVersion>1.0.0</AnkusExtensionVersion>
</PropertyGroup>
```

| Property | Default | Purpose |
| --- | --- | --- |
| `AnkusExtensionName` | Lowercase assembly name, with periods replaced by underscores | Names the control and SQL files |
| `AnkusExtensionVersion` | Project `Version` | Selects the versioned SQL filename and control-file version |
| `AnkusPostgresMajor` | `18` | Selects the server headers used to compile the native wrapper |
| `AnkusPgConfigPath` | Registered or discovered installation | Selects an exact `pg_config`; the tool sets this automatically |
| `AnkusClangPath` | `clang` on Linux/macOS; `clang-cl.exe` on Windows | Selects LLVM Clang 20 or later for native declaration discovery |
| `AnkusLibClangPath` | Matching library from the selected Clang installation | Selects `libclang` when it is installed separately |
| `AnkusBindingCacheDirectory` | Ankus's directory in the current user's local application data | Selects shared generated sources, compiled companions and native objects |

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

## Project SDK

An extension uses `Ankus.Sdk` as its project SDK. The SDK sets `PublishAot` and
`IsAotCompatible` to `true`, `NativeLib` to `Shared`, and enables unsafe code for
generated native entry points. The output type is `Library`.

Pin the SDK version in the project (`Ankus.Sdk/1.0.0`) or centrally in `global.json`:

```json
{
  "msbuild-sdks": {
    "Ankus.Sdk": "1.0.0"
  }
}
```

With a central SDK version, use `<Project Sdk="Ankus.Sdk">`. Runtime and generator
package versions follow the SDK, including in projects using
`Directory.Packages.props`.

## Installation directories

| Artifact | Standard location |
| --- | --- |
| Native library | `pg_config --pkglibdir` |
| Control and versioned SQL files | `extension/` under `pg_config --sharedir` |

The control file resolves the library through `dynamic_library_path`, normally
`$libdir`. PostgreSQL 18's `extension_control_path` also allows a separate
directory for extension files.
