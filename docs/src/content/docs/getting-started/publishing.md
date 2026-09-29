---
title: Publish and install
description: Publish an extension library and load it into PostgreSQL.
---

Publish your configured extension project on the machine that matches the
target server's operating system and architecture.

## Prerequisites

- .NET 10 SDK. See [.NET support](/reference/dotnet-support/) for supported
  targets and runtime servicing.
- The [.NET Native AOT toolchain](https://learn.microsoft.com/dotnet/core/deploying/native-aot/).
- LLVM Clang 20 or later and its matching `libclang` library. Make `clang`
  available on Linux/macOS or `clang-cl.exe` on Windows. Ankus uses it to read
  native declarations from your PostgreSQL headers.
- PostgreSQL 18 with `pg_config`, server executables, and development headers.
  Windows also needs the PostgreSQL server import library.

Register the installation with the Ankus tool:

```console
dotnet tool install --global Ankus.Tool --version 0.1.0
ankus init --pg18 /path/to/postgresql/bin/pg_config
```

Use the Ankus version available from your configured feed.

## Publish

From your extension project or generated solution directory:

```console
ankus publish --output publish
```

Ankus uses .NET Native AOT to build for the current platform. It passes the
registered PostgreSQL installation to the build so the wrapper uses its headers.

You can also use `dotnet publish` directly. For a generated `Hello` solution on Linux x64:

```console
dotnet publish src/Hello/Hello.csproj -c Release -r linux-x64 -o publish -p:AnkusPgConfigPath=/path/to/pg_config
```

The SDK supplies Native AOT settings and native build integration. No Ankus source
checkout is needed, and the server does not need an installed .NET runtime.

For a project named `Hello`, with extension name `hello` and version `0.1.0`, the
output includes:

```text
Hello.so
Hello.sql
ankus.extension.json
extension/
  hello.control
  hello--0.1.0.sql
```

The library suffix is `.dll` on Windows and `.dylib` on macOS.

## Inspect installation SQL

Build your extension and write its installation SQL to a file:

```console
ankus schema --output schema.sql
```

Without `--output`, SQL goes to stdout and build diagnostics go to stderr.
Use the same `--project`, `--pg`, `--pg-config`, and `--configuration` options
as `ankus publish`. `ankus schema --skip-build` reads the project's existing
default publication under `bin/ankus/`, selected by PostgreSQL major, host
runtime, and configuration. It does not require a registered PostgreSQL
installation; use `--pg` to select the existing publication's major.

To read a library published to another directory, use its path directly:

```console
ankus schema --from publish/Hello.so --output schema.sql
```

This reads the library's embedded metadata without loading its native code,
building the project, or requiring adjacent SQL, control, or JSON files. Use
`.dll` on Windows or `.dylib` on macOS. You can inspect another supported
platform's library; `--runtime osx-arm64` or `--runtime osx-x64` selects a slice
from a universal macOS library. For a thin library, `--runtime` checks its target.
Do not combine `--from` with project, configuration, PostgreSQL, or build options.

The output is the complete installation script, including custom SQL and
declarations in dependency order. It retains PostgreSQL's `MODULE_PATHNAME`
marker, which `CREATE EXTENSION` resolves through the control file. Extracting
SQL does not install the extension or replace those installation files. Named-item
selection, extension-attachment SQL, and Graphviz output are not yet available.

## Load into PostgreSQL

Install the published files:

```console
ankus install --from publish
```

Then connect to your database:

```sql
CREATE EXTENSION hello;
SELECT public.add(40, 2);       -- 42
SELECT public.greet('world');   -- Hello, world!
```

See [build settings](/reference/build-settings/) to change the extension name or
version.
