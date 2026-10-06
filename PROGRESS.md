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
The latest successful primary CI source is **f86e0ac**, with runtime **10.0.12-ankus.4**.
[CI 37386501183](https://github.com/willibrandon/ankus/actions/runs/37386501183)
and [Docs 37386500184](https://github.com/willibrandon/ankus/actions/runs/37386500184)
pass. All eighteen actual reports and all sixty-seven required native recovery
partitions independently verify; no primary job timed out.

| Source | Platform / PostgreSQL | Result | Duration |
| --- | --- | --- | --- |
| Latest primary CI, **f86e0ac / ankus.4** | Linux x64 / 18 | 12,630 total; 12,582 passed; 48 platform skips; zero failures | 39m27s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 12,630 total; 12,570 passed; 60 platform skips; zero failures | 34m10s job |
| Same CI / revision / runtime | Windows x64 / 17 | 12,630 total; 12,602 passed; 28 platform skips; zero failures | 35m21s job |
| [New primary CI](https://github.com/willibrandon/ankus/actions/runs/37401674049), **15efe37 / ankus.4** | macOS ARM64 / 18 | 12,799 total; 12,739 passed; 60 platform skips; zero failures | 29m40s job |
| Same CI / revision / runtime | Linux x64 / 18 | 12,799 total; 12,751 passed; 48 platform skips; zero failures | 39m40s job |
| Same CI / revision / runtime, rejected | Windows x64 / 17.11 | 12,799 total; 12,770 passed; 28 platform skips; one recovery timeout | 35m28s job; superseded by the successful crash-isolation replacement |
| [Crash-isolation replacement CI](https://github.com/willibrandon/ankus/actions/runs/37410596204), **b2fe3e7 / ankus.4** | Windows x64 / 17 | 12,799 total; 12,771 passed; 28 platform skips; zero failures | 37m11s job |
| Same replacement CI / revision / runtime | Linux x64 / 18 | 12,799 total; 12,751 passed; 48 platform skips; zero failures | 40m11s job |
| Same replacement CI, rejected | macOS ARM64 / 18 | Generator allocation regression fails; only four of six module reports exist | 30m43s job; corrected complete validation passes; fresh CI remains required |
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

- **15efe37** implements the bytea/strings samples and separates invalid names
  (`ANKUS002`) from duplicate SQL signatures (`ANKUS207`). The immutable Windows
  composition passes all **12,799** cases, including **75** new native sample
  executions. Release, API freshness and site checks pass. Its primary CI passes
  Linux and macOS but reports one Windows recovery timeout; that run is rejected.
- Four concurrent allocation-fault crash cases exceed the recovery deadline.
  Six terminal methods now run without parallel integration cases. All **122**
  affected Windows/**17.11** cases pass; actual timestamps prove zero overlap
  for the **26** terminal rows. Deadlines, fsync and durability checks remain.
  Release and documentation checks pass. The complete **1,813**-input Windows
  suite passes **12,799** cases; all six reports, **67** recovery cases and
  actual crash-test intervals independently verify. Replacement Windows and Linux
  CI pass all **12,799** cases; Windows terminal intervals again prove zero overlap.
  Replacement macOS CI rejects one generator allocation regression. Its native
  integration module passes, but two later unit modules did not execute.
- **7dca056** repairs reconnection before postmaster reinitialization finishes.
  Complete primary CI passes. The prior twenty Windows startup/recovery failures
  and their correction remain in the evidence archive.
- Seventeen arbitrary-address compiler transport methods require unsafe
  contexts. All **21** regressions detect the unchanged baseline and pass with
  the correction; complete primary-platform suites preserve native recovery.
  Full source-contract and supported-version acceptance remain required.
- **f86e0ac** accepts SPI construction, alias, control-flow and SQL token-context
  analysis. All **150** focused cases and complete primary CI pass; the unchanged
  analyzer fails **33** regressions. Hot-standby write-intent restrictions are
  documented from source; actual standby validation remains required.
- Trigger, initialization and worker corrections separate **68** declaration
  contracts. Their **162** invalid cases detect the respective unchanged
  validators; all **14** valid controls pass. The combined native composition
  passes all **12,985** cases on Linux x64/PostgreSQL **18.6**.
- A conditional native handler can be silently omitted while its dispatcher
  reports success. The `ANKUS276` correction rejects that attribute,
  including partial handlers and currently defined symbols. All **10** focused
  cases pass; the unchanged validator fails **8**, retaining **2** real managed
  dispatch controls. Metadata edits and repair preserve sibling caches and
  current diagnostic locations. The combined generator module passes **4,092**
  actual executions in **1m24.739s**; all named partitions independently verify.
  Its candidate preserves crash isolation. Complete Linux validation rejected the
  **1,826**-input bundle because it omitted the shared package README. All **539**
  failed rows independently match that packaging initialization error. The corrected
  **1,827**-input bundle passes all **12,985** cases: **12,937** passed and **48**
  platform skips. All six reports, named regressions, recovery cases and the
  **1,839**-file source/report archive independently verify. Its conditional-entry
  child also passes the Release and documentation gates; the separate macOS CI
  repair remains under full validation before the next declaration milestone.
- SQL functions, event triggers and backend tests have the same conditional-call
  omission hazard. Nine actual managed dispatcher executions reproduce missing
  effects and preserve ordinary/enabled-symbol controls. A shared `ANKUS277`
  correction passes all **30** declaration, compiled-call and caching cases;
  the unchanged validators fail **24**, retaining **6** compiled-call controls.
  The combined module passes all **4,122** actual cases in **54.594s**. Complete
  native acceptance of its frozen **1,829**-input child passes all **13,015**
  cases on Linux x64/PostgreSQL **18.6**: **12,967** passed, **48** platform
  skips and zero failures. All six reports, declaration partitions and native
  recovery cases independently verify. Release, API freshness, site build and
  site diagnostics pass with zero warnings and errors. Its first real-source
  build exposed `IDE0042` in a test helper; the accepted child fixes it with
  deconstruction. The retained archive independently matches **1,840** files.
  All **38** changed/new declaration files now match the accepted source bytes
  in the working tree. Combined with the separate cache-test correction, all
  **4,122** generator cases and the Release/API/site gates pass locally; fresh
  complete primary-platform CI remains required after publication.
- The macOS allocation failure remains under investigation. Its exact CI binary
  passes three complete **3,906**-case repetitions, and full workload reproduction
  passes all **12,799** cases. Reader observations show that the measured interval
  includes constructor decoding. A private correction measures the actual shared
  owner lookup after the real decoder warms it; every returned handle and exact
  string is still verified, with the original allocation bound retained. All
  **3,906** generator cases pass. Independent rescan and decoder-cache-bypass
  mutations both fail that test. The exact correction now passes all **3,906**
  Linux generator cases, Release, API freshness and both site gates in the working
  tree. The corrected macOS suite passes all **12,799** cases. All six actual
  reports, **67** native recovery partitions, **2,093** source inputs and the
  **2,101**-file source/report archive independently verify. The rejected private
  overlay run remains recorded; its project-path scoping is corrected. Fresh
  primary CI for the combined declaration/cache milestone remains required.
  The precise original CI byte variance remains
  unattributed; constructor decoding is outside the corrected cache measurement.
- Precise diagnostics, remaining code fixes, source-case mapping, samples,
  supported PostgreSQL/platform combinations, .NET servicing and release gates
  remain in scope. Detailed accepted and rejected evidence lives in the archive.
- The enum declaration correction separates thirteen contracts and preserves
  exact labels, backing values and metadata handling. Its **38** focused cases
  distinguish **27** baseline failures from **11** valid controls; all **4,160**
  corrected generator cases pass. The frozen **1,831**-input composition is
  running complete Linux/PostgreSQL **18.6** acceptance. It is not promoted yet.

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
