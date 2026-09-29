---
title: Command-line tool
description: Create an extension, install or register PostgreSQL, and build, publish, or install with ankus.
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

## Install or register PostgreSQL

Download a local development installation, including server headers and contrib modules:

```console
ankus init --pg18 download
ankus info --pg 18
```

The tool selects the latest published release of the requested major. You can
request PostgreSQL 13 through 19, combine selections, and mix downloads with
existing `pg_config` paths:

```console
ankus init --pg13 download --pg18 download --pg19 /path/to/pg19/bin/pg_config
```

Linux and macOS build PostgreSQL's upstream sources with debug information and
assertions. Source downloads are checked against upstream SHA-256 checksums.
Windows x64 uses EDB's binary archives over HTTPS. A prerelease can be downloaded
when its platform distribution is available; otherwise register an existing build.
When a major has no stable release yet, source downloads select its newest beta
or release candidate.

Source builds need a C compiler, GNU Make, Bison, Flex, pkg-config, and development
files for Readline, zlib, and ICU. On macOS, install the Xcode command-line tools
and the corresponding Homebrew dependencies. Ankus discovers Homebrew's ICU
pkg-config directory automatically. Set `CC`, `CPPFLAGS`, `LDFLAGS`, or
`PKG_CONFIG_PATH` when using another toolchain or dependency location.

Use `--jobs 8` to limit parallel compilation. Repeat
`--configure-flag=--with-icu` for additional Unix configure feature options.
`--valgrind` enables PostgreSQL's memory-context instrumentation and requires
Valgrind headers. These source options do not apply to Windows binary archives.

Installations live under `~/.ankus/postgres/`. Repeating the same request reuses
a validated installation with matching build options; a new release gets its
own directory. Temporary downloads and build files are removed after success
or failure. System installations and databases are preserved. `init` does not
start a server or create a database cluster.

With no version options, `init` requests every supported major. All requested
installations must validate before the registrations are updated. If a later
installation fails, completed installations remain available for the next attempt.

Point Ankus at the `pg_config` executable from your PostgreSQL installation:

```console
ankus init --pg18 /path/to/postgresql/bin/pg_config
ankus info --pg 18
```

`init` validates the server version, executables, and headers, then records the
installation in `~/.ankus/config.json`. It preserves other registrations. Use
`--home` to keep a separate Ankus configuration directory.

Save port bases when initializing or updating a registration:

```console
ankus init --pg18 /path/to/postgresql/bin/pg_config --base-port 29000 --base-testing-port 33000
```

Ankus adds the PostgreSQL major to each base, giving development port `29018`
and test port `33018` for this example. Bases accept integers from `0` through
`65516`. Omitted options preserve saved values; defaults are `28800` and `32200`.
`ankus info --pg 18` reports both resolved ports. Registrations and port settings
are saved together only after validation succeeds.

Development servers use the saved development base on their next start.
Test fixtures keep automatic ports unless you explicitly select the saved test
port; see [choosing a test port](/getting-started/testing/#choose-a-test-port).

## Run a development server

```console
ankus start --pg 18
ankus status --pg 18
psql -h 127.0.0.1 -p 28818 -U postgres -d postgres
ankus stop --pg 18
```

The first `start` initializes a persistent cluster under `~/.ankus/clusters/pg18`.
Later starts reuse its databases. `stop` performs a fast shutdown and keeps the
data. Repeating `start` or `stop` is harmless. `status` reports `running` or
`stopped`; it does not create a missing cluster. All three commands accept
`--all` to select every registered major, or `--pg-config` with `--pg` to select
an installation explicitly.

Each major has its own cluster and TCP port: the saved development base plus
the major version, or `28800` plus the major when no base is saved.
Servers listen on `127.0.0.1` and use local trust authentication as the `postgres`
database user. These are development clusters; other local processes can connect.
Server logs live beside the data directories, such as `~/.ankus/clusters/pg18.log`.

Choose a different port or pass literal PostgreSQL settings when starting:

```console
ankus start --pg 18 --port 15432 --postgresql-conf work_mem=16MB
```

Repeat `--postgresql-conf` for multiple `name=value` settings. Values do not need
PostgreSQL quotes; use your shell's quoting when an argument contains spaces.
Settings and `--port` apply to that start. Repeat them after stopping the server
to use them again. Without `--port`, the next start uses the saved development
base. An already running server keeps its current settings and port, even when
you change the saved base.
`--timeout` changes the startup wait from 60 seconds, with a range of 1–600.

Ankus manages data and authentication paths, loopback connection routing, and
log routing. Those settings and configuration include directives cannot be
overridden through `--postgresql-conf`. Existing unowned or incompatible data
directories are rejected. A failed start preserves initialized data and reports
the server log path so you can correct the setting and retry.

## Build and publish

For an interactive development loop, run this from your extension project or solution:

```console
ankus run --pg 18
```

`run` stops the selected development server, publishes and installs the native
extension, starts the server, and opens `psql`. It creates a database named after
the extension if that database does not exist. Existing databases retain their
contents. In psql, use `CREATE EXTENSION your_extension;` to load the installed
extension into the database. Rebuilding does not automatically upgrade SQL
objects in an existing database.

Use `--project` to select another project, `--configuration Debug` or a custom
configuration such as `Shipping` to change the build settings, or `--database`
to choose a literal database name.
`--no-build` installs the project's existing publication. `--install-only`
leaves PostgreSQL stopped after installation and does not open a client.
Installation requires write access to the selected PostgreSQL installation.

To open a database without rebuilding or installing an extension:

```console
ankus connect --pg 18
ankus connect --pg 18 --database playground
```

`connect` starts a stopped development server and creates or reuses the database.
Without `--database`, it evaluates the selected project's extension name, including
imported MSBuild properties. An explicit database name works outside a project.
Both commands leave the server running when the client exits; use `ankus stop`
to shut it down. An already running server retains its actual port and settings.

Both commands accept the startup options described above, and `--pgcli` selects
an installed `pgcli` from PATH. Arguments after `--` go directly to the client:

```console
ankus connect --pg 18 --database playground -- -c "SELECT 19 + 23"
```

Client input and output stay interactive, and its exit code becomes the Ankus
exit code. In an interactive session, Ctrl+C interrupts the query while keeping
the client open. During preparation or noninteractive execution, Ctrl+C cancels
the command. Database names are literal, including quotes and connection-string
characters. Names exceeding PostgreSQL's identifier limit are rejected rather
than truncated.

### Publish without starting a server

From an extension project directory, or a solution directory containing one Ankus SDK project:

```console
ankus build
ankus publish --output publish
```

Both commands compile the native library and generate the SQL installation files.
The default configuration is `Release`; use `--configuration Debug` or a
[custom configuration](/getting-started/publishing/#custom-build-configurations)
such as `--configuration Shipping` to change it.
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

## Package for distribution

Build an installation tree beneath a separate directory:

```console
ankus package --pg 18 --output dist
```

The package contains the native library, extension control file, versioned
installation SQL, and selected [upgrade scripts](/getting-started/publishing/#upgrade-an-existing-extension).
Its paths mirror the selected installation's `--pkglibdir`
and `--sharedir`, relative to the package root. For example, an installation
whose libraries live in `/usr/lib/postgresql/18/lib` puts the packaged library
under `dist/usr/lib/postgresql/18/lib`. Windows packages instead use `lib/` and
`share/extension/` relative to the PostgreSQL installation root.
The selected PostgreSQL installation and its databases are unchanged.

Packages target the host operating system and architecture and the selected
PostgreSQL major. Use a matching PostgreSQL directory layout when deploying the
tree. You can move or archive the package root after creation.

`--project` selects an extension project or directory. The default build
configuration is `Release`; `--configuration` also accepts `Debug` and custom
MSBuild configurations such as `Shipping`.
The package retains the extension name, version, and native library name from
the publication, including a custom MSBuild `AssemblyName`.

Package an existing publication without rebuilding:

```console
ankus package --from publish --pg 18 --output dist
```

`--from` and `--project` cannot be combined. Without `--output` (or `-o`), the
package root is `<extension>-pg<major>` beneath the publish directory. For a
new build, that directory is
`bin/ankus/pg<major>/<runtime>/<configuration>/` in the project directory.

The command validates the artifact set and PostgreSQL/runtime target before
copying. Repeating it updates the package's files and preserves unrelated files
in the output directory. A failed build leaves the existing package unchanged.

Use `ankus --help` or `ankus <command> --help` for the available options.
