---
title: Test an extension
description: Run managed and PostgreSQL tests with ordinary dotnet test.
---

From a solution created by `ankus new`:

```console
dotnet test
```

The MSTest project contains two kinds of tests. Managed tests call ordinary C#
methods directly. Backend tests publish the extension with Native AOT, start an
isolated PostgreSQL cluster, and exercise its generated SQL functions.

Backend testing requires PostgreSQL with server headers and the
[Native AOT toolchain](https://learn.microsoft.com/dotnet/core/deploying/native-aot/).
Extension builds also require LLVM Clang 20 or later with matching `libclang`;
see [publishing prerequisites](/getting-started/publishing/#prerequisites).
The fixture discovers PostgreSQL 18 by default. Register a nonstandard
installation with `ankus init --pg18 /path/to/pg_config`. Missing prerequisites
fail initialization; tests are never silently skipped.

For SQL files with expected text output, use `ankus regress --pg 18`.
The [SQL regression guide](/reference/cli/#run-sql-regression-suites) covers
PostgreSQL's native comparator, setup scripts, and reviewing changed expectations.
On supported Unix systems, `ankus regress --pg 18 --valgrind` runs the server
under Memcheck. See [native memory diagnostics](/reference/cli/#inspect-native-memory-with-valgrind)
for prerequisites and reading the server log.

## Declare tests inside the extension

Use `[PgTest]` on a synchronous static `void` method in a public or internal
partial class:

```csharp
using Ankus;

public static partial class BackendChecks
{
    [PgTest]
    public static void AdditionInsidePostgres()
    {
        if (Spi.ExecuteScalar<int>("SELECT 19 + 23") != 42)
        {
            throw new InvalidOperationException("Unexpected addition result.");
        }
    }

    [PgTest(ExpectedError = "expected failure")]
    public static void ExpectedFailure()
        => throw new InvalidOperationException("expected failure");
}
```

The method body executes inside the PostgreSQL backend. You can use SPI,
memory contexts and other backend APIs there. Methods accept no SQL arguments;
injected `PgFunctionContext` and `PgMemoryContext` parameters remain available.
Methods and containing classes must be accessible to generated code and cannot
be generic. Every containing class must be partial. Invalid declarations produce
`ANKUS023` rather than silently disappearing from discovery.

`BackendChecks.PostgresTests.Cases` contains immutable `PgTestCase` values.
Reading this catalog does not run the enclosing class's static constructor or
its test methods. Reserve the nested name `PostgresTests` for generated code.

The scaffold connects these cases to MSTest's ordinary data-driven tests:

```csharp
public static IEnumerable<TestDataRow<PgTestCase>> NativeCases
    => BackendChecks.PostgresTests.Cases.Select(test => new TestDataRow<PgTestCase>(test)
    {
        DisplayName = test.Name,
        IgnoreMessage = test.IgnoreReason,
    });

[TestMethod]
[DynamicData(nameof(NativeCases))]
public Task DeclaredTestsExecuteInPostgres(PgTestCase test)
    => extension.RunTestAsync(test, context.CancellationToken);
```

Start `extension` once during test-class initialization with test publication
enabled:

```csharp
extension = await PostgresExtensionTest.StartAsync(new PostgresExtensionTestOptions
{
    ProjectPath = projectPath,
    IncludeTests = true,
    PostgreSqlConfiguration = ["my_extension.mode = 'test'"],
}, context.CancellationToken);
```

Dispose it during test-class cleanup. The options also select `Installation`,
`Configuration` (default `Release`), `SharedPreload` and `Port`.
The fixture uses the selected installation's headers and server together.

Each case runs in its own transaction, which rolls back after success, failure
or cancellation. `ExpectedError` matches the server's primary message exactly;
a different error or unexpected success fails the test. Failure diagnostics
include the original managed name, database exception and backend session log.
`[PgTest(IgnoreReason = "reason")]` supplies an explicit framework skip reason.
Directly invoking an ignored case fails; the host must report the skip.

Names in reports retain their C# identity. Generated SQL names fit PostgreSQL's
identifier limit, even for long C# method names. An explicit `[PgSchema]` selects
the function schema; otherwise the fixture uses the installed extension schema,
including a schema supplied by its control file.

Normal `dotnet publish` excludes test functions. The fixture explicitly sets
`AnkusIncludeTests=true` only when `IncludeTests` is enabled. Other test hosts
can consume the same framework-neutral catalog and fixture. Backend benchmarks,
additional framework templates and automatic multi-version test runs remain
unimplemented.

## Use the fixture

[`PostgresExtensionTest`](/api/ankus.testing.postgresextensiontest/) is included
in the `Ankus.Testing` package:

```csharp
await using PostgresExtensionTest extension = await PostgresExtensionTest.StartAsync(
    projectPath, cancellationToken: context.CancellationToken);

await extension.Cluster.RunInTransactionAsync("addition", async (connection, transaction, token) =>
{
    await using var command = new NpgsqlCommand("SELECT public.add(19, 23)", connection, transaction);
    Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
}, context.CancellationToken);
```

The fixture passes the same PostgreSQL installation to native compilation and
cluster startup, then executes `CREATE EXTENSION`. It leaves the selected
installation untouched. Supply an explicit `PostgresInstallation` to test a
particular version.

If your control file declares a custom SQL `directory`, the fixture remaps it
inside its temporary storage. It stages the declared installation SQL, upgrades
and secondary controls without changing your author file or writing to its
original destination, including absolute paths.

For workers and shared-memory extensions, enable shared preload in the fixture:

```csharp
await using PostgresExtensionTest extension = await PostgresExtensionTest.StartAsync(
    projectPath, sharedPreload: true, cancellationToken: context.CancellationToken);
```

The fixture loads the published library before starting backends, then creates
the extension's SQL objects. `ankus new MyWorker --background-worker` generates
this setup and a test that observes a worker running in a separate process.

`RunInTransactionAsync` opens a connection and rolls back when the callback
finishes, including after assertion failures. Parallel tests use independent
connections. The generated fixture shares one cluster for its test class and
disposes it after all tests finish.

## Choose a test port

Fixtures choose an available port automatically so independent test runs can
start in parallel. For a predictable port while debugging, save a test base
with `ankus init --pg18 /path/to/pg_config --base-testing-port 33000`, then
request the resolved port explicitly:

```csharp
var registry = new PostgresRegistry();
PostgresInstallation installation = await registry.GetAsync(18, context.CancellationToken);
await using PostgresExtensionTest extension = await PostgresExtensionTest.StartAsync(
    projectPath, sharedPreload: false, port: registry.GetTestPort(18),
    installation: installation, cancellationToken: context.CancellationToken);
```

This starts the fixture on `33018`. You can also pass a literal port from `1`
through `65535`, or set `PostgresTestClusterOptions.Port` when creating a cluster
directly. An occupied explicit port fails startup; it never switches to another
port. Use different ports for concurrent fixtures, or leave port selection automatic.

## Failures and cleanup

Unhandled managed exceptions become PostgreSQL errors. The generated recovery
test uses a savepoint to recover from an expected error and verifies the next
query on the same connection.

Build logs and PostgreSQL logs remain under the extension project's
`bin/ankus-test-logs/`. The cluster and temporary published library are removed
on disposal. A failed build or extension load fails initialization and cleans up
the resources it created.

If another process takes an automatically selected TCP port during startup, the fixture
retries with a new isolated cluster and port. It allows up to three attempts
within the original startup timeout, retaining each failed attempt's log and
removing its data and sockets. Configuration errors and other startup failures
are reported immediately; cancellation stops further attempts.

PostgreSQL 18 uses its per-cluster extension search path. For earlier versions,
the fixture makes and removes an isolated copy of the installation.
