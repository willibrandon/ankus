# Ankus — pgrx → .NET Native AOT Port: Progress Tracker

> A faithful port of [pgrx](https://github.com/pgcentralfoundation/pgrx) (PostgreSQL
> extensions in Rust) to **C# compiled with .NET Native AOT**, produced as a native
> shared library that PostgreSQL loads directly on Windows, Linux, and macOS.
>
> Implementation status, feature coverage, and validation results.

## Goal

Full pgrx parity in idiomatic .NET Native AOT: runtime APIs, macro equivalents,
extension features, custom scans and nodes, tooling, examples, and testing.
Completion includes validation across PostgreSQL 13–18 plus 19 beta on Windows,
Linux, and macOS.

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
| .NET SDK | 10.0.400; `global.json` uses `rollForward: latestMajor` |
| C toolchain | clang 21 + lld; GCC 14 used for the local PostgreSQL build |
| Primary test target | **PostgreSQL 18** |

## Current verified milestone

- `Ankus.slnx` contains the runtime, source generator, native build tool, native sample,
  PostgreSQL discovery, test infrastructure, and five developer-visible MSTest projects.
- **`dotnet test`**: **149 passed, 0 failed, 0 skipped** on Linux x64 with PostgreSQL 18.6.
- Test infrastructure lives in `tests/Ankus.Testing`; executable tests live in
  `tests/Ankus.IntegrationTests`, `tests/Ankus.Examples.Hello.Tests`, `tests/Ankus.PgConfig.Tests`,
  `tests/Ankus.Generators.Tests`, and `tests/Ankus.Runtime.Tests`.
- The sample contains ordinary `[PgFunction]`-attributed `Add` and `Greet` methods. Ankus generates
  managed dispatchers, native entry points, module magic, finfo, datum conversions, and SQL.
- Generated native code compiles against the discovered PostgreSQL server headers, then links
  into the Native AOT library. Export inspection confirms magic, finfo, and the SQL entry point.
- Managed exceptions return to the native wrapper before it raises PostgreSQL ERROR.
  Both checked-overflow cases return SQLSTATE `38000`; rollback and subsequent queries succeed
  on the same backend. No PostgreSQL error is raised through a managed frame on this path.
- Generator tests cover compilable wrappers and diagnostics for unsupported signatures,
  inaccessible/generic types, async methods, invalid SQL names, and duplicate SQL signatures. Runtime tests verify
  UTF-8 truncation, buffer guards, null termination, and a throwing exception-message accessor.
- PostgreSQL discovery checks `~/.ankus/config.json`, Ankus-managed installations,
  PATH, and conventional Windows/Linux/macOS installation directories.
- Validation uses an assertion-enabled PostgreSQL 18.6 built from the official source archive.
- Integration setup publishes the native sample for the host RID, reserves a dynamic port,
  initializes fresh PGDATA, starts `pg_ctl`, creates a test database, and runs `CREATE EXTENSION ankus_hello`.
- Publishing emits the native library, generated SQL, and `extension/` control and versioned SQL files.
  PG18 fixtures use per-cluster `extension_control_path` and `dynamic_library_path` settings to
  load the actual published files without copying into the shared PostgreSQL installation.
- Integration tests verify extension-owned function catalog entries, schema relocation,
  DROP EXTENSION removing the function, and reinstallation into a requested schema.
- The 84 PostgreSQL integration cases include scalar bounds, signed zero and NaN bit patterns,
  nullable contracts, SQL overloads, Unicode, bytea, packed headers, compressed/external TOAST,
  LATIN1 conversion, and recovery from native output-encoding errors on the same backend.
- `tests/Ankus.TestExtension` supplies backend test functions. The fixture publishes and installs
  this separate extension alongside the minimal sample, exercising multiple AOT libraries in one backend.
- Backend test functions run in individual rollback-only transactions. Tests prove rollback
  after success, failure, and cancellation; expected-error matching; retained session logs;
  independent concurrent clusters; failed-start cleanup; and idempotent shutdown.
- Normal process exit attempts shutdown. Forced process termination cannot guarantee cleanup.
- Windows and macOS code paths are implemented but have not yet been executed on those hosts.

### Work in progress

The generated API currently supports accessible, synchronous static methods with by-value
`bool`, `sbyte`, `short`, `int`, `long`, `uint` (OID), `float`, `double`, `string`, and `byte[]`
parameters/results, their nullable forms, and `void` results. Strictness follows argument nullability.
The native library, control file, and versioned SQL are published and installed through PostgreSQL's extension mechanism.
Full `[PgTest]` generation, installation/package tooling, extension upgrade scripts, more data types,
guarded calls into PostgreSQL, and the PG13–19 matrix remain pending. The MSBuild import is repository-local;
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
(varlena uses tagged one-byte or four-byte headers, with compressed/external forms).
Generated native wrappers use PostgreSQL's own access and detoasting APIs rather than
reimplementing those layouts in managed code. Text/bytea conversions are implemented;
the full pgrx datum and memory-context API remains required work.

## Architecture direction

The target architecture consists of:

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
| `#[pg_extern]` | `[PgFunction]` + source generator (exports, DDL, metadata) | Partial: scalar/text/bytea, nullability, overloads |
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
| Type mapping (`FromDatum`/`IntoDatum`) | `Datum` converters for built-in and user-defined SQL types | Partial: scalars, text/bytea, nullable forms |
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

The phases track implementation of the complete pgrx feature surface.

- [x] **P0 — Feasibility spike**
   - [x] Minimal attributed `add(int,int)→int` extension
    - [x] `Greet(string)→string` / `greet(text)→text`
   - [x] Generated native magic, finfo, integer argument access, and managed-exception error reporting
   - [x] AOT publish and actual PostgreSQL 18 integer-function invocation
   - [x] Error path: C# exception ⇒ Postgres `ERROR`, transaction aborts cleanly, backend survives
- [ ] **P1 — Runtime core**
  - [ ] `Ankus.Runtime`: `FunctionCallInfo` reader, `Datum`/`Value`, varlena/detoast, type conversion table
  - [ ] `Ankus.PgSys`: symbol resolution (`dlopen(NULL)`+`dlsym`), P/Invoke surface (SPI, elog via shim, memory, catalog)
  - [ ] `Spi` API; `PgError`; memory contexts; `_PG_init` bootstrap
- [ ] **P2 — Source generator** (`Ankus.Generators`)
    - [x] `[PgFunction]` → per-function dispatcher + `pg_finfo` shim emission + DDL metadata
    - [x] Scalar/text/bytea conversions, inferred strictness, `T?` NULL handling, SQL overloads
   - [ ] `[PgSchema]`, explicit function options, SETOF, arrays, remaining datum mappings
  - [ ] `.ankusc` metadata section (JSON) embedded in the `.so`; `ankus schema`
- [ ] **P3 — Extension features**
  - [ ] triggers, event triggers, aggregates, operators, casts, `ExtensionSql`
  - [ ] composites (`[PostgresType]`), enums (`[PostgresEnum]`)
  - [ ] GUC options; background workers
- [ ] **P4 — Tooling** (`ankus` dotnet tool)
   - [ ] `new`, `build`, `schema`, `test`, `run`, and `package` commands
   - [x] Publish native library, `.control`, and versioned `.sql` artifacts
   - [ ] Installation and distribution packaging commands, extension upgrades
- [ ] **P5 — Multi-version matrix**
   - [ ] PostgreSQL 13–18 (+19 beta) and Windows/Linux/macOS validation matrix
- [ ] **P6 — Examples + docs**
  - [ ] `samples/` mirroring pgrx-examples (aggs, gucs, triggers, bgworker, customscan…)
    - [x] README and verified datum-boundary design notes (`docs/native-boundary.md`)
    - [ ] Complete getting-started, API, deployment, and ported-feature documentation
- [ ] **P7 — Custom scan + nodes**
   - [ ] Full custom scan provider API, native callbacks, and lifecycle integration
   - [ ] PostgreSQL node representations and pgrx node support APIs
   - [ ] Corresponding examples and backend-executed tests

## Risk register

| Risk | Mitigation |
|---|---|
| AOT `.so` inside a Postgres backend | Validate runtime initialization and signal behavior before expanding |
| Postgres `longjmp` crossing AOT frames | Design a guarded native boundary and avoid finalizer-dependent state |
| Variadic PostgreSQL C functions | Add minimal native helpers only where a non-variadic API is unavailable |
| Struct layout drift across PG versions | Generated shim + layout table generated from per-version headers; matrix tests |
| Native library size | Initial integer probe was approximately 933 KB on Linux x64 |
| `dlclose` unsupported by AOT libs | N/A — Postgres keeps extension modules loaded for the backend's lifetime |

## Milestones

- 2026-09-21 — Native AOT shared-library exports and the PostgreSQL module contract verified.
- 2026-09-22 — `c819160`: local PostgreSQL cluster harness under `tests/`.
  `dotnet test`: 30 passed, including 16 PostgreSQL integration cases.
- 2026-09-22 — `82ca5e6`: `[PgFunction]`, Roslyn incremental generation,
  metadata-only artifact extraction, and a native error boundary linked into the AOT image.
  `dotnet test`: 58 passed, including 16 PostgreSQL integration cases.
- 2026-09-22 — `e227b14`: extension control and versioned SQL files; installation with `CREATE EXTENSION`.
  `dotnet test`: 61 passed, 0 failed, 0 skipped, including 19 PostgreSQL integration cases.
- 2026-09-22 — `a5523ff`: scalar/text/bytea conversions, nullable signatures and results, SQL overloads,
  strict UTF-8 conversion, server-encoding conversion, and native buffer cleanup across PostgreSQL errors.
  The sample now includes `Greet`; backend-only datum probes live in `tests/Ankus.TestExtension`.
  `dotnet test`: 149 passed, 0 failed, 0 skipped (84 PostgreSQL integration cases).
