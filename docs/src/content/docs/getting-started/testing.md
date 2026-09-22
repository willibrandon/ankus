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

Backend testing requires PostgreSQL 18+ with server headers and the
[Native AOT toolchain](https://learn.microsoft.com/dotnet/core/deploying/native-aot/).
The fixture discovers PostgreSQL 18 by default. Register a nonstandard
installation with `ankus init --pg18 /path/to/pg_config`. Missing prerequisites
fail initialization; tests are never silently skipped.

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
cluster startup, then executes `CREATE EXTENSION`. It uses per-cluster search
paths, leaving the shared PostgreSQL installation untouched. Supply an explicit
`PostgresInstallation` to test a particular installation.

`RunInTransactionAsync` opens a connection and rolls back when the callback
finishes, including after assertion failures. Parallel tests use independent
connections. The generated fixture shares one cluster for its test class and
disposes it after all tests finish.

## Failures and cleanup

Unhandled managed exceptions become PostgreSQL errors. The generated recovery
test uses a savepoint to recover from an expected error and verifies the next
query on the same connection.

Build logs and PostgreSQL logs remain under the extension project's
`bin/ankus-test-logs/`. The cluster and temporary published library are removed
on disposal. A failed build or extension load fails initialization and cleans up
the resources it created.

The automatic publication fixture requires PostgreSQL 18's `extension_control_path`.
For earlier versions, `PostgresTestCluster` accepts an explicitly prepared
installation and cluster settings.
