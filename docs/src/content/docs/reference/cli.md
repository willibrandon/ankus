---
title: Command-line tool
description: Create an extension, register PostgreSQL, and build, publish, or install with ankus.
---

## Create an extension

```console
ankus new Acme.Search
cd Acme.Search
dotnet test
```

`new` creates a solution with an extension under `src/` and an MSTest project
under `tests/`. It pins matching Ankus packages, enables Central Package Management,
and configures the .NET 10 test runner. The generated tests exercise both managed
methods and a Native AOT library loaded into PostgreSQL.

Project names use C# identifier segments such as `Acme.Search`. SQL extension names
default to snake case (`acme_search`); use `--extension-name` to choose one explicitly.
`--output` selects a new destination directory. Existing destinations are preserved.

Creation needs no PostgreSQL installation. Running the generated backend tests
requires PostgreSQL 18+ with development headers and the Native AOT toolchain.
See [testing an extension](/getting-started/testing/).

## Register PostgreSQL

Point Ankus at the `pg_config` executable from your PostgreSQL installation:

```console
ankus init --pg18 /path/to/postgresql/bin/pg_config
ankus info --pg 18
```

`init` validates the server version, executables, and headers, then records the
installation in `~/.ankus/config.json`. It preserves other registrations. Use
`--home` to keep a separate Ankus configuration directory.

## Build and publish

From an extension project directory, or a solution directory containing one Ankus SDK project:

```console
ankus build
ankus publish --output publish
```

Both commands compile the native library and generate the SQL installation files.
The default configuration is `Release`; use `--configuration Debug` to change it.
Builds target the host operating system and architecture.

Use `--project` to select a project elsewhere. `--pg` selects a registered
PostgreSQL major and defaults to `18`. To use a different installation for one
invocation, pass `--pg-config /path/to/pg_config` as well.

## Install

Build and install into the selected PostgreSQL installation:

```console
ankus install --pg 18
```

Or install a previous publish:

```console
ankus install --from publish --pg 18
```

The library goes to `pg_config --pkglibdir`. Control and versioned SQL files go
to the `extension` subdirectory of `pg_config --sharedir`. The command requires
write access to those directories.

To stage those files under a separate root while preserving the installation
paths, add `--destdir staging`.

Connect to your database and run `CREATE EXTENSION` to make its functions
available. `install` copies files; it does not modify databases.

Use `ankus --help` or `ankus <command> --help` for the available options.
