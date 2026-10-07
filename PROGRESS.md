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
`fc91c63ebad11784647b50ee7e265c1fd9c9924f`. The refresh covers every release
change, including expanded headers and their source-derived maintenance command,
PG19 beta 4, CLI options, allocation cleanups, shared-memory lookup, reporting
interrupts and varlena safety. The pin and all seven header manifests already
match; matching inputs do not complete the release audit. See:

- [Complete requirement inventory](docs/contributing/evidence/parity-requirements.md): every inventoried command, API family, test corpus and release contract remains in scope.
- [pgrx 0.19.3 refresh gates](docs/contributing/evidence/parity-requirements.md#pgrx-0193-release-delta-and-refresh-gates): all eleven release commits, their .NET equivalents, existing evidence and remaining acceptance.
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
| Errors and cancellation | Sticky query cancellation/FATAL, retained raw/memory errors, fail-fast backend access and explicit rollback recovery. Transaction-completion callback failures use a fixed-buffer emergency log path without PostgreSQL diagnostic allocation; it preserves the primary error and durable outcome. Abort-time FATAL remains FATAL while irreversible FATAL is promoted to PANIC. Primary-platform full CI passes. | Retain recovery guarantees and complete the version/platform matrix. |
| Workers and shared memory | Native signal globals, lifecycle/transaction boundaries and shared memory. Idle Wait recovers repeated real cancellation; transaction waits still abort and terminal reports remain sticky. Complete primary-platform CI passes. | Full source-contract and complete version/platform acceptance remain required. |
| Functions and callbacks | Scalar/array/SETOF/TABLE, triggers/events, lifecycle, native callbacks, operators/conversions and installation-schema search paths. | Full upstream declaration/option audit and complete version/platform evidence. |
| Aggregates and SQL graph | Static abstract aggregate capabilities, typed Requires/Before/SupportFunction references, deterministic SQL provenance and extended module magic. Empty SQL dependency anchors and exact authored CR/CRLF values pass complete native acceptance. | Remaining precise provider/graph diagnostics, inventoried contracts and complete version/platform acceptance. |
| Generator caching | Detached equatable declaration/provider/reference models, declaration-relative locations and cached dispatcher/C/SQL artifacts across multi-declaration body edits and unrelated file insertion. Only selected SQL files are read, with the complete path catalog retained. | Inserting an unrelated member earlier in a file still invalidates composition. Broader precise diagnostics, useful semantic code fixes and final inventory audit remain required. |
| Values and ownership | Documented scalar, array, composite, temporal, JSON, network, geometry, custom codec and raw datum contracts. Interval equality, hashing and ordering agree with PostgreSQL while retaining exact components; complete primary-platform CI passes. | Remaining mapped/container contracts, parsing/formatting ergonomics and complete source-case mapping remain required. |
| Runtime performance | Owned binary PgNumeric, direct decimal coefficient encoding, constant-time fixed-width array indexing, and a measured native guard for allowlisted pure built-ins. Catalog-miss errors require actual rollback; real-resource regressions and complete primary-platform CI pass. Persistent in-backend benchmarks provide batching, transaction modes and statistical baseline comparisons. | Complete current guard supported-version acceptance. Extend that tier only where ownership proofs and measurements justify it. Never weaken error recovery to reduce overhead. |
| Native bindings | pgrx 0.19.3 inventories, matching PostgreSQL 19 beta 4 inputs and target-compiler ABI checks; typed pointers and ANKUS129 make raw caller obligations explicit. Complete current primary-platform compositions pass, with focused binding checks on all seven majors. | Full supported-major/platform tests and final inventory audit. |
| Development CLI | Version/project selection, installation/registration, cluster lifecycle, run/connect, test/regress/bench, property forwarding, environment selection, scriptable info and package prefixes. Benchmarks are measured inside PostgreSQL and retained in named comparison groups. | Persistent Windows diagnostic collection, account/privilege selection and remaining inventoried CLI/platform contracts. |
| .NET templates | Version-matched ordinary extension and worker templates reuse the CLI assets and pin local tools. Optional xUnit/NUnit consumers exercise managed and named backend cases, ignore reasons, worker processes and cleanup alongside default MSTest consumers. Complete primary-platform CI passes. | Remaining discovery contracts and complete version/platform acceptance. |
| API discoverability | Idiomatic attributed declarations, documented runtime APIs, named logging helpers and typed SPI interpolation. Compiler transport helpers are isolated in Ankus.CompilerServices and hidden from IntelliSense; complete primary-platform CI passes. | Remaining value/assertion helpers and final inventory audit. |
| Packages and release | MIT license, Brandon Williams copyright, author/repository/project metadata and deliberate SDK/runtime boundaries. | Full release gates and supported-platform packages before publishing 0.1.0. |
| Documentation and samples | Public guides, generated API pages, pgrx migration and backend-execution guidance; current status separated from historical evidence. SPI, error/reporting, bytea, strings, datetime and in-backend benchmark samples cover their complete authoring loops. | Every remaining inventoried representative sample and final usage/limitation review. |

The custom-type alignment review found no defect: variable-length PostgreSQL
types require at least four-byte datum alignment. Managed codec payload layout
is a separate contract, verified through copying/packed-field tests. Deriving
datum alignment from a CLR carrier or adding one/two-byte alignment would be
incorrect. See [the detailed review](docs/contributing/evidence/port-history.md#custom-datum-alignment-review).

## Current complete acceptance evidence

Primary CI runs all six modules against real published Native AOT extensions.
The latest successful primary CI source is **aa49c7d**, with runtime **10.0.12-ankus.4**.
[CI 37535642829](https://github.com/willibrandon/ankus/actions/runs/37535642829)
passes; [Docs run 37535642827](https://github.com/willibrandon/ankus/actions/runs/37535642827)
also passes. All eighteen actual reports and all sixty-seven required native recovery
partitions independently verify, alongside the callback and prefix corpora on
every platform; no primary job timed out.
A complete [Intel refresh](https://github.com/willibrandon/ankus/actions/runs/37460600236)
also passes on source **e41c687**.

| Source | Platform / PostgreSQL | Result | Duration |
| --- | --- | --- | --- |
| Latest primary CI, **aa49c7d / ankus.4** | Linux x64 / 18 | 13,422 total; 13,374 passed; 48 platform skips; zero failures | 40m37s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 13,422 total; 13,362 passed; 60 platform skips; zero failures | 28m49s job |
| Same CI / revision / runtime | Windows x64 / 17 | 13,422 total; 13,394 passed; 28 platform skips; zero failures | 39m37s job |
| SQL/datetime composition, parent **e41c687 / ankus.4** | Linux x64 / 18.6 | 13,337 total; 13,289 passed; 48 platform skips; zero failures | 42m46.885s tests; 43m41.597s command |
| Function-provider composition, parent **4da0bec / ankus.4** | Linux x64 / 18.6 | 13,365 total; 13,317 passed; 48 platform skips; zero failures | 42m39.165s tests; 43m46.937s command |
| Type-provider composition, parent **97842d3 / ankus.4** | Linux x64 / 18.6 | 13,411 total; 13,363 passed; 48 platform skips; zero failures | 42m39.033s tests; 43m35.244s command |
| Transaction-completion cleanup composition, parent **de27abd / ankus.4** | Linux x64 / 18.6 | 13,409 total; 13,361 passed; 48 platform skips; zero failures | 53m42.735s tests |

All eighteen primary reports and the module identity, callback, prefix and native
recovery partitions independently verify. Normal Release, API freshness and site
checks pass. Earlier source counts, timings, rejected attempts and superseded
results remain in the [acceptance history](docs/contributing/evidence/port-history.md#superseded-active-and-primary-acceptance-detail-before-sqldatetime-acceptance-2026-10-06).
These results establish the named combinations, not full-port completion.

Additional-platform results below are older revisions. Refreshing them against
the current source remains required, alongside the never-covered combinations.

| Earlier complete evidence | Platform / PostgreSQL | Result | Duration |
| --- | --- | --- | --- |
| [Version CI 37154140634](https://github.com/willibrandon/ankus/actions/runs/37154140634), **c24297f / ankus.4** | Linux x64 / each of 13–17 and 19 beta 4 | 11,640 total per major; 11,623 passed; 17 platform skips; zero failures | 38m20s–40m57s jobs |
| [Version CI 37145038020](https://github.com/willibrandon/ankus/actions/runs/37145038020), **b9b7eb5 / ankus.4** | macOS ARM64 / 15 and 16 | 11,609 total per major; 11,580 passed; 29 platform skips; zero failures | 26m17s / 26m32s jobs |
| Same version CI / revision / runtime | Windows x64 / 13 and 18 | 11,609 total per major; 11,584 passed; 25 platform skips; zero failures | 33m51s / 31m46s jobs |
| [Intel CI 37536883192](https://github.com/willibrandon/ankus/actions/runs/37536883192), **8b7e33b / ankus.4** | macOS x64 / 18.6 | 13,422 total; 13,362 passed; 60 platform skips; zero failures | 1h46m04s job |

Earlier timings, outcomes, failed checks and superseded acceptance details remain
in the [evidence archive](docs/contributing/evidence/port-history.md#acceptance-table-before-spi-flow-acceptance-2026-10-05).

## Active validation and work

- Refreshed supported-major run **37573419284** failed before test execution
  because the dedicated Linux version runner selected Clang 19 after header
  collection began requiring the LLVM 20 declaration-only frontend. The run was
  cancelled. Version CI now prepares LLVM 20 or later once before its parallel
  jobs start, and Linux selection prefers any installed supported toolchain.
  Replacement 13–17 and 19 evidence is pending.
- **aa49c7d** passes complete primary CI; its docs run also passes. All eighteen platform reports
  and native recovery, datetime, SQL, function-provider, callback, prefix and
  module-identity partitions independently verify.
  Crash tests retain their deadlines, fsync and durability checks. The earlier
  macOS allocation variance remains unattributed.
- Dedicated Intel refresh **37536883192** passes its complete **13,422**-case
  macOS x64/PostgreSQL 18 suite: **13,362** passed, **60** platform skips and
  zero failures. The full integration module completed in **1h39m56.422s**;
  the complete job took **1h46m04s**.
- A private Intel macOS x64 runner is online with macOS **15.8.1**, PostgreSQL
  **18.6** and LLVM **20.1.8**. The additional-platform workflow now selects its
  generic repository label only for owner-triggered `main` runs, gives the complete
  x64 suite GitHub's **360-minute** job limit and uses persistent local
  package/binding caches. Initial
  provisioning exposed a missing `pkg-config` installation and an unset macOS SDK
  root during Native AOT linking. The dependency is installed; runtime builds now
  select Apple Clang and the active SDK explicitly while binding generation retains
  LLVM 20. The first corrected CI attempt exposed one missed environment handoff
  in the Native AOT host publish. The second attempt passes the complete runtime
  job, then exposes the same missing SDK root in generated consumer builds during
  the full test step: MacPorts Clang cannot find standard C headers and Native AOT
  cannot resolve `-ldl`. The test-build command now persists the active SDK root
  for its own build and the following test step while retaining LLVM 20 as the
  binding frontend. An exact Intel Native AOT extension publish passes, followed
  by all **1,223** build-tool cases with zero failures and nine platform skips.
  The first corrected complete live suite ran for **54m26s** before the inherited
  60-minute job limit cancelled it. Its six-hour replacement completed successfully
  in **1h46m04s**.
- Function-provider diagnostics pass complete Linux/PostgreSQL **18.6** acceptance:
  **13,365** total, zero failures. All **1,853** ordinary source inputs and the
  exact **2,038**-file evidence archive verify; ten changed/new files are promoted.
  Release, API freshness and both site gates pass. Committed/pushed as
  **97842d3**; [primary CI](https://github.com/willibrandon/ankus/actions/runs/37480623539)
  passes all jobs. [Docs](https://github.com/willibrandon/ankus/actions/runs/37480623554)
  passes, including independent inspection of all eighteen platform reports.
- Type-provider diagnostics pass **46** focused cases, **4,455** generator cases
  and complete Linux/PostgreSQL **18.6** acceptance: **13,411** total, zero
  failures. All **1,855** source inputs and the **1,872**-file evidence archive
  verify. Sixteen changed/new files are promoted; Release, API freshness and both
  site gates pass. The unchanged baseline retains **38** failures and **8** controls.
- The SQL flow analyzer now preserves only independently verified complete quoted
  atoms through loop widening and layout overflow, recognizes direct runtime quote
  selectors in joined sequences, and fails closed after a shared **10,000-step**
  analysis budget. Unsafe array-flow misses, conditional-ref crashes and stale
  increment values are also corrected. All **14** focused cases, **4,468** generator
  cases and the complete Linux/PostgreSQL **18.6** suite pass: **13,422** total,
  **13,374** passed, **48** platform skips and zero failures in **42m54s**. Release,
  API freshness and both site gates pass. Transaction-completion cleanup now uses
  a fixed-buffer, encoding-independent server-log path that bypasses PostgreSQL
  diagnostic allocation and log hooks. All **33** focused executor cases pass, including abort, savepoint,
  durable commit/prepare, reporter-allocation faults and exact FATAL/PANIC behavior.
  The first complete run correctly rejected five stale client-notice expectations;
  their corrected server-log replacements pass **7/7**. The clean complete
  Linux/PostgreSQL **18.6** suite passes **13,409** cases with zero failures and
  **48** platform skips in **53m42.735s**.
- PostgreSQL-version documentation, LF source/SQL checkout attributes and the
  missing crash-test isolation annotation are corrected. The complete v0.19.3
  release audit remains an explicit requirement; no release-delta item is
  deferred. Header manifests now have a deterministic .NET maintenance command
  that applies pgrx 0.19.3's discovery and exclusion rules directly to each
  installed PostgreSQL 13–19 server-header tree. Catalog regeneration no longer
  copies pgrx's generated manifests; its check mode retains an exact pinned-release
  drift guard. Linux full-suite jobs check the selected
  manifest before running, so primary PostgreSQL 18 CI and the scheduled 13–17
  and 19 matrix continuously cover all seven majors. Exact current seven-major
  validation passes against PostgreSQL **13.23**, **14.24**, **15.19**, **16.15**,
  **17.11**, **18.6** and **19beta4**. The generated manifests byte-match pgrx
  v0.19.3 and contain **481**, **490**, **497**, **501**, **508**, **524** and
  **535** includes respectively. The first refreshed PostgreSQL 13 and 14 suites
  exposed one benchmark-fixture defect: they selected their root-owned system
  installations instead of the reserved writable pre-18 copies. Benchmark
  commands and their cluster now select the same isolated installation
  explicitly. Two newly registered matrix services also exposed missing
  PostgreSQL 15 and 16 toolchains before any test build; every 13–19 toolchain is
  now installed and verified there. Version-job concurrency is dispatch- or
  repository-configurable, with three eligible services available. Replacement
  supported-major evidence is pending. The refreshed Windows/PostgreSQL 18 cell
  also exposed a package-consumer Source Link path at exactly 260 characters and
  a cross-platform SQL-fixture assumption. Its owned root is now compact, and
  the fixture retains physical CRLF while expecting the Windows backend's deliberate
  normalization during extension-script parsing. Replacement Windows evidence is
  pending. The expanded-inventory audit now retains exact representative function,
  mutable-global, callback, inline-helper and struct-field contracts. A real backend
  case compares generated access to `max_prepared_xacts` with PostgreSQL's own
  setting value. Focused PostgreSQL 18 backend validation passes **16/16**, including
  that global and the corrected SQL-text fixture. The corrected PostgreSQL 14
  benchmark path passes its real in-backend case. The complete Linux
  x64/PostgreSQL 18 suite passes **13,447** total: **13,399** passed, **48**
  platform skips and zero failures in **42m02.194s**. After the final manifest
  edge correction, the exact-current build-tool module passes **1,234** total:
  **1,225** passed, **nine** platform skips and zero failures. The exact-current
  Release build, API freshness and both site gates pass. Replacement
  supported-major results remain pending.
- The benchmark sampling audit found that the earlier runner recalibrated and
  discarded short samples instead of retaining Criterion's fixed plan. The
  current change uses Criterion's exponential warmup and automatic linear/flat
  plan, rejects zero-duration samples, records the chosen mode, emits mean,
  median, median-absolute-deviation, slope and standard-deviation estimates,
  varies per-sample stack offsets, keeps Criterion's fixed 95% confidence level
  separate from significance, and reports slope as the primary linear estimate.
  Batched timing now matches pgrx's actual backend bridge by including input
  preparation, transaction boundaries and the routine in the elapsed interval.
  The six Linux modules pass **13,434** total: **13,386** passed, **48** platform
  skips and zero failures. The integration module accounts for **5,017** total,
  **5,002** passed and **15** skips in **43m11.907s**. The rebuilt generator suite
  passes **4,469/4,469** after the final SQL-literal correction. The exact-current
  Release build has zero warnings and errors; API freshness verifies **244** pages
  and **2,791** members; the site builds **295** pages and reports zero errors,
  warnings or hints.
- That matrix exposed a test-only PostgreSQL 13 incompatibility: the datetime
  cached-plan test queried execution-counter columns introduced in PostgreSQL 14.
  The test now inspects the prepared-statement row through JSON, verifies the
  absent PG13 fields and retains exact generic/custom execution counts on PG14+.
  All four PostgreSQL 13 cases pass against a real server. The Windows/PostgreSQL
  18 failure was separate: generated escape-string literals embedded physical
  CR/LF bytes, allowing checkout normalization to change an enum label before
  installation. Generated literals now emit explicit `\\r` and `\\n` escapes;
  the exact publication case and all four datetime cases pass **5/5** on
  PostgreSQL 18.
- Earlier-member cache investigation confirms that declaration ordinals change
  after unrelated insertions. Stable header identities and exact-current-span
  regression tests are drafted separately; they have no execution evidence yet.

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

1. Resolve discovered correctness and CI failures before accepting affected work. Finish the remaining declaration/source-case audits. Malformed AssemblyRef handling and the owner index already pass all eight regressions in the current complete suite.
2. Complete every pgrx 0.19.3 release-delta gate, including deterministic header discovery/regeneration, newly exposed native contracts, all CLI/configuration/output forwarding and allocation/shared-memory/varlena audits. Refresh evidence against the corrected current source.
3. Verify the dedicated Intel runner with a complete live run and finish supported-major/platform coverage, including Intel macOS and the macOS 15/16 library-suffix boundary.
4. Complete declaration diagnostics/code fixes, API discoverability and unsafe raw-call contracts.
5. Use the in-backend baseline to finish measured hot-path improvements that preserve recovery and ownership guarantees.
6. Close remaining CLI, account/privilege and platform-installation contracts.
7. Complete framework/discovery support, parsing/formatting helpers, representative samples and documentation.
8. Complete .NET servicing and every release requirement in the full inventory before publishing 0.1.0. If .NET 11 reaches GA first, complete its acceptance for that release; preview validation does not block the initial .NET 10 release.

Continue independent work while CI runs. Before every commit and push, check and
record previous run outcomes, including live runs, and resolve reported failures.
Keep this file current; append detailed implementation evidence to the archive
with a clear tested revision, platform/version, outcome and any remaining scope.
