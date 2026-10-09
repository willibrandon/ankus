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
| Aggregates and SQL graph | Static abstract aggregate capabilities, typed Requires/Before/SupportFunction references with per-contract diagnostics at the authored value, deterministic SQL provenance and extended module magic. Empty SQL dependency anchors and exact authored CR/CRLF values pass complete native acceptance. | Inventoried contracts and complete version/platform acceptance. |
| Generator caching | Detached equatable declaration/provider/reference models, stable declaration-header locations and cached dispatcher/C/SQL artifacts across multi-declaration body edits, unrelated member insertion and unrelated file insertion. Only selected SQL files are read, with the complete path catalog retained. | Broader precise diagnostics, useful semantic code fixes and final inventory audit remain required. |
| Values and ownership | Documented scalar, array, composite, temporal, JSON, network, geometry, custom codec and raw datum contracts. Interval equality, hashing and ordering agree with PostgreSQL while retaining exact components; complete primary-platform CI passes. | Remaining mapped/container contracts, parsing/formatting ergonomics and complete source-case mapping remain required. |
| Runtime performance | Owned binary PgNumeric, direct decimal coefficient encoding, constant-time fixed-width array indexing, and a measured native guard for allowlisted pure built-ins. Catalog-miss errors require actual rollback; real-resource regressions and complete primary-platform CI pass. Persistent in-backend benchmarks provide batching, transaction modes and statistical baseline comparisons. | Complete current guard supported-version acceptance. Extend that tier only where ownership proofs and measurements justify it. Never weaken error recovery to reduce overhead. |
| Native bindings | pgrx 0.19.3 inventories, matching PostgreSQL 19 beta 4 inputs and target-compiler ABI checks; typed pointers and ANKUS129 make raw caller obligations explicit. Complete current primary-platform compositions pass, with focused binding checks on all seven majors. | Full supported-major/platform tests and final inventory audit. |
| Development CLI | Version/project selection, installation/registration, cluster lifecycle, run/connect, test/regress/bench, property forwarding, environment selection, scriptable info and package prefixes. Benchmarks are measured inside PostgreSQL and retained in named comparison groups. `install --sudo`, `test --runas` and `regress --runas` follow cargo-pgrx with real-account evidence. | Persistent Windows diagnostic collection, run-as provisioning of the macOS and version runners, and remaining inventoried CLI/platform contracts. |
| .NET templates | Version-matched ordinary extension and worker templates reuse the CLI assets and pin local tools. Optional xUnit/NUnit consumers exercise managed and named backend cases, ignore reasons, worker processes and cleanup alongside default MSTest consumers. Complete primary-platform CI passes. | Remaining discovery contracts and complete version/platform acceptance. |
| API discoverability | Idiomatic attributed declarations, documented runtime APIs, named logging helpers and typed SPI interpolation. Compiler transport helpers are isolated in Ankus.CompilerServices and hidden from IntelliSense; complete primary-platform CI passes. | Remaining value/assertion helpers and final inventory audit. |
| Packages and release | MIT license, Brandon Williams copyright, author/repository/project metadata and deliberate SDK/runtime boundaries. | Full release gates and supported-platform packages before publishing 0.1.0. |
| Documentation and samples | Public guides, generated API pages, pgrx migration and backend-execution guidance; current status separated from historical evidence. All 37 of pgrx's examples map to runnable samples with backend integration tests. | Final usage/limitation review across samples and guides. |

The custom-type alignment review found no defect: variable-length PostgreSQL
types require at least four-byte datum alignment. Managed codec payload layout
is a separate contract, verified through copying/packed-field tests. Deriving
datum alignment from a CLR carrier or adding one/two-byte alignment would be
incorrect. See [the detailed review](docs/contributing/evidence/port-history.md#custom-datum-alignment-review).

## Current complete acceptance evidence

Primary CI runs all six modules against real published Native AOT extensions.
The latest successful primary CI source is **e0e469a**, with runtime **10.0.12-ankus.4**.
[CI 37825380597](https://github.com/willibrandon/ankus/actions/runs/37825380597)
passes; [Docs run 37825380526](https://github.com/willibrandon/ankus/actions/runs/37825380526)
also passes. Each platform completed all six modules with zero failures; no
primary job timed out. It is the first green primary run since **67c9cb5**.
A complete [Intel refresh](https://github.com/willibrandon/ankus/actions/runs/37460600236)
also passes on source **e41c687**.

| Source | Platform / PostgreSQL | Result | Duration |
| --- | --- | --- | --- |
| Latest primary CI, **e0e469a / ankus.4** | Linux x64 / 18 | 13,883 total; 13,833 passed; 50 platform skips; zero failures | 11m37s test step; 13m48s job |
| Same CI / revision / runtime | macOS ARM64 / 18 | 13,883 total; 13,821 passed; 62 platform skips; zero failures | 12m03s test step; 13m44s job |
| Same CI / revision / runtime | Windows x64 / 17 | 13,883 total; 13,847 passed; 36 platform skips; zero failures | 21m50s test step; 24m04s job |
| SQL/datetime composition, parent **e41c687 / ankus.4** | Linux x64 / 18.6 | 13,337 total; 13,289 passed; 48 platform skips; zero failures | 42m46.885s tests; 43m41.597s command |
| Function-provider composition, parent **4da0bec / ankus.4** | Linux x64 / 18.6 | 13,365 total; 13,317 passed; 48 platform skips; zero failures | 42m39.165s tests; 43m46.937s command |
| Type-provider composition, parent **97842d3 / ankus.4** | Linux x64 / 18.6 | 13,411 total; 13,363 passed; 48 platform skips; zero failures | 42m39.033s tests; 43m35.244s command |
| Transaction-completion cleanup composition, parent **de27abd / ankus.4** | Linux x64 / 18.6 | 13,409 total; 13,361 passed; 48 platform skips; zero failures | 53m42.735s tests |

Normal Release, API freshness and site checks pass. Earlier source counts,
timings, rejected attempts and superseded
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

- **2b841a3** passes the complete Linux x64 suite on PostgreSQL 18.6 with run-as
  (**14,442** total; **14,391** passed; 51 skips; zero failures; **41m03s**). Its
  Windows x64 suites on PostgreSQL 17.11 and 13.23, run concurrently at package
  concurrency 20 (**14,443** total each; **14,397** passed; 44 skips; **38m25s**
  and **36m34s**), failed only the bench test's cleanup: Git marks the object files
  of the repository that test creates read-only, and Windows would not delete
  them. **f1a6895** removes that repository with its attributes cleared, and the
  bench test then passes on both majors.
- `PgPropertyRunner` and `PgGenerators` port pgrx's `PgTestRunner`: a property
  runs against 256 generated inputs inside the backend, each in its own
  subtransaction, so a PostgreSQL error is a failing input whose work rolls back
  while the run continues. Failing inputs shrink by editing the recorded random
  choices, as Hypothesis does, so composed generators shrink without extra code;
  integers are a sign and a magnitude and reach their exact boundary. The report
  names the smallest input, its error and the seed. .NET has no Microsoft-owned
  property-testing package, so the generators are Ankus's own. pgrx-unit-tests'
  eight temporal `proptests` now run through it with pgrx's raw-value strategies.
- `ankus bench --report` prints pgrx's history report: a section per benchmark
  with its first successful run as the baseline, the ten most recent groups as
  bars with their change, drift marks for build configuration, PostgreSQL major,
  runtime and non-default settings, and counts of omitted failed runs. Run groups
  now record the extension and .NET SDK versions, the command line, the tool
  version, the Git commit, branch, dirty state and describe output, and each
  setting's unit, source and boot and reset values. A refresh blocked by a
  dependent object names `--cascade`, and `--wait` prints pgrx's waiting message.
  The bench test now covers default group names, `--postgresql-conf`, `--cascade`,
  `--resetdb`, partial and failed groups and comparison filtering. Test clusters
  can run under Valgrind (`ankus test --valgrind`), and every Valgrind start
  passes PostgreSQL's `valgrind.supp`, which `ankus init` now keeps in the PGXS
  tree. The cluster locale is asserted per platform, and an unknown
  `upgrade --package` fails before any change. Messages a test raises that
  contain `TMSG: ` reach its output live, as pgrx echoes them. Installing from a
  solution directory and `schema` from the project directory with a relative
  intermediate path are tested. `[PgBenchmark]` can report throughput
  in pgrx's units, which pgrx's own `Bencher` cannot set. The tooling mapping now
  has 82 covered rows, 12 documented differences, 24 Rust-specific rows and no
  gaps or partials.
- **921113f** passes complete suites on this repository's validation machine:
  Windows x64 on PostgreSQL 17.11 and 13.23 run concurrently at package
  concurrency 20 (**14,410** total each; **14,368** passed; 42 skips; zero
  failures; **41m44s** and **41m51s**) and Linux x64 on PostgreSQL 18.6 with
  run-as (**14,410** total; **14,359** passed; 51 skips; zero failures; **43m56s**),
  all three at once. Its primary CI
  ([37933668208](https://github.com/willibrandon/ankus/actions/runs/37933668208))
  passes Linux x64 on 18 (18m), Windows x64 on 17 (21m) and macOS ARM64 on 18
  (16m). Its PostgreSQL 19 beta 4 rerun
  ([37934631721](https://github.com/willibrandon/ankus/actions/runs/37934631721))
  passes Linux x64 with the PGLZ sample fix (**14,410** total; **14,359** passed;
  51 skips; zero failures; 25m job), completing PostgreSQL 13 through 17 and 19
  on the version matrix after 5b72b18's run. The platform version run on **10c1391**
  ([37917953859](https://github.com/willibrandon/ankus/actions/runs/37917953859))
  passes macOS ARM64 on PostgreSQL 15 (23m) and 16 (21m) and Windows x64 on 13
  (1h11m). Windows x64 on 18 failed two cleanups that deleted a cluster directory
  while a stopped server still held handles: the regression-prerequisite cleanup
  fixed in **ac6486e**, and the development-diagnostics cleanups, which now use
  the same retrying delete.
- Generated custom-type storage now serializes `Guid`, `DateOnly`, `TimeOnly`,
  `DateTime`, `DateTimeOffset` and `TimeSpan` members, as System.Text.Json does
  and as pgrx's serde storage does for its date type. JSON text uses
  System.Text.Json's formats; CBOR uses the registered UUID (37), full-date (1004)
  and date/time (0) tags where they exist. Runtime tests pin the bytes and text
  and reject other spellings; seeded pgrx `RandomData` values with unsigned
  64-bit and date-list members survive table storage, compression, arrays and
  every exchange path in the backend.
- A second source-case mapping covers pgrx's compile-fail, `todo` and UI cases,
  its testing framework, its benchmark runner and `cargo-pgrx/tests` (203 cases in
  118 rows: 66 covered, 14 partial, 3 gaps, 11 deliberate differences and 24
  Rust-specific at the time; see the entry above for the current counts). The test fixture now creates the extension with `CASCADE`, as
  pgrx's does, so an extension that `requires` another starts; a failed test's
  report includes the server's account of its backend when that process died;
  `ankus bench` prints pgrx's running line, intervals, change and group summary,
  with benchmarks missing from the current run and why a comparison is
  unavailable; and new tests pin the comparison summaries, concurrent test
  isolation, process-exit cleanup, the logging defaults and the composite and
  text-array signatures pgrx keeps in its `todo` corpus. The ranked gaps start with Valgrind runs of test clusters and the
  benchmark history report.
- `PgMemoryContext.CreateFlatArray<T>` builds a zeroed PostgreSQL array of a
  fixed-size scalar in place, the counterpart of pgrx's `FlatArray::new_zeroed_in`;
  it rejects zero-length dimensions, more than six dimensions and PostgreSQL's
  element and 1 GB allocation limits before allocating. `PgArrayView` and
  `PgArrayView<T>` expose the native SQL NULL bitmap like pgrx's `RawArray::nulls`.
  With these, every case in pgrx's unit-test corpus maps to a passing Ankus test,
  a documented difference or a Rust-specific guarantee.
- More of pgrx's unit tests now run in the backend: a backend ERROR's managed
  stack trace, seeded temporal round trips checked against `*_send` bytes, the
  installed signature matrix read back from `pg_proc`, a typed
  `PgArrayView<bool>`, a trigger opening its relation, an array-of-composite
  field set in code, the current memory context's parent, an untracked worker's
  termination, a parameter named like its function and a test-only function
  under `#if ANKUS_TESTS`. Test publications define `ANKUS_TESTS` and benchmark
  publications `ANKUS_BENCHMARKS`, like pgrx's `pg_test` feature. A generator
  test compiles every declaration kind beside types named `System`, `Ankus`,
  `Spi` and other names generated code uses. SPI conversion errors now name the
  source value's type.
- **ac6486e** passes the complete Windows x64 suite on PostgreSQL 17.11 (**14,382**
  total; **14,340** passed; 42 skips; zero failures; **58m** while the machine also
  ran PostgreSQL 13 and Linux suites and focused runs). Its PostgreSQL 13.23 run
  failed only two regression-prerequisite cleanups, which deleted a directory
  while a stopped server's handles were still closing; that cleanup now waits as
  PostgreSQL's own Windows `unlink` does. Its Linux run failed two cases on
  30-second client timeouts while five suites shared the machine; the next
  revision is validated without that contention.
- **805783b** passes complete Windows x64 suites on PostgreSQL 17.11 (**14,312**
  total; **14,270** passed; 42 skips; zero failures; **40m** while the machine also
  ran CI and Linux) and Linux x64 on PostgreSQL 18.6 with run-as (**14,261** passed;
  51 skips; zero failures; **43m**). Its PostgreSQL 13.23 Windows run failed only
  the benchmark payload test: a constant-time routine could measure zero ticks on
  Windows's 100 ns stopwatch under load. **10c1391** gives that test measurable work.
  **5b72b18** also passes Linux x64 on PostgreSQL 18.6 with run-as (**14,203**
  passed; 51 skips; **17m**) and CI
  ([37909292198](https://github.com/willibrandon/ankus/actions/runs/37909292198):
  Linux 25m, Windows 29m, macOS 15m while sharing hosts with local validation).
  The version matrix rerun with the socket fix
  ([37909296064](https://github.com/willibrandon/ankus/actions/runs/37909296064))
  passes PostgreSQL 13 through 17; 16 and 17 had failed on the socket path, and 13
  and 14 took **70m** on the flex runners. PostgreSQL 19 failed one pglz sample
  case: its beta defaults `default_toast_compression` to LZ4 when built with it,
  so the stored size no longer matched the PGLZ estimate. That case now sets the
  column to PGLZ on PostgreSQL 14 and later.

- `System.Text.Rune` maps to `varchar`, as Rust's `char` does in pgrx. Generated
  arguments, results, `Rune[]` and `PgArray<Rune?>` (`varchar[]`), SPI parameters,
  cells and edited rows all carry the scalar value as character text, and returned
  arrays keep the `varchar` element type. pgrx's `rt_char`, `rt_array_char`
  (including U+10FFFF and NULL elements) and `takes_char` values pass through all
  eight SPI paths in the backend. Where pgrx takes the first character and reads
  empty text as SQL NULL, a `Rune` requires exactly one scalar value: empty text,
  `'ab'`, a base letter with a combining accent and padded `bpchar` fail with
  38000 and the backend continues. Runtime tests pin the UTF-8 bytes and the
  unpaired-surrogate message.
- Two more mapped gaps are closed in the backend. A background worker whose wait
  loop ends on termination commits a final transaction that the launching
  backend reads after shutdown, as pgrx's `bgworker` does. pgrx's composite
  default and variadic cases run with and without arguments, including NULL
  rows, and an explicit function cast between two composite types installs,
  converts and keeps strict NULL. Borrowed-text set functions read table columns
  stored compressed inline, out of line and compressed out of line, and 10,000
  composites built from one descriptor stream to the client and through SPI.
  Methods whose only parameter is a nullable custom-text record or enum run on
  SQL NULL from non-strict functions, and a `string? = null` default is executed
  omitted, supplied and NULL.
- A configuration check that throws on its boot value while a library load
  registers the setting now fails that load with an ERROR carrying the managed
  message, matching pgrx and a C hook's ERROR; previously it was a rejection,
  which PostgreSQL turns into FATAL for a boot value. Only registration changes:
  returned rejections, reload checks and ordinary `SET` keep their policy, and
  the same backend loads the library again afterwards. `SHOW ALL` omits a
  no-show setting, and an unlabelled GUC enum member is set by its C# name and
  restored in parallel workers.
- Complete Windows x64 suites on **5b72b18** pass PostgreSQL 17.11 and 13.23
  concurrently at package concurrency 20 (**14,254** total each; **14,212** passed;
  42 skips; zero failures; **34m** each while sharing the machine).

- Three pgrx capabilities are added. `PgDiagnostic.Domain` and `PgException.Domain`
  carry the message domain of C's `ereport_domain` and pgrx's `ereport_domain!`:
  the native report passes it to `errstart`, and captured ErrorData returns it,
  so a caught error reports `test_extension_domain` after crossing PostgreSQL and
  backend errors report `postgres-<major>`. `PgLog.Write(level, $"...")` uses a new
  interpolated-string handler that evaluates nothing when PostgreSQL would not
  report the level, as pgrx's logging macros skip their arguments; at default
  thresholds DEBUG is skipped and WARNING evaluated. JSON and JSONB aggregate
  states run with strict, immutable, parallel-safe transitions. Direct calls pass
  and return path and polygon values, a borrowed empty bytea reports zero bytes,
  and double infinities, NaN and numeric-to-real overflow match SQL casts exactly,
  including PostgreSQL 13's rejection of numeric infinity. The pgrx mapping's
  remaining gaps are `Rune` to `varchar`, background-worker SPI after termination,
  composite VARIADIC/DEFAULT execution and a few smaller cases; `Rune` has since
  been added.
- The dedicated Intel macOS runner passes its complete suite on **a3e39c8**
  ([additional platforms 37892336665](https://github.com/willibrandon/ankus/actions/runs/37892336665), **1h15m**).
  The Linux version matrix on the same revision
  ([37892281684](https://github.com/willibrandon/ankus/actions/runs/37892281684))
  passes PostgreSQL 14 and 15. PostgreSQL 16, 17 and 19 fail the same 32
  consumer-fixture cases on newly added runners whose work directories are long:
  without an `ankus test` session, a fixture placed its Unix socket under `TMPDIR`
  and exceeded PostgreSQL's 107-byte socket path. The socket directory now stays
  under `TMPDIR` only while the longest socket path fits and otherwise uses `/tmp`,
  as PostgreSQL's own test clusters do; direct tests cover the byte boundary,
  including multibyte names. PostgreSQL 13's job stopped when its runner service
  received a shutdown signal. The matrix is to be rerun with the fix.
- Complete Windows x64 suites on **0edd7e6** pass PostgreSQL 17.11 and 18.6
  concurrently at package concurrency 20 (**14,236** total each; **14,194** passed;
  42 skips; zero failures; **26m**), confirming the PostgreSQL 18 staging fix.

- Every test case in pgrx's `pgrx-unit-tests/src/tests/` (539 cases, 63 files)
  is now mapped to a named Ankus test, a deliberate difference or a Rust-specific
  reason in the [pgrx unit-test mapping](docs/contributing/evidence/pgrx-test-mapping.md).
  Most cases were already covered, often with more boundary values. New backend
  cases close the safety-relevant gaps: SPI errors inside set-returning functions
  before and during streaming and materialization (exact SQLSTATE and message,
  cleanup once, the backend reruns the function, and a caught error continues
  the set); ordinary exceptions from a callback PostgreSQL invokes, uncaught and
  caught from a subtransaction; configuration check rejection for bool, real,
  string and enum settings with PostgreSQL's standard and custom diagnostics;
  LWLock release during managed unwinding without an abort; range reconstruction
  from bounds and daterange infinity, missing-bound and empty edges; omitted
  trailing defaults by name and by OID; the default call collation; missing
  types, unknown tuple fields and non-composite reads; cursor argument counts and
  parse errors; utility-statement metadata; and nested domains. Three behaviors
  changed: a blank function name now fails with PostgreSQL's 42602 as a malformed
  name does, a non-nullable read of an empty result says the query returned no
  rows instead of reporting SQL NULL, and PostgreSQL 18 test staging on Windows
  returns to its short path after the run-as change had moved it past MAX_PATH.
  That last regression appeared in a complete Windows x64/PostgreSQL 18.6 run
  (**14,184** total; **14,141** passed; one failure; **15m41s**). The ranked
  remaining gaps include `Rune` to `varchar`, an `ereport` message domain, JSON
  aggregate states and background-worker SPI after termination.

- cargo-pgrx's account and privilege options are ported. `ankus install --sudo`
  stages the publication as the current account, then runs `sudo cp` beside each
  destination and `sudo mv` into place, control file last. Unlike pgrx's in-place
  copy, a running server never maps a partly written library. A denied ordinary
  installation now suggests `--sudo`, and Windows rejects the option. `ankus test
  --runas` and `PostgresTestClusterOptions.RunAs` run `initdb`, `pg_ctl` and the
  server's directory, configuration, log and cleanup operations through `sudo -u`.
  That account owns the data, socket and native log, and logs are read back through
  `sudo`. Publications stay with the test account, so unlike pgrx no root-owned
  installation is written. `ankus regress --runas` creates and drops the database
  through `sudo -u` psql as the role of the same name, as pgrx runs `createdb` and
  `dropdb`. Cancellation stops only `sudo`, whose descendants belong to another
  account. Downloaded installations carry an absolute `RUNPATH` into their home
  directory, so another account cannot run them from a private home; distribution
  and Homebrew packages work. Evidence on Linux x64/PostgreSQL 18.6 uses a real
  `ankus-runas` account reached through a sudoers rule that grants only that
  account, with a root-owned relocatable installation. The backend process runs as
  that account; `ankus test --runas` passes the consumer's backend tests; regress
  fails on a missing role as `createdb` would, then creates, owns, drops and
  recreates its database as that role. In a Debian container with real root,
  `install --sudo` writes root-owned files into a root-owned installation, removes
  its staging, and PostgreSQL loads the extension (`add(40, 2)` returns 42), including
  a reinstall while the server has the library mapped. A stand-in `sudo` verifies the
  exact command sequence, a failed command's exit code and temporary-file removal.
  Run-as cases run where passwordless sudo reaches `ANKUS_TEST_RUNAS_ACCOUNT` and
  report inconclusive elsewhere. CI test runs use `ankus-runas` when the runner
  provides it, create it on GitHub-hosted Linux runners and warn otherwise. The
  primary Linux runner is provisioned; the macOS ARM64 and version runners remain
  to be provisioned. The complete suite passes on Linux x64/PostgreSQL 18.6 with
  every run-as case running (**14,184** total; **14,133** passed; 51 platform
  skips; zero failures; **14m25s**) and on Windows x64 with PostgreSQL 13.23 and
  17.11 concurrently at package concurrency 20 (**14,184** total each; **14,142**
  passed; 42 skips; zero failures; **26m**). That Windows run also covers a fix for
  a reload test that read `config_exec_params` while the postmaster renamed it.
- CI **37879843859** on **3abfca8** passes on all three platforms; its Windows test
  job takes **17m50s**, within the 15–20 minute target (Linux **14m37s**, macOS
  **16m01s**).

- Every pgrx example now has an Ankus counterpart. `notify`, `rewrite_manip`,
  `pglz_inspect` (all 24 pgrx tests in the backend) and `wal_decoder` are ported,
  the last through a new `[PgOutputPlugin]` attribute. It exports
  `_PG_output_plugin_init` beneath the native callback guard, and diagnostics
  ANKUS515–526 cover invalid declarations. PostgreSQL 14.24, 15.19, 16.15, 17.11,
  18.6 and 19 load only output plugins listed in `output_plugin_libraries`, even
  for superusers; the guide and sample say so. `custom_libname`, `versioned_so`
  and `versioned_custom_libname_so` use new SDK properties. `AnkusLibraryName`
  renames only the native library, like Cargo's `[lib] name`, and
  `AnkusVersionedLibrary` installs `<name>-<version>` libraries side by side as
  cargo-pgrx's versioned shared-object mode does; `ALTER EXTENSION UPDATE` moves
  between them in both directions. `postgres_type_variants` maps all five type
  variants. `bad_ideas` demonstrates how Ankus contains each one, and `nostd`
  maps to a minimal-runtime sample. Agents reported, on Linux/PostgreSQL 18.6:
  68 native-sample integration cases, 18 SDK-sample cases, 4,735 generator, 2,348
  runtime, 1,285 build and 483 PgConfig cases passing. The integrated
  branch, including shared tool-test builds, passes the complete suite on
  Linux x64/PostgreSQL 18.6 (**14,177** total; **14,127** passed; 50 platform
  skips; zero failures; **13m39s**) and on Windows x64 with PostgreSQL 13.23 and
  17.11 running concurrently at package concurrency 20 (**14,177** total each;
  **14,141** passed; 36 platform skips; zero failures; **25m46s**). The first
  Windows runs found three defects, now fixed. PostgreSQL 13 does not mark the
  PGLZ strategy globals `PGDLLIMPORT`, so a Windows extension cannot import them;
  the sample builds PostgreSQL 13's own strategy values locally there. Its README
  demos now expect pgrx's PostgreSQL 13 trigger advice, since column compression
  begins in 14. Deleting a staged installation could meet `icudt67.dll` still
  open in an exiting server, so staged installations and extension publications
  now use the retrying PostgreSQL storage delete.
- A rare Windows tool-test failure (`could not bind IPv4 address`) came from
  released test ports. Windows assigns ports from one sequential counter shared by
  listeners and outbound connections over IPv4 and IPv6, so it reissues a released
  port only after the counter wraps. WSL's localhost relay, however, binds every
  Linux listener's randomly chosen ephemeral port on the Windows host. The 26
  tests that release a reservation before a command binds the port now use
  `[RetryPortCollisionTestMethod]`. This `TestMethodAttribute` reruns an attempt
  only when every failure reports PostgreSQL's bind collision, at most three
  attempts, and records superseded attempts in the final result. MSTest 4.4.1's
  `RetryBaseAttribute` requires suppressing its experimental `MSTESTEXP`
  diagnostic, so it is not used. Direct tests cover the rerun policy.
- Explicit scopes can now opt out of per-statement recovery, the remaining SPI
  cost difference from pgrx. `PgTransaction.RunInSubtransaction(action,
  PgSubtransactionMode.Atomic)` runs its statements directly in the scope's single
  subtransaction, as pgrx runs SPI in the current transaction. The first
  PostgreSQL error makes later backend calls in the scope rethrow it; the whole
  scope then rolls back before `PgException` reaches the caller. The native
  bridge reuses the direct-SPI frame of transaction callbacks and pre-17 parallel
  execution. Every explicit scope now shadows the enclosing frame, so a nested
  recoverable scope restores per-statement recovery inside an atomic one, and a
  failure its own rollback recovered no longer poisons the enclosing frame. In a
  private PostgreSQL 18.6 cluster, 200 writes assign exactly **1** subtransaction
  ID atomically, against **201** with per-statement recovery. Recovery,
  whole-scope rollback, nested recovery and session continuity pass, along with
  all transaction-callback, cleanup and try/catch sample cases (**63** total).
  **2,372** runtime, **4,707** generator and **1,259** build cases (9 skips) pass.
- Configuration parameters can now be defined at run time, closing the review's
  "GUC names are compile-time only" difference from pgrx. `PgGucRegistry.DefineBool`,
  `DefineInt`, `DefineReal`, `DefineString` and `DefineEnum`, called from
  `[PgModuleLoad]` as pgrx calls `GucRegistry` from `_PG_init`, return a
  `PgGucSetting<T>` reader. Definitions are persistent native records registered
  through the same path as attribute settings, so placeholder adoption, ownership
  checks and reload handling are shared. An identical redefinition is
  idempotent, which keeps a retried module load harmless. A conflicting one, or
  one reusing an attribute's name, fails with `42710`. Names follow the
  attributes' PostgreSQL custom-name rules on every major; PostgreSQL itself
  validates none inside `DefineCustom*Variable`. Like pgrx's plain
  `define_*_guc`, run-time definitions have no managed hooks. The generator emits
  the registry only when source calls `PgGucRegistry`, so other extensions'
  native source is unchanged. Linux x64/PostgreSQL **18.6**: **4,707** generator
  cases, **24** runtime validation cases, **1,259** build cases (9 skips) and
  **78** configuration, module-load and runtime-definition integration cases pass.
  Integration covers `pg_settings` metadata, SET/SET LOCAL/RESET and rollback,
  placeholder adoption, conflicts and postmaster context. The first complete
  Windows runs (PostgreSQL 17.11 and 13.23) failed about 60 parallel-worker and
  session-preload cases. Those contexts load the library outside a transaction,
  where the guarded backend call is unavailable, and the test extension defines
  its run-time settings at load. Definitions now share the configuration read
  binding passed to load callbacks, so they work wherever reads do. On Linux
  18.6, the previously failing session-preload and parallel cases pass with the
  runtime, module-load and configuration cases (**64/64**). Complete Windows x64
  suites then pass for PostgreSQL **17.11** (**14,019** total, **13,983** passed,
  **36** skips, zero failures, **31m26.991s**) and **13.23** (same counts apart
  from one development-cluster start that exceeded a test's five-second budget
  on the loaded host). Three tests now give starts that must succeed the suite's
  budget, and both affected cases pass on 13.23. Linux x64/PostgreSQL **18.6**
  passes **14,019** total, **13,969** passed and **50** skips with zero failures.
- Integrated three parallel milestones. Graph diagnostics: `ANKUS005` is retired.
  Its free-text cases are fixed contracts `ANKUS490`–`ANKUS514`, each reported at
  the authored value, and `ANKUS149` has a fixed message; 4,699 generator cases
  pass. Eleven pgrx examples are now runnable samples with backend tests:
  `schemas`, `custom_sql`, `generic_agg`, the expanded `operators` and
  `composite_type`, `memory_contexts`, `shmem`, `pgtrybuilder`,
  `subtrans_infos`, `pgthread` and `hooks`. Twenty-seven of pgrx's 37 examples
  now map to samples. Generated custom-type JSON text now escapes only what
  serde_json escapes (quotes, reverse solidus and C0 controls), so `café`
  prints as pgrx prints it. A self-chained native callback, possible when a
  failed `PgModuleLoad` is retried after installing a hook, now raises
  PostgreSQL's stack-depth error instead of crashing the backend. The retry
  itself is documented Ankus behavior; PostgreSQL never re-runs a failed
  `_PG_init`.
  A worker report before the worker's first transaction in a non-UTF-8 database
  raised "cannot perform encoding conversion outside a transaction"; a LATIN1
  worker test reproduced it. Exact reports outside a transaction now convert
  each code point through PostgreSQL's startup-prepared UTF-8 converter
  (`pg_unicode_to_server`), and the test finds the database-encoding bytes in the
  server log. The same test showed that published test clusters set
  `dynamic_library_path` without `$libdir`. PostgreSQL 18 then cannot load its
  own encoding conversions, so the fixture now appends `$libdir` as product code
  does.
  Windows x64 complete suites for PostgreSQL **17.11** and **13.23**, run
  concurrently on the CI host at package concurrency 20, each pass **13,977**
  total, **13,941** passed and **36** platform skips with zero failures, in
  **29m45.961s** and **29m54.633s**. Linux x64/PostgreSQL **18.6**
  passes **13,977** total, **13,927** passed and **50** platform skips with zero
  failures in **12m50.602s**. Release has zero warnings/errors; API freshness
  (**245** pages, **2,795** members) and site checks (**297** pages) pass. The new
  timing summaries show the Windows cost is concurrency, not per-operation
  speed: a Native AOT consumer publish averages **84s** on Windows, with twenty
  slots per suite and two suites, against **21s** on Linux with twelve slots on
  24 processors; idle Windows publishes take 4–8s.
- Windows test time: primary CI **37825380597** takes **21m50s** for the Windows
  test step, against **11m37s** on Linux and **12m03s** on macOS. Per-class
  durations from the CI reports place the critical path in package-consumer
  tests (Windows **13.9** minutes, Linux **8.5**). Their builds and Native AOT
  publishes run about twice as long on Windows, and classes that start their own
  clusters run three to nine times as long. On the idle CI host, real-time
  anti-malware scanning of the runner directories doubled `initdb` (**1.48s** to
  **0.72s** when excluded) and Native AOT publication (**8.18s** to **3.95s**).
  Restore, `pg_ctl` and template copies were unaffected. The runner directories
  are now excluded on that host. Copying an initdb template takes **0.29s**,
  versus **1.48s** for `initdb`, and `fsync = off` cuts a shutdown after five
  database creations from **3.10s** to **1.01s**. PostgreSQL's own TAP clusters
  use both techniques. `PostgresTestCluster` now writes `fsync = off` before
  test-supplied configuration, which can enable it again; crash recovery still
  replays the operating system's cached WAL. On Linux/PostgreSQL 18.6, the cluster
  tests and the PANIC, post-commit PANIC and prepared-transaction recovery
  cases pass (**35/35**). An initdb template remains to be evaluated.
  After the exclusions, CI **37839931438** on **038d063** completes the Windows
  test job in **19m10s** (integration module **16m22s**), down from **24m04s**,
  despite 94 more cases and eleven more samples. Single local Windows suites
  finish the integration module in **15m48.712s** at package concurrency 12 and
  **14m46.222s** at 20. At 12, a Native AOT consumer publish averages **20.4s**,
  matching Linux's **21.0s**, so per-operation parity is reached and 20 remains
  the Windows setting. Two overlapping Windows suites still take about 30 minutes
  each, because they divide the same processors; the remaining lever is less
  total compilation. CI now uploads and prints the timing summary for passing
  runs too.
  The integration suite now writes `process-timings.log`, summarizing time per
  child-process command and cluster startup on each platform. The target is a
  Windows test job of 15–20 minutes or less. CI **37868933710** on **7f15982**
  passes on all three platforms. Its summary shows equal Native AOT publish means
  on Windows and Linux (**48.2s** and **50.5s**), while a cluster start averages
  **5.57s** on Windows against **1.09s** on Linux and **1.03s** on macOS. Its
  Windows job (**22m17s**) overlapped two local Windows suites on the same host,
  so **19m10s** remains the clean measurement.
- Primary CI has failed on Windows since **a04472b**; the last green primary run
  remains **67c9cb5**. Every failure in CI **37766380749**, **37773258253**,
  **37778899670**, **37782541757**, **37785972780**, **37791387341** and
  **37791387910**, and in platform run **37786233416**, has an identified cause.
  The three Windows runners share one host and run two complete suites at once.
  Under that load, 30-second budgets expired while PostgreSQL was still healthy:
  initdb, `pg_ctl start`, the first connection and the bootstrap
  `CREATE DATABASE` (PostgreSQL 13 and 14 copy the template through a
  checkpoint). Crash recovery also overran: one recovery spent **20.95s** in its
  data-directory fsync and **6.632s** in its end-of-recovery checkpoint, then
  became ready after the test's 30-second deadline. `StartupTimeout` now
  defaults to **180s**, PostgreSQL's TAP `PG_TEST_TIMEOUT_DEFAULT`. Bootstrap
  database creation and every crash-recovery wait share that budget. Isolated
  on the same host, recovery takes about **4s**.
  ToolCommandTests class cleanup could not delete
  `Microsoft.CodeAnalysis.CSharp.NetAnalyzers.dll`. On Windows, a Roslyn
  compiler server started by a consumer build keeps shadow copies of analyzers
  loaded from that build's `TEMP`, which is the class's temporary root. A
  concurrent job's `dotnet build-server shutdown` makes a consumer build start
  that server. Tool-test children now build without shared compilation.
  PostgreSQL 13's `ProcessInterrupts` reports a terminated worker as
  "terminating connection due to administrator command"; PostgreSQL 14 added
  the worker-specific message. The worker-termination assertion now expects each
  major's message. PostgreSQL 13 and 14 treat a `NETWORK SERVICE` process as a
  Windows service before checking stderr, so their reports go to the event log
  as UTF-16. The LATIN1 commit-report test now accepts that sink, while still
  requiring database-encoding bytes when the report reaches native stderr. The
  PostgreSQL 13 CI artifact's native stderr file is empty and its event-derived
  log reads `commit report café`. An unconverted UTF-8 report would read
  `cafÃ©`, so the bridge converted correctly; the old test could not see the
  event log. A worker report before the worker's first transaction in a
  non-UTF-8 database is fixed separately (see the sample-integration entry).
  On the CI host, Windows x64, SDK **10.0.401**, with package concurrency 20,
  complete suites ran concurrently as two CI jobs do. PostgreSQL **17.11**:
  **13,879** total, **13,843** passed, **36** platform skips, zero failures in
  **39m48.674s**. PostgreSQL **13.23**: the same counts and zero failures in
  **36m40.360s**. Before these changes were complete, PostgreSQL 17.11 alone also
  passed with the same counts in **25m47.602s**. On PostgreSQL 13.23, the
  previously failing worker, LATIN1 and selection cases pass **15/15**. Linux
  x64/PostgreSQL **18.6** passes the complete suite: **13,879** total,
  **13,831** passed, **48** platform skips, zero failures in **12m29.807s**.
  Release has zero warnings/errors; API freshness and site checks pass.
  CI **37810465744** on **e05d946** passes quality, runtime, Linux and macOS,
  and every previously failing Windows case. One new intermittent Windows failure
  remains: after deliberately abandoning a cluster, `ankus test` exited **1**
  instead of preserving the runner's **-1**. The tool returns 130 for
  cancellation and 1 for any other exception, so its 30-second cleanup timeout
  is excluded. Sixteen concurrent isolated repetitions on the same host pass.
  The test now reports the tool's stderr. Stopped-server storage is now deleted
  through `PostgresServerStorage.Delete`, which on Windows retries access and
  sharing failures every 100ms for up to 10 seconds, as PostgreSQL's `pgunlink`
  does. Both `ankus test` sessions and `PostgresTestCluster` use it.
  Two concurrent complete PostgreSQL 17.11 suites, each with sixteen extra
  repetitions of that case, did not reproduce the failure, with or without the
  deletion change (**13,893** cases, zero failures without it). With it, the
  other suite exposed a separate load failure: `ankus stop`'s hard-coded
  `pg_ctl -t 60` expired during a development cluster's shutdown checkpoint after
  ten database creations. The same case failed on Linux when three complete
  suites shared one host. `cargo pgrx stop` passes no `-t`, so `pg_ctl` honors
  `PGCTLTIMEOUT`; Ankus stop now does the same, keeping pg_ctl's 60-second
  default. The integration suite sets `PGCTLTIMEOUT` to its 180-second startup
  budget unless the caller chose one, as PostgreSQL's harness allows on slow hosts.
  With these changes, complete Windows x64 suites for PostgreSQL **17.11** and
  **13.23** ran concurrently on the CI host at package concurrency 20. Each
  passed **13,883** total, **13,847** passed and **36** platform skips with zero
  failures, in **29m54.715s** and **29m57.561s**.
  Linux x64/PostgreSQL **18.6** passes **13,883** total, **13,833** passed and
  **50** platform skips with zero failures in **12m52.442s**.
  Local sessions cannot run under the runners' service account, so the
  event-log branch was checked against the CI artifact, not executed locally.

- Test scheduling now reserves crash recovery rather than excluding the whole
  integration suite. Isolated SDK rejection projects run alongside read-only
  checks; completed cases return installation leases before deleting build files.
  Shared managed dependencies precede parallel publication of every fixture,
  and package assertions read resolved properties from the publish itself.
  Compiler slots now retain the complete processor budget: 32 processors and
  twenty slots allocate twelve budgets of two and eight of one. Windows
  x64/PostgreSQL **17.11**, SDK **10.0.401** passes **109/109** affected cases in
  **3m46.965s**, including fixture startup, cold package consumers, all terminal
  recovery paths and seven processor-allocation regressions. Release has zero
  warnings/errors; API freshness verifies **244** pages and **2,793** members;
  site checks pass and all **295** pages build. Before this milestone's commit,
  primary CI **37741393964** and Docs **37741394110** are completed and green.
  Replacement complete-suite platform timings remain required.
- Windows .NET **10.0.12** test-process capture now uses dedicated readers for
  synchronous redirected pipes. With twenty idle children and 32 logical
  processors, a 100ms continuation took **3,414.728ms** with thread-pool pipe
  reads and **124.608ms** with dedicated readers. The updated assembly preserves
  4,000 exact Unicode lines on each stream, nonzero exit status and cancellation
  of a live process tree. Worker polls collect failure diagnostics lazily;
  `install`/`package`/`schema --from` and `schema --skip-build` no longer wait
  for compiler slots. Windows x64/PostgreSQL **17.11**, SDK **10.0.401** passes
  all **52** affected scheduling, publication, worker and cancellation cases in
  **3m18.763s**, including fixture startup. Release has zero warnings/errors and
  documentation checks pass. Complete CI **37741393964** retains every previous
  case and outcome, plus eight scheduling cases, on all three primary platforms.
  Windows tests take **20m04s**, versus **20m41s** before this change; macOS takes
  **12m11s**, versus **12m52s**, and Linux **12m27s**, versus **11m52s**.
  These results do not resolve the full-suite runtime gap. Port work has resumed
  with the round-3 product review findings; see the entries at the end of this list.
- Six worker-cancellation cases and two lock-interrupt cases now share two
  immutable packaged probe publications instead of compiling eight identical
  binaries. Every case retains its own PostgreSQL cluster and all assertions;
  cold-package and compilation-behavior tests retain fresh outputs. On macOS
  x64/PostgreSQL **18.6**, the eight-case execution interval falls from
  **114.063s** to **39.166s**, with **8/8** passing. Fixture startup is excluded
  from that comparison. Windows x64/PostgreSQL **17.11** passes **8/8** in a
  **28.590s** case interval (**2m51.778s** including fixture startup). Both use
  SDK **10.0.401**. Standalone integration-project restore now includes all
  published fixture projects through explicit build-only references; the
  missing dependency previously caused `NETSDK1004` before tests started.
  Windows Release has zero warnings/errors; documentation checks pass.
  All six modules pass on every primary platform in **37738335710**. Windows
  test time decreased from **21m06s** to **20m41s**; Linux/macOS remained within
  six seconds of the previous run. This does not resolve full-suite latency.
- Compiler-cache hits now retain shared read ownership instead of serializing
  every consumer's content validation and artifact copy. Replacement waits for
  all readers and closes admission to new readers; retention remains exclusive.
  Focused cache checks pass **37/37** on macOS x64 and **33** with **4** platform
  skips on Windows x64. They cover overlapping leases, blocked replacement,
  cancellation, cold and stale concurrent misses, corruption and retention.
  With SDK **10.0.401**, eight concurrent warm validations of all installed SDK
  file contents take **0.800–0.858s**, versus **1.662–1.685s**, on Windows;
  macOS x64 takes **1.811–2.144s**, versus **2.512–2.682s**. These isolated
  measurements establish a cache improvement, not a complete-suite speedup.
  The packaged-helper relocation, reuse, changed-content and cleanup test also
  passes on Windows/PostgreSQL **17.11**. Release and documentation checks pass.
  Package commands that only inspect or reuse an existing publication no longer
  wait for compiler slots; actual compilation and test-host publication retain
  the existing bound. All **19** scheduling and real regression-command cases
  pass on Windows/PostgreSQL **17.11**, including native diffs, failed clients
  and read-only dry runs. All six modules subsequently passed on every primary
  platform in **37734356955**, with timings above. The cache microbenchmark
  improvement did not materially shorten complete-suite feedback.
- Windows native-tool helpers with a one-processor build budget spent time
  waiting for thread-pool workers blocked on synchronous redirected pipes.
  Dedicated readers for non-cancellable Windows pipes reduce a measured warm
  binding-source command from **5.319–5.546s** to **3.749–3.891s**, retaining
  the same compiler checks and byte-identical generated sources. Cancellation,
  bounded output and child cleanup remain enforced. CI also reuses its selected
  Visual Studio environment; native-source caching removes a redundant tool
  content read under the entry lock while retaining key and final-observation
  verification. Hashing uses buffered asynchronous sequential reads.
  Windows/PostgreSQL **17.11**, SDK **10.0.401**, retains twenty package slots:
  the earlier eight-slot composition passed but took **27m55.990s**, versus
  **22m43s** in primary CI. Sharing the selection pool and selecting the toolchain
  once passes **13,486** tests with **36** platform skips in **21m04.173s**.
  The pipe/cache composition passes all six Windows modules: **13,530** total,
  **13,494** passed, **36** platform skips and zero failures in **19m41.590s**.
  All **5,037** integration case identities and outcomes match the baseline.
  Repository outputs were warm and generated consumers fresh in these local
  comparisons; the final composition subsequently passed primary CI **37730340878**
  in the times recorded above. The subsequent reader-start
  failure cleanup and evaluation-only packaged-tool lookup pass focused checks.
  The lookup no longer executes native binding compilation just to read a tool path.
  Final native-process checks pass **393** cases with **2** platform skips on
  Windows and **382** cases with **9** skips on macOS ARM64/PostgreSQL **18.6**;
  all **13** packaged-tool cases pass on macOS, and the real packaged-header
  check passes on Windows. The final Windows configuration module passes **482**
  cases with **1** platform skip. An intermediate macOS composition
  also passed all six modules (**13,462** passed, **60** skips) in **13m26.199s**;
  these changes do not establish a macOS speedup. Release and documentation
  checks pass. The requested full-suite feedback time remains unresolved.
- Version-selection tests shared neither their idle projects nor the configured
  build capacity: two independent pools each used one quarter of the package
  limit, capped at four. They now share one pool bounded by the existing
  configurable package limit. All **22** affected cases pass. Complete macOS
  ARM64/PostgreSQL **18.6** runs with SDK **10.0.401** and eight package slots
  retain **13,518** total tests, **13,458** passes, **60** platform skips and zero
  failures. Test duration changes from **12m00.271s** to **11m40.965s**; total
  command duration changes from **12m20.92s** to **12m03.33s**. The 22 selection
  cases complete in **112.14s**, versus **187.68s** previously, while overlapping
  other tests. This is a modest complete-suite improvement, not resolution of
  the Windows latency gap. Release build and site checks pass. Windows profiling
  separates command queue waits from native publication and project evaluation.
- Primary CI **37716633362** on **358fd04** passed Linux/PostgreSQL 18,
  Windows/PostgreSQL 17, runtime jobs and quality; Docs **37716633355** passed.
  macOS ARM64/PostgreSQL 18 failed **41** consumer cases:
  the first socket fix reserved a GUID but omitted MTP's monitoring prefix.
  A directory that fits the GUID still produced a **117-byte** monitoring socket,
  exceeding macOS's **103-byte** limit. The earlier complete local macOS run
  (**13,453** passed, **60** skips, **11m57s**) used a deeper directory, selected
  the short fallback, and missed this boundary. The corrected check reserves the
  complete **46-byte** monitoring name. Real socket tests now cover the exact
  boundary, one byte over, the failing directory length and multibyte paths.
  All **16** focused checks pass on macOS ARM64/PostgreSQL **18.6**, including
  eight real package-consumer runs using the failed CI directory length.
  Complete `dotnet test` on the same platform and PostgreSQL version, SDK
  **10.0.401**, passes **13,458** tests with **60** platform skips and zero
  failures in **12m00s** (**12m21s** including build). All **41** previously
  failing cases pass; consumer and socket directories were removed. Release
  build and site checks pass. Complete primary CI **37719535843** on **7317ca1**
  and Docs **37719535780** now pass; current platform counts and timings appear above.
  Test latency still exceeds the requested feedback time.
- Local test-performance work removes restore and compiler-input discovery from
  managed-binding cache hits. A second content cache shares compilation only after
  each consumer's restore and input resolution succeed. Source, runtime, SDK,
  package contents and directory membership still invalidate reuse; **40/40**
  focused tests pass in **18.700s**, including changed package compiler options;
  selected framework-pack resolution metadata is also tracked. Final Release
  build, API freshness, `pnpm check` and `pnpm build` pass without warnings.
  Complete Linux x64/PostgreSQL **18.6**, SDK **10.0.401** validation decreased
  from **14m08s** to **12m53s** including build. Bounding nested compiler workers
  retained **12m53s** elapsed time while reducing user CPU from **174m27s** to
  **148m48s**; **13,464** passed, **48** platform skips, zero failures.
  The complete per-case-cleanup run also passes **13,464 + 48**, in **11m52s**
  including build, with 16 package slots. Test execution was **11m28s**, versus
  **11m29s** with 20 slots; the elapsed reduction mostly came before execution,
  not from increased throughput. Sampled consumer storage stayed below **9 GiB**
  instead of retaining **29 GiB** until class cleanup. Consumer builds retain
  stable publication paths and reuse restored dependencies. Completed consumer
  trees are deleted after each case; CI roots use runner-owned temporary storage.
  PostgreSQL **17** package, upgrade and benchmark staging checks pass **7/7**,
  including runner-owned temporary paths. This still does not meet the requested
  feedback time; fresh primary-platform acceptance remains in progress.
- Previous primary CI **37680275690** on **70f3fef** passed quality, runtime
  preparation and Linux/PostgreSQL 18. Windows/PostgreSQL 17 failed two connection
  bootstrap checks against a fixed ten-second limit; connections now honor the
  configured startup timeout. macOS ARM64/PostgreSQL 18 exhausted disk space and
  the runner could not finish its log. Revised consumer ownership and cleanup
  require fresh primary-platform acceptance; neither failure is recorded as green.
- Primary-run timing analysis found the fixed package-consumer gate dominated
  elapsed time: 555 tool-command cases accumulated **59,180.7 seconds**, with
  many cases spending about five minutes waiting before their work began.
- The package limit now surrounds build-intensive `dotnet` and `ankus` child
  processes instead of every PostgreSQL 18 tool test from initialization through
  cleanup. Fast CLI checks, filesystem assertions and backend work no longer wait
  behind Native AOT compilers. PostgreSQL 13–17 retain the full-test lease only
  where each active test requires its own staged installation.
- Complete run **37675149074** then exposed nested oversubscription: twenty outer
  package slots produced as many as **174** `dotnet` processes and Linux load
  **78.48** on 32 logical processors because each child created additional MSBuild
  workers. Parallel sample publications and generated consumer builds now use one
  MSBuild node each, so the adjustable outer limit controls actual concurrent
  builds. PostgreSQL-selection tests also use the same limit. The changed
  integration project builds in Release with zero warnings and errors. A following
  run began with **35** retained MSBuild workers consuming **11.4 GiB** before its
  full suite started. Workflows and build-intensive test children now disable node
  reuse, and the local default uses half of available logical processors.
  Complete Linux timings for the subsequent composition are recorded above;
  other platforms still require a refresh.
- The hosted quality job also forced solution restore and build through one
  MSBuild node. That obsolete serialization is removed; its clean build remains
  complete and warnings remain errors while independent projects build in parallel.
- Linux version run **37656004533** exposed a CI throughput defect rather than a
  PostgreSQL 15 failure. Its 10-processor runner was restricted to two package
  slots, while initial Native AOT publication ignored that setting and retained
  a separate hard cap of three. PostgreSQL 15 passed **13,485** cases with zero
  failures, but the job took **1h40m41s**. Both phases now share the adjustable
  `ANKUS_PACKAGE_TEST_CONCURRENCY` setting; Linux primary and version runners use
  adjustable slots. Primary Linux CI is pinned to its dedicated runner so version-matrix
  work cannot consume that lane. Superseded matrices using the old setting were
  cancelled.
- A local complete-suite run exposed a test-only Valgrind startup race: its
  separate 30-second witness deadline could expire under Native AOT load before
  the bounded cluster start completed. The regression now initializes the owned
  cluster before injecting the preload delay and relies on the production
  timeout/cancellation contract. Both cases pass **2/2** in **8m52.961s**.
- Refreshed supported-major run **37573419284** failed before test execution
  because the dedicated Linux version runner selected Clang 19 after header
  collection began requiring the LLVM 20 declaration-only frontend. The run was
  cancelled. Version CI now prepares LLVM 20 or later once before its parallel
  jobs start, and Linux selection prefers any installed supported toolchain.
  Replacement 13–17 and 19 evidence is pending.
- **82503bf** passes complete primary CI; its docs run also passes. All eighteen platform reports
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
  repository-configurable, with six eligible services available. Replacement
  supported-major evidence is pending. The refreshed Windows/PostgreSQL 18 cell
  also exposed a package-consumer Source Link path at exactly 260 characters and
  a cross-platform SQL-fixture assumption. Its owned root is now compact, and
  the fixture retains physical CRLF while expecting the Windows backend's deliberate
  normalization during extension-script parsing on PostgreSQL 18 and later. PostgreSQL
  13–17 predate that backend change and preserve the bytes. Replacement Windows
  evidence is pending. The expanded-inventory audit now retains exact representative function,
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
- Generator source coordinates now use stable, trivia-independent declaration
  header paths instead of ordinals among every member. Inserting an unrelated
  field, method, nested type or earlier source tree keeps final composition and
  rendering cached, while cached diagnostics resolve the exact current span.
  The complete generator module passes **4,480/4,480** on Linux x64. The clean
  Linux x64/PostgreSQL 18 complete suite passes **13,468 total / 13,420 succeeded /
  48 skipped / 0 failed** in **43m35s**. The Release build succeeds with zero
  warnings and errors in **1m11s**.
- The pgrx **bc09b536** cargo-option delta is reconciled through Ankus's
  repeatable `--property` / `-p` boundary. Properties now reach automatic-response
  project discovery, restore, every build command, benchmark selection, the managed
  test build and nested Native AOT fixture publication. Custom restore configuration,
  package and intermediate roots with spaces, response-file precedence, repeated
  values, schema no-build reuse and relative `pg_config` selection have source cases.
  The SDK now distinguishes its PostgreSQL 18 fallback from an authored major,
  normalizes a relative `pg_config` against the project and derives the major from
  that executable only when the project did not author one. Focused build,
  generator, command-selection, response-file and nested-publication validation
  passes, including all **11** affected Windows x64/PostgreSQL 17 cases. The exact
  current Linux x64/PostgreSQL 18 source passes **13,468 total / 13,420 succeeded /
  48 skipped / 0 failed**; its integration module completes in **27m21s**. Two
  earlier local attempts are rejected as evidence: one exhausted the filesystem,
  and one placed generated consumer projects inside the repository, causing them
  to inherit repository-only build policy.
- Linux x64 run **37581568110** passes the complete suite against PostgreSQL
  **13, 14, 15, 16, 17 and 19** at `557a470`; the primary PostgreSQL 18 cell in
  run **37580394349** also passes. The slowest compatibility cell completed in
  **73m54s**, validating the measured 120-minute job limit.
- Primary run **37580394349** passes quality, all runtime jobs, Linux/PostgreSQL
  18 and macOS ARM64/PostgreSQL 18. Its Windows-only PostgreSQL 17 newline
  expectation is corrected and passes in the newer run. Primary run
  **37600534091** at `0a5f02f` passes quality, all runtime jobs, Linux and macOS,
  but rejects four Windows cases: the SDK retained its fallback major for a
  path-only project, isolated `pg_config.exe` fixtures omitted adjacent DLLs, and
  three simultaneous full Windows suites starved a bounded collision test. The
  fixes derive the implicit major from `pg_config`, copy its Windows runtime DLLs
  and admit two cross-workflow Windows full-suite test commands at a time.
  Replacement complete Windows evidence remains pending.
  Platform-version run **37583027994** is also rejected as acceptance evidence:
  concurrent macOS ARM64/PostgreSQL 15 and 16 jobs exhausted their shared disk.
  The retained PG16 failures are all write failures after the disk filled, and
  the PG15 unit reports contain no product-test failures. Stale generated data
  was removed and both runner services now share the concurrency-safe binding
  cache. Replacement run **37617324614** exposed a GitHub Actions scheduling
  contract: a shared concurrency group cancels the older pending job when a newer
  job queues, even with `cancel-in-progress: false`. The same replacement then
  cancelled the pending Windows/PostgreSQL 13 cell when a newer primary job entered
  its lane. All GitHub concurrency groups are removed. Runner capacity queues
  macOS work, while two machine-wide Windows test mutexes admit two complete suites
  and preserve every additional queued job. Self-hosted test jobs clean ignored
  and untracked checkout outputs after uploading reports.
- Primary run **37620019090** rejected one macOS ARM64 build test when five
  parallel binding compiler cases refreshed NuGet vulnerability metadata through
  one inherited HTTP cache. NuGet raced on its replacement file and emitted
  NU1900, which correctly remained an error. Generated-binding restores now own
  a transient HTTP cache per compiler workspace while retaining the consumer's
  shared package cache, sources, credentials and enforced audit policy.
- PostgreSQL-version run **37617305925** rejected all six cells because the
  SDK-default constants test inherited each matrix cell's selected major and
  `pg_config`. The test's child MSBuild process now clears both selection variables,
  preserving real matrix selection for the suite while testing the SDK's unselected
  PostgreSQL 18 fallback in isolation. Replacement run **37630022751** was queued
  against the preceding `c42a138` source and therefore cannot validate this fix.
  Replacement [run **37656004533**](https://github.com/willibrandon/ankus/actions/runs/37656004533)
  is active against `82503bf` and covers PostgreSQL 13–17 and 19.
- Platform-version run **37583027994** predates the workspace cleanup, Windows
  suite admission and PostgreSQL 18 newline correction described above.
  Replacement [run **37656015714**](https://github.com/willibrandon/ankus/actions/runs/37656015714)
  is active against `82503bf` for macOS ARM64/PostgreSQL 15 and 16 and Windows
  x64/PostgreSQL 13 and 18.
- That replacement proves macOS ARM64/PostgreSQL 15 and 16. Its Windows 18
  integration suite passes **5,029/5,029**, but the job exposed a benchmark-test
  defect: a one-millisecond comparison fixture measured a no-op and could record
  zero stopwatch ticks. The fixture now performs bounded measured work. The
  focused regression and all **2,237/2,237** Runtime tests pass locally;
  replacement Windows evidence remains required.
- The same Windows report exposed a privacy gap: TRX `computerName` depended on
  the configured private-identifier list. Report preparation now redacts that
  schema field unconditionally. The affected uploaded reports were removed; path
  and device identifiers retain the existing configured replacement pass.
- ANKUS111 now distinguishes an omitted aggregate capability from unrelated
  same-name helpers by exact callback shape, and a shared inherited callback is
  reported once. Exact malformed role implementations remain errors without a
  guessed code fix. The focused aggregate/code-fix set passes **49/49**, and the
  complete generator module passes **4,487/4,487**.
- Backend test discovery and execution now cover an expected error containing
  embedded quotes. The errors sample documents the deliberate managed-exception
  SQLSTATE difference from a pgrx Rust panic. The real PostgreSQL regression
  passes in both source-generation modes.
- Exact-current Linux x64/PostgreSQL 18.6 acceptance passes **13,475 total /
  13,427 succeeded / 48 platform skips / 0 failed** in **40m02s**. The Release
  solution build has zero warnings and errors. API freshness verifies **244**
  pages and **2,793** members; documentation checks report no errors, warnings or
  hints, and the production site builds all **295** pages.
- The former free-text set-result, SQL type-binding and generated-operator
  diagnostics are split into `ANKUS396`–`ANKUS418`. Each error identifies one
  correction and points to the return type, parameter, binding attribute or
  generated-type declaration that must change. Focused binding and operator
  validation passes **92/92**, and the complete generator suite passes
  **4,497/4,497**.
- Free-text custom-type diagnostic `ANKUS017` is retired. `ANKUS419`–`ANKUS469`
  distinguish declaration, codec, native layout, generated serialization and
  polymorphic contracts and navigate to the exact authored option or member.
  Metadata-only failures remain `ANKUS206`. The source map now retains those
  precise diagnostic coordinates instead of failing during composition. The
  complete generator suite passes **4,517/4,517**. The Release solution build,
  API freshness and both documentation gates pass.
- Free-text `ANKUS026`/`ANKUS027` are retired. `ANKUS470`–`ANKUS489` identify
  each typed dependency and planner-support contract and point at the type,
  method name, `ParameterTypes` element or `DeclarationId` to correct. All five
  round-3 review defects in the first draft are corrected: argument locations,
  separate source/target IDs, source emission before selector matching, distinct
  name and overload diagnostics, and an unreachable support path. Planner support
  now applies to every function generated from one method. The complete generator
  suite passes **4,527/4,527**; Release, API freshness and both site gates pass.
- Round-3 high-severity backend findings 1–3 are fixed. Read-only SPI execute,
  cursor and `EXPLAIN` requests take the transaction snapshot only when none is
  active, so a utility hook can open a cursor during `SET` without crashing the
  backend, and callbacks without SQL keep REPEATABLE READ snapshot timing. Process
  exit no longer waits for shared-read admissions a FATAL report strands on the
  backend thread. Diagnostic capture and FATAL/PANIC reports no longer raise
  during encoding conversion in LATIN1 or SQL_ASCII databases outside a
  transaction. Ordinary reports remain exact, and read-only or parallel-worker
  commits keep FATAL instead of PANIC. The crash and hang regressions fail
  without their fixes and pass with them on Linux x64/PostgreSQL **18.6**.
- Scalar call-site state now follows PostgreSQL's `fn_extra` contract, so a
  fresh `FmgrInfo` at a reused stack address no longer inherits another call's
  state. Primary CI **37778899670** on **1504d0d** and **37782541757** on
  **d5f27a0** passed Linux and macOS; their Windows-only test defects (CRLF
  template comparison, and reading the decoded log snapshot instead of the raw
  LATIN1 log) are corrected. Run **37785972780** on **1ccd719** confirms both
  corrections: Linux and macOS pass, and Windows passes every module except two
  rows whose cleanup deleted a case directory while Windows still held a handle
  after the stopped cluster exited. Case cleanup now retries such a deletion for
  up to ten seconds before failing.
- ANKUS044 now follows LINQ and stored callbacks, `+` concatenation in its
  fallback, unassigned parameter paths through branch merges, `StringBuilder`
  and tuple contents, mixed quoted/raw `string.Format` arguments including the
  `params ReadOnlySpan<object>` overload, and writes through ref aliases and
  deconstruction. `string.Format` layout enumeration counts toward the
  analysis budget. The ANKUS129 code fix keeps `scoped` when hoisting a
  stack-bound span and is no longer offered for escaping pattern variables.
  **4,667/4,667** generator cases pass and the Release solution build, including
  every sample and test extension, has no new diagnostics.
- Primary CI **37773258253** on **d0df4e7** passes quality, all runtime jobs and
  the complete Linux/PostgreSQL 18 and macOS ARM64/PostgreSQL 18 suites. Windows
  x64/PostgreSQL 17 passes every module except one new case: the backend
  survived its LATIN1 commit-callback report, but Windows routes that LOG line
  through the UTF-16 Event Log, so the raw LATIN1 bytes never appeared. The case
  now accepts the message in the database encoding or UTF-8.
- Round-3 transaction-callback findings are fixed. Savepoint `Commit` callback
  failures raise ERROR instead of PANIC; savepoint `Abort` failures become
  WARNINGs so the rollback finishes. Callbacks now see savepoints nested inside
  guarded SQL and ignore every loaded Ankus extension's guard subtransactions
  through a shared rendezvous registry. Deferred triggers queued by `PreCommit`
  SQL fire before commit. SIGTERM during a worker transaction now ends the
  running statement as PostgreSQL's `die()` does. Generator file-scoped
  namespace keys, `Directory.Build.targets` majors, aggregate `[PgSchema]`
  recovery, case-sensitive `pg_` reservation, defaults containing `--`, quoted
  `ankus new` extension names, ISO interval JSON, interval `Abs`, strict
  `decimal` scale, exact CBOR NaN payloads and ordered dictionary keys are also
  fixed. Per-call SPI subtransaction cost is now documented; an explicit opt-out
  scope for bulk writes remains a design item.
- Primary CI **37766380749** on **a04472b** passed Linux and macOS but failed one
  Windows/PostgreSQL 17 benchmark case: Hyper-V excluded TCP **28760–28859**,
  covering all default development ports. The case now reserves its port like
  every other cluster-starting tool test. Replacement Windows evidence is required.

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
