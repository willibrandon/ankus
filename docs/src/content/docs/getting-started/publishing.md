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

### Custom build configurations

Ankus defaults to `Release`. Use `--configuration` (or `-c`) to select `Debug`
or a custom MSBuild configuration:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Shipping'">
  <Optimize>true</Optimize>
  <AnkusExtensionVersion>0.2.0</AnkusExtensionVersion>
</PropertyGroup>
```

```console
ankus publish --configuration Shipping
ankus package --configuration Shipping --output dist
```

Custom configurations use your project's settings; they do not automatically
inherit `Release` settings. The default publication directory includes the
configuration name, keeping configurations separate. Use the same name with
`schema --skip-build` or `run --no-build` to reuse that publication.

`build`, `publish`, `install`, `package`, `schema`, `run`, and `connect` share this
option. Names may include spaces and Unicode; quote names containing spaces.
Each name must fit one directory component, without path separators, invalid
filename characters, or a trailing dot or space.

### Published files

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

An authored control `directory` setting changes the installed SQL location.
The publication still keeps its SQL and control files together under
`extension/`; installation and packaging apply the declared layout. See
[SQL directories](/reference/build-settings/#sql-directories).

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

## Upgrade an existing extension

Changing `AnkusExtensionVersion` creates a new installation script. Existing
databases also need an upgrade script that changes their current SQL objects.
Place it in your extension project's `sql/` directory:

```text
sql/
  hello--0.1.0--0.2.0.sql
```

For example, an extension that already owns a `hello_settings` table could add
a column with:

```sql
ALTER TABLE hello_settings ADD COLUMN enabled boolean NOT NULL DEFAULT true;
```

Set `AnkusExtensionVersion` to `0.2.0`, publish and install the new files, then
update each database:

```console
ankus install --pg 18
```

```sql
ALTER EXTENSION hello UPDATE TO '0.2.0';
```

`dotnet publish`, `ankus publish`, `ankus install`, and `ankus package` include
the selected upgrade scripts. PostgreSQL chooses the upgrade path, including
multiple intermediate scripts. Version names are literal PostgreSQL versions;
they need not follow semantic versioning. PostgreSQL rolls back SQL changes
when an upgrade fails.

Write scripts as UTF-8. Ankus replaces `@EXTENSION_VERSION@` with the configured
extension version and `@GIT_HASH@` with the project's current commit. Git is
required only when a script uses that token. PostgreSQL resolves
`MODULE_PATHNAME` through the new control file when it executes the script.

When native functions change, use a distinct `AssemblyName`, such as
`Hello.0.2.0`, and replace their SQL declarations in the upgrade script with
`CREATE OR REPLACE FUNCTION ... AS 'MODULE_PATHNAME', 'native_export'`.
Use the matching generated installation SQL for the actual declarations and
export names. Already connected backends can then load the new library while
retaining references to the old one. Ankus does not automatically version the
native filename or generate migration SQL.

Default discovery includes matching files directly under `sql/`. To remove a
default item or include a script from elsewhere:

```xml
<ItemGroup>
  <AnkusUpgradeScript Remove="sql/hello--0.1.0--0.2.0.sql" />
  <AnkusUpgradeScript Include="migrations/hello--0.1.0--0.2.0.sql" />
</ItemGroup>
```

Set `EnableDefaultAnkusUpgradeScripts` to `false` to select every script
explicitly. Duplicate output names and invalid upgrade filenames fail the
publish. A successful republish removes obsolete SQL files owned by the previous
publication and preserves unrelated files. Installation preserves previously
installed versions so databases can still use their existing libraries.

When an upgrade changes dependencies or installation permissions, add
`sql/hello--0.2.0.control`. PostgreSQL uses that version's overrides for both
installation and updates. Publishing, installing and packaging include these
files. See [version-specific control files](/reference/build-settings/#version-specific-control-files)
for inheritance, item selection and native-library settings.
