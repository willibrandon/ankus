# Development and testing

Install the stable .NET SDK selected by `global.json`, the platform's
[Native AOT toolchain](https://learn.microsoft.com/dotnet/core/deploying/native-aot/),
and PostgreSQL 18 with server development headers. Windows also needs the server
import library. The repository's collation tests require PostgreSQL built with
ICU support.

Build-tool tests compile standalone C layout probes. Make `cc` on Linux/macOS or
`clang-cl.exe` on Windows available on `PATH`. Windows also uses `cl.exe` from
Visual Studio 2022 17.9 or later for the selected PostgreSQL header probe. Its
[`__typeof__` support](https://learn.microsoft.com/cpp/c-language/typeof-c)
lets the probe measure anonymous native values with a type operand to MSVC's
alignment operator. A Visual Studio Developer Command Prompt supplies headers
and libraries. Install the Visual Studio Clang tools or LLVM for the independent
standard C fixture.

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

Every repository project inherits `MSTestAnalysisMode=All` and
`TreatWarningsAsErrors=true` from `Directory.Build.props`. Fix analyzer findings
without suppressing diagnostics or reducing the enforced analysis mode.

To run a subset:

```console
dotnet test --project tests/Ankus.IntegrationTests/Ankus.IntegrationTests.csproj --filter 'FullyQualifiedName~SpiSessionTests'
```

Filtering still runs Native AOT publishing and cluster startup. Server logs are
retained in `artifacts/test-logs`. A failure report includes the failing test's
PostgreSQL session log.

If another process takes the reserved TCP port before PostgreSQL binds it, the
cluster harness retries with fresh data, socket and log paths and a new port.
It allows at most three attempts within one `StartupTimeout`, cleans each failed
attempt's data and sockets, and retains its server log. Other startup failures
are reported immediately; cancellation stops further attempts.

For a separate PostgreSQL 18 build, set `ANKUS_TEST_PG_CONFIG` to its `pg_config`
path for the integration test process. The fixture uses that installation for
both native publishing and cluster startup, preserving the user's registered
installation. The allocator tests compile a test-only native module against the
same headers. Run the Bump cases on both assertion-enabled and ordinary server
builds: ordinary Bump allocations have no chunk header.

`tests/Ankus.TestExtension` contains attributed backend probes. `tests/Ankus.IntegrationTests`
invokes them and checks their SQL results. The public `Ankus.Testing` package lives
in `src/Ankus.Testing` and owns cluster startup, transactions, diagnostics, and
shutdown. Repository-specific fixtures stay under `tests/`. FATAL and PANIC tests
use dedicated clusters.

### Large memory allocation tests

The allocation lifecycle tests always run five small cases for ordinary,
no-OOM, over-aligned, aligned no-OOM, and zeroed allocation. To also execute the
five cases above PostgreSQL's ordinary allocation limit, use a dedicated run:

```sh
timeout --signal=TERM --kill-after=20s 6m env \
  ANKUS_TEST_PG_CONFIG=/path/to/release/postgresql/bin/pg_config \
  ANKUS_TEST_HUGE_ALLOCATIONS=1 \
  dotnet test --project tests/Ankus.IntegrationTests -c Release \
  --filter 'FullyQualifiedName~AllocationLifecycleTests'
```

The additional cases allocate 1 GiB + 17 bytes, grow by 1 MiB while still above
the ordinary limit, shrink to 128 bytes, and free the chunk before deleting its
context. They check exact endpoint values, checked views, retained policy and
alignment, and independent native/catalog accounting. No-OOM and over-aligned
resize copy the retained payload; zeroed allocation writes the initial payload.

This resource run currently requires 64-bit Linux with cgroup v2, readable
process/resource accounting, `prlimit`, and `timeout`. Keep an external deadline
for the run: SQL cancellation alone cannot bound a native allocation or copy.
Run it separately from other builds and tests. It admits each large case only
with more than 5 GiB of available host
memory and the same headroom under any finite cgroup memory limits. Each fresh,
warmed backend receives an additional 3 GiB address-space allowance. This bounds
new address-space allocation; it does not reserve physical memory or constrain
use of pages in mappings the backend already holds. Admission failures fail the
requested test instead of counting it as passed or skipped.

Use a matching release server/header installation for the economical run.
PostgreSQL debug allocation randomization and freed-memory clobbering can touch
the entire buffer. The test retains server, memory high-water, and cgroup
observations in `artifacts/test-logs/huge-allocations/`. An ordinary `dotnet test`
run without the opt-in variable supplies small-case evidence only; report the
large resource run separately.

## Publish the sample

On Linux x64:

```console
dotnet publish samples/Ankus.Examples.Hello -c Release -r linux-x64 --self-contained -o artifacts/hello
```

The sample imports `src/Ankus.Sdk/Ankus.Sdk.targets`, which supplies the runtime,
generator, and build-tool references. Shared .NET settings come from
`Directory.Build.props`.

Both `dotnet build` and `dotnet publish` require the selected PostgreSQL server
headers and a C compiler. Before compiling an extension, the SDK measures the
native layouts and builds a generated companion assembly under the extension's
intermediate directory. `AnkusPostgresMajor` selects the major (18 by default),
and `AnkusPgConfigPath` selects an explicit installation. The same properties
must apply to referenced extension projects. A requested runtime identifier
that differs from the compiled probe's target fails explicitly.

## Build the packages

Pack the solution into a local feed:

```console
dotnet pack -c Release -o artifacts/packages
```

This produces `Ankus.Sdk`, `Ankus.Runtime`, `Ankus.Generators`, `Ankus.PgConfig`,
`Ankus.Testing`, and `Ankus.Tool`. The SDK includes its native build helper and
matching runtime/generator versions. It uses NuGet's MSBuild SDK resolver.

Add the absolute feed path to a consumer's `NuGet.Config` and use
`<Project Sdk="Ankus.Sdk/1.0.0">`. Both the SDK resolver and package restore read
that configuration. The consumer needs its own `TargetFramework`, nullable, and
implicit-using settings; it does not import repository build files.

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

`ToolCommandTests` packs all six packages with a unique version, then creates
consumer projects outside the repository with an empty NuGet package directory.
It verifies installed-tool publishing and staging, direct `dotnet publish` with
Central Package Management, and real SQL execution. A separate MSTest consumer
references only the packed `Ankus.Testing`, runs ordinary `dotnet test`, and checks
native error recovery as well as successful calls.

`ankus new` bundles source templates under `src/Ankus.Tool/Templates/Extension`.
It creates a version-matched solution with CPM and native MTP discovery. The
package tests run the generated solution's `dotnet test` outside the checkout,
then change an extension function and verify its backend test fails. The same
tests check keyword namespaces, path handling, and preservation of existing files.

## C# type style

Ankus follows the `dotnet/runtime` and `dotnet/msbuild` convention: use explicit
local types when the right-hand side does not name the type. Built-in values and
non-apparent results are enforced as IDE0008 errors through `.editorconfig` and
`EnforceCodeStyleInBuild`. A constructor or explicit cast that names its type
permits either `var` or an explicit declaration; IDE0007 does not force `var`.
Roslyn's own repository generally prefers `var`, so its type-style rules are not
the convention used here.

Use primary constructors wherever supported by IDE0290. The repository enforces
`csharp_style_prefer_primary_constructors` and IDE0290 as errors.
Constructor validation, visibility and native layouts must remain
unchanged when applying the conversion.

Remove redundant casts. IDE0004 is enforced as an error throughout the repository.
Use collection expressions where IDE0300 applies; it is also enforced as an error.
Remove unnecessary `unsafe` modifiers; IDE0380 is enforced as an error.

Leave a blank line after a closing block brace before the next statement or
declaration. IDE2003 enforces statement separation during repository builds.
Connected `else`, `catch`, and `finally` clauses and adjacent enclosing closing
braces stay together. Do not insert a blank line immediately after an opening brace.

Fix warnings at their source. Do not add warning pragmas, suppression attributes,
`NoWarn`, or lower an enforced diagnostic's severity.

These conventions apply to Ankus development. Consumer projects created by
`ankus new` choose their own style; the scaffold does not include an `.editorconfig`
or enable code-style enforcement in their builds.
