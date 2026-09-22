# Ankus

Ankus is an in-progress, full port of [pgrx](https://github.com/pgcentralfoundation/pgrx)
to .NET Native AOT. All pgrx functionality is required, including custom scans,
nodes, runtime APIs, source generation, tooling, examples, and backend testing.
Write ordinary C# functions and publish them as a native PostgreSQL extension library.

```csharp
[PgFunction]
public static int Add(int left, int right) => checked(left + right);
```

The [minimal sample](samples/Ankus.Examples.Hello/Hello.cs) contains only the user
function. Ankus generates PostgreSQL module magic, exports, argument conversion,
SQL declarations, and the managed-to-native error boundary.

## Develop and test

Prerequisites:

- A stable .NET SDK compatible with `global.json` (currently .NET 10.0.400).
- The platform's [.NET Native AOT toolchain](https://learn.microsoft.com/dotnet/core/deploying/native-aot/).
- PostgreSQL 18, including `pg_config`, server executables, and server development
  headers. On Windows, the server import library is also needed.

From the repository root, run:

```console
dotnet test
```

All tests are ordinary MSTest cases, discovered through Microsoft.Testing.Platform.
The integration fixture publishes the native sample, creates an isolated local
PostgreSQL cluster, installs with `CREATE EXTENSION`, executes SQL tests, rolls back test transactions, and shuts
down the cluster. Logs remain in `artifacts/test-logs`. Integration setup failures
are reported as failures.

No environment variables are required. PostgreSQL discovery checks:

1. `~/.ankus/config.json` (the current user's home directory on every platform).
2. `~/.ankus/postgres/*/bin/pg_config` (`pg_config.exe` on Windows).
3. `PATH` and conventional PostgreSQL installation directories for the host OS.

For a nonstandard installation, an optional configuration entry supplies its path:

```json
{
  "pg18": "/path/to/postgresql/bin/pg_config"
}
```

## Implementation status

The implementation currently supports static methods with `int` arguments and an `int`
return value, strict SQL NULL handling, and managed exceptions reported as
PostgreSQL errors. The exception dispatcher returns completely to native code
before PostgreSQL raises ERROR; PostgreSQL must never longjmp across managed frames.

The build integration is currently a repository-local MSBuild import. Publishing
the sample produces a native library and these PostgreSQL installation files:

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
```

Tests configure PG18's extension and library search paths on their isolated
cluster to load the published files directly. They verify extension ownership,
schema relocation, removal, and reinstallation.

NuGet distribution, installation/package commands, extension upgrades, additional
PostgreSQL types and APIs, and generated backend test methods are still being developed.

Windows, Linux, and macOS are required targets. Validation so far is on Linux x64
with PostgreSQL 18.6. The full target matrix is PostgreSQL 13–18 plus 19 beta.

See [PROGRESS.md](PROGRESS.md) for verified milestones, the feature map, and the
remaining work.
