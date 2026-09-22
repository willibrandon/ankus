# Ankus — pgrx → .NET Native AOT Port: Progress Tracker

> A faithful port of [pgrx](https://github.com/pgcentralfoundation/pgrx) (PostgreSQL
> extensions in Rust) to **C# compiled with .NET Native AOT**, produced as a native
> shared library that PostgreSQL loads directly via `dlopen`.
>
> This document is the working tracker for the port. Update it as phases complete.

## Goal

Fully port pgrx to .NET Native AOT in the most ideal way possible, in a way that all
.NET developers will expect:

- **Faithful port**: mirror pgrx's feature surface and mental model (see [Feature map](#feature-map-pgrx--ankus)),
  translated into idiomatic C# (attributes + source generators instead of proc macros,
  `IEnumerable<T>` for SETOF, exceptions → `ereport(ERROR)`, etc.).
- **Native AOT**: the extension ships as a self-contained native `.so` (no .NET runtime
  install required on the Postgres host), built with `PublishAot`.
- **Multi-version**: one C# codebase targeting PostgreSQL 13–18 (+19 beta), matching
  pgrx's supported versions. Version differences are isolated in a small generated C shim.
- **Developer experience** modeled on `cargo pgrx`: `ankus new / build / schema / test / run / package`.

## Environment

| Item | Value |
|---|---|
| .NET SDK | 10.0.400 (Native AOT mature) |
| Docker | 29.7.2 — test images: `postgres:18` (primary), `postgres:16` (fetched); pull per version when testing a matrix leg |
| C toolchain | clang 21 + lld (AOT uses its own bundled clang anyway); gcc 14 fallback |
| References | `~/src/pgrx` (read-only), `~/src/postgres` (read-only, tags `REL_13_23`…`REL_18_6`, `REL_19_BETA3`), `~/src/runtime`, `~/src/roslyn` |
| Primary test target | **PostgreSQL 18** (19 imminent; pgrx supports 13–18 + 19 beta) |

## Key research findings (verified)

### .NET Native AOT (Microsoft docs, verified)

- Publishing a **class library** with `<PublishAot>true</PublishAot>` produces a
  **self-contained native shared library** (`.so`) consumable from non-.NET code
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
   - **PG 18**: `len == 76 && memcmp(&module->abi_fields, &server_abi_fields, sizeof(Pg_abi_values)) == 0`
     (name/version pointers may be NULL).
3. `dlsym(handle, "_PG_init")` — called if present (central declaration since PG 12;
   older servers also look for `PG_init` — shim exports both).
4. Function lookup (`fmgr.c`): `dlsym` for `pg_finfo_<name>` (a **function** returning
   `const Pg_finfo_record *` where `Pg_finfo_record = { int api_version /* =1 */ }`),
   falling back to `<name>` directly.

### `Pg_magic_struct` version matrix (must be compiled per PG version, like pgrx)

| PG | sizeof | layout | values |
|----|--------|--------|--------|
| 13 | 24 | `{len, version, funcmaxargs, indexmaxkeys, namedatalen, float8byval}` | 24, 13, 100, 32, 64, 1 |
| 14 | 24 | same | 24, 14, 100, 32, 64, 1 |
| 15 | 56 | same + `char abi_extra[32]` | 56, 15, 100, 32, 64, 1, "PostgreSQL" |
| 16 | 56 | same | 56, 16, 100, 32, 64, 1, "PostgreSQL" |
| 17 | 56 | same | 56, 17, 100, 32, 64, 1, "PostgreSQL" |
| 18 | 76 | `{len, Pg_abi_values{version, funcmaxargs, indexmaxkeys, namedatalen, float8byval, abi_extra[32]}, name*, version*}` | 76, {18, 100, 32, 64, 1, "PostgreSQL"}, NULL, NULL |

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
(varlena: 4-byte int32 header — negative = short header — or 1-byte packed header).

## Architecture

```
┌──────────────────────────────────────────────────────────────────────┐
│  ankus .so  (Native AOT image, one per PG major version)             │
│                                                                      │
│  C# (source-generated + hand-written)                               │
│  ├── [UnmanagedCallersOnly(EntryPoint="fn_name")] dispatchers       │
│  │     read FunctionCallInfoBaseData, unmarshal args, invoke the     │
│  │     user C# function, marshal result, set fcinfo->isnull         │
│  │     all exceptions ⇒ ereport(ERROR) (pgrx #[pg_guard] analogue)   │
│  ├── [UnmanagedCallersOnly(EntryPoint="_PG_init")] bootstrap        │
│  │     resolve PG symbols (dlopen(NULL)+dlsym), register state       │
│  ├── Ankus.Runtime: Spi, PgError, Datum/Value, memory contexts,     │
│  │     GUC, triggers, aggregates, background workers, …             │
│  └── embedded SQL-entity metadata (ELF section ".ankusc")           │
│        ⇒ `ankus schema` reads schema from the built .so             │
│          (faithful analogue of pgrx v18 ".pgrxsc" one-compile model) │
│                                                                      │
│  C shim (generated per PG version, compiled with clang, linked in): │
│  ├── Pg_magic_func / PG_MODULE_MAGIC (version-specific layout)      │
│  ├── per-function pg_finfo_<name>() ⇒ { api_version = 1 }           │
│  ├── PG_init() { _PG_init(); }  (compat alias for PG ≤ 11-style)    │
│  └── variadic C helpers (ereport wrappers) for C# callers           │
└──────────────────────────────────────────────────────────────────────┘
        loaded by PostgreSQL via dlopen (RTLD_NOW|RTLD_GLOBAL)
        PG symbols (SPI_*, elog, …) resolved at runtime from the host process
```

Key decisions:

1. **All SQL-facing entry points are C#** (`UnmanagedCallersOnly` exports). No C trampoline
   per function — the C shim only provides what only C can: the magic struct, finfo
   records, and variadic helpers. This is the most .NET-idiomatic decomposition.
2. **PG API calls** go through `LibraryImport` resolved from the host process
   (`dlopen(NULL)` + `dlsym`), mirroring pgrx's `pg_sys` undefined-symbol design.
   Raw access lives in `Ankus.PgSys` (analogue of `pgrx::pg_sys`).
3. **Errors**: unhandled C# exceptions at the export boundary are caught and translated
   to `ereport(ERROR, ...)` — the exact analogue of pgrx's `#[pg_guard]`. Postgres'
   `longjmp` over AOT frames is accepted as a documented constraint (same as pgrx/Rust).
4. **Multi-version**: C# core is version-independent; the generated C shim carries the
   version-specific constants. `ankus build -p 13…18` produces one `.so` per version
   (same matrix model as `cargo pgrx`).
5. **Schema generation**: metadata embedded in the built `.so` (ELF section), read by
   `ankus schema` — faithful to pgrx v18's one-compile `.pgrxsc` design.
6. **Naming**: project `ankus` (to steer the rhino 🦏 → steer Postgres).
   Namespace root `Ankus`; SQL-facing defaults follow pgrx conventions (snake_case names).

## Feature map (pgrx → Ankus)

| pgrx | Ankus | status |
|---|---|---|
| `#[pg_extern]` | `[PgFunction]` + source generator (exports, DDL, metadata) | ☐ |
| `#[pg_schema]` | `[PgSchema("name")]` | ☐ |
| `#[pg_guard]` | automatic at export boundary (always on) | ☐ |
| SETOF (`SetOfIterator`) | return `IEnumerable<T>` ⇒ `RETURNS SETOF` | ☐ |
| `#[pg_trigger]` | `[PgTrigger]` | ☐ |
| `#[pg_event_trigger]` | `[PgEventTrigger]` | ☐ |
| `#[pg_aggregate]` + `Aggregate` trait | `[PgAggregate]` + `IAggregate<TState>` (init/transition/combine/final, (de)serializable) | ☐ |
| `#[pg_operator]` | `[PgOperator]` (+ SQL DDL) | ☐ |
| `#[pg_cast]` | `[PgCast]` (+ SQL DDL) | ☐ |
| `extension_sql!` | `[ExtensionSql]` attribute / `.sql` files | ☐ |
| `#[derive(PostgresType)]` (composites) | `[PostgresType]` on records + generator | ☐ |
| `#[derive(PostgresEnum)]` | `[PostgresEnum]` on C# enums + generator (CREATE TYPE) | ☐ |
| Type mapping (`FromDatum`/`IntoDatum`) | `Datum`/`Value` conversion table (int2/4/8, float4/8, text, bool, numeric, dates/times, uuid, bytea, json(b), arrays, composites, enums, `T?` ⇒ NULL) | ☐ |
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
| custom scan (`pgrx::customscan`, `pgrx::nodes`) | later phase (large surface) | ☐ |
| `cargo pgrx` CLI | `ankus` dotnet tool: `new/init/build/schema/test/run/package` | ☐ |
| `cargo pgrx schema` (one-compile, `.pgrxsc`) | `ankus schema` (reads `.ankusc` section from built `.so`) | ☐ |
| pgrx-examples | `samples/` mirroring the example set | ☐ |

## Phase plan

- [ ] **P0 — Feasibility spike** *(current)*
  - [ ] Minimal extension: `hello(text)→text`, `add(int,int)→int` hand-written `UnmanagedCallersOnly` exports
  - [ ] C shim: `Pg_magic_func` (PG 18 layout), `pg_finfo_*`, `PG_init` alias, `ankus_ereport`
  - [ ] AOT publish → `ankus.so`; load into docker `postgres:18`; `SELECT` works
  - [ ] Error path: C# exception ⇒ Postgres `ERROR`, transaction aborts cleanly, backend survives
- [ ] **P1 — Runtime core**
  - [ ] `Ankus.Runtime`: `FunctionCallInfo` reader, `Datum`/`Value`, varlena/detoast, type conversion table
  - [ ] `Ankus.PgSys`: symbol resolution (`dlopen(NULL)`+`dlsym`), P/Invoke surface (SPI, elog via shim, memory, catalog)
  - [ ] `Spi` API; `PgError`; memory contexts; `_PG_init` bootstrap
- [ ] **P2 — Source generator** (`Ankus.Analyzers`)
  - [ ] `[PgFunction]` → per-function dispatcher + `pg_finfo` shim emission + DDL metadata
  - [ ] `[PgSchema]`, strictness, SETOF, `T?` NULL handling, arrays
  - [ ] `.ankusc` metadata section (JSON) embedded in the `.so`; `ankus schema`
- [ ] **P3 — Extension features**
  - [ ] triggers, event triggers, aggregates, operators, casts, `ExtensionSql`
  - [ ] composites (`[PostgresType]`), enums (`[PostgresEnum]`)
  - [ ] GUC options; background workers
- [ ] **P4 — Tooling** (`ankus` dotnet tool)
  - [ ] `new` (template), `build` (AOT + shim, per PG version), `schema`, `test` (docker matrix), `run` (psql), `package` (`.so` + `.control` + `.sql` layout)
- [ ] **P5 — Multi-version matrix**
  - [ ] shim generator for 13–18 (+19 beta), CI matrix in docker
- [ ] **P6 — Examples + docs**
  - [ ] `samples/` mirroring pgrx-examples (aggs, gucs, triggers, bgworker, customscan…)
  - [ ] README, getting started, design notes, SAFETY notes (longjmp constraints)
- [ ] **P7 — Custom scan + nodes** (largest pgrx surface; deferred)

## Conventions (user directives)

- **C# best practices for 2026**; formatting "like the dotnet team": style follows the
  `dotnet/runtime` repo conventions + the strictness of `ilrepl`'s `.editorconfig`
  (merged into this repo's `.editorconfig`): file-scoped namespaces, `var` when
  apparent, one type per file (IDE0044 error), XML doc comments on **all public and
  internal** members, nothing over 140 characters, Allman braces in C shim code.
- **Commits**: commit incrementally whenever a coherent unit lands.
- **Testing**: faithful to pgrx's approach — SQL-level integration tests against a real
  PostgreSQL instance (docker), asserting behavior, errors, GUCs, triggers, etc., plus
  unit tests for conversion logic. Test stack: **MSTest v4 on Microsoft.Testing.Platform
  (MTP)** (`dotnet test` compatible).

## Risk register

| Risk | Mitigation |
|---|---|
| AOT `.so` dlopen'd inside Postgres backend (PAL init, GC threads, signal handlers) | Spike validates first; monitor for signal-handler clashes; AOT libs are designed for exactly this (custom frameworks) |
| Postgres `longjmp` (ereport ERROR) crossing AOT frames skips C# unwinding | Accept as documented constraint (pgrx does the same in Rust); avoid finalizer-dependent state; no re-entry across errors |
| Variadic PG C functions (`elog`, `ereport`, `appendStringInfo`) can't be P/Invoked directly | C shim variadic helpers (`ankus_ereport(elevel, sqlstate, msg)`) |
| Struct layout drift across PG versions | Generated shim + layout table generated from per-version headers; matrix tests |
| AOT size in extension `.so` (~10–30 MB) | Documented; `OptimizeForSize` later if needed |
| `dlclose` unsupported by AOT libs | N/A — Postgres keeps extension modules loaded for the backend's lifetime |

## Log

- 2026-09-21 — Goal set. Environment verified (SDK 10.0.400, docker, postgres:18 pulled).
  AOT shared-library + C-export path confirmed via Microsoft docs. PG module contract
  (magic, finfo, `_PG_init`) verified from `~/src/postgres` for PG 13–18. pgrx v18
  one-compile (`.pgrxsc`) design noted as the model for `ankus schema`.
  User direction: primary test target PG 18 (19 imminent); compatibility across all
  pgrx-supported versions; faithful port of pgrx's feature surface.
