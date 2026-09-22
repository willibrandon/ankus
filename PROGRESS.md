# Ankus — pgrx → .NET Native AOT Port: Progress Tracker

> A faithful port of [pgrx](https://github.com/pgcentralfoundation/pgrx) (PostgreSQL
> extensions in Rust) to **C# compiled with .NET Native AOT**, produced as a native
> shared library that PostgreSQL loads directly on Windows, Linux, and macOS.
>
> This document is the working tracker for the port. Update it as phases complete.

## Goal

Fully port pgrx to .NET Native AOT in the most ideal way possible, in a way that all
.NET developers will expect:

**Scope is the entire pgrx framework. Every feature, runtime facility, macro equivalent,
tooling capability, example, and testing capability must be ported. Custom scans and
nodes are required. Phase ordering describes implementation order only; it does not
exclude or make any work optional. The port is complete only when full parity has
been implemented and validated across the required PostgreSQL and operating-system matrix.**

- **Faithful port**: mirror pgrx's feature surface and mental model (see [Feature map](#feature-map-pgrx--ankus)),
  translated into idiomatic C# (attributes + source generators instead of proc macros,
  `IEnumerable<T>` for SETOF, exceptions → `ereport(ERROR)`, etc.).
- **Native AOT**: the extension ships as a self-contained native library (no .NET runtime
  install required on the Postgres host), built with `PublishAot`.
- **Multi-version**: one C# codebase targeting PostgreSQL 13–18 (+19 beta), matching
  pgrx's supported versions. Only PostgreSQL 18 on Linux x64 has been exercised so far.
- **Developer experience**: ordinary .NET projects, source generators, `dotnet publish`, and `dotnet test`,
  with development tooling corresponding to `cargo pgrx`.

## Environment

| Item | Value |
|---|---|
| .NET SDK | 10.0.400 (Native AOT mature); `global.json` uses `rollForward: latestMajor` (no pinning) |
| Docker | 29.7.2 — test images: `postgres:18` (primary), `postgres:16` (fetched); pull per version when testing a matrix leg |
| C toolchain | clang 21 + lld; GCC 14 used for the local PostgreSQL build |
| Primary test target | **PostgreSQL 18** (19 imminent; pgrx supports 13–18 + 19 beta) |

## Current verified milestone

- `Ankus.slnx` contains the runtime, source generator, native build tool, native sample,
  PostgreSQL discovery, test infrastructure, and five developer-visible MSTest projects.
- **Plain `dotnet test`** is the canonical entry point: **58 passed, 0 failed, 0 skipped**
  on Linux x64 with PostgreSQL 18.6. No environment variables or wrapper command are required.
- Test infrastructure lives in `tests/Ankus.Testing`; executable tests live in
  `tests/Ankus.IntegrationTests`, `tests/Ankus.Examples.Hello.Tests`, `tests/Ankus.PgConfig.Tests`,
  `tests/Ankus.Generators.Tests`, and `tests/Ankus.Runtime.Tests`.
- The sample is now an ordinary `[PgFunction]`-attributed C# method. Ankus generates the
  managed dispatcher, native entry point, module magic, finfo, integer conversion, and SQL.
- Generated native code compiles against the discovered PostgreSQL server headers, then links
  into the Native AOT library. Export inspection confirms magic, finfo, and the SQL entry point.
- Managed exceptions return to the native wrapper before it raises PostgreSQL ERROR.
  Both checked-overflow cases return SQLSTATE `38000`; rollback and subsequent queries succeed
  on the same backend. No PostgreSQL error is raised through a managed frame on this path.
- Generator tests cover compilable wrappers and diagnostics for unsupported signatures,
  inaccessible/generic types, invalid SQL names, and duplicate names. Runtime tests verify
  UTF-8 truncation, buffer guards, null termination, and a throwing exception-message accessor.
- PostgreSQL discovery checks `~/.ankus/config.json`, Ankus-managed installations,
  PATH, and conventional Windows/Linux/macOS installation directories.
- A local, assertion-enabled PostgreSQL 18.6 was built from the official source archive
  into `~/.ankus/postgres/18.6`. The read-only reference clones were not modified.
  Bison and Flex were extracted into temporary storage for provisioning, without a system install.
- Integration setup publishes the native sample for the host RID, reserves a dynamic port,
  initializes fresh PGDATA, starts `pg_ctl`, creates a test database, and registers sample functions.
- Backend test functions run in individual rollback-only transactions. Tests prove rollback
  after success, failure, and cancellation; expected-error matching; retained session logs;
  independent concurrent clusters; failed-start cleanup; and idempotent shutdown.
- Normal process exit attempts shutdown. Forced process termination cannot guarantee cleanup.
- Windows and macOS code paths are implemented but have not yet been executed on those hosts.

### Work in progress

The generated API currently supports accessible, synchronous static methods with by-value
`int` arguments and an `int` return value. SQL functions are strict. Generated SQL is published
alongside the native library; the fixture currently executes it directly.
Full `[PgTest]` generation, extension packaging/installation, more data types, guarded calls
into PostgreSQL, and the PG13–19 matrix remain pending. The MSBuild import is repository-local;
an independently consumable NuGet SDK has not been packaged yet. PostgreSQL discovery is
automatic, but prerequisite installation is currently manual.

### Local read-only reference repos (absolute paths)

- **pgrx** → `/home/brandon/src/pgrx` — the port's reference implementation. Key paths:
  - `/home/brandon/src/pgrx/pgrx/src/` — runtime modules (`spi.rs`, `datum/`,
    `guc.rs`, `memcx.rs`, `trigger_support/`, `iter.rs`, `bgworkers.rs`, …)
  - `/home/brandon/src/pgrx/pgrx-macros/src/lib.rs` — the proc macros (`pg_extern`, `pg_trigger`, `pg_aggregate`, …)
  - `/home/brandon/src/pgrx/cargo-pgrx/` — CLI model to mirror (`new/init/build/schema/test/run/package`)
  - `/home/brandon/src/pgrx/pgrx-examples/` — example set to mirror in `samples/`
  - `/home/brandon/src/pgrx/pgrx-tests/`, `/home/brandon/src/pgrx/pgrx-unit-tests/` — test strategy reference
  - `/home/brandon/src/pgrx/v18-ONE-COMPILE-CHANGELOG.md` — one-compile `.pgrxsc` schema model (our `.ankusc` analogue)
- **postgres** → `/home/brandon/src/postgres` — ABI source of truth. Tags:
  `REL_13_23`…`REL_18_6`, `REL_19_BETA3`. Key files are `src/include/fmgr.h`,
  `src/backend/utils/fmgr/dfmgr.c`, `src/backend/utils/fmgr/fmgr.c`, and
  `src/include/utils/elog.h`.
- **runtime** → `/home/brandon/src/runtime` — .NET runtime source (Native AOT: `src/coreclr/nativeaot/`, PAL: `src/coreclr/pal/src/`).
- **roslyn** → `/home/brandon/src/roslyn` — compiler source (function-pointer grammar, source generators).
- **ilrepl** → `/home/brandon/src/ilrepl` — `.editorconfig` style reference (merged into `/home/brandon/src/ankus/.editorconfig`).

## Key research findings (verified)

### .NET Native AOT (Microsoft docs, verified)

- Publishing a **class library** with `<PublishAot>true</PublishAot>` produces a
   **self-contained native shared library** consumable from non-.NET code
  (`learn.microsoft.com/dotnet/core/deploying/native-aot/libraries`).
- Methods annotated **`[UnmanagedCallersOnly(EntryPoint = "name")]`** are exported as
  **public C entry points** from the AOT image — exactly the symbols PostgreSQL
  `dlsym`s for `CREATE FUNCTION ... AS 'module', 'func'` and `pg_finfo_*`.
- Caveats: no `dlclose` support for AOT libraries (Postgres keeps modules loaded; fine).
  `NativeLibrary`/`LinkerArg` MSBuild items allow linking extra native objects/libs.

### PostgreSQL module contract (verified from `~/src/postgres`, tags REL_13_23 … REL_18_6)

Postgres `dlopen(filename, RTLD_NOW|RTLD_GLOBAL)` then:

1. `dlsym(handle, "Pg_magic_func")` — must return a `const Pg_magic_struct *`
   (function, not data). Missing ⇒ `ERROR: incompatible library: missing magic block`.
2. Magic validation:
   - **PG 13–17**: `len == server's len && memcmp(module_magic, &server_magic, len) == 0` (byte-exact).
   - **PG 18, 64-bit**: magic length is **72 bytes**; PostgreSQL compares the ABI fields
     (name/version pointers may be NULL).
3. `dlsym(handle, "_PG_init")` — called if present. No compatibility alias is needed
   for the supported PostgreSQL majors.
4. Function lookup (`fmgr.c`): `dlsym` for `pg_finfo_<name>` (a **function** returning
   `const Pg_finfo_record *` where `Pg_finfo_record = { int api_version /* =1 */ }`),
   followed by lookup of the function entry point.

### `Pg_magic_struct` version matrix (must be compiled per PG version, like pgrx)

| PG | sizeof | layout | values |
|----|--------|--------|--------|
| 13 | 24 | `{len, version, funcmaxargs, indexmaxkeys, namedatalen, float8byval}` | 24, 1300, 100, 32, 64, 1 |
| 14 | 24 | same | 24, 1400, 100, 32, 64, 1 |
| 15 | 56 | same + `char abi_extra[32]` | 56, 1500, 100, 32, 64, 1, "PostgreSQL" |
| 16 | 56 | same | 56, 1600, 100, 32, 64, 1, "PostgreSQL" |
| 17 | 56 | same | 56, 1700, 100, 32, 64, 1, "PostgreSQL" |
| 18 | 72 | `{len, Pg_abi_values, name*, version*}` | 72, 1800, 100, 32, 64, 1, "PostgreSQL" |

(`FUNC_MAX_ARGS=100` for all 13–18; `INDEX_MAX_KEYS=32`; `NAMEDATALEN=64`; `FLOAT8PASSBYVAL=1`.)

### `FunctionCallInfoBaseData` layout (identical PG 10–18, x86-64)

```
offset 0:  FmgrInfo  *flinfo
offset 8:  fmNodePtr  context
offset 16: fmNodePtr  resultinfo
offset 24: Oid        fncollation
offset 28: bool       isnull      (result NULL flag; function sets it)
offset 30: short      nargs
offset 32: NullableDatum args[n]   // { Datum value; bool isnull; } = 16 bytes each
```
`Datum` = 64-bit: pass-by-value types are inline; pass-by-reference types are pointers
(varlena uses tagged one-byte or four-byte headers, with compressed/external forms;
its representation and detoasting are not yet implemented).

## Architecture direction

These are requirements for the port, not claims that the full architecture exists:

1. Source generators translate ordinary attributed C# methods into dispatchers and SQL.
2. Native entry points use the selected PostgreSQL headers for magic, finfo, and argument access.
3. Managed exceptions must be caught inside managed code. Native PostgreSQL ERROR is raised
   only after returning out of all managed frames. **Never longjmp across Native AOT frames.**
4. Calls from managed code into PostgreSQL need their own guarded native boundaries before
   SPI, memory allocation, or other error-producing server APIs are exposed.
5. Build one native extension per PostgreSQL major and host architecture, as pgrx does.
6. Schema metadata must support ELF, PE/COFF, and Mach-O; an ELF-only design is insufficient.
7. `dotnet test` discovers all test projects normally. Its fixtures own publishing,
   local cluster setup, backend execution, diagnostics, and shutdown.

## Feature map (pgrx → Ankus)

| pgrx | Ankus | status |
|---|---|---|
| `#[pg_extern]` | `[PgFunction]` + source generator (exports, DDL, metadata) | Partial: strict `int` functions |
| `#[pg_schema]` | `[PgSchema("name")]` | ☐ |
| `#[pg_guard]` | automatic at export boundary (always on) | Partial: managed exception → native ERROR |
| SETOF (`SetOfIterator`) | return `IEnumerable<T>` ⇒ `RETURNS SETOF` | ☐ |
| `#[pg_trigger]` | `[PgTrigger]` | ☐ |
| `#[pg_event_trigger]` | `[PgEventTrigger]` | ☐ |
| `#[pg_aggregate]` + `Aggregate` trait | `[PgAggregate]` + `IAggregate<TState>` (init/transition/combine/final, (de)serializable) | ☐ |
| `#[pg_operator]` | `[PgOperator]` (+ SQL DDL) | ☐ |
| `#[pg_cast]` | `[PgCast]` (+ SQL DDL) | ☐ |
| `extension_sql!` | `[ExtensionSql]` attribute / `.sql` files | ☐ |
| `#[derive(PostgresType)]` (composites) | `[PostgresType]` on records + generator | ☐ |
| `#[derive(PostgresEnum)]` | `[PostgresEnum]` on C# enums + generator (CREATE TYPE) | ☐ |
| Type mapping (`FromDatum`/`IntoDatum`) | `Datum` converters for built-in and user-defined SQL types | ☐ |
| `Spi` | `Spi` (connect, select, execute, update/insert, get_one, function calls) | ☐ |
| `PgError` | `PgError` exception + `Elog` helpers | ☐ |
| `pgrx::guc` | `[PgGucInt/Real/String/Bool/Enum]` (registered in `_PG_init`) | ☐ |
| `background_worker` | `BackgroundWorker` registration (C# `void(Datum)` via function pointer) | ☐ |
| `palloc`/`MemoryContextManager` | `PgMemoryContext`, `Palloc` | ☐ |
| `pgrx::rel` (`PgRelation`) | `PgRelation`, `PgIndex` | ☐ |
| `pgrx::tuplestore` | `TupleStore` | ☐ |
| `pgrx::callback` (xact callbacks) | `TransactionCallback` | ☐ |
| `pgrx::catalog` (`Oid`, `PgType`) | `Oid`, `PgType`, catalog helpers | ☐ |
| `pgrx::log` | PG log-level mapping for .NET logging | ☐ |
| `pgrx::pg_sys` (raw FFI) | `Ankus.PgSys` (raw `LibraryImport` surface) | ☐ |
| custom scan (`pgrx::customscan`, `pgrx::nodes`) | Custom scan providers, node types, callbacks, and supporting APIs | ☐ Required |
| `cargo pgrx` CLI | `ankus` dotnet tool: `new/init/build/schema/test/run/package` | ☐ |
| `cargo pgrx schema` (one-compile, `.pgrxsc`) | `ankus schema` (reads `.ankusc` section from built `.so`) | ☐ |
| pgrx-examples | `samples/` mirroring the example set | ☐ |

## Phase plan

Every phase is required for the faithful port. Unchecked items are remaining work,
not scope exclusions. The feature map is a tracking aid; the complete read-only
pgrx reference defines the required surface, including capabilities not yet itemized here.

- [ ] **P0 — Feasibility spike** *(integer path verified; text remains)*
   - [x] Minimal attributed `add(int,int)→int` extension
   - [ ] `hello(text)→text`
   - [x] Generated native magic, finfo, integer argument access, and managed-exception error reporting
   - [x] AOT publish and actual PostgreSQL 18 integer-function invocation
   - [x] Error path: C# exception ⇒ Postgres `ERROR`, transaction aborts cleanly, backend survives
- [ ] **P1 — Runtime core**
  - [ ] `Ankus.Runtime`: `FunctionCallInfo` reader, `Datum`/`Value`, varlena/detoast, type conversion table
  - [ ] `Ankus.PgSys`: symbol resolution (`dlopen(NULL)`+`dlsym`), P/Invoke surface (SPI, elog via shim, memory, catalog)
  - [ ] `Spi` API; `PgError`; memory contexts; `_PG_init` bootstrap
- [ ] **P2 — Source generator** (`Ankus.Generators`)
   - [x] `[PgFunction]` → per-function dispatcher + `pg_finfo` shim emission + DDL metadata (integer subset)
  - [ ] `[PgSchema]`, strictness, SETOF, `T?` NULL handling, arrays
  - [ ] `.ankusc` metadata section (JSON) embedded in the `.so`; `ankus schema`
- [ ] **P3 — Extension features**
  - [ ] triggers, event triggers, aggregates, operators, casts, `ExtensionSql`
  - [ ] composites (`[PostgresType]`), enums (`[PostgresEnum]`)
  - [ ] GUC options; background workers
- [ ] **P4 — Tooling** (`ankus` dotnet tool)
  - [ ] `new`, `build`, `schema`, `test`, `run`, and `package` commands
  - [ ] Package `.so`, `.control`, and versioned `.sql` artifacts
- [ ] **P5 — Multi-version matrix**
   - [ ] PostgreSQL 13–18 (+19 beta) and Windows/Linux/macOS validation matrix
- [ ] **P6 — Examples + docs**
  - [ ] `samples/` mirroring pgrx-examples (aggs, gucs, triggers, bgworker, customscan…)
   - [ ] README, getting started, and verified native-boundary design notes
- [ ] **P7 — Custom scan + nodes**
   - [ ] Full custom scan provider API, native callbacks, and lifecycle integration
   - [ ] PostgreSQL node representations and pgrx node support APIs
   - [ ] Corresponding examples and backend-executed tests

## Conventions (user directives)

- **C# best practices for 2026**; formatting "like the dotnet team": style follows the
  `dotnet/runtime` repo conventions + the strictness of `ilrepl`'s `.editorconfig`
  (merged into this repo's `.editorconfig`): file-scoped namespaces, `var` when
  apparent, one type per file (repository rule), XML doc comments on **all public and
  internal** members, nothing over 140 characters, Allman braces in C shim code.
- **Commits**: commit incrementally whenever a coherent unit lands.
- **Testing**: faithful to pgrx's local PostgreSQL lifecycle, asserting real backend
  behavior, errors, GUCs, triggers, etc., plus
  unit tests for conversion logic. Test stack: **MSTest v4 on Microsoft.Testing.Platform
  (MTP)**. Plain **`dotnet test`** is required; no wrapper commands or environment setup.
- Windows, Linux, and macOS are all required targets.
- No mocking frameworks; only Microsoft/dotnet-owned NuGet packages unless approved.
  Npgsql is approved. Use central package management and current stable packages.
- Automation uses C# file-based apps or proper .NET projects, never shell/PowerShell scripts.
- Do not overwrite user work or touch `.gitignore`. Do not add copyright/license headers.
- Use separate-line XML summaries, warnings as errors, and incremental validated commits to `main`.
- Port all of pgrx. Do not reduce scope or classify any required feature as optional.

## Risk register

| Risk | Mitigation |
|---|---|
| AOT `.so` inside a Postgres backend | Validate runtime initialization and signal behavior before expanding |
| Postgres `longjmp` crossing AOT frames | Design a guarded native boundary and avoid finalizer-dependent state |
| Variadic PostgreSQL C functions | Add minimal native helpers only where a non-variadic API is unavailable |
| Struct layout drift across PG versions | Generated shim + layout table generated from per-version headers; matrix tests |
| Native library size | Initial integer probe was approximately 933 KB on Linux x64 |
| `dlclose` unsupported by AOT libs | N/A — Postgres keeps extension modules loaded for the backend's lifetime |

## Log

- 2026-09-21 — Goal set. Environment verified (SDK 10.0.400, docker, postgres:18 pulled).
  AOT shared-library + C-export path confirmed via Microsoft docs. PG module contract
  (magic, finfo, `_PG_init`) verified from `~/src/postgres` for PG 13–18. pgrx v18
  one-compile (`.pgrxsc`) design noted as the model for `ankus schema`.
  User direction: primary test target PG 18 (19 imminent); compatibility across all
  pgrx-supported versions; faithful port of pgrx's feature surface.
- 2026-09-21 — Restarted P0 from the last clean commit after discarding an
  uncompilable prototype. Corrected the PostgreSQL 18 magic size to 72 bytes and
  replaced the false IDE0044 one-type-per-file claim with a repository rule.
- 2026-09-21 — First AOT library published with all required ELF exports. The
  first PostgreSQL load safely exposed a version encoding error: module magic
  requires `PG_VERSION_NUM / 100` (1800 for PG 18), not the human major (18).
- 2026-09-22 — Local pgrx-style cluster harness implemented under `tests/`.
  Plain `dotnet test` passes all 30 cases, including 16 real PostgreSQL integration cases.
  Provisioned an assertion-enabled local PostgreSQL 18.6 with no system package changes.
  User clarified that minimal samples must contain user functions, with framework plumbing generated by Ankus.
- 2026-09-22 — Committed the validated baseline to `main` as `c819160`.
  Replaced the handwritten sample with `[PgFunction]`, a Roslyn incremental generator,
  metadata-only artifact extraction, and a native error boundary linked into the AOT image.
  Plain `dotnet test`: 58 passed, including 16 real PostgreSQL integration cases.
- 2026-09-22 — Corrected unauthorized scope-reduction language. All pgrx features,
  including custom scans and nodes, are required for completion. Milestones record
  incremental implementation and validation; they do not narrow the full-port objective.
