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

Primary CI below runs all six modules against real published Native AOT
extensions. Counts supplement the backend, ownership and recovery assertions.

| Source | Platform / PostgreSQL | Result | Duration |
| --- | --- | --- | --- |
| Frozen raw transport / crash-recovery readiness composition, runtime **ankus.4** | Linux x64 / 18.6 | 12,561 total; 12,513 passed; 48 platform skips; zero failures | 41m19.728s tests |
| Same composition and runtime | Windows x64 / 17.11 | 12,561 total; 12,533 passed; 28 platform skips; zero failures | 47m38.639s tests; 48m23.936s command |
| Frozen metadata image / editor scope composition, runtime **ankus.4** | Linux x64 / 18.6 | 12,540 total; 12,492 passed; 48 platform skips; zero failures | 40m52.575s tests |
| [CI 37335229927](https://github.com/willibrandon/ankus/actions/runs/37335229927), b9eb9db / runtime **ankus.4** | Linux x64 / 18 | 12,524 total; 12,476 passed; 48 platform skips; zero failures | 39m16s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 12,524 total; 12,464 passed; 60 platform skips; zero failures | 29m14s job |
| Same CI / revision / runtime | Windows x64 / 17 | 12,524 total; 12,496 passed; 28 platform skips; zero failures | 35m03s job |
| Frozen native input/resource recovery composition, runtime **ankus.4** | Linux x64 / 18.6 | 12,524 total; 12,476 passed; 48 platform skips; zero failures | 41m15.595s tests |
| [CI 37313742483](https://github.com/willibrandon/ankus/actions/runs/37313742483), ff7e07c / runtime **ankus.4** | Linux x64 / 18 | 12,453 total; 12,405 passed; 48 platform skips; zero failures | 39m21s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 12,453 total; 12,393 passed; 60 platform skips; zero failures | 28m59s job |
| Same CI / revision / runtime | Windows x64 / 17 | 12,453 total; 12,425 passed; 28 platform skips; zero failures | 35m11s job |
| Frozen command-specific copy assertions, runtime **ankus.4** | Linux x64 / 18.6 | 12,453 total; 12,405 passed; 48 platform skips; zero failures | 36m05.545s tests |
| [CI 37302379250](https://github.com/willibrandon/ankus/actions/runs/37302379250), afb59ae / runtime **ankus.4** | Linux x64 / 18 | 12,453 total; 12,405 passed; 48 platform skips; zero failures | 39m15s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 12,453 total; 12,393 passed; 60 platform skips; zero failures | 28m58s job |
| Frozen diagnostic-allocation / CLI / JSON composition, runtime **ankus.4** | Linux x64 / 18.6 | 12,453 total; 12,405 passed; 48 platform skips; zero failures | 36m21.402s tests |
| [CI 37286283685](https://github.com/willibrandon/ankus/actions/runs/37286283685), fd79e24 / runtime **ankus.4** | Linux x64 / 18 | 12,438 total; 12,390 passed; 48 platform skips; zero failures | 39m03s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 12,438 total; 12,378 passed; 60 platform skips; zero failures | 28m54s job |
| Same CI / revision / runtime | Windows x64 / 17 | 12,438 total; 12,410 passed; 28 platform skips; zero failures | 34m27s job |
| Frozen cleanup-reporter / benchmark composition, runtime **ankus.4** | Windows x64 / 17.11 | 12,438 total; 12,410 passed; 28 platform skips; zero failures | 41m41.075s tests |
| Frozen numeric-example composition / runtime **ankus.4** | Linux x64 / 18.6 | 12,422 total; 12,374 passed; 48 platform skips; zero failures | 39m51.287s tests; 40m18.80s command |
| [CI 37265430538](https://github.com/willibrandon/ankus/actions/runs/37265430538), 708d93a / runtime **ankus.4** | Linux x64 / 18 | 12,389 total; 12,341 passed; 48 platform skips; zero failures | 38m24s job |
| Same CI / revision / runtime | Windows x64 / 17 | 12,389 total; 12,361 passed; 28 platform skips; zero failures | 35m51s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 12,389 total; 12,329 passed; 60 platform skips; zero failures | 29m41s job |
| [CI 37261359905](https://github.com/willibrandon/ankus/actions/runs/37261359905), 73c83db / runtime **ankus.4** | Linux x64 / 18 | 12,377 total; 12,329 passed; 48 platform skips; zero failures | 38m18s job |
| [CI 37261359905](https://github.com/willibrandon/ankus/actions/runs/37261359905), 73c83db / runtime **ankus.4** | Windows x64 / 17 | 12,377 total; 12,349 passed; 28 platform skips; zero failures | 35m19s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 12,377 total; 12,317 passed; 60 platform skips; zero failures | 36m06s job |
| Frozen borrowed-array signature/lifetime composition / runtime **ankus.4** | Linux x64 / 18.6 | 12,389 total; 12,341 passed; 48 platform skips; zero failures | 39m35.194s tests; 40m02.50s command |
| [CI 37256732695](https://github.com/willibrandon/ankus/actions/runs/37256732695), 063011d / runtime **ankus.4** | Linux x64 / 18 | 12,289 total; 12,241 passed; 48 platform skips; zero failures | 38m04s job |
| Same CI / revision / runtime | Windows x64 / 17 | 12,289 total; 12,261 passed; 28 platform skips; zero failures | 35m29s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 12,289 total; 12,229 passed; 60 platform skips; zero failures | 28m05s job |
| Frozen array/JSON composition / runtime **ankus.4** | Linux x64 / 18.6 | 12,377 total; 12,329 passed; 48 platform skips; zero failures | 39m48.005s tests; 40m15.68s command |
| Frozen transaction-completion cleanup composition / runtime **ankus.4** | Linux x64 / 18.6 | 12,289 total; 12,241 passed; 48 platform skips; zero failures | 39m18.598s tests; 39m41.40s command |
| Frozen remaining-reader composition / runtime **ankus.4** | Linux x64 / 18.6 | 12,260 total; 12,212 passed; 48 platform skips; zero failures | 39m21.681s tests; 39m44.06s command |
| [CI 37244724738](https://github.com/willibrandon/ankus/actions/runs/37244724738), a04f6ed / runtime **ankus.4** | Linux x64 / 18 | 12,260 total; 12,212 passed; 48 platform skips; zero failures | 37m58s job |
| Same CI / revision / runtime | Windows x64 / 17 | 12,260 total; 12,232 passed; 28 platform skips; zero failures | 36m27s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 12,260 total; 12,200 passed; 60 platform skips; zero failures | 28m23s job |
| [CI 37241377151](https://github.com/willibrandon/ankus/actions/runs/37241377151), dc48724 / runtime **ankus.4** | Linux x64 / 18 | 12,244 total; 12,196 passed; 48 platform skips; zero failures | 37m56s job |
| Same CI / revision / runtime | Windows x64 / 17 | 12,244 total; 12,216 passed; 28 platform skips; zero failures | 34m39s job |
| [CI 37241377151](https://github.com/willibrandon/ankus/actions/runs/37241377151), dc48724 / runtime **ankus.4** | macOS ARM64 / 18 | 12,244 total; 12,184 passed; 60 platform skips; zero failures | 28m58s job |
| [CI 37236731631](https://github.com/willibrandon/ankus/actions/runs/37236731631), a78be2c / runtime **ankus.4** | Linux x64 / 18 | 12,078 total; 12,030 passed; 48 platform skips; zero failures | 37m58s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 12,078 total; 12,018 passed; 60 platform skips; zero failures | 34m45s job |
| Same CI / revision / runtime | Windows x64 / 17 | 12,078 total; 12,050 passed; 28 platform skips; zero failures | 35m30s job |
| Frozen enum/serialization composition / runtime **ankus.4** | Linux x64 / 18.6 | 12,244 total; 12,196 passed; 48 platform skips; zero failures | 39m27.245s tests; 39m48.86s command |
| Frozen referenced-identity/namespace composition / runtime **ankus.4** | Linux x64 / 18.6 | 12,078 total; 12,030 passed; 48 platform skips; zero failures | 39m27.651s tests; 39m50.14s command |
| Frozen cold-editor/reload composition / runtime **ankus.4** | Linux x64 / 18.6 | 11,993 total; 11,945 passed; 48 platform skips; zero failures | 39m16.950s tests; 39m40.34s command |
| [CI 37224209715](https://github.com/willibrandon/ankus/actions/runs/37224209715), bed79ab / runtime **ankus.4** | Linux x64 / 18 | 11,991 total; 11,943 passed; 48 platform skips; zero failures | 37m52s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 11,991 total; 11,931 passed; 60 platform skips; zero failures | 28m08s job |
| Same CI / revision / runtime | Windows x64 / 17 | 11,991 total; 11,963 passed; 28 platform skips; zero failures | 33m54s job |
| Datum mapping diagnostic milestone / runtime **ankus.4** | Linux x64 / 18.6 | 11,927 total; 11,879 passed; 48 platform skips; zero failures | 42m37.991s tests; 44m55s command |
| [CI 37218726638](https://github.com/willibrandon/ankus/actions/runs/37218726638), 4c9cc43 / runtime **ankus.4** | Linux x64 / 18 | 11,927 total; 11,879 passed; 48 platform skips; zero failures | 37m47s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 11,927 total; 11,867 passed; 60 platform skips; zero failures | 28m21s job |
| Same CI / revision / runtime | Windows x64 / 17 | 11,927 total; 11,899 passed; 28 platform skips; zero failures | 35m55s job |
| [CI 37214601834](https://github.com/willibrandon/ankus/actions/runs/37214601834), a5690d4 / runtime **ankus.4** | Linux x64 / 18 | 11,906 total; 11,858 passed; 48 platform skips; zero failures | 37m56s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 11,906 total; 11,846 passed; 60 platform skips; zero failures | 30m25s job |
| Same CI / revision / runtime | Windows x64 / 17 | 11,906 total; 11,878 passed; 28 platform skips; zero failures | 40m18s job |
| Frozen CLI/template/reporting-hook composition / runtime **ankus.4** | Linux x64 / 18.6 | 11,840 total; 11,792 passed; 48 platform skips; zero failures | 51m13.968s command |
| Frozen generator-discovery composition / runtime **ankus.4** | Linux x64 / 18.6 | 11,830 total; 11,782 passed; 48 platform skips; zero failures | 50m29.932s command |
| [CI 37174019108](https://github.com/willibrandon/ankus/actions/runs/37174019108), 811f8d0 / runtime **ankus.4** | macOS ARM64 / 18 | 11,830 total; 11,770 passed; 60 platform skips; zero failures | 26m56s job |
| Same CI / revision / runtime | Linux x64 / 18 | 11,830 total; 11,782 passed; 48 platform skips; zero failures | 36m27s job |
| Same CI / revision / runtime | Windows x64 / 17 | 11,830 total; 11,802 passed; 28 platform skips; zero failures | 33m59s job |
| [CI 37169593738](https://github.com/willibrandon/ankus/actions/runs/37169593738), 596a8af / runtime **ankus.4** | Linux x64 / 18 | 11,816 total; 11,768 passed; 48 platform skips; zero failures | 36m32s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 11,816 total; 11,756 passed; 60 platform skips; zero failures | 26m44s job |
| Same CI / revision / runtime | Windows x64 / 17 | 11,816 total; 11,788 passed; 28 platform skips; zero failures | 33m14s job |
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

- Native recovery is committed as **b9eb9db**; [CI 37335229927](https://github.com/willibrandon/ankus/actions/runs/37335229927)
  passes all primary suites, quality and runtime jobs; all eighteen actual
  reports independently verify. [Docs 37335229895](https://github.com/willibrandon/ankus/actions/runs/37335229895)
  passes. Successful value operations retain the lightweight guard; caught
  native failures require real rollback. Current supported-version acceptance
  remains required.
- Metadata/editor fixes are committed as **a4a30e2**. Their complete Linux
  x64/PostgreSQL **18.6** suite passes **12,540 total / 12,492 passed / 48
  platform skips / zero failures**, with all reports, required native recovery
  partitions and **1,786** frozen inputs independently verified. Release and
  documentation checks pass. Replacement [CI 37343346227](https://github.com/willibrandon/ankus/actions/runs/37343346227)
  passes quality, runtime, Linux and macOS jobs. Windows reported twenty startup
  and crash-recovery timeouts. Its server logs
  show twelve concurrent cleanup-recovery cases and substantial disk-sync
  delays. [Docs 37343346223](https://github.com/willibrandon/ankus/actions/runs/37343346223) passes.
- Recovery tests now wait for postmaster readiness after reinitialization
  before reconnecting, preserving deadlines and durability assertions. All
  **122** affected Windows/**17.11** cases pass. The complete Linux/**18.6**
  suite passes **12,561 total / 12,513 passed / 48 platform skips / zero
  failures** in **41m19.728s**. Complete Windows/**17.11** acceptance passes
  **12,561 total / 12,533 passed / 28 platform skips / zero failures** in
  **47m38.639s**. All twelve reports, required native recovery partitions
  and **1,788** frozen inputs verify. Release, API freshness and site checks
  pass. Replacement CI remains required.
- Seventeen remaining arbitrary-address/handle compiler transport methods
  now require explicit unsafe contexts. All **21** regression cases fail
  against the unchanged baseline and pass with the correction; the complete
  isolated generator and runtime suites pass **3,821** and **2,148** cases.
  The promoted composition passes all **52** affected analyzer cases and
  the complete Linux x64/PostgreSQL
  **18.6** suite: **12,561 total / 12,513 passed / 48 platform skips / zero
  failures**, **41m26.979s**. All six reports, required native recovery cases
  and **1,787** frozen inputs independently verify. Replacement primary CI and
  current supported-platform/version acceptance remain required.
- SPI guidance now documents hot-standby restrictions on write-intent helpers;
  this is source-derived guidance, not standby test evidence. Remaining work
  includes full raw contract acceptance, meaningful code fixes, SPI interpolation
  placement/dataflow, precise diagnostics, generator caching and source examples.
  The complete PostgreSQL/platform, .NET servicing and release requirements remain
  in scope.

Detailed implementation, failures and corrections remain in the
[evidence archive](docs/contributing/evidence/port-history.md).

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
