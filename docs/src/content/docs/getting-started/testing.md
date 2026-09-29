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
