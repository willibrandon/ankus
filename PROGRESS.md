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

The platform-fix milestone **bdb5d1c** selects embedded runtime and compiler host **10.0.12-ankus.4**,
based on runtime fork commit `d23f4e7374cd3878dc5696fbc26ee6ecdddde1ca` and paired
with the 10.0.12 Native AOT framework and compiler targets. Complete primary CI
passes with **ankus.4**; Intel acceptance also passes. Additional-version
acceptance remains in progress.
Servicing requires
immutable patched-runtime and compiler packages, matching compiler selection,
complete backend validation and extension rebuild/redeployment. Check current
upstream servicing before releasing 0.1.0.

| Runtime line | Evidence and remaining requirements |
| --- | --- |
| .NET 10 LTS | Patched runtime and complete primary-platform suites exist. Complete supported PostgreSQL/platform combinations and final release acceptance remain required. |
| .NET 11 STS | SDK 11 RC1 targeting net10.0 passes complete PostgreSQL 18.6/Linux x64 acceptance. This does not implement net11.0. Its own patched compiler/runtime, GA and cross-platform acceptance remain required. |
| Later LTS/STS releases | Repeat explicit compiler/runtime/RID and packed-consumer acceptance, following upstream support lifecycles and servicing supported majors. |

See the [public support policy](docs/src/content/docs/reference/dotnet-support.md)
and [maintenance/acceptance plan](docs/contributing/dotnet-support.md). Each
declared .NET major must have actual evidence; SDK or package configuration is
not runtime support proof.
The .NET 11 plan also requires compiler memory-safety acceptance for raw calls,
globals, callbacks and checked managed APIs. Typed pointer contracts and explicit
scalar-call unsafe contexts pass the current complete .NET 10 compositions;
that evidence does not establish the future .NET 11 contract.

## Implementation status

This matrix summarizes current conclusions. The complete inventory linked above
defines the full scope; family-level implementation is not API-by-API completion.

| Area | Verified implementation | Remaining work |
| --- | --- | --- |
| Errors and cancellation | Sticky query cancellation/FATAL, retained raw/memory errors, fail-fast backend access and explicit rollback recovery. Primary-platform full CI passes. | Preserve these guarantees in every remaining API and performance change; complete the version/platform matrix. |
| Workers and shared memory | Native signal globals, lifecycle/transaction boundaries and shared memory. Idle Wait recovers repeated real cancellation; transaction waits still abort and terminal reports remain sticky. Complete primary-platform CI passes. | Full source-contract and complete version/platform acceptance remain required. |
| Functions and callbacks | Scalar/array/SETOF/TABLE, triggers/events, lifecycle, native callbacks, operators/conversions and installation-schema search paths. | Full upstream declaration/option audit and complete version/platform evidence. |
| Aggregates and SQL graph | Static abstract aggregate capabilities, typed Requires/Before/SupportFunction references, deterministic SQL provenance and extended module magic. | Remaining inventoried contracts and complete version/platform acceptance. |
| Generator caching | Detached equatable declaration/provider/reference models, declaration-relative locations and cached dispatcher/C/SQL artifacts across multi-declaration body edits and unrelated file insertion. Only selected SQL files are read, with the complete path catalog retained. | Broader precise diagnostics, useful semantic code fixes and final inventory audit remain required. |
| Values and ownership | Documented scalar, array, composite, temporal, JSON, network, geometry, custom codec and raw datum contracts. Interval equality, hashing and ordering agree with PostgreSQL while retaining exact components; complete primary-platform CI passes. | Remaining mapped/container contracts, parsing/formatting ergonomics and complete source-case mapping remain required. |
| Runtime performance | Owned binary PgNumeric, direct decimal coefficient encoding, constant-time fixed-width array indexing, and a measured native guard for allowlisted pure built-ins. Catalog-miss errors require actual rollback; real-resource regressions and complete primary-platform CI pass. Persistent in-backend benchmarks provide batching, transaction modes and statistical baseline comparisons. | Complete current guard supported-version acceptance. Extend that tier only where ownership proofs and measurements justify it. Never weaken error recovery to reduce overhead. |
| Native bindings | pgrx 0.19.3 inventories, matching PostgreSQL 19 beta 4 inputs and target-compiler ABI checks; typed pointers and ANKUS129 make raw caller obligations explicit. Complete current primary-platform compositions pass, with focused binding checks on all seven majors. | Full supported-major/platform tests and final inventory audit. |
| Development CLI | Version/project selection, installation/registration, cluster lifecycle, run/connect, test/regress/bench, property forwarding, environment selection, scriptable info and package prefixes. Benchmarks are measured inside PostgreSQL and retained in named comparison groups. | Persistent Windows diagnostic collection, account/privilege selection and remaining inventoried CLI/platform contracts. |
| .NET templates | Version-matched ordinary extension and worker templates reuse the CLI assets and pin local tools. Optional xUnit/NUnit consumers exercise managed and named backend cases, ignore reasons, worker processes and cleanup alongside default MSTest consumers. Complete primary-platform CI passes. | Remaining discovery contracts and complete version/platform acceptance. |
| API discoverability | Idiomatic attributed declarations, documented runtime APIs, named logging helpers and typed SPI interpolation. Compiler transport helpers are isolated in Ankus.CompilerServices and hidden from IntelliSense; complete primary-platform CI passes. | Remaining value/assertion helpers and final inventory audit. |
| Packages and release | MIT license, Brandon Williams copyright, author/repository/project metadata and deliberate SDK/runtime boundaries. | Full release gates and supported-platform packages before publishing 0.1.0. |
| Documentation and samples | Public guides, generated API pages, pgrx migration and backend-execution guidance; current status separated from historical evidence. SPI, error/reporting, bytea, strings and in-backend benchmark samples cover their complete authoring loops. | Every remaining inventoried representative sample and final usage/limitation review. |

The custom-type alignment review found no defect: variable-length PostgreSQL
types require at least four-byte datum alignment. Managed codec payload layout
is a separate contract, verified through copying/packed-field tests. Deriving
datum alignment from a CLR carrier or adding one/two-byte alignment would be
incorrect. See [the detailed review](docs/contributing/evidence/port-history.md#custom-datum-alignment-review).

## Current complete acceptance evidence

Primary CI runs all six modules against real published Native AOT extensions.
The latest successful primary CI source is **a177cfc**, with runtime **10.0.12-ankus.4**.
[CI 37448633288](https://github.com/willibrandon/ankus/actions/runs/37448633288)
and [Docs 37448633273](https://github.com/willibrandon/ankus/actions/runs/37448633273)
pass. All eighteen actual reports and all sixty-seven required native recovery
partitions independently verify, alongside the callback and prefix corpora on
every platform; no primary job timed out.

| Source | Platform / PostgreSQL | Result | Duration |
| --- | --- | --- | --- |
| Module-identity composition, parent **a177cfc / ankus.4** | Linux x64 / 18.6 | 13,210 total; 13,162 passed; 48 platform skips; zero failures | 42m42.532s tests; 43m29.757s command |
| Latest primary CI, **a177cfc / ankus.4** | Linux x64 / 18 | 13,187 total; 13,139 passed; 48 platform skips; zero failures | 40m01s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 13,187 total; 13,127 passed; 60 platform skips; zero failures | 29m08s job |
| Same CI / revision / runtime | Windows x64 / 17 | 13,187 total; 13,159 passed; 28 platform skips; zero failures | 38m11s job |
| Callback/prefix composition, **a177cfc / ankus.4** | macOS ARM64 / 18.6 | 13,187 total; 13,127 passed; 60 platform skips; zero failures | 28m21.670s tests; 29m17.922s command |
| Earlier primary CI, **5a11b8c / ankus.4** | Linux x64 / 18 | 13,111 total; 13,063 passed; 48 platform skips; zero failures | 39m55s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 13,111 total; 13,051 passed; 60 platform skips; zero failures | 29m33s job |
| Same CI / revision / runtime | Windows x64 / 17 | 13,111 total; 13,083 passed; 28 platform skips; zero failures | 38m03s job |
| Earlier primary CI, **ec19b55 / ankus.4** | Linux x64 / 18 | 13,015 total; 12,967 passed; 48 platform skips; zero failures | 39m52s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 13,015 total; 12,955 passed; 60 platform skips; zero failures | 29m31s job |
| Same CI / revision / runtime | Windows x64 / 17 | 13,015 total; 12,987 passed; 28 platform skips; zero failures | 38m14s job |
| Earlier primary CI, **f86e0ac / ankus.4** | Linux x64 / 18 | 12,630 total; 12,582 passed; 48 platform skips; zero failures | 39m27s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 12,630 total; 12,570 passed; 60 platform skips; zero failures | 34m10s job |
| Same CI / revision / runtime | Windows x64 / 17 | 12,630 total; 12,602 passed; 28 platform skips; zero failures | 35m21s job |
| [New primary CI](https://github.com/willibrandon/ankus/actions/runs/37401674049), **15efe37 / ankus.4** | macOS ARM64 / 18 | 12,799 total; 12,739 passed; 60 platform skips; zero failures | 29m40s job |
| Same CI / revision / runtime | Linux x64 / 18 | 12,799 total; 12,751 passed; 48 platform skips; zero failures | 39m40s job |
| Same CI / revision / runtime, rejected | Windows x64 / 17.11 | 12,799 total; 12,770 passed; 28 platform skips; one recovery timeout | 35m28s job; superseded by the successful crash-isolation replacement |
| [Crash-isolation replacement CI](https://github.com/willibrandon/ankus/actions/runs/37410596204), **b2fe3e7 / ankus.4** | Windows x64 / 17 | 12,799 total; 12,771 passed; 28 platform skips; zero failures | 37m11s job |
| Same replacement CI / revision / runtime | Linux x64 / 18 | 12,799 total; 12,751 passed; 48 platform skips; zero failures | 40m11s job |
| Same replacement CI, rejected | macOS ARM64 / 18 | Generator allocation regression fails; only four of six module reports exist | 30m43s job; superseded by the successful declaration/cache CI milestone |
| Allocation workload reproduction, **b2fe3e7 / ankus.4**, private phase observations | macOS ARM64 / 18.6 | 12,799 total; 12,739 passed; 60 platform skips; zero failures | 28m37.771s tests; 29m33.377s command; does not reproduce the CI failure |
| Frozen owner-lookup measurement correction, runtime **ankus.4** | macOS ARM64 / 18.6 | 12,799 total; 12,739 passed; 60 platform skips; zero failures | 28m50.398s tests; 29m46.185s command |
| Frozen crash-isolation repair, runtime **ankus.4** | Windows x64 / 17.11 | 12,799 total; 12,771 passed; 28 platform skips; zero failures | 50m30.838s tests; 51m21.747s command |
| Frozen bytea/strings and SQL-identity composition, runtime **ankus.4** | Windows x64 / 17.11 | 12,799 total; 12,771 passed; 28 platform skips; zero failures | 48m38.373s tests; 49m26.804s command |
| Frozen corrected SPI composition, **f86e0ac / ankus.4** | Windows x64 / 17.11 | 12,630 total; 12,602 passed; 28 platform skips; zero failures | 47m45.216s tests; 48m33.194s command |
| Frozen raw transport / recovery composition, runtime **ankus.4** | Linux x64 / 18.6 | 12,561 total; 12,513 passed; 48 platform skips; zero failures | 41m19.728s tests |
| Same frozen composition / runtime | Windows x64 / 17.11 | 12,561 total; 12,533 passed; 28 platform skips; zero failures | 47m38.639s tests; 48m23.936s command |

The corrected SPI composition verifies all six reports, sixty-seven native
recovery partitions and **1,791** source inputs. Its exact source/report archive
matches all **1,802** retained files. Release, API freshness, site build and site
diagnostics pass without warnings or errors. Replacement primary-platform CI
passes; current supported-version and additional-platform acceptance remain required.
The earlier frozen composition verifies all twelve reports and **1,788** source inputs.
Its Release build, API freshness and site checks pass. These results prove the
named source and PostgreSQL/platform combinations, not the complete port.

Additional-platform results below are older revisions. Refreshing them against
the current source remains required, alongside the never-covered combinations.

| Earlier complete evidence | Platform / PostgreSQL | Result | Duration |
| --- | --- | --- | --- |
| [Version CI 37154140634](https://github.com/willibrandon/ankus/actions/runs/37154140634), **c24297f / ankus.4** | Linux x64 / each of 13–17 and 19 beta 4 | 11,640 total per major; 11,623 passed; 17 platform skips; zero failures | 38m20s–40m57s jobs |
| [Version CI 37145038020](https://github.com/willibrandon/ankus/actions/runs/37145038020), **b9b7eb5 / ankus.4** | macOS ARM64 / 15 and 16 | 11,609 total per major; 11,580 passed; 29 platform skips; zero failures | 26m17s / 26m32s jobs |
| Same version CI / revision / runtime | Windows x64 / 13 and 18 | 11,609 total per major; 11,584 passed; 25 platform skips; zero failures | 33m51s / 31m46s jobs |
| [Intel CI 37131051660](https://github.com/willibrandon/ankus/actions/runs/37131051660), **04f0a8a / ankus.4** | macOS x64 / 18.6 | 11,568 total; 11,541 passed; 27 platform skips; zero failures | 3h59m34s job |

Earlier timings, outcomes, failed checks and superseded acceptance details remain
in the [evidence archive](docs/contributing/evidence/port-history.md#acceptance-table-before-spi-flow-acceptance-2026-10-05).

## Active validation and work

- **5a11b8c** is the accepted enum/backend-test milestone. Normal Release, API
  freshness, both site checks and complete primary CI pass. All eighteen actual
  reports and **67** required native recovery cases per platform independently
  verify; no primary job times out. The matching docs deployment passes after
  retrying a GitHub ID-token timeout. Crash tests retain their original deadlines,
  fsync and durability checks; warmed owner lookups preserve their allocation
  bound. The original macOS CI allocation variance remains unattributed.
- **a177cfc** separates **39** actionable callback/prefix declaration errors
  while preserving handler selection, native ABI and exact prefix registration.
  Separate unchanged-validator baselines reproduce all **54** callback and **17**
  prefix regressions; all compiled controls pass. Normal combined source passes
  **4,294** generator cases and plain, unsharded macOS ARM64/PostgreSQL **18.6**
  acceptance: **13,187** total, **13,127** passed, **60** platform skips and zero
  failures. All six reports, **67** native recovery partitions, **158** callback
  cases and **54** prefix cases verify. All **1,837** source inputs and **39**
  runtime payload inputs match after execution. The exact **1,851**-file archive
  is retained, and all **12** changed/new files are promoted byte-for-byte.
  Normal Release, API freshness and both site checks pass.
  [Replacement CI 37448633288](https://github.com/willibrandon/ankus/actions/runs/37448633288)
  passes on all three platforms. All eighteen actual reports, **67** native
  recovery cases, **158** callback cases and **54** prefix cases per platform
  independently verify. The matching
  [docs deployment](https://github.com/willibrandon/ankus/actions/runs/37448633273)
  passes. Separate callback-only
  Linux acceptance has **13,165** total, **13,117** passed, **48** skips and zero failures;
  its rejected setup run remains recorded separately.
- A separate module-identity prototype distinguishes name/version zero characters
  and malformed Unicode without changing emitted native bytes or defaults. On
  the accepted **5a11b8c** parent, the unchanged validator fails all **19** precise
  regressions while **4** compiled Unicode controls pass. The correction passes
  all **51** affected cases and **4,241** complete generator cases; all **1,840**
  authoring/prototype inputs match before and after each run. The next normal-source
  child passes all **4,317** generator cases, including both accepted corpora;
  all **1,839** source/guide inputs match before and after. Its plain, unsharded
  Linux/PostgreSQL **18.6** Native AOT suite passes: **13,210** total, **13,162**
  passed, **48** platform skips and zero failures. All six reports and all
  module/callback/prefix/native-recovery partitions verify. The **1,851**-file
  source/report archive matches every byte; all **6** changed/new files are
  promoted with **1,839** accepted inputs unchanged after the final gates.
  Normal Release, API freshness and both site checks pass. Replacement CI remains
  required; completed owned temporary files are removed and evidence is retained.
- The datetime sample draft maps all **21** upstream exports, including exact SQL
  argument names, upper-exclusive sampler bounds, calendar arithmetic, timezone
  projection and the four clock columns. Normal analyzers pass and all **58**
  focused macOS ARM64/PostgreSQL **18.6** Native AOT cases pass. Actual-report
  checks verify every behavioral partition, with all **1,845** source inputs
  unchanged. Plain, unsharded full macOS acceptance is running; no datetime sample
  source is promoted before complete acceptance. Rejected analyzer builds remain
  recorded separately, with no skipped tests or relaxed diagnostics.

Remaining diagnostics/code fixes, source-case mapping, samples, API and CLI
contracts, supported PostgreSQL/platform combinations, .NET servicing and release
acceptance remain in scope. Detailed accepted and rejected evidence is retained
in the [history](docs/contributing/evidence/port-history.md).

## Remaining work order

The accepted declaration milestones have complete primary-platform CI evidence.
The earlier editor composition passes its full native suite and replacement primary CI.
The current cold-load/reload correction also passes complete Linux/18.6 acceptance;
its replacement primary CI passes all three complete platform suites.
The Intel diagnostic retry exposed six path-related failures; the corrected
complete Intel suite now passes. An explanation of the earlier hosted-runner
disconnect and remaining version/platform acceptance are still required.

1. Resolve discovered correctness and CI failures before accepting affected work, and finish the remaining declaration/source-case audits. Malformed AssemblyRef handling and the owner index are already implemented; all eight regressions pass in the current complete suite.
2. Finish the Intel timing milestone and complete supported-major/platform coverage, including Intel macOS and the macOS 15/16 library-suffix boundary.
3. Complete declaration diagnostics/code fixes, API discoverability and unsafe raw-call contracts.
4. Use the in-backend baseline to finish measured hot-path improvements that preserve recovery and ownership guarantees.
5. Close remaining CLI, account/privilege and platform-installation contracts.
6. Complete framework/discovery support, parsing/formatting helpers, representative samples and documentation.
7. Complete .NET servicing and every release requirement in the full inventory before publishing 0.1.0. If .NET 11 reaches GA first, complete its acceptance for that release; preview validation does not block the initial .NET 10 release.

Continue independent work while CI runs. Before every commit and push, check and
record previous run outcomes, including live runs, and resolve reported failures.
Keep this file current; append detailed implementation evidence to the archive
with a clear tested revision, platform/version, outcome and any remaining scope.
