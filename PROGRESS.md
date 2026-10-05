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
| Documentation and samples | Public guides, generated API pages, pgrx migration and backend-execution guidance; current status separated from historical evidence. SPI, error/reporting and in-backend benchmark samples cover their complete authoring loops. | Every remaining inventoried representative sample and final usage/limitation review. |

The custom-type alignment review found no defect: variable-length PostgreSQL
types require at least four-byte datum alignment. Managed codec payload layout
is a separate contract, verified through copying/packed-field tests. Deriving
datum alignment from a CLR carrier or adding one/two-byte alignment would be
incorrect. See [the detailed review](docs/contributing/evidence/port-history.md#custom-datum-alignment-review).

## Current complete acceptance evidence

Primary CI runs all six modules against real published Native AOT extensions.
The latest primary CI source is **7dca056**, with runtime **10.0.12-ankus.4**.
[CI 37361236252](https://github.com/willibrandon/ankus/actions/runs/37361236252)
and [Docs 37361236408](https://github.com/willibrandon/ankus/actions/runs/37361236408)
pass. All eighteen actual reports and all sixty-seven required native recovery
partitions independently verify; no primary job timed out.

| Source | Platform / PostgreSQL | Result | Duration |
| --- | --- | --- | --- |
| Latest primary CI, **7dca056 / ankus.4** | Linux x64 / 18 | 12,561 total; 12,513 passed; 48 platform skips; zero failures | 39m19s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 12,561 total; 12,501 passed; 60 platform skips; zero failures | 32m34s job |
| Same CI / revision / runtime | Windows x64 / 17 | 12,561 total; 12,533 passed; 28 platform skips; zero failures | 34m58s job |
| Frozen corrected SPI composition, runtime **ankus.4** | Windows x64 / 17.11 | 12,630 total; 12,602 passed; 28 platform skips; zero failures | 47m45.216s tests; 48m33.194s command |
| Frozen raw transport / recovery composition, runtime **ankus.4** | Linux x64 / 18.6 | 12,561 total; 12,513 passed; 48 platform skips; zero failures | 41m19.728s tests |
| Same frozen composition / runtime | Windows x64 / 17.11 | 12,561 total; 12,533 passed; 28 platform skips; zero failures | 47m38.639s tests; 48m23.936s command |

The corrected SPI composition verifies all six reports, sixty-seven native
recovery partitions and **1,791** source inputs. Its exact source/report archive
matches all **1,802** retained files. Release, API freshness, site build and site
diagnostics pass without warnings or errors. Replacement primary-platform CI
remains required.
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

- **7dca056** fixes recovery-test reconnection before postmaster reinitialization
  finishes. The original thirty-second deadline and durability assertions remain.
  All **122** affected Windows/**17.11** cases and complete primary CI pass.
  The prior [CI 37343346227](https://github.com/willibrandon/ankus/actions/runs/37343346227)
  reported twenty Windows startup/crash-recovery timeouts; its other jobs and
  [Docs 37343346223](https://github.com/willibrandon/ankus/actions/runs/37343346223)
  passed. The failure and diagnosis remain recorded in the archive.
- Seventeen remaining arbitrary-address/handle compiler transport methods require
  unsafe contexts. All **21** regressions fail against the unchanged baseline and
  pass with the correction; the accepted complete suites preserve native recovery.
  Full source-contract and current supported-version acceptance remain required.
- The accepted Windows SPI analyzer/shared-lexer composition follows reaching strings,
  mutable builder/array aliases, local callbacks and exception cleanup, and checks
  quoted fragments in PostgreSQL token contexts. All **3,890** generator cases,
  **2,148** runtime cases and **150** focused cases pass; the unchanged analyzer
  fails **33** of the same focused cases. Source documentation is prepared. The
  corrected complete Windows x64/PostgreSQL **17.11** suite passes all **12,630**
  executions with **28** platform skips and zero failures. The worker fixture now
  binds its two process-ID commands, preserving its lifecycle assertions. All
  source hashes match before and after; the exact accepted sources are promoted.
  The rejected predecessor and focused correction remain in the archive.
  Replacement primary-platform and supported-version acceptance remain required.
- The bytea sample's owned gzip framing draft preserves empty input, first-member
  boundaries, complete trailers and pgrx's decoder policy around the standard
  DEFLATE codec. Its **32** direct cases pass, including independently framed
  stored, fixed and dynamic blocks, malformed input and strict UTF-8. The complete
  isolated runtime module passes **2,180** cases in **3.457s**, with independently
  verified execution identities including corrupt header CRC. The actual
  generator compiles both sample sources without warnings/errors. Eighteen
  prepared backend cases compile with the actual PostgreSQL fixture sources;
  the **1,799**-input candidate includes the normal project/test graph and
  public guide. Normal analyzer, native SQL and complete platform acceptance
  remain required. The sample is not promoted.
- The strings sample's private Unicode **17.0** lowercase draft matches Rust
  **1.97.1** for all **1,112,064** valid scalars and their final-sigma contexts.
  Eight focused executions pass, including actual whole-string outputs, Turkish
  culture and invalid UTF-16. Sample APIs, native execution and complete acceptance
  remain required; the draft is not promoted.
- SPI guidance documents hot-standby restrictions on write-intent helpers. This
  is source-derived guidance, not standby test evidence. Precise diagnostics,
  remaining code fixes, source-case mapping and samples remain open. The full
  PostgreSQL/platform, .NET servicing and release requirements remain in scope.

## Remaining work order

The accepted declaration milestones have complete primary-platform CI evidence.
The earlier editor composition passes its full native suite and replacement primary CI.
The current cold-load/reload correction also passes complete Linux/18.6 acceptance;
its replacement primary CI passes all three complete platform suites.
The Intel diagnostic retry exposed six path-related failures; the corrected
complete Intel suite now passes. An explanation of the earlier hosted-runner
disconnect and remaining version/platform acceptance are still required.

1. Resolve discovered correctness and CI failures before accepting affected work, including the remaining imported-string metadata audit.
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
