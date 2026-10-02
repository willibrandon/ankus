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
| Workers and shared memory | Native signal globals, lifecycle/transaction boundaries and shared memory. Idle Wait recovers repeated real cancellation; transaction waits still abort and terminal reports remain sticky. Complete primary-platform CI passes. | Full source-contract and complete version/platform acceptance remain required. |
| Functions and callbacks | Scalar/array/SETOF/TABLE, triggers/events, lifecycle, native callbacks, operators/conversions and installation-schema search paths. | Full upstream declaration/option audit and complete version/platform evidence. |
| Aggregates and SQL graph | Static abstract aggregate capabilities, typed Requires/Before/SupportFunction references, deterministic SQL provenance and extended module magic. | Remaining inventoried contracts and complete version/platform acceptance. |
| Generator caching | Detached equatable declaration/provider/reference models, declaration-relative locations and cached dispatcher/C/SQL artifacts across multi-declaration body edits and unrelated file insertion. Only selected SQL files are read, with the complete path catalog retained. | Broader precise diagnostics, useful semantic code fixes and final inventory audit remain required. |
| Values and ownership | Documented scalar, array, composite, temporal, JSON, network, geometry, custom codec and raw datum contracts. Interval equality, hashing and ordering agree with PostgreSQL while retaining exact components; complete primary-platform CI passes. | Remaining mapped/container contracts, parsing/formatting ergonomics and complete source-case mapping remain required. |
| Runtime performance | Owned binary PgNumeric, direct decimal coefficient encoding, native recovery/allocator tests and measured query comparisons. | Appropriately guarded pure operations, array costs and permanent benchmarks. Never weaken error recovery to reduce overhead. |
| Native bindings | pgrx 0.19.3 inventories, matching PostgreSQL 19 beta 4 inputs and target-compiler ABI checks; focused binding checks cover all seven majors. | Full supported-major/platform tests, raw-call unsafe visibility and final inventory audit. |
| Development CLI | Version/project selection, installation/registration, cluster lifecycle, run/connect, test/regress, property forwarding, environment selection, scriptable info and package prefixes. | Account/privilege selection, in-backend benchmarking and remaining inventoried CLI/platform contracts. |
| .NET templates | Version-matched ordinary extension and worker templates reuse the CLI assets and pin local tools. Optional xUnit/NUnit consumers exercise managed and named backend cases, ignore reasons, worker processes and cleanup alongside default MSTest consumers. | Fresh complete primary-platform acceptance and remaining discovery contracts. |
| API discoverability | Idiomatic attributed declarations, documented runtime APIs, named logging helpers and explicit typed SPI interpolation with native ownership/recovery tests. | Compiler-helper namespaces/visibility and remaining value/assertion helpers. |
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
| 7b6ce06 source composition with parameterized SPI commands | Linux x64 / 18.6 | 11,120 total; 11,106 passed; 14 platform skips; zero failures | 24m31.735s tests |
| [CI 37016083898](https://github.com/willibrandon/ankus/actions/runs/37016083898), c3db5ed | Linux x64 / 18 | 11,120 total; 11,106 passed; 14 platform skips; zero failures | 26m36s job |
| Same CI / revision | macOS ARM64 / 18 | 11,120 total; 11,094 passed; 26 platform skips; zero failures | 20m12s job |
| Same CI / revision | Windows x64 / 17 | 11,120 total; 11,095 passed; 25 platform skips; zero failures | 25m22s job |
| [Version CI 37016275325](https://github.com/willibrandon/ankus/actions/runs/37016275325), c3db5ed | Linux x64 / 19 beta 4 | 11,120 total; 11,106 passed; 14 platform skips; zero failures | 28m12s job |
| c3db5ed composition with owned binary numeric | Linux x64 / 18.6 | 11,202 total; 11,188 passed; 14 platform skips; zero failures | 26m26.659s tests |
| [CI 37024581268](https://github.com/willibrandon/ankus/actions/runs/37024581268), ac01b4b | Linux x64 / 18 | 11,202 total; 11,188 passed; 14 platform skips; zero failures | 26m50s job |
| Same CI / revision | macOS ARM64 / 18 | 11,202 total; 11,176 passed; 26 platform skips; zero failures | 20m18s job |
| Same CI / revision | Windows x64 / 17 | 11,202 total; 11,177 passed; 25 platform skips; zero failures | 26m59s job |
| ac01b4b composition with direct decimal encoding | Linux x64 / 18.6 | 11,248 total; 11,234 passed; 14 platform skips; zero failures | 25m08.623s tests |
| [CI 37030652354](https://github.com/willibrandon/ankus/actions/runs/37030652354), e38dce4 | Linux x64 / 18 | 11,248 total; 11,234 passed; 14 platform skips; zero failures | 26m39s job |
| Same CI / revision | macOS ARM64 / 18 | 11,248 total; 11,222 passed; 26 platform skips; zero failures | 20m12s job |
| Same CI / revision | Windows x64 / 17 | 11,248 total; 11,223 passed; 25 platform skips; zero failures | 26m49s job |
| e38dce4 composition with worker/interval corrections | Linux x64 / 18.6 | 11,250 total; 11,236 passed; 14 platform skips; zero failures | 34m47.341s tests |
| [CI 37041069194](https://github.com/willibrandon/ankus/actions/runs/37041069194), 68fd2de | Linux x64 / 18 | 11,250 total; 11,236 passed; 14 platform skips; zero failures | 27m00s job |
| Same CI / revision | macOS ARM64 / 18 | 11,250 total; 11,224 passed; 26 platform skips; zero failures | 20m14s job |
| Same CI / revision | Windows x64 / 17 | 11,250 total; 11,225 passed; 25 platform skips; zero failures | 25m39s job |
| 68fd2de with compiler/selection corrections | Linux x64 / 18.6 | 11,303 total; 11,289 passed; 14 platform skips; zero failures | 30m14.890s tests |
| c4a092d with resolver, aggregate and framework templates | Linux x64 / 18.6 | 11,341 total; 11,327 passed; 14 platform skips; zero failures | 44m09.185s tests |
| Same composition with final Windows fixture corrections | Windows x64 / 17.11 | 11,341 total; 11,316 passed; 25 platform skips; zero failures | 39m26.748s tests |

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

- Final review-composition acceptance passes the complete Windows x64/PostgreSQL **17.11** suite: **11,341 total; 11,316 passed; zero failures; 25 platform skips; 39m26.748s**. This verifies both reported path-case failures and the concurrent consumer restore correction, plus the new resolver, inherited aggregate and xUnit/NUnit template contracts. All **327** source and **52** runtime identities verify afterward. The **325** non-evidence files match the final Linux-validated composition apart from Git line endings. The earlier complete Linux run used longer consumer cache paths; corrected Linux template checks and final Release/API/site gates also pass. Fresh primary CI remains required.
- The direct-decimal milestone is committed and pushed as **e38dce4**. [CI 37030652354](https://github.com/willibrandon/ankus/actions/runs/37030652354) passes quality, all runtime jobs and all three complete primary-platform suites; counts above come from six completed TRX modules per platform. [Docs 37030652372](https://github.com/willibrandon/ankus/actions/runs/37030652372) passes. Previous primary/docs/version outcomes were checked and retained before both commit and push; the Intel timeout remains unresolved.
- The worker/interval corrections are committed as **68fd2de**. [CI 37041069194](https://github.com/willibrandon/ankus/actions/runs/37041069194) passes quality, runtime and all three complete primary-platform suites; six downloaded TRX modules confirm each platform's counters above. [Docs 37041069249](https://github.com/willibrandon/ankus/actions/runs/37041069249) passes. The earlier native red/green, complete local acceptance and final gates remain in the history.
- The composed compiler/selection corrections pass literal plain `dotnet test` against PostgreSQL **18.6/Linux x64**: **11,303 total; 11,289 passed; zero failures; 14 platform skips; 30m14.890s**. The generator module passes **3,083/3,083**, zero skips. Multi-declaration body edits and earlier file insertion preserve actual final output caches; cached diagnostics bind to current exact spans. ANKUS044 rejects direct nonconstant SPI string interpolation without changing raw-string or typed-parameter contracts. Selected SQL files are read without losing full-path ambiguity checks. Conflicting project selections fail explicitly, while overrides and absent-project defaults remain supported. All 307 source and 26 runtime hashes verify after success; the additional pipeline file matches the frozen transfer archive. Final composed gates and fresh CI remain required.
- Extension commands share evaluated SDK-project resolution for `.sln`, `.slnx`, child SDK elements and imported SDK selection. Visible inherited aggregate roles require their capability interfaces. Focused resolver checks pass **15/15** and inherited capability checks **13/13**. Installed CLI/template framework combinations pass **13/13** with real managed/backend tests. Complete Linux and corrected Windows acceptance pass as recorded above. Environment, diagnostics, namespace, performance and platform-coverage work remains open.
- [CI 37046774035](https://github.com/willibrandon/ankus/actions/runs/37046774035), **c4a092d**: quality, all runtime jobs and complete Linux/macOS ARM64 suites pass; Windows fails two path-case assertions and one concurrent local-tool restore. Linux reports **11,303 total / 11,289 passed / 14 skips**, and macOS **11,303 / 11,277 / 26**, with zero failures. Windows reports only four completed modules, **8,792 total / 8,764 passed / three failures / 25 skips**; this is not complete Windows acceptance. Both path cases and both original installed consumers pass the focused Windows repair. [Docs 37046774045](https://github.com/willibrandon/ankus/actions/runs/37046774045) passes. Detailed root causes, corrections and validation outcomes are retained in the history.
- Owned binary numeric is committed and pushed as **ac01b4b**. [Primary CI 37024581268](https://github.com/willibrandon/ankus/actions/runs/37024581268) passes quality, runtime and all three complete primary-platform suites. Counts above come from six completed TRX modules per platform. [Docs 37024581449](https://github.com/willibrandon/ankus/actions/runs/37024581449) passes.
- Direct-decimal encoding preserves coefficient/sign/scale without intermediate text. Focused runtime checks pass **136/136**, and PostgreSQL **18.6/Linux x64** numeric/range/tuple checks pass **271/271**, with zero skips. The plain complete suite passes **11,248 total; 11,234 passed; zero failures; 14 platform skips; 25m08.623s**. All 293 source and 26 runtime hashes verify afterward. Same-toolchain warm ABBA query comparisons reduce the managed-decimal median from **7.921ms to 5.720ms**, ratio **0.722**, with 20,000 rows and 22 measured samples per variant; all sums match independent PostgreSQL expressions. These are query timings, not isolated arithmetic/guard evidence. Final composed Release passes with zero warnings/errors (**1m22.56s**), API freshness verifies **238 pages / 2,756 members**, and site build/check passes (**286 pages, 3.27s; zero diagnostics**). All 290 applicable promoted identities match. Fresh platform CI remains required after publishing.
- PostgreSQL 19 fixture corrections are committed as **7b6ce06**, followed by parameterized SPI commands as **c3db5ed**; both are pushed. [Primary CI 37016083898](https://github.com/willibrandon/ankus/actions/runs/37016083898) passes quality, all runtime jobs and all three complete platform suites. [Docs 37016083680](https://github.com/willibrandon/ankus/actions/runs/37016083680) and [PostgreSQL 19 replacement CI 37016275325](https://github.com/willibrandon/ankus/actions/runs/37016275325) also pass. Each of the four platform/version suites has six downloaded TRX modules independently confirming the counters above.
- Owned binary numeric passes the plain complete PostgreSQL **18.6/Linux x64** suite: **11,202 total; 11,188 passed; zero failures; 14 platform skips; 26m26.659s**. All six module summaries finish successfully and all **293** frozen source identities verify afterward. Focused managed checks pass **81/81**, and focused numeric/range/tuple checks pass **271/271** on both PostgreSQL **18.6** and **13.23**, with no skips. The latter also verifies all **26** accepted runtime files after completion. Release/API/site gates pass. Measured 20,000-row comparisons retain independent SQL checksums and 22 samples per variant: binary/text median ratios are **0.850** for small identity, **0.940** for small addition, **0.505** for 1,024-digit identity and **0.581** for wide addition; the native SQL control is **1.009**. Managed decimal returns regress to **1.133** and need direct binary encoding rather than intermediate text. These are query timings, not isolated arithmetic/allocation or guard-tier claims. Fresh complete platform CI remains required after committing, and pure guards, array costs, permanent benchmarks and complete version/platform acceptance remain open. Detailed failures, corrections and timings are retained in the history.
- Explicit `Spi.Sql` interpolation and typed `SpiCommand` overloads now pass the final complete PostgreSQL **18.6/Linux x64** composition: **11,120 total; 11,106 passed; zero failures; 14 platform skips; 24m31.735s**. All six completed TRX modules confirm the counters and all 279 frozen source identities verify afterward. The final Release build has zero warnings/errors (**31.37s**); API freshness verifies **238 pages / 2,756 members**, and site build/check passes with zero diagnostics. Native guards, transaction snapshots and original string APIs remain intact. Fresh primary-platform CI remains required after publishing.
- Current-helper isolated PostgreSQL **18.6/Linux x64** diagnostics preserve all ten native artifacts and all three managed companion artifacts across cold and two warm calls. Source collection takes **12.151s**, then **3.355s / 3.340s**; companion compilation takes **37.862s**, then **1.568s / 1.579s**. A representative warm source call spends **1.236s** on native declaration verification and **0.727s** on layout measurement; SDK/runtime hashing in a warm companion call takes **0.140s**. This narrows the investigation without establishing Intel's cause or a performance correction. The diagnostic instrumentation remains isolated and is not promoted.
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
- The accepted SPI command composition preserves declared types/SQL NULL, original string APIs, snapshot roles and native guards. Compiler support lives in `Ankus.CompilerServices`. Focused managed/compiler/native checks pass **15/15**, **8/8** and **70/70**; complete final acceptance and final Release/API/site evidence are recorded above. Both unsuccessful validation-launch attempts are retained in the history, with zero product assertions executed by those invalid launches.
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
4. Finish appropriate guard/array improvements with recovery tests and measured benchmarks.
5. Close remaining CLI, account/privilege, benchmark and platform-installation contracts.
6. Complete framework/discovery support, parsing/formatting helpers, representative samples and documentation.
7. Complete .NET servicing and every release requirement in the full inventory before publishing 0.1.0. If .NET 11 reaches GA first, complete its acceptance for that release; preview validation does not block the initial .NET 10 release.

Continue independent work while CI runs. Before every commit and push, check and
record previous run outcomes, including live runs, and resolve reported failures.
Keep this file current; append detailed implementation evidence to the archive
with a clear tested revision, platform/version, outcome and any remaining scope.

Final review-composition verification passes Release with zero warnings or errors
(**1m38.80s**), API freshness (**238 pages / 2,756 members**), site build (**286
pages, 4.90s**) and zero check diagnostics. All **327** frozen source identities
match afterward. Complete Windows acceptance also passes; fresh primary-platform
CI remains required. The earlier Intel timeout remains open. Detailed negative, corrected
and final evidence is retained in the linked history.
