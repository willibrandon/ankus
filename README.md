# Ankus

Ankus is an in-progress port of [pgrx](https://github.com/pgcentralfoundation/pgrx)
to .NET Native AOT. Write ordinary C# functions and publish them as a native
PostgreSQL extension library.

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
PostgreSQL cluster, executes SQL tests, rolls back test transactions, and shuts
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

## Current scope

The working subset supports static methods with `int` arguments and an `int`
return value, strict SQL NULL handling, and managed exceptions reported as
PostgreSQL errors. The exception dispatcher returns completely to native code
before PostgreSQL raises ERROR; PostgreSQL must never longjmp across managed frames.

The build integration is currently a repository-local MSBuild import. It publishes
generated SQL alongside the native library. NuGet distribution, extension control
files, additional PostgreSQL types and APIs, and generated backend test methods
are still being developed.

Windows, Linux, and macOS are required targets. Validation so far is on Linux x64
with PostgreSQL 18.6. The full target matrix is PostgreSQL 13–18 plus 19 beta.

See [PROGRESS.md](PROGRESS.md) for verified milestones, the feature map, and the
remaining work.
