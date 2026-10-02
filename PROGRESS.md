# Ankus — pgrx → .NET Native AOT Port: Progress Tracker

Ankus is a faithful port of pgrx to idiomatic C# and .NET Native AOT. The port
is still in progress. Passing implemented subsets does not establish full parity.

## Goal and complete scope

Implement pgrx's runtime APIs, declaration macros, extension features, native
bindings, tooling, examples and testing contracts. Preserve PostgreSQL errors,
transactions, exact values, SQL NULL, type identity, encoding and native ownership.
Consumers use ordinary C# attributes, source generation, NuGet project SDKs,
`dotnet publish` and `dotnet test`, without runtime code generation or unbounded
reflection. Higher-level custom scan providers are additional Ankus scope.

Completion requires the supported PostgreSQL 13–18 and 19 beta contracts on
Linux x64, Windows x64, macOS ARM64 and macOS x64, including real published
extensions, backend recovery, complete suites and release/package validation.
The initial release is **0.1.0**; no NuGet packages are published yet.

The upstream inventory is pinned to pgrx **0.19.3**, commit
`fc91c63ebad11784647b50ee7e265c1fd9c9924f`. See:

- [Complete requirement inventory](docs/contributing/evidence/parity-requirements.md): every inventoried command, API family, test corpus and release contract remains in scope.
- [Original phase plan](docs/contributing/evidence/port-history.md#phase-plan): the full implementation requirements and original milestones.
- [Implementation and acceptance history](docs/contributing/evidence/port-history.md): complete retained evidence, including failures and later corrections. Historical completion states may be superseded.
- [Extension-author documentation](docs/src/content/docs/) and [development prerequisites](docs/contributing/development.md).

## .NET support and servicing plan

Repository `global.json` selects stable SDK **10.0.400** with
`rollForward: latestFeature` and prereleases disabled. The initial extension
target is **net10.0**. Installing another SDK does not silently change an
extension's selected compiler, target framework or embedded runtime.

The current embedded runtime is **10.0.12-ankus.2**, paired with the 10.0.12
Native AOT compiler/framework packs. Servicing requires immutable patched-runtime
packages, matching compiler selection, complete backend validation and extension
rebuild/redeployment. Check current upstream servicing before releasing 0.1.0.

| Runtime line | Evidence and remaining requirements |
| --- | --- |
| .NET 10 LTS | Patched runtime and complete primary-platform suites exist. Complete supported PostgreSQL/platform combinations and final release acceptance remain required. |
| .NET 11 STS | SDK 11 RC1 targeting net10.0 passes complete PostgreSQL 18.6/Linux x64 acceptance. This does not implement net11.0. Its own patched compiler/runtime, GA and cross-platform acceptance remain required. |
| Later LTS/STS releases | Repeat explicit compiler/runtime/RID and packed-consumer acceptance, following upstream support lifecycles and servicing supported majors. |

See the [public support policy](docs/src/content/docs/reference/dotnet-support.md)
and [maintenance/acceptance plan](docs/contributing/dotnet-support.md). Each
declared .NET major must have actual evidence; SDK or package configuration is
not runtime support proof.

## Implementation status

This matrix summarizes current conclusions. The complete inventory linked above
defines the full scope; family-level implementation is not API-by-API completion.

| Area | Verified implementation | Remaining work |
| --- | --- | --- |
| Errors and cancellation | Sticky query cancellation/FATAL, retained raw/memory errors, fail-fast backend access and explicit rollback recovery. Primary-platform full CI passes. | Preserve these guarantees in every remaining API and performance change; complete the version/platform matrix. |
| Workers and shared memory | Native signal globals, errno preservation, lifecycle/connection/transaction callbacks, shared-memory attachment and bounded containers. | Full source-contract and version/platform acceptance, including all worker phases. |
| Functions and callbacks | Scalar/array/SETOF/TABLE, triggers/events, lifecycle, native callbacks, operators/conversions and installation-schema search paths. | Full upstream declaration/option audit and complete version/platform evidence. |
| Aggregates and SQL graph | Static abstract aggregate capabilities, typed Requires/Before/SupportFunction references, deterministic SQL provenance and extended module magic. | Remaining inventoried contracts and complete version/platform acceptance. |
| Generator caching | Detached equatable declaration/provider/reference models and cached dispatcher/C/SQL artifact rendering, with tracked-step regressions. | Broader precise diagnostics, useful semantic code fixes and final inventory audit. |
| Values and ownership | Documented scalar, array, composite, temporal, JSON, network, geometry, custom codec and raw datum contracts. Interval ordering matches PostgreSQL outside the backend. | Remaining mapped/container contracts, parsing/formatting ergonomics and complete source-case mapping. |
| Runtime performance | Recovery and allocator invariants have native tests. | Owned binary PgNumeric, appropriately guarded pure operations, array costs and measured benchmarks. Never weaken error recovery to reduce overhead. |
| Native bindings | pgrx 0.19.3 inventories, matching PostgreSQL 19 beta 4 inputs and target-compiler ABI checks; focused binding checks cover all seven majors. | Full supported-major/platform tests, raw-call unsafe visibility and final inventory audit. |
| Development CLI | Version/project selection, installation/registration, cluster lifecycle, run/connect, test/regress, property forwarding, environment selection, scriptable info and package prefixes. | Account/privilege selection, in-backend benchmarking and remaining inventoried CLI/platform contracts. |
| .NET templates | Version-matched ordinary extension and worker templates reuse the CLI assets, pin local tools and run real backend/MSTest consumers. Complete primary-platform CI passes. | Additional test-framework templates and discovery documentation. |
| API discoverability | Idiomatic attributed declarations, documented runtime APIs and named literal/structured logging helpers with native recovery tests. | Compiler-helper namespaces/visibility, convenient safe SPI binding and remaining value/assertion helpers. |
| Packages and release | MIT license, Brandon Williams copyright, author/repository/project metadata and deliberate SDK/runtime boundaries. | Full release gates and supported-platform packages before publishing 0.1.0. |
| Documentation and samples | Public guides, generated API pages, pgrx migration and backend-execution guidance; current status separated from historical evidence. | Every inventoried representative sample and final usage/limitation review. |

The custom-type alignment review found no defect: variable-length PostgreSQL
types require at least four-byte datum alignment. Managed codec payload layout
is a separate contract, verified through copying/packed-field tests. Deriving
datum alignment from a CLR carrier or adding one/two-byte alignment would be
incorrect. See [the detailed review](docs/contributing/evidence/port-history.md#custom-datum-alignment-review).

## Current complete acceptance evidence

These are complete six-module suites with real Native AOT extensions, not smoke
tests or binding-only probes. Counts supplement the native behavior assertions.

| Source | Platform / PostgreSQL | Result | Duration |
| --- | --- | --- | --- |
| 3c5ab73 composition | Linux x64 / 13.23 | 10,978 total; 10,964 passed; 14 platform skips; zero failures | 23m02.233s |
| 99660ea template composition | Linux x64 / 17.11 | 10,984 total; 10,970 passed; 14 platform skips; zero failures | 24m20.771s |
| [CI 36983211444](https://github.com/willibrandon/ankus/actions/runs/36983211444), 3c5ab73 | Linux x64 / 18 | 10,978 total; 10,964 passed; 14 platform skips; zero failures | 26m00s job |
| Same CI / revision | macOS ARM64 / 18 | 10,978 total; 10,952 passed; 26 platform skips; zero failures | 19m38s job |
| Same CI / revision | Windows x64 / 17 | 10,978 total; 10,953 passed; 25 platform skips; zero failures | 24m25s job |
| [CI 36988584773](https://github.com/willibrandon/ankus/actions/runs/36988584773), 99660ea | Linux x64 / 18 | 10,984 total; 10,970 passed; 14 platform skips; zero failures | 26m40s job |
| Same CI / revision | macOS ARM64 / 18 | 10,984 total; 10,958 passed; 26 platform skips; zero failures | 20m12s job |
| Same CI / revision | Windows x64 / 17 | 10,984 total; 10,959 passed; 25 platform skips; zero failures | 24m59s job |
| [Version CI 36988634319](https://github.com/willibrandon/ankus/actions/runs/36988634319), 99660ea | Linux x64 / 13 | 10,984 total; 10,970 passed; 14 platform skips; zero failures | 26m15s job |
| Same CI / revision | Linux x64 / 14 | 10,984 total; 10,970 passed; 14 platform skips; zero failures | 27m06s job |
| Same CI / revision | Linux x64 / 15 | 10,984 total; 10,970 passed; 14 platform skips; zero failures | 26m57s job |
| Same CI / revision | Linux x64 / 16 | 10,984 total; 10,970 passed; 14 platform skips; zero failures | 27m12s job |
| Same CI / revision | Linux x64 / 17 | 10,984 total; 10,970 passed; 14 platform skips; zero failures | 27m28s job |
| 0e0db27 logging composition | Linux x64 / 18.6 | 11,027 total; 11,013 passed; 14 platform skips; zero failures | 24m57.780s tests |
| [CI 36998501676](https://github.com/willibrandon/ankus/actions/runs/36998501676), 0e0db27 | Linux x64 / 18 | 11,027 total; 11,013 passed; 14 platform skips; zero failures | 26m39s job |
| Same CI / revision | macOS ARM64 / 18 | 11,027 total; 11,001 passed; 26 platform skips; zero failures | 20m05s job |
| Same CI / revision | Windows x64 / 17 | 11,027 total; 11,002 passed; 25 platform skips; zero failures | 25m20s job |
| [CI 37003911215](https://github.com/willibrandon/ankus/actions/runs/37003911215), 906b752 | Linux x64 / 18 | 11,027 total; 11,013 passed; 14 platform skips; zero failures | 26m43s job |
| Same CI / revision | macOS ARM64 / 18 | 11,027 total; 11,001 passed; 26 platform skips; zero failures | 20m05s job |
| Same CI / revision | Windows x64 / 17 | 11,027 total; 11,002 passed; 25 platform skips; zero failures | 25m23s job |
| 906b752 with both regression-fixture corrections | Linux x64 / 19 beta 4 | 11,027 total; 11,013 passed; 14 platform skips; zero failures | 26m39.146s tests |

The accepted template composition passes Release with **zero warnings/errors,
1m38.33s**. API freshness verifies **235 pages / 2,700 members**; the site builds
**283 pages in 2.98s**, with zero check errors, warnings or hints. All **251**
owned source identities match the tested draft, validator and committed source.
[Docs 36988584868](https://github.com/willibrandon/ankus/actions/runs/36988584868)
also passes on 99660ea.

The original Windows regression-output failure is corrected by 5ea18b9, with
complete replacement CI passing on all three primary platforms. The later SPI
parallel child-identity failure is corrected by 0c96349; repair and composed
primary-platform CI pass without weakening the original assertions.

## Active validation and work

- Both PostgreSQL 19 regression-fixture corrections now pass the complete six-module Native AOT suite: **11,027 total; 11,013 passed; zero failures; 14 platform skips; 26m39.146s**. All 267 frozen source identities verify after completion. Final composed Release passes with zero warnings/errors (**1m28.83s**); API freshness verifies **235 pages / 2,726 members**; site build/check passes with zero diagnostics. No ambient major override or relaxed CLI validation is used. The focused property fixture also passes against PostgreSQL 18.6. Replacement compatibility CI remains required after publishing the fixes.
- The final CLI property fixture correction also passes in the SPI composition against PostgreSQL **18.6/Linux x64**: **one selected case; zero failures/skips; 4m42.686s**, including real native publication, installation and query assertions. All 279 frozen source identities verify after completion. Final composed Release/documentation and complete-suite acceptance remain required before committing.
- Binding timing automation is committed and pushed as **906b752**. [Primary CI 37003911215](https://github.com/willibrandon/ankus/actions/runs/37003911215) passes quality, runtime and all three complete primary-platform suites; counts above come from six completed TRX modules per platform. [Docs 37003911206](https://github.com/willibrandon/ankus/actions/runs/37003911206) passes. [Intel macOS 37003910952](https://github.com/willibrandon/ankus/actions/runs/37003910952) completes preparation and reaches the full suite with two package-consumer slots and the new early task timings. No performance correction or Intel acceptance is claimed before completed evidence.
- [Template CI 36988584773](https://github.com/willibrandon/ankus/actions/runs/36988584773), **99660ea**: quality, all runtime jobs and all three complete platform suites pass. Counts above come from all six completed TRX modules per platform.
- [Additional-major CI 36988634319](https://github.com/willibrandon/ankus/actions/runs/36988634319), **99660ea**: runtime and complete PostgreSQL 13–17 suites pass. PostgreSQL 19 completes all six modules with **10,984 total; 10,969 passed; one failure; 14 platform skips**. Database-removal setup enables `standard_conforming_strings=off`, which PostgreSQL 19 rejects with **0A000**. An isolated repair retains and verifies the older setting on 13–18, asserts 19's exact rejection and `on` setting, and preserves every database name/data/recovery check. Focused Linux x64 checks pass **4/4** each on PostgreSQL **19 beta 4** (**2m26.599s**) and **18.6** (**3m02.058s**), with zero skips and all 267 frozen source identities verified afterward. Composed-source Release passes with zero warnings/errors (**1m20.35s**), API freshness verifies **235 pages / 2,726 members**, and site build/check passes with zero diagnostics. The final complete PostgreSQL 19 repair composition passes, as recorded above. Every job retains the 60-minute timeout.
- [Logging CI 36998501676](https://github.com/willibrandon/ankus/actions/runs/36998501676), **0e0db27**: quality, all three runtime jobs and all three full primary-platform suites pass. Counts above come from all six completed TRX modules per platform. [Docs 36998501642](https://github.com/willibrandon/ankus/actions/runs/36998501642) also passes.
- Intel macOS [two-slot 36986333238](https://github.com/willibrandon/ankus/actions/runs/36986333238) and [four-slot 36986335992](https://github.com/willibrandon/ankus/actions/runs/36986335992) compare the same **3c5ab73** code. Both exceed the 60-minute limit; job durations including cleanup are 61m00s and 62m22s. Each retains five completed modules: 6,597 total, 6,588 passed, nine skips, zero failures; neither has a completed integration report. Both restore the same NuGet and native binding cache baseline and use LLVM 20.1.8. Initial builds take 8m53s and 12m04s. Retained publication timings require root-cause analysis; neither is full platform acceptance.
- The prior Intel [two-slot run 36977693891](https://github.com/willibrandon/ankus/actions/runs/36977693891) timed out at 60 minutes. Five completed modules report 6,578 total, 6,569 passed, nine skips and zero failures; integration has no completed report. This is not Intel platform acceptance. Its 67 retained build-timing reports overlap and include initial solution preparation.
- Logging helpers provide literal/structured severity methods and genuine nonreturning terminal contracts. Managed checks pass 75/75, and focused PostgreSQL 18.6/Linux x64 checks pass 58/58, including same-backend recovery with PostgreSQL's own client. The complete unchanged-source suite and fresh full primary-platform CI pass. Release passes with zero warnings/errors; API freshness verifies 235 pages / 2,726 members and the site check has zero diagnostics.
- Earlier complete local logging runs fail from missing staged runtime prerequisites and exhausted RAM-backed temporary storage. The matching CI runtime payload is verified and the complete disk-backed rerun passes. All negative outcomes and corrections remain in [the retained history](docs/contributing/evidence/port-history.md#current-logging-and-evidence-validation); contributor prerequisites document temporary build capacity.
- Intel timing investigation retains raw job logs, which show progress omitted by ordinary searches that stop at embedded NUL test values. Completed consumer cases can take several minutes. A private reader extracts individual binding-task durations from existing binlogs without printing paths or command arguments; Actual Intel phase measurements now exist; finer verification/compilation measurements and a verified correction remain required.
- Accepted repository timing automation separates source verification, managed companion compilation and native linking in retained logs. Controlled event-log and real report-preparation checks pass, including overlapping nodes/submissions, exact durations/outcomes, incomplete logs, text/XML redaction and exclusion of raw binary uploads. Real completed Linux logs agree with the private prototype. Hosted builds print the first timings before tests finish. Actual Intel measurements are recorded above; a verified performance correction remains pending.
- The binding timing automation passes the plain PostgreSQL 18.6/Linux x64 suite: **11,027 total; 11,013 passed; zero failures; 14 platform skips; 25m10.158s**. All 266 frozen identities verify after terminal success. Final composed-evidence checks pass Release with zero warnings/errors (**31.24s**), API freshness (**235 pages / 2,726 members**), site build and zero check diagnostics. Actual Intel phase measurements and a verified performance correction remain required.
- The SPI ergonomics draft adds explicit `Spi.Sql` parameterized interpolation and typed command overloads, preserving declared types/SQL NULL, string APIs, snapshot roles and native guards. Compiler support lives in `Ankus.CompilerServices`. Focused managed/compiler/native checks pass **15/15**, **8/8** and **70/70**. The complete frozen-source PostgreSQL **18.6/Linux x64** suite passes **11,120 total; 11,106 passed; zero failures; 14 platform skips; 24m38.017s**, and all 279 source identities verify afterward. Release passes with zero warnings/errors, API freshness verifies **238 pages / 2,756 members**, and the site builds **286 pages** with zero check diagnostics. The first full attempt's invalid validation installation path is corrected and retained in the history. Composition with the final CLI fixture correction and final verification remain required before committing.
- The first complete PostgreSQL 19 repair run exposes a second fixture defect after the original database-removal repair passes. Both defects are corrected and the final complete suite passes, as recorded above; the original one-failure outcome remains in the retained history.
- Focused final PostgreSQL 19 checks pass **five cases, zero failures/skips, 4m58.563s**, including database prerequisites and CLI property forwarding. The same property fixture passes against PostgreSQL **18.6/Linux x64**, with **one selected case, zero failures/skips, 4m42.686s**.
- Intel macOS [37003910952](https://github.com/willibrandon/ankus/actions/runs/37003910952), **906b752 / two package slots**, exceeds the 60-minute limit again; total job duration including cleanup is **61m23s**. Five completed modules report **6,621 total; 6,612 passed; nine skips; zero failures**; integration has no completed report. Preparation takes **13m57s**, and full tests run **43m27s** before timeout. Individual phase evidence now proves initial source production **111.159s** and companion production **219.510s**, with overlapping cache readers waiting; representative later source/companion reuse takes **29.489s / 17.669s** and **15.579s / 9.618s**. These are command durations, including verification and waiting, not isolated CPU costs. A measured correction and complete Intel acceptance remain required.
- A read-only compiler-contract namespace audit identifies **53 helper types across 81 declarations**, including **16 public types, all hidden from IntelliSense**, and **211 generated/fixture literal reference sites**. This confirms that file count overstates the public surface. Deliberate relocation of compiler-only contracts and complete generator/consumer validation remain open; author APIs and native behavior must be preserved.

Keep complete version/platform gaps visible. The scheduled workflow, labels,
installed prerequisites or an unfinished suite do not replace successful results.
Every job retains the requested **60-minute** limit. Record measured durations
and timeouts; do not shard the complete suite or cancel runs automatically.

## Remaining work order

1. Resolve discovered correctness and CI failures before accepting affected work.
2. Finish the Intel timing milestone and complete supported-major/platform coverage, including Intel macOS and the macOS 15/16 library-suffix boundary.
3. Complete declaration diagnostics/code fixes, API discoverability and unsafe raw-call contracts.
4. Implement binary numeric storage and appropriate guard/array improvements with recovery tests and measured benchmarks.
5. Close remaining CLI, account/privilege, benchmark and platform-installation contracts.
6. Complete framework/discovery support, parsing/formatting helpers, representative samples and documentation.
7. Complete .NET servicing and every release requirement in the full inventory before publishing 0.1.0. If .NET 11 reaches GA first, complete its acceptance for that release; preview validation does not block the initial .NET 10 release.

Continue independent work while CI runs. Before every commit and push, check and
record previous run outcomes, including live runs, and resolve reported failures.
Keep this file current; append detailed implementation evidence to the archive
with a clear tested revision, platform/version, outcome and any remaining scope.

Final logging/documentation verification passes Release with zero warnings or
errors (**32.32s**), API freshness (**235 pages / 2,726 members**), site build
(**283 pages, 3.16s**) and zero check diagnostics. Preceding CI and queued/live
version jobs are checked and recorded again before commit. Detailed negative,
corrected and final evidence is retained in the linked history.
