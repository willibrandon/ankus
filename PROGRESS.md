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
| Runtime performance | Owned binary PgNumeric, direct decimal coefficient encoding, native recovery/allocator tests and measured query comparisons. | Appropriately guarded pure operations, array costs and permanent benchmarks. Never weaken error recovery to reduce overhead. |
| Native bindings | pgrx 0.19.3 inventories, matching PostgreSQL 19 beta 4 inputs and target-compiler ABI checks; typed pointers and ANKUS129 make raw caller obligations explicit. Complete current primary-platform compositions pass, with focused binding checks on all seven majors. | Full supported-major/platform tests and final inventory audit. |
| Development CLI | Version/project selection, installation/registration, cluster lifecycle, run/connect, test/regress, property forwarding, environment selection, scriptable info and package prefixes. | Persistent Windows diagnostic collection, account/privilege selection, in-backend benchmarking and remaining inventoried CLI/platform contracts. |
| .NET templates | Version-matched ordinary extension and worker templates reuse the CLI assets and pin local tools. Optional xUnit/NUnit consumers exercise managed and named backend cases, ignore reasons, worker processes and cleanup alongside default MSTest consumers. Complete primary-platform CI passes. | Remaining discovery contracts and complete version/platform acceptance. |
| API discoverability | Idiomatic attributed declarations, documented runtime APIs, named logging helpers and typed SPI interpolation. Compiler transport helpers are isolated in Ankus.CompilerServices and hidden from IntelliSense; complete primary-platform CI passes. | Remaining value/assertion helpers and final inventory audit. |
| Packages and release | MIT license, Brandon Williams copyright, author/repository/project metadata and deliberate SDK/runtime boundaries. | Full release gates and supported-platform packages before publishing 0.1.0. |
| Documentation and samples | Public guides, generated API pages, pgrx migration and backend-execution guidance; current status separated from historical evidence. | Every inventoried representative sample and final usage/limitation review. |

The custom-type alignment review found no defect: variable-length PostgreSQL
types require at least four-byte datum alignment. Managed codec payload layout
is a separate contract, verified through copying/packed-field tests. Deriving
datum alignment from a CLR carrier or adding one/two-byte alignment would be
incorrect. See [the detailed review](docs/contributing/evidence/port-history.md#custom-datum-alignment-review).

## Current complete acceptance evidence

Primary CI below runs all six modules against real published Native AOT
extensions. Counts supplement the backend, ownership and recovery assertions.

| Source | Platform / PostgreSQL | Result | Duration |
| --- | --- | --- | --- |
| [CI 37149579835](https://github.com/willibrandon/ankus/actions/runs/37149579835), c24297f / runtime **ankus.4** | Linux x64 / 18 | 11,640 total; 11,623 passed; 17 platform skips; zero failures | 36m33s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 11,640 total; 11,611 passed; 29 platform skips; zero failures | 26m54s job |
| Same CI / revision / runtime | Windows x64 / 17 | 11,640 total; 11,615 passed; 25 platform skips; zero failures | 32m40s job |
| [Version CI 37154140634](https://github.com/willibrandon/ankus/actions/runs/37154140634), c24297f / runtime **ankus.4** | Linux x64 / 13 | 11,640 total; 11,623 passed; 17 platform skips; zero failures | 38m20s job |
| Same version CI / revision / runtime | Linux x64 / 14 | 11,640 total; 11,623 passed; 17 platform skips; zero failures | 39m15s job |
| Same version CI / revision / runtime | Linux x64 / 15 | 11,640 total; 11,623 passed; 17 platform skips; zero failures | 39m10s job |
| Same version CI / revision / runtime | Linux x64 / 16 | 11,640 total; 11,623 passed; 17 platform skips; zero failures | 39m29s job |
| Same version CI / revision / runtime | Linux x64 / 17 | 11,640 total; 11,623 passed; 17 platform skips; zero failures | 40m01s job |
| Same version CI / revision / runtime | Linux x64 / 19 beta 4 | 11,640 total; 11,623 passed; 17 platform skips; zero failures | 40m57s job |
| Frozen 84-input diagnostic/cleanup/idle-interrupt composition / runtime **ankus.4** | Linux x64 / 18.6 | 11,689 total; 11,641 passed; 48 platform skips; zero failures | 57m20.886s command |
| Same frozen composition / runtime | Windows x64 / 13.23 | 11,689 total; 11,662 passed; 27 platform skips; zero failures | 1h19m46.379s command |
| Frozen 64-file pointer/diagnostic/fixture composition / runtime **ankus.4** | Linux x64 / 18.6 | 11,609 total; 11,592 passed; 17 platform skips; zero failures | 29m25.565s tests |
| Same frozen composition / runtime | macOS ARM64 / 18.6 | 11,609 total; 11,580 passed; 29 platform skips; zero failures | 22m29.195s tests |
| Same frozen composition / runtime | Windows x64 / 13.23 | 11,609 total; 11,584 passed; 25 platform skips; zero failures | 38m42.708s tests |
| Frozen 66-file composition with ANKUS129 editor correction / runtime **ankus.4** | Linux x64 / 18.6 | 11,640 total; 11,623 passed; 17 platform skips; zero failures | 50m45.859s tests |
| [CI 37145034638](https://github.com/willibrandon/ankus/actions/runs/37145034638), b9b7eb5 / runtime **ankus.4** | Linux x64 / 18 | 11,609 total; 11,592 passed; 17 platform skips; zero failures | 36m31s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 11,609 total; 11,580 passed; 29 platform skips; zero failures | 26m35s job |
| Same CI / revision / runtime | Windows x64 / 17 | 11,609 total; 11,584 passed; 25 platform skips; zero failures | 31m43s job |
| [Version CI 37145038020](https://github.com/willibrandon/ankus/actions/runs/37145038020), b9b7eb5 / runtime **ankus.4** | macOS ARM64 / 16 | 11,609 total; 11,580 passed; 29 platform skips; zero failures | 26m32s job |
| Same version CI / revision / runtime | Windows x64 / 13 | 11,609 total; 11,584 passed; 25 platform skips; zero failures | 33m51s job |
| Same version CI / revision / runtime | macOS ARM64 / 15 | 11,609 total; 11,580 passed; 29 platform skips; zero failures | 26m17s job |
| Same version CI / revision / runtime | Windows x64 / 18 | 11,609 total; 11,584 passed; 25 platform skips; zero failures | 31m46s job |
| [Intel CI 37131051660](https://github.com/willibrandon/ankus/actions/runs/37131051660), 04f0a8a / runtime **ankus.4** | macOS x64 / 18.6 | 11,568 total; 11,541 passed; 27 platform skips; zero failures | 3h59m34s job |
| [CI 37131029131](https://github.com/willibrandon/ankus/actions/runs/37131029131), 04f0a8a / runtime **ankus.4** | Linux x64 / 18 | 11,568 total; 11,553 passed; 15 platform skips; zero failures | 36m04s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 11,568 total; 11,541 passed; 27 platform skips; zero failures | 26m50s job |
| Same CI / revision / runtime | Windows x64 / 17 | 11,568 total; 11,543 passed; 25 platform skips; zero failures | 32m10s job |
| [Version CI 37131055805](https://github.com/willibrandon/ankus/actions/runs/37131055805), 04f0a8a / runtime **ankus.4** | macOS ARM64 / 16 | 11,568 total; 11,541 passed; 27 platform skips; zero failures | 26m32s job |
| Same version CI / revision / runtime | Windows x64 / 18 | 11,568 total; 11,543 passed; 25 platform skips; zero failures | 31m18s job |
| Same version CI / revision / runtime | macOS ARM64 / 15 | 11,568 total; 11,541 passed; 27 platform skips; zero failures | 26m18s job |
| [CI 37117515450](https://github.com/willibrandon/ankus/actions/runs/37117515450), 7f58d03 | Linux x64 / 18 | 11,528 total; 11,514 passed; 14 platform skips; zero failures | 36m09s job |
| Same CI / revision | macOS ARM64 / 18 | 11,528 total; 11,502 passed; 26 platform skips; zero failures | 27m09s job |
| Same CI / revision | Windows x64 / 17 | 11,528 total; 11,503 passed; 25 platform skips; zero failures | 32m02s job |
| Current compatibility, array/editor and runtime/compiler **ankus.4** changes | macOS ARM64 / 15.19 | 11,568 total; 11,541 passed; 27 platform skips; zero failures | 25m20.803s tests |
| Same source and runtime/compiler **ankus.4** changes | macOS ARM64 / 16.15 | 11,568 total; 11,541 passed; 27 platform skips; zero failures | 25m53.580s tests |
| Same source and runtime/compiler **ankus.4** changes | Linux x64 / 18.6 | 11,568 total; 11,553 passed; 15 platform skips; zero failures | 48m57.583s tests |
| Current compatibility and array/editor changes, runtime/compiler **ankus.3** | Windows x64 / 13.23 | 11,568 total; 11,543 passed; 25 platform skips; zero failures | 41m49.962s tests |
| 3a06bd9 with editor correction and diagnostic help links | Linux x64 / 18.6 | 11,528 total; 11,514 passed; 14 platform skips; zero failures | 36m50.035s tests |
| [Version CI 36988634319](https://github.com/willibrandon/ankus/actions/runs/36988634319), 99660ea | Linux x64 / each of 13–17 | 10,984 total per major; 10,970 passed; 14 platform skips; zero failures | 26m15s–27m28s jobs |
| [Version CI 37016275325](https://github.com/willibrandon/ankus/actions/runs/37016275325), c3db5ed | Linux x64 / 19 beta 4 | 11,120 total; 11,106 passed; 14 platform skips; zero failures | 28m12s job |

All six TRX reports were independently checked for each current primary
platform, including the **04f0a8a / ankus.4** CI run, and the editor composition.
The latter also passes Release with zero
warnings/errors (**3m04.15s**), API freshness (**238 pages / 2,757 members**),
site build (**286 pages, 3.97s**) and site checks with zero diagnostics. Its
50 source identities match the tested draft, validator and promoted source.
The older major-version runs prove their named revisions; they do not establish
current-source acceptance for every advertised combination.

Earlier platform results, timings and failures remain in the
[evidence archive](docs/contributing/evidence/port-history.md#progress-snapshot-before-editor-acceptance).
[Docs 37114971196](https://github.com/willibrandon/ankus/actions/runs/37114971196)
passes on 3a06bd9. The editor change is committed as **21d586f** and the version
workflow as **7f58d03**. Replacement [CI 37117515450](https://github.com/willibrandon/ankus/actions/runs/37117515450)
passes quality, all runtime jobs and all three complete suites. [Docs 37117515453](https://github.com/willibrandon/ankus/actions/runs/37117515453)
passes.

## Active validation and work

- Shared persistent server diagnostics are uncommitted. The configuration suite
  passes **462 total / 459 passed / three Windows skips / zero failures** on
  Linux; its frozen **76-file** complete Linux suite passes **11,660 total /
  11,639 passed / 21 skips / zero failures**, with Release/API/site gates green.
  Expanded Windows configuration passes **483 total / 482 passed / one skip**.
  The corrected native scope passes **eight cases / nine Windows skips** on
  Linux/18.6 and **16 cases / one Unix skip** on Windows/13.23. Its frozen
  **84-input** complete Linux suite passes **11,689 total / 11,641 passed /
  48 skips / zero failures**. Windows/13.23 passes **11,689 total / 11,662 passed /
  27 skips / zero failures** against the same composition. Release passes on
  both; API freshness and site gates pass on Linux. Later corrections still
  require final complete acceptance.
- The latest review confirms that diagnostic-read failures bypass test-cluster
  cleanup at both startup and shutdown. Native regressions reproduce both
  failures. Cleanup corrections pass native startup/shutdown regressions on
  both platforms. Repeat-disposal reproduces an additional shutdown-state bug:
  after successful deletion, retrying probes the removed data directory. Its
  correction passes cleanup, cluster lifecycle and port-handoff acceptance:
  **21 total / 19 passed / two skips** on Linux and **21 / 20 / one skip** on
  Windows, both with zero failures.
- Idle-worker cancellation through `PgInterrupts.Check()` also reproduces with
  a real cancel and worker exit. Its recovery correction passes repeated real
  cancels, subsequent work and terminal-error tests on both platforms.
  SPI quote-helper and parameter-token corrections pass **225 managed cases**
  and **nine published PostgreSQL cases**. Editor/aggregate regressions reproduce
  14 failures before correction; corrected acceptance passes **80/80** cases.
  Raw-address APIs now carry ANKUS129, including generated-code callers. The
  complete generator module passes **3,407/3,407** cases. Existing raw-call
  fixtures and packaged consumers have explicit unsafe blocks.
  Invalid `PgTest` declarations also reproduced loss of unrelated output in all
  six new cases. Per-item rejection now preserves valid catalogs and exports;
  **41/41** related cases pass. Four older catalog assertions were updated to
  execute surviving catalogs; the entire generator module now passes
  **3,413/3,413** cases. A Windows publication cleanup failure prompted child
  MSBuild node isolation; the full build module passes **1,210 cases / six
  skips**. Its original native lock holder was not captured. Release passes
  with zero warnings/errors; API freshness and site gates pass. The expanded
  complete Linux run finishes in **57m10s** with **11,815 total / 11,763 passed /
  48 skips / four failures**, all four from the old generator assertions above;
  native integration passes. The original failed reports remain retained.
  An additional real-server regression confirms canceled shutdown tasks prevent
  repeated disposal. Retrying canceled shutdowns passes native lifecycle,
  diagnostic cleanup and port-handoff tests: **22 total / 20 passed / two skips /
  zero failures** on Linux/18.6. Corrected complete Linux acceptance passes
  **11,816 total / 11,768 passed / 48 platform skips / zero failures** in
  **50m23s**. Native integration passes **4,564 cases / 15 skips**. All 137
  recorded authored inputs remain unchanged through the complete result.
  Windows' expanded complete run finishes in **81m16s** with **11,809 total /
  11,781 passed / 27 skips / one failure**: the original publication-fixture
  sharing failure. Its native integration passes **4,558 cases / 20 skips**.
  After correction, complete Windows generator acceptance passes **3,413/3,413**,
  lifecycle acceptance passes **20 cases / two skips**, and Release passes with
  zero warnings/errors. Final Linux Release, API freshness and site checks all
  pass. Updated complete platform CI remains required before accepting this
  composition across platforms. Guard performance work follows correctness
  acceptance.
- **c24297f** primary CI passes docs, quality, runtime builds and all six test
  modules on Linux/18, macOS ARM64/18 and Windows/17. Version CI **37145038020** passes
  complete macOS 15/16 and Windows 13/18 suites on **b9b7eb5**; Intel CI
  **37131051660** passes all six modules on **04f0a8a**. Complete Linux 13–17/19
  evidence against the current runtime is refreshed in
  [version CI 37154140634](https://github.com/willibrandon/ankus/actions/runs/37154140634)
  on **c24297f**. PostgreSQL 13–17 and 19 pass all six modules.
- Remaining review work includes discovery/caching, precise diagnostics and code
  fixes, CLI environment selection (`PG_VERSION`, `DBNAME` and pgcli), samples
  and final platform acceptance.
  Prior detailed outcomes and superseded states are retained in the
  [evidence archive](docs/contributing/evidence/port-history.md#status-snapshot-before-the-23-commit-follow-up-review).

## Remaining work order

The accepted declaration milestones have complete primary-platform CI evidence.
The editor composition passes its full native suite and replacement primary CI.
The Intel diagnostic retry exposed six path-related failures; the corrected
complete Intel suite now passes. An explanation of the earlier hosted-runner
disconnect and remaining version/platform acceptance are still required.

1. Resolve discovered correctness and CI failures before accepting affected work.
2. Finish the Intel timing milestone and complete supported-major/platform coverage, including Intel macOS and the macOS 15/16 library-suffix boundary.
3. Complete declaration diagnostics/code fixes, API discoverability and unsafe raw-call contracts.
4. Establish faithful in-backend benchmarks, then finish appropriate guard/array improvements with recovery tests and measured comparisons.
5. Close remaining CLI, account/privilege, benchmark and platform-installation contracts.
6. Complete framework/discovery support, parsing/formatting helpers, representative samples and documentation.
7. Complete .NET servicing and every release requirement in the full inventory before publishing 0.1.0. If .NET 11 reaches GA first, complete its acceptance for that release; preview validation does not block the initial .NET 10 release.

Continue independent work while CI runs. Before every commit and push, check and
record previous run outcomes, including live runs, and resolve reported failures.
Keep this file current; append detailed implementation evidence to the archive
with a clear tested revision, platform/version, outcome and any remaining scope.
