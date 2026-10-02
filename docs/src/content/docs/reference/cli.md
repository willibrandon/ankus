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

Generated projects use ordinary .NET warning defaults. To require warning-free
builds, set `TreatWarningsAsErrors` in your project's build properties.
Invalid Ankus declarations remain errors regardless of that setting.

Alternatively, install `Ankus.Templates` from your configured feed:

```console
dotnet new install Ankus.Templates::0.1.0
dotnet new ankus -n Acme.Search
cd Acme.Search
dotnet tool restore
dotnet test
```

Use `ankus-worker` instead of `ankus` for a preloaded background worker. These
templates share the CLI scaffold and its managed/backend tests. Both creation
paths pin the matching local tool; invoke it with `dotnet ankus` after restoration.
Select a package version available in your feed; the initial public release
remains pending.

Each extension also contains `pg_regress/sql/setup.sql` and a matching
`pg_regress/expected/setup.out`. They create the extension before your
[SQL regression tests](#run-sql-regression-suites) run.

Project names use C# identifier segments such as `Acme.Search`. SQL extension names
default to snake case (`acme_search`); use `--extension-name` to choose one explicitly.
`--output` selects a new destination directory. Existing destinations are preserved.

Creation needs no PostgreSQL installation. Running the generated backend tests
requires PostgreSQL with development headers and the Native AOT toolchain.
The default selection follows `AnkusPostgresMajor` in the project, falling back
to PostgreSQL 18. Plain `dotnet test -p:AnkusPostgresMajor=17` and `ankus test --pg 17`
both select matching PostgreSQL 17 headers and a test server.
See [testing an extension](/getting-started/testing/).

## Run extension tests

```console
ankus test --pg 18
ankus test --pg 17 -- --filter "FullyQualifiedName~BackendTests"
ankus test --all --configuration Release -- --report-trx
```

`test` runs ordinary `dotnet test` from the current directory. It selects the
same PostgreSQL major for the managed build and the backend fixture's native
publication. Use `--pg` for a registered version from 13 through 19, or
`--pg-config /path/to/pg_config` for an explicit installation. When supplied
together, the path must match the requested major. These options override
project defaults.

Without an explicit selection, project commands evaluate `AnkusPostgresMajor`
and `AnkusPgConfigPath` with the selected configuration. `ankus test` also uses
the project or `.sln`/`.slnx` selected by forwarded runner arguments. A test project
without its own selection inherits one from its referenced extension projects.
An empty or ambiguous project selection falls back to PostgreSQL 18; use an
explicit selector to choose another version. Invalid properties and broken imports
remain errors. `start`, `stop`, `status` and `info` use the current project's
selection when it is unambiguous, otherwise PostgreSQL 18. Installing or
packaging with `--from` uses the existing publication's major by default.

`--all` runs each registered version in order. Every registration is checked
before tests start. A missing or broken installation fails the command. Test
failures do not prevent later versions from running; the command returns the
first nonzero runner exit code. Do not combine `--all` with `--pg` or `--pg-config`.

Use `--pgdata ./test-data` to choose where test clusters store their data.
Each invocation creates its own child directory, including with `--all` or
concurrent test runs. Cleanup removes only that child after stopping its servers;
the parent and unrelated contents remain. Unix sockets stay in a separate short
temporary path, so a long data-directory path does not exceed the socket limit.

After an ordinary run has published the extension, add `--no-schema` to retain
that installation SQL while rebuilding native code. This also retains the
matching embedded schema graph; changes to SQL blocks wait until the next run
without the option. Managed function bodies can change, but changed native
declarations fail explicitly and require regenerating the schema.

Saved schemas are separate for each PostgreSQL major and for test and ordinary
publications, within the build's configuration, target framework and runtime
directory. A missing, corrupt or incompatible saved schema fails the command.
Failed publications preserve the last successful snapshot. `dotnet clean`
removes saved schemas with the other intermediate build artifacts.

Put ordinary test-runner arguments after `--`, including `--project`, `--solution`,
filters and reporting options. Use the syntax supported by your project's test
platform. Ankus passes individual arguments without shell interpretation.

`--configuration` defaults to `Debug` and accepts custom configurations. Set it
on `ankus test` or in the forwarded arguments so both the host build and default
fixture publication agree. Conflicting configuration options fail before tests start.
An explicitly configured fixture can still choose its own build configuration.
Forwarded `-p:AnkusPostgresMajor` and `-p:AnkusPgConfigPath` select the same server
and build installation. Conflicting command selectors fail before tests start;
`--all` cannot be combined with these properties. A fixture that selects a
different major fails rather than silently testing the wrong version.

Reports go beneath `TestResults/ankus/<invocation>/pg<major>`. Set the command's
`--results-directory` to choose another root. Each major keeps its own directory,
including when you request a fixed report filename from the test runner.

Fixtures keep build and server logs under the extension project's
`bin/ankus-test-logs/`. The command owns temporary publications, installations,
cluster data and sockets. It shuts down surviving fixture servers after a host
failure or cancellation before removing that storage. Direct `dotnet test`
continues to work with the fixture's ordinary discovery and defaults.

## Pass MSBuild properties

Project commands accept repeatable `--property` (`-p`) assignments:

```console
ankus publish --project MyExtension.csproj --property Configuration=Shipping --property DefineConstants=FEATURE_ONE
ankus get extname --property ExtensionFlavor=preview
```

Properties participate in project evaluation as well as compilation. Conditions
and imports therefore select the same extension identity and PostgreSQL installation
for `build`, `publish`, `install`, `package`, `schema`, `regress`, `run`, `connect`
and `get`. A repeated property name uses its last value; names are case-insensitive.
Each assignment supplies one literal value. Quote spaces or shell metacharacters
as required by your shell. Semicolons, percent escapes and MSBuild expansion syntax
in that value are preserved, and `Name=` supplies an empty value.

`Configuration` selects the default output directory as well as the build.
Conflicting `--configuration`, `--pg` or `--pg-config` options fail explicitly.
These commands publish for the host runtime and require `SelfContained=true`.
The command controls its publication directory and selected PostgreSQL target.
Existing publications selected with `--from` reject new build properties.

`ankus test` continues to accept ordinary MSBuild property arguments after `--`.
SQL client arguments after `--` on `run` and `connect`, and positional schema
item selectors, retain their usual meanings.

## Upgrade Ankus references

Preview framework package and project SDK updates from an extension solution:

```console
ankus upgrade --dry-run
ankus upgrade
```

The command updates Ankus references in the selected projects, shared package
files and `global.json`. It preserves unrelated dependencies, .NET SDK selection,
target frameworks, comments and file encoding. Ankus Native AOT runtime packages
have their own servicing versions and are not part of this update set.
LF, CRLF and CR line endings are preserved.

Use `--project` for a project or directory, or `--solution` for a `.slnx` or `.sln`
file. `--package ProjectName` selects one project within a solution. Changes to
shared files also affect other projects that use those files; inspect the
`--dry-run` output before applying them.

Discovery uses configured NuGet sources and package-source mappings, selecting
stable versions by default. Add `--include-prereleases` to include prereleases.
`--configfile` selects an explicit NuGet configuration, and repeated `--source`
options restrict discovery to selected sources. A failed source lookup leaves
the manifests unchanged. Relative source paths are resolved from the directory
where you invoke `ankus`; configured source names retain their NuGet meaning.
Local feed paths reached through directory aliases retain the configured feed's
package-source mapping.

To choose a version explicitly:

```console
ankus upgrade --to 0.1.0
```

`--to` also accepts NuGet version ranges. Package references retain the requested
range; SDK declarations resolve it to a concrete eligible version. Automatic
updates retain exact pins and existing upper bounds. Floating requirements keep
their existing version band until you replace it with `--to`.

Dedicated version properties declared in the selected project are updated with
their references. For imported properties, or properties also used by unrelated
dependencies, the Ankus reference receives its new version directly. The property
remains unchanged so other consumers keep their existing versions.

Package identities can use source-declared properties and semicolon-separated
lists. In these declarations, version metadata is restricted to each actual
Ankus item. Unrelated packages keep their versions, aliases and conditions.
Repeating the same upgrade does not add duplicate metadata or rewrite files.
Cyclic or unresolved package-name properties are reported before any files change.
Package identities built with MSBuild property functions are not resolved by the
source editor; declare their Ankus package names explicitly before upgrading.

Conditional and computed import paths use the installed SDK's MSBuild evaluation.
Discovery includes the default evaluation and the project's declared
`Configurations` and `TargetFrameworks`. Disabled imports and custom paths for
shared build or package files retain their MSBuild behavior. These projects need
a resolvable project SDK; evaluation errors leave every selected manifest unchanged.
Files imported from SDK or NuGet package caches are excluded from updates.
The command evaluates imports without running build or restore targets. It
ignores build response files, which can request target execution.

If a manifest changes while versions are being resolved, the command leaves the
user edit intact and rejects the stale plan. Replacement retains Unix file
permissions, and an observed write failure rolls back earlier replacements.

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

Installations live under the Ankus home's `postgres/` directory, by default
`~/.ankus/postgres/`. Repeating the same request reuses
a validated installation with matching build options; a new release gets its
own directory. Temporary downloads and build files are removed after success
or failure. System installations and databases are preserved. `init` does not
start a server or create a database cluster.

With no version options or environment defaults, `init` requests every supported major. All requested
installations must validate before the registrations are updated. If a later
installation fails, completed installations remain available for the next attempt.

Point Ankus at the `pg_config` executable from your PostgreSQL installation:

```console
ankus init --pg18 /path/to/postgresql/bin/pg_config
ankus info --pg 18
```

`init` validates the server version, executables, and headers, then records the
installation in the Ankus home's `config.json`. It preserves other registrations.
Use `--home` to keep a separate Ankus configuration directory.

`ANKUS_HOME` selects the home directory for all commands when `--home` is absent.
Relative home paths resolve from the directory where you invoke Ankus. The
default is `~/.ankus`.

`PG13_PG_CONFIG` through `PG19_PG_CONFIG` provide defaults for the corresponding
`init --pgNN` options. Values are `pg_config` paths or `download`; explicit options
override them. These defaults select which majors `init` registers. Later commands
use the saved registrations and their ordinary PostgreSQL selectors. Invalid
paths or mismatched versions fail before changing the registry.

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

## Read installation values in scripts

`ankus info` prints a labelled installation report. Its subcommands print one
value followed by a newline, with no labels:

```console
ankus info path 18
ankus info pg-config pg18
ankus info version 18
```

`path` returns the directory above the selected `pg_config` directory, matching
pgrx's installation-root convention. `pg-config` returns the executable's
absolute path. `version` returns its release version, such as `18.6` or `19beta4`.
Paths are printed verbatim, including spaces. Preserve the returned value
rather than splitting it on whitespace.

The optional positional major accepts `13` through `19`, with or without a
`pg` prefix. You can instead use `--pg` and `--pg-config`, before or after the
subcommand. If both major selectors are given, they must agree, and an explicit
executable must match that major. Without selectors, the current project's
unambiguous PostgreSQL selection applies, otherwise PostgreSQL 18. Registry
lookup honors `--home` and `ANKUS_HOME`.

These commands do not start a server or update registrations. Failed selection
returns a nonzero exit code and writes its diagnostic to standard error without
printing an information value.

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

### Inspect native memory with Valgrind

On a Unix platform supported by Valgrind, install Valgrind and put it on `PATH`:

```console
ankus start --pg 18 --valgrind
```

`start`, `run`, `connect`, and `regress` accept `--valgrind`. PostgreSQL and its
children run under Memcheck, with diagnostics in the server log. Error reports
are delimited by `VALGRINDERROR-BEGIN` and `VALGRINDERROR-END`. Inspect that log
after running your extension's queries and stopping the server. Instrumentation
adds overhead; use `--timeout` when the server needs longer to start.
A successful SQL test can still produce Memcheck reports, including diagnostics
from PostgreSQL itself. Inspect the server log as part of memory testing.

Valgrind limits the virtual address space available to an instrumented process.
Ankus supplies a **32 GiB** .NET GC region range for Valgrind launches when
`DOTNET_GCRegionRange` is unset or empty. This reserves address space; it does not
allocate 32 GiB of physical memory. An explicit `DOTNET_GCRegionRange` is
preserved, and ordinary server starts keep their existing runtime settings.
See [.NET GC region range](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector#region-range)
for hexadecimal environment-variable values and sizing guidance. Multiple
Native AOT extensions in one backend each need space for their runtime's range.

An already running server keeps its instrumentation mode. Stop it before using
`start` or `connect` to change modes; `run` and `regress` restart it themselves.
For PostgreSQL memory-context annotations, build the server using
`ankus init --pg18 download --valgrind` with Valgrind development headers installed.
That build option and the execution option serve separate purposes.

Native Windows PostgreSQL cannot run under Valgrind. Other systems require a
working Valgrind build for their architecture. Missing tools and unsupported
startup fail explicitly, preserving initialized cluster data.

## Run SQL regression suites

Place SQL files under `pg_regress/sql/` beside your extension project. Expected
output lives in `pg_regress/expected/`, using the same basename and an `.out`
extension. `ankus new` creates an initial `setup.sql` and its expected output.
The setup script creates your extension; extend it with shared test objects as needed:

```sql
CREATE EXTENSION hello;
```

For example, put `SELECT add(19, 23);` in `pg_regress/sql/addition.sql`, then
record and review its initial output:

```console
ankus regress --pg 18 --add addition
ankus regress --pg 18
```

`--add` recreates the regression database, runs setup first when present, and
creates the new expected file. It also records setup output when that file is
missing. Existing expectations cannot be overwritten with `--add`. Review the
recorded output before committing it, including any SQL errors your test intends
to exercise. A failed client process never becomes a successful expectation.

Ankus stops the selected development server, builds and installs the extension,
then restarts the server. It uses a database named `<extension>_regress`.
`--database` selects another literal name. Ordinary runs preserve that database
and skip setup on reuse.
`--resetdb` recreates it and reruns setup. A setup script newer than its expected
file also triggers recreation. Recreation disconnects existing clients to that
database and deletes its contents. The development server remains running afterward.

Tests run sequentially in ordinal filename order, with setup first when needed.
Each file gets a separate psql session. A positional filter matches a
case-sensitive substring of ordinary test names:

```console
ankus regress --pg 18 addition --no-build
ankus regress --pg 18 --repeat 3 --verbose
ankus regress --pg 18 --dry-run
```

Unfiltered runs report and skip tests without expected output. An explicit filter
that matches nothing, or selects a test without expected output, fails.
`--no-build` installs the existing publication. `--project`, `--configuration`,
`--home`, `--port`, `--timeout`, `--valgrind`, and repeated `--postgresql-conf name=value`
options select the project and development environment. `--dry-run` reports the
selection and intended actions without building, starting a server, or writing files.

Comparison uses the selected PostgreSQL installation's `pg_regress`, including
its alternate expected outputs and `resultmap` behavior. That executable must
be installed alongside PGXS; `diff` must be available on `PATH` (Git for Windows
supplies it). Paths and test names that the native driver's shell command cannot
represent safely are rejected. Spaces in suite paths and test names are supported.

Results are written to `pg_regress/results/`; differences go to
`pg_regress/regression.diffs`. `--verbose` prints the differences.
With `--repeat`, each failed iteration keeps `regression.<number>.diffs`.
Any failed iteration makes the command fail, even if a later iteration succeeds.
`--auto` replaces expected output only for selected tests that the native
comparator reports as different; the failed run still returns a nonzero exit code.
Review those changes before accepting them. `--add` cannot be combined with a
filter, `--auto`, or multiple iterations.

SQL errors use psql's `terse` verbosity by default. Choose `default`, `verbose`,
or `sqlstate` with `--psql-verbosity`. SQL errors can be expected test output;
use `\set ON_ERROR_STOP on` in a SQL file when they should instead fail the
client process. Alternate operating-system users or data
directories are not yet supported by this command.

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
Database names retain their exact spelling and must satisfy the selected server's
rules. PostgreSQL 19 rejects names containing newline or carriage-return characters.
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
PostgreSQL major; otherwise the project's `AnkusPostgresMajor` applies, falling
back to `18`. To use a different installation for one
invocation, pass `--pg-config /path/to/pg_config` as well.

## Inspect or select SQL

Extract the complete installation script or select declarations and their dependencies:

```console
ankus schema --output schema.sql
ankus schema --from publish/Hello.so add --output selected.sql
ankus schema --from publish/Hello.so add --dot dependencies.dot --no-alter-extension
```

Without item names, extraction retains `MODULE_PATHNAME` for installation.
Named selection uses the installed library under `$libdir` and normally wraps
creation and extension attachments in one transaction. `--no-alter-extension`
omits that wrapper and the attachment statements. `--dot` exports the full graph.
For named selection, `--schema reporting` supplies the actual installation
schema, qualifies generated names and resolves `@extschema@` search-path tokens.
It must agree with any fixed control schema. Without an explicit or fixed target,
selected SQL containing that token is rejected. Full extraction retains it for
PostgreSQL and cannot use `--schema`.
See [schema extraction and selection](/getting-started/publishing/#inspect-installation-sql)
for naming, fixed schemas, custom SQL and standalone libraries.

## Query extension properties

Print a primary control property or a derived project value:

```console
ankus get default_version --pg 18
ankus get relocatable --project src/MyExtension/MyExtension.csproj
ankus get extname --configuration Shipping
ankus get git_hash
```

Control queries compile the managed project and combine its generated metadata
with `AnkusExtensionControlFile`. They honor imports, `--configuration`,
`--pg` and `--pg-config`. They need the selected PostgreSQL development files
and build prerequisites, but do not publish a native library or start a server.
Build messages go to stderr; stdout contains only the requested value.

`extname` evaluates the selected project's extension name without compiling it.
`git_hash` reports that project's current Git commit and preserves Git failures,
including repositories without a commit. Neither query requires PostgreSQL
registration. `--project` accepts a project file or an unambiguous project or
solution directory.

Query an existing publication without building or discovering PostgreSQL:

```console
ankus get default_version --from publish
ankus get comment --from publish
ankus get extname --from publish
```

`--from` accepts a published directory containing `ankus.extension.json` and
its primary control file. It cannot be combined with project, configuration or
PostgreSQL selection options. `git_hash` requires a source project.

Property names are case-sensitive. An absent property succeeds without output;
an explicitly empty value prints a blank line. Values retain spaces, quotes and
embedded equals signs. These are primary control values; PostgreSQL applies
[version-specific controls](/reference/build-settings/#version-specific-control-files)
when installing or updating to a selected extension version.

## Install

Build and install into the selected PostgreSQL installation:

```console
ankus install --pg 18
```

Or install a previous publish:

```console
ankus install --from publish --pg 18
```

The library goes to `pg_config --pkglibdir`. The primary control goes to the
`extension` subdirectory of `pg_config --sharedir`. SQL and secondary controls
use that directory unless the primary declares a custom [SQL directory](/reference/build-settings/#sql-directories).
The command requires write access to the selected destinations.

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
`share/extension/` relative to the PostgreSQL installation root. Custom SQL
directories also apply to package layout; see [SQL directories](/reference/build-settings/#sql-directories)
for absolute destinations and Windows parent-relative paths.
The selected PostgreSQL installation and its databases are unchanged.

On Linux and macOS, `--prefix-dir` puts the native library, primary control file
and default SQL files in one directory beneath the output root:

```console
ankus package --pg 18 --output dist --prefix-dir custom/extension
```

This produces `dist/custom/extension/`. An absolute prefix such as
`/custom/extension` produces the same layout: its filesystem root is removed.
Parent-directory segments must stay beneath the output root. Windows retains its portable
`lib/` and `share/extension/` layout, matching pgrx. Explicit SQL `directory`
settings still apply. On PostgreSQL 18 and later, a relative SQL directory
uses the chosen control directory's parent; earlier versions use the selected
installation's shared directory.

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
