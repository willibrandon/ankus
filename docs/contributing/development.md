# Development and testing

Install the stable .NET SDK selected by `global.json`, the platform's
[Native AOT toolchain](https://learn.microsoft.com/dotnet/core/deploying/native-aot/),
and PostgreSQL 18 with server development headers. Windows also needs the server
import library.

## PostgreSQL discovery

Ankus checks `~/.ankus/config.json`, installations under `~/.ankus/postgres/`,
then `PATH` and conventional installation directories. For a nonstandard path:

```json
{
  "pg18": "/path/to/postgresql/bin/pg_config"
}
```

## Tests

From the repository root:

```console
dotnet test
```

The suite uses MSTest with Microsoft.Testing.Platform. The integration fixture
publishes the extensions, starts an isolated cluster, installs the extensions,
and invokes their functions through SQL. Test transactions roll back before their
connections close. The cluster shuts down after the run.

To run a subset:

```console
dotnet test --project tests/Ankus.IntegrationTests/Ankus.IntegrationTests.csproj --filter 'FullyQualifiedName~SpiSessionTests'
```

Filtering still runs Native AOT publishing and cluster startup. Server logs are
retained in `artifacts/test-logs`. A failure report includes the failing test's
PostgreSQL session log.

`Ankus.TestExtension` contains attributed backend probes. `Ankus.IntegrationTests`
invokes them and checks their SQL results. `Ankus.Testing` owns cluster startup,
transactions, diagnostics, and shutdown. FATAL and PANIC tests use dedicated
clusters.

## Publish the sample

On Linux x64:

```console
dotnet publish samples/Ankus.Examples.Hello -c Release -r linux-x64 --self-contained -o artifacts/hello
```

The sample imports `src/Ankus.Sdk/Ankus.Sdk.targets`, which supplies the runtime,
generator, and build-tool references. Shared .NET settings come from
`Directory.Build.props`.

## Build the tool

Pack and install from the local feed:

```console
dotnet pack src/Ankus.Tool -c Release -o artifacts/packages
dotnet tool install Ankus.Tool --tool-path artifacts/tools --add-source artifacts/packages --version 1.0.0
```

Invoke `artifacts/tools/ankus` (`ankus.exe` on Windows), or put that directory on
`PATH`. For development without packing:

```console
dotnet run --project src/Ankus.Tool -- --help
```

The integration suite packs a uniquely versioned tool and installs it into a
temporary directory. It verifies registration, publishing, installation, and SQL
execution using that installed command.
