---
title: Publish and install
description: Publish an extension library and load it into PostgreSQL.
---

Publish your configured extension project on the machine that matches the
target server's operating system and architecture.

## Prerequisites

- .NET 10 SDK.
- The [.NET Native AOT toolchain](https://learn.microsoft.com/dotnet/core/deploying/native-aot/).
- PostgreSQL 18 with `pg_config`, server executables, and development headers.
  Windows also needs the PostgreSQL server import library.

Register the installation with the Ankus tool:

```console
ankus init --pg18 /path/to/postgresql/bin/pg_config
```

## Publish

From your extension project directory:

```console
ankus publish --output publish
```

Ankus uses .NET Native AOT to build for the current platform. It passes the
registered PostgreSQL installation to the build so the wrapper uses its headers.

For a project named `Hello`, with extension name `hello` and version `1.0.0`, the
output includes:

```text
Hello.so
Hello.sql
ankus.extension.json
extension/
  hello.control
  hello--1.0.0.sql
```

The library suffix is `.dll` on Windows and `.dylib` on macOS.

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
