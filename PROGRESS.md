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
passes with **ankus.4**; additional-version and Intel acceptance is still in progress.
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
globals, callbacks and checked managed APIs. The current raw-pointer review
remains open; changing pointer syntax alone does not establish that contract.

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
| Native bindings | pgrx 0.19.3 inventories, matching PostgreSQL 19 beta 4 inputs and target-compiler ABI checks; focused binding checks cover all seven majors. | Full supported-major/platform tests, raw-call unsafe visibility and final inventory audit. |
| Development CLI | Version/project selection, installation/registration, cluster lifecycle, run/connect, test/regress, property forwarding, environment selection, scriptable info and package prefixes. | Account/privilege selection, in-backend benchmarking and remaining inventoried CLI/platform contracts. |
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
| Frozen 64-file pointer/diagnostic/fixture composition / runtime **ankus.4** | Linux x64 / 18.6 | 11,609 total; 11,592 passed; 17 platform skips; zero failures | 29m25.565s tests |
| Same frozen composition / runtime | macOS ARM64 / 18.6 | 11,609 total; 11,580 passed; 29 platform skips; zero failures | 22m29.195s tests |
| Same frozen composition / runtime | Windows x64 / 13.23 | 11,609 total; 11,584 passed; 25 platform skips; zero failures | 38m42.708s tests |
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

CI outcomes checked before every local milestone commit on **2026-10-03**:
primary and docs runs on **04f0a8a** pass; the Windows 13 version failure
is repaired locally, with all 80 failed cases passing in the completed suite,
and awaits replacement service-context acceptance; Intel remains
active. The complete outcome snapshot is retained with the validation evidence.

- Repairs and validation evidence are pushed through **04f0a8a**. The verified
  Apple object-fixture correction is committed locally as **7fb9700**, and the
  Windows diagnostic/selected-installation correction as **7251034**. Their
  single push follows the completed local Windows run. Replacement
  [primary CI 37131029131](https://github.com/willibrandon/ankus/actions/runs/37131029131),
  [docs 37131029115](https://github.com/willibrandon/ankus/actions/runs/37131029115),
  [platform versions 37131055805](https://github.com/willibrandon/ankus/actions/runs/37131055805)
  and [Intel macOS 37131051660](https://github.com/willibrandon/ankus/actions/runs/37131051660)
  have passed docs, quality, all three primary runtime builds and all three
  complete primary suites. Their 18 TRX reports are retained and independently
  counted above. The additional macOS 15/16 and Windows 18 suites also pass; all
  eighteen additional reports were independently counted. Windows 13 completes
  with **11,568 total / 11,463 passed / 80 failures / 25 skips, 34m23s job**.
  All 125 retained server logs are empty. The missing diagnostics are confirmed
  in Windows Application events from the same CI window: PostgreSQL 13 routes
  stderr to Event Log when it inherits a service token. That revision's harness
  reads only its requested file, losing error evidence and port-collision
  detection. Correct diagnostic collection and service-context acceptance are
  required; the preceding interactive Windows 13 pass did not cover this route.
  The diagnostic collector now isolates each cluster with its own event source
  and retains original messages alongside stderr. A real event-only regression
  fails before the fix and passes afterward; both new Windows 13.23 tests pass
  (**2/2, 2m56.456s**, zero skips). They verify Unicode fields, cluster isolation,
  incremental/concurrent reads, startup failure and shutdown retention. The
  first complete interactive Windows run finishes with **11,570 total /
  11,516 passed / 29 failures / 25 skips**. Of the original 80 CI failures,
  78 pass; the other two stop before discovery because generated projects lack
  the selected PostgreSQL path. Replacement service-context CI remains required.
  No runtime change or test suppression is involved.
  The local run exposes a consumer-fixture selection defect: generated
  projects receive the selected major but not a nonstandard PostgreSQL path.
  CI supplies the path globally, masking this local failure. The fixture now
  passes its resolved installation through the child build environment; this
  correction passes the affected installed-consumer scope. The private launcher's temporary root
  also makes native SourceLink filenames reach 260/262 characters; the Microsoft
  linker cannot open them. Its owned root is shortened without changing compiler
  options or warnings. The failed directory-control assertion is also included
  in the affected rerun. All six original reports and logs are retained; affected
  cases and both event regressions now pass: **34/34, zero failures/skips,
  14m36.093s**. This includes the previously failed nested-parent directory
  assertion without a production staging change. The final complete interactive
  Windows 13.23 composition passes **11,609 total / 11,584 passed / 25 skips /
  zero failures, 38m42.708s**. All 80 originally failed cases pass. Release passes
  with zero warnings/errors (**57.76s**); all six reports and all 64 frozen source
  identities verify. Replacement service-context CI is still required.
  Intel remains active and retains two
  package-consumer slots and its approved six-hour limit. These remaining active
  workflows are not acceptance results.
- The frozen raw native API correction is applied locally. Data pointers retain
  their C# pointee types; incomplete types have no allocation contract; typed
  carriers preserve native stride in generic containers. `ANKUS129` requires an
  explicit unsafe context for raw scalar operations and delegate assignments.
  Transport preserves every address bit in checked consumers, while native guards,
  owned diagnostics and rollback rules remain intact.
  The final 64-file composition passes complete Linux x64, macOS ARM64 and Windows
  x64 suites, Release and the documentation gates recorded above. All eighteen
  TRX reports and all 64 source identities verify. Windows uses PostgreSQL 13.23;
  Linux and macOS use 18.6. This is interactive Windows evidence; service-context
  CI remains required.
  The preceding checked draft passes all 70 affected backend cases on each of
  PostgreSQL 13.23, 14.24, 15.19, 16.15, 17.11 and 19 beta 4. These focused checks
  do not replace complete version/platform acceptance.
  Original checked-address, helper-list and launcher/SDK failures, corrected
  regressions and the Apple object-fixture repair are retained in the
  [detailed evidence](docs/contributing/evidence/port-history.md#checked-raw-pointer-contracts-and-windows-service-diagnostics).
  Replacement CI and remaining supported platform/version acceptance stay open.
- Function/schema, operator/cast and aggregate declarations now have specific
  diagnostics, authored locations and correction links. Their accepted primary
  suites pass. The editor composition adds a semantic `ANKUS056` correction,
  fourteen workspace cases and the nineteen remaining diagnostic help links.
  Normal and no-build packages pass fresh-cache consumer checks. The complete
  suite also passes `SdkRestoresWithoutRepositoryReferences`, including both
  analyzer assemblies and absence of Roslyn runtime dependencies. Other useful
  semantic corrections remain in scope. An isolated `ANKUS111` combine-interface
  correction now passes all **3,288** generator cases, including 24 new workspace
  cases. This completed sequential run replaces the earlier test-host abort;
  the composed complete Linux suite and normal/no-build external package checks
  now pass. The correction is committed as **2de3069** and is included in the
  complete passing Linux, Windows 13 and Mac 15 compositions. The commit is
  part of the platform-fix milestone; replacement runtime/platform CI remains required.
- Intel macOS [37104402210](https://github.com/willibrandon/ankus/actions/runs/37104402210)
  retains its six-hour limit. Attempt one loses communication with its hosted
  runner after **2h00m14s**, without final integration reports. Attempt two
  completes all six modules on **ca5ab99 / PostgreSQL 18.6**: **11,360 total /
  11,328 passed / six failures / 26 skips**, **4h25m13.994s tests / 4h40m41s job**.
  It progresses beyond the reported operator cases. Package-consumer cases span
  **3h52m23.566s**; the slowest individual cases take about 29 minutes. All six
  failures involve temporary-path aliases: relative installation paths resolve
  incorrectly, and nested builds disagree about the physical project/home path.
  The pending physical-root fixture repair addresses this mechanism and passes
  the complete macOS ARM64/PostgreSQL 15 suite. Corrected Intel acceptance and
  the first attempt's disconnect cause remain unresolved. All six Intel reports,
  final logs and build timings are retained; no further timeout increase is needed
  to let this measured run finish.
- [Platform-version CI 37117650497](https://github.com/willibrandon/ankus/actions/runs/37117650497)
  passes both runtime jobs and the old-source macOS ARM64/PostgreSQL 16 suite.
  Its three failures are repaired in the current composition: macOS PostgreSQL
  15 now publishes `.so` before linking/control generation; Windows PostgreSQL
  13 uses explicit native export declarations and its supported signal API;
  Windows PostgreSQL 18 stages extension controls under the owned cluster's
  shorter directory. Native fixture filenames, older Windows regression-driver
  discovery, diagnostic transport and macOS temporary-path handling are also
  corrected. Original failures, independent reproductions and focused checks
  remain in the [evidence archive](docs/contributing/evidence/port-history.md).
  Complete corrected **15.19 and 16.15/macOS ARM64** and **13.23/Windows x64** suites pass
  as shown above, with all six reports and frozen source identities verified.
  Windows **18.6/ankus.3** writes all six reports: **11,568 total / 11,543 passed /
  25 skips / zero failures**. Its native test processes exit, but the outer
  interoperability launcher remains open without a captured command exit code.
  This older run did not confirm launcher success. Replacement native Windows
  **18 / ankus.4** CI now completes successfully in **31m18s**, with all six
  reports verified above, closing that acceptance gap for **04f0a8a**.
  Linux **18.6/ankus.3** also passes **11,568 total / 11,553 passed / 15 skips /
  zero failures, 36m41.247s**. The new-runtime Linux suite also passes, as recorded above.
- Indexed array lookup is committed as **46d0237**. It replaces repeated iterator scans with PostgreSQL's
  native indexed lookup, preserving the existing error guard. Thirteen more
  native storage-witness inputs cover element widths, NULL bitmap boundaries,
  multidimensional arrays and extreme lower bounds. All **236** affected native
  cases and **3,264** generator cases pass. At 10,000 cells the measured native
  storage-witness workload falls from **356.863 to 46.875 ms** for fixed-size
  values, and **510.075 to 195.115 ms** for nullable text. These are medians of
  seven warmed runs on Linux x64/PostgreSQL 18.6, not isolated accessor timings.
  Release passes with zero warnings/errors; API freshness and site checks pass.
  The ordinary complete Linux x64/PostgreSQL 18.6 suite passes **11,541 total /
  11,527 passed / 14 platform skips / zero failures, 38m36.172s**. All six reports
  and the seven array/API plus fifty accepted editor source identities verify.
  The subsequent 11,568-case Linux composition also passes with the platform
  fixture repairs and aggregate editor change. Complete Windows 13 and Mac 15
  compositions also pass. Replacement runtime-servicing CI remains pending.
  Pure-operation guard work and
  permanent benchmarks are still required.
- The preceding primary suites used compiler/runtime **10.0.12-ankus.3**, fork **a20021d**. Its
  universal-transition flag correction is verified in the rebuilt compiler's
  machine code; **128/128** retained-input compiles and complete primary suites
  pass. The original intermittent crash matches upstream evidence, but no local
  failing GC root was captured. See the
  [root-cause evidence and compiler packaging correction](docs/contributing/evidence/port-history.md#native-aot-gc-correction-and-compiler-packaging).
  The signal-mask correction is pushed as **d23f4e7**; the working tree selects
  new **ankus.4** packages and runs both native-object and real-runtime signal
  regressions before staging Unix runtime builds. The actual macOS ARM64 runtime
  build, matching package creation and complete PostgreSQL 15 and 16 suites pass.
  Linux x64 also passes the actual runtime build, compiler publication,
  all shutdown/signal checks and matching package creation. Final Release passes
  with zero warnings/errors (**2m04.17s**); API freshness and site build/check also
  pass. The complete Linux x64/PostgreSQL 18.6 servicing suite also passes.
  The platform fixes are committed as **bdb5d1c**. Replacement primary CI on
  **04f0a8a** passes all three complete suites with the new **ankus.4** payload.
  Corrected Intel and additional-version CI acceptance remain pending.

## Remaining work order

The accepted declaration milestones have complete primary-platform CI evidence.
The editor composition passes its full native suite and replacement primary CI.
The Intel diagnostic retry completes with six path-related failures. Corrected
Intel acceptance and an explanation of the earlier hosted-runner disconnect
remain required.

1. Resolve discovered correctness and CI failures before accepting affected work.
2. Finish the Intel timing milestone and complete supported-major/platform coverage, including Intel macOS and the macOS 15/16 library-suffix boundary.
3. Complete declaration diagnostics/code fixes, API discoverability and unsafe raw-call contracts.
4. Finish appropriate guard/array improvements with recovery tests and measured benchmarks.
5. Close remaining CLI, account/privilege, benchmark and platform-installation contracts.
6. Complete framework/discovery support, parsing/formatting helpers, representative samples and documentation.
7. Complete .NET servicing and every release requirement in the full inventory before publishing 0.1.0. If .NET 11 reaches GA first, complete its acceptance for that release; preview validation does not block the initial .NET 10 release.

Continue independent work while CI runs. Before every commit and push, check and
record previous run outcomes, including live runs, and resolve reported failures.
Keep this file current; append detailed implementation evidence to the archive
with a clear tested revision, platform/version, outcome and any remaining scope.
