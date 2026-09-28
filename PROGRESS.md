# Ankus — pgrx → .NET Native AOT Port: Progress Tracker

> A faithful port of [pgrx](https://github.com/pgcentralfoundation/pgrx) (PostgreSQL
> extensions in Rust) to **C# compiled with .NET Native AOT**, produced as a native
> shared library that PostgreSQL loads directly on Windows, Linux, and macOS.
>
> Implementation status, feature coverage, and validation results.

## Goal

Full pgrx parity in idiomatic .NET Native AOT: runtime APIs, macro equivalents,
extension features, custom scans and nodes, tooling, examples, and testing.
Completion includes validation across PostgreSQL 13–18 plus 19 beta on Windows,
Linux, and macOS.

- **Faithful port**: mirror pgrx's feature surface and mental model (see [Feature map](#feature-map-pgrx--ankus)),
  translated into idiomatic C# (attributes + source generators instead of proc macros,
  `IEnumerable<T>` for SETOF, exceptions → `ereport(ERROR)`, etc.).
- **Native AOT**: the extension ships as a self-contained native library (no .NET runtime
  install required on the Postgres host), built with `PublishAot`.
- **Multi-version**: one C# codebase targeting PostgreSQL 13–18 (+19 beta), matching
  pgrx's supported versions. PostgreSQL 18 has been exercised on Linux x64,
  macOS ARM64, and Windows x64.
- **Developer experience**: ordinary .NET projects, source generators, `dotnet publish`, and `dotnet test`,
  with development tooling corresponding to `cargo pgrx`.

## Environment

| Item | Value |
|---|---|
| .NET SDK | 10.0.400; `global.json` uses `rollForward: latestMajor` |
| C toolchain | clang 21 + lld; GCC 14 used for the local PostgreSQL build |
| Primary test target | **PostgreSQL 18** |

## Current verified milestone

The complete Linux x64 suites now pass on PostgreSQL **13.23 and 18.6**: each
executes **8,277 cases with 8,271 passes, zero failures and six Windows-only
skips**, in 11m42.791s and 12m53.136s respectively. All 62 failures from the
previous complete PostgreSQL 13 diagnostic are resolved. Numeric, array, range,
JSON and event-trigger checks preserve selected-version rejection, ownership and
same-backend recovery. All 279 affected cases also pass without failures/skips
on PostgreSQL 14.20 and 15.19. Release, API freshness and site checks pass.
Complete PostgreSQL 14 validation is running. The intermittent GUC query stall,
remaining PostgreSQL 13–19/platform matrix and other faithful-port requirements
remain open.

Temporal checks now preserve exact interval endpoints, selected-version feature
rejection, all existing storage owners, and same-backend recovery. All **405
affected cases pass with zero failures/skips** on Linux x64/PostgreSQL 13.23,
14.20 and 15.19. Final plain PostgreSQL 18.6 testing passes **8,271 tests, zero
failures and six Windows-only skips, 8,277 total**, in 12m39.426s. Release, API
freshness and site checks pass. The latest complete PostgreSQL 13 diagnostic
still records 62 failures; separate numeric/container/JSON/event corrections
and a refreshed complete run remain in progress. The intermittent GUC query
stall, full PostgreSQL 13–19/platform matrix, and other port requirements remain open.

Numeric constraint and conversion checks now verify exact selected-version
rejection diagnostics and same-backend recovery. All **67 affected cases pass
with zero failures/skips** on Linux x64/PostgreSQL 13.23, 14.20 and 15.19.
Final plain PostgreSQL 18.6 testing passes **8,271 tests, zero failures and six
Windows-only skips, 8,277 total**, in 12m42.534s. Release, API freshness and site
checks pass. The refreshed complete PostgreSQL 13.23 diagnostic records **8,209
passes, 62 failures and six Windows-only skips**. Remaining temporal, numeric,
array, range, JSON and event-trigger contracts, the intermittent GUC query stall,
the complete PostgreSQL 13–19/platform matrix, and other port requirements remain open.

Allocator and list checks now verify selected-version feature rejection,
transaction-owned fixture cleanup, and recovery without skipping unavailable
feature cases. All **143 affected cases pass with zero failures/skips** on Linux
x64/PostgreSQL 13.23 and 15.19. Supported versions retain the complete allocation,
ownership, alias, diagnostic, and lifetime assertions. Final plain PostgreSQL
18.6 testing passes **8,271 tests, zero failures and six Windows-only skips,
8,277 total**, in 12m50.740s. Release, API freshness and site checks pass.
Numeric and temporal version contracts, the intermittent GUC query stall,
the full PostgreSQL 13–19/platform matrix, and other port requirements remain open.

Worker configuration replay now keeps each checked value and its matching hook
extra together in native rollback history, preventing a secondary assignment
failure during worker error cleanup. All 96 configuration cases pass on Linux
x64/PostgreSQL 13.23, and all 26 expanded worker cases pass on 15.19. A deliberate
extra-only mutation fails the four worker-normalization regressions. The final
plain PostgreSQL 18.6 suite passes **8,271 tests, zero failures and six
Windows-only skips, 8,277 total**, in 12m48.733s. Release, generated API and site
checks pass. The separate earlier PostgreSQL 13 query stall, other older-version
contracts, the complete PostgreSQL 13–19/platform matrix, and remaining port
requirements are still open.

The packaged native-binding test now follows selected-header availability and
worker startup ordering on older PostgreSQL. Its complete publish/load/preload/
rebuild/clean sequence passes on Linux x64/PostgreSQL 13.23 and 15.19. The final
plain PostgreSQL 18.6 suite passes **8,263 tests, zero failures and six
Windows-only skips, 8,269 total**, in 12m45.819s. Release, generated API and
site checks pass. Separate stronger worker-error assertions reproduced the GUC
cleanup defect subsequently fixed by the checked-history milestone above. The
earlier PostgreSQL 13 query timeout remains required follow-up. Full PostgreSQL 13–19/platform parity
and other faithful-port work remain incomplete.

Native storage, node-format and full-range date tests now use the selected
server's observable contracts without dropping boundary or recovery assertions.
All 30 affected node/TOAST cases pass on Linux x64/PostgreSQL 13.23 and 14.20,
four formatting cases pass on 15.19, and all fourteen date-boundary cases pass
on 13.23. The final plain PostgreSQL 18.6 suite passes **8,263 tests, zero
failures and six Windows-only skips, 8,269 total**, in 12m34.903s. Release,
generated API and site checks pass. The complete PostgreSQL 13.23 diagnostic
records **8,161 passes, 102 failures and six Windows-only skips**; remaining
native-version contracts and a parallel GUC query timeout require follow-up.
The complete PostgreSQL 13–19/platform matrix and other full-port work remain open.

Relation metadata now preserves PostgreSQL 13 index descriptors without trying
to look up their absent row type. Typed tuple, array and SPI factories reject
those descriptors explicitly. All 44 managed tuple tests and 44 relation cases
on each of Linux x64/PostgreSQL 13.23 and 14.20 pass. The final plain PostgreSQL
18.6 suite passes **8,263 tests, zero failures and six Windows-only skips,
8,269 total**, in 12m46.442s. Release, generated API and site checks pass.
Further version-specific test corrections are independently verified below;
the complete PostgreSQL 13–19/platform matrix and other full-port work remain open.

Storage ownership and temporal field tests now exercise PostgreSQL 13's native
allocation and extraction behavior without dropping their assertions. Explicit
SQL aliases also make 261 custom-type and packaged lifecycle cases executable
on PostgreSQL 13.23. The final plain PostgreSQL 18.6 suite passes **8,251 tests,
zero failures and six Windows-only skips, 8,257 total**, in 12m23.357s. Release,
API freshness and site checks pass. A complete PostgreSQL 13.23 diagnostic
records **8,138 passes, 113 failures and six Windows-only skips**; the remaining
native-version contracts and index descriptor bug are required follow-up work.
The full version/platform matrix remains open.

Interval construction now rejects arithmetic overflow on PostgreSQL 13–16
inside the native error boundary and retains PostgreSQL 17+'s native checks.
All 59 affected cases pass without failures/skips on Linux x64/PostgreSQL
13.23, 14.20 and 15.19. The final plain PostgreSQL 18.6 suite passes **8,251
tests, zero failures and six Windows-only skips, 8,257 total**, in 13m05.496s.
Release, generated API freshness and site checks pass. The complete older-server
diagnostic and remaining compatibility work are recorded below; full
version/platform parity remains required.

GUC tests now verify the selected server's prefix, privilege, reporting and
allocation contracts. All 91 affected GUC/worker cases pass without failures or
skips on Linux x64/PostgreSQL 13.23, 14.20 and 15.19. The final plain PostgreSQL
18.6 suite passes **8,203 tests, zero failures and six Windows-only skips,
8,209 total**, in 13m24.279s. Release, API freshness and site checks pass.
Full older-version diagnostics still identify required compatibility work;
these affected results do not establish full version/platform parity.

The PostgreSQL 13/14 bridge now uses the selected headers' actual function-call,
GUC, logging and datum contracts. Independent native allocator observations keep
memory ownership checks executable on PostgreSQL 13. All 335 affected cases pass
without failures or skips on Linux x64/PostgreSQL 13.23 and 14.20. The final plain
PostgreSQL 18.6 suite passes **8,203 tests, zero failures and six Windows-only
skips, 8,209 total**, in 13m00.338s. Release, all 2,100 generator tests, API
freshness and site checks pass. Broader PostgreSQL 14 diagnostics identify
additional required version-contract work, recorded in the latest evidence
section; full older-version and platform validation is not complete.

The SDK now defines one selected-major compilation symbol, and the trace
provider selects the matching parameter-helper ABI. Sixteen new SDK/native
helper cases pass on Linux and Windows. On Linux x64/PostgreSQL 15.19, all
51 scoped SDK, initialization, allocator and parameter contracts pass; all
102 affected cases pass on Windows x64/PostgreSQL 17.11. Final Release builds
and API/site checks pass. Plain full Linux `dotnet test` against PostgreSQL
18.6 passes **8,195 tests, zero failures and six Windows-only skips, 8,201
total**, in 11m37.486s (integration 11m36.805s). PostgreSQL 15's initial eleven
parallel-worker failures were subsequently resolved by the older-worker recovery
milestone recorded below; the full older-version/platform matrix is not complete.

Independent native probes now verify the trace provider's predecessor-hook and
registry boundaries. All 73 custom-scan and initialization cases pass on Linux
x64/PostgreSQL 18.6 in 2m13.335s and Windows x64/PostgreSQL 17.11 in
3m40.975s, including seventeen new boundary cases. Release builds and API/site
checks pass. Final plain full Linux `dotnet test` passes 8,179 tests with zero
failures and six Windows-only skips, 8,185 total, in 10m46.793s (integration
10m46.216s).

The trace provider now retains native outer-variable expressions, remaps them
through partition ancestry and places them in the final plan's expression list
for PostgreSQL's reference adjustments. All 43 custom-scan cases pass on Linux
x64/PostgreSQL 18.6 in 1m52.567s, including reordered/dropped columns,
multilevel partitions, prepared-plan ownership, volatile evaluation and error
recovery. All 56 combined custom-scan and initialization cases pass on Windows
x64/PostgreSQL 17.11 in 4m09.809s. Final plain full Linux `dotnet test` passes
8,162 tests with zero failures and six Windows-only skips, 8,168 total, in
11m08.131s (integration 11m07.029s). Release builds and API/site checks pass.

Packaged backend tests now reuse their test-owned NuGet directory while two
explicit cold-restore cases retain empty package directories. This removes
repeated binding collection and compilation caused by changing package paths.
The same three Linux backend cases improve from 5m03.583s to 3m53.082s (23.2%);
all eleven affected Windows cases pass. Complete Linux verification passes
8,153 tests with zero failures and six platform skips in 11m18.383s. CI now
retains per-module TRX timing reports. The first completed hosted Ubuntu result
with reuse passes in 34m57s overall, with a 28m02.838s integration module,
compared with 41m49s and 35m42.554s in the preceding completed Ubuntu run.
This is an observed improvement across runs, not an isolated CI benchmark.
The macOS ARM64 job also passes in 45m38s, with a 35m32.998s integration
module; Windows x64 passes in 53m41s, with a 38m03.125s integration module.
Further optimization is deferred
until those timings identify a concrete need, keeping this investigation bounded.

The trace provider now wraps index and index-only children and delegates native
mark/restore to children whose access method supports it. Real Merge Joins with
duplicate keys prove actual callbacks and exact nullable/text result pairs;
cursor, parameterized rescan, parallel index, error and recovery cases also pass.
All 34 custom-scan cases pass on Linux x64/PostgreSQL 18.6 in 1m54.751s; all
47 combined custom-scan and initialization cases pass on Windows
x64/PostgreSQL 17.11 in 3m47.398s. Release builds have zero warnings/errors
on Linux (1m11.53s) and Windows (2m03.81s). API freshness verifies 200 pages/
2,437 members; the site builds 245 pages and checks with zero diagnostics.
Final plain full Linux `dotnet test`, with the package-fixture reuse change also
present, passes **8,153 tests, zero failures and six Windows-only skips,
8,159 total**, in 11m18.383s (integration 11m17.598s).
Reparameterization, independent hook and
registry boundaries, and the complete PostgreSQL/platform matrix remain required.

The trace provider now implements its own parallel DSM lifecycle using
selected-header native atomics. PostgreSQL owns each coordinate block; leader
and worker callbacks initialize, attach, reinitialize and copy shutdown
observations without retaining shared addresses after shutdown. Complete scans
report exact combined row/call counts, while early exits report bounded
per-counter snapshots. All 22 custom-scan cases pass on Linux x64/PostgreSQL
18.6 in 2m03.685s (module 2m02.758s), including complete and early-stop Gather
rescans, worker errors, leader participation modes and zero-worker fallback.
All 35 combined custom-scan and initialization cases pass on Windows
x64/PostgreSQL 17.11 in 4m17.626s. Release builds pass with zero warnings/errors
on Linux (1m10.00s) and Windows (2m12.42s). API freshness verifies 200 pages/
2,437 members; the site builds 245 pages and checks with zero diagnostics.
Final plain full Linux `dotnet test` passes **8,141 tests, zero failures and six
Windows-only skips, 8,147 total**, in 15m07.371s (integration 15m06.346s).
The remaining custom-scan protocols and full PostgreSQL/platform matrix remain
required; this milestone does not establish full custom-scan parity.

The standalone custom-scan trace sample now executes real native paths, plans
and child scans. Its thirteen cases verify exact projected rows and SQL NULL,
rescans, cached plans, backward reads, EXPLAIN, managed/native errors,
same-session recovery, actual parallel workers and concurrent-update rechecks.
The provider preserves PostgreSQL's slot contract with `ExecCopySlot` into its
own scan slot. Its fixture publishes and loads the standalone extension,
keeping its planner hook separate from deliberately failing test initializers.
All 26 combined custom-scan and initialization cases pass on Linux
x64/PostgreSQL 18.6 (2m30.708s) and Windows x64/PostgreSQL 17.11 (3m39.787s).
Final plain full Linux `dotnet test` passes **8,132 tests, zero failures and six
Windows-only skips, 8,138 total**, in 13m58.212s (integration 13m57.534s).
Final Release builds have zero warnings/errors on Linux and Windows; API
freshness verifies 200 pages/2,437 members and the site builds 245 pages with
zero check diagnostics. Full custom-scan parity and the complete
version/platform matrix remain required.

The prior Windows CI job reached its one-hour limit after a clean 15m28.99s
build, passing unit modules and an unfinished integration suite. CI now
persists the existing content-verified native binding cache between runs and
saves it after building, before running tests. Cold source collection alone
cost about five minutes in that job. Full suites, native validation and the
one-hour limit remain; hosted cache timing improvement is not yet measured.

Generated `<Record>_<Field>Callback` types now preserve canonical pointer storage
and guarded invocation while letting method-table handlers use names derived
from native declarations. Seven binding cases pass on Linux and Windows; four
real PostgreSQL callback cases pass on Linux x64/PostgreSQL 18.6 (1m57.756s) and
Windows x64/PostgreSQL 17.11 (4m19.817s). These exercise actual native table
storage, exact state/slot addresses, managed/native errors, both managed unwind
frames and same-session recovery. This is groundwork for custom-scan providers;
planner/executor protocol implementation is still required. Final plain full
Linux `dotnet test` passes **8,119 tests, zero failures and six Windows-only
skips, 8,125 total**, in 14m48.009s (integration 14m47.274s). Release builds pass
with zero warnings/errors on
Linux and Windows; API freshness verifies 200 pages/2,437 members, and the site
builds 244 pages and checks with zero errors/warnings/hints.

The macOS failure in [CI 36352480777](https://github.com/willibrandon/ankus/actions/runs/36352480777/job/108713921584)
comes from the allocation test worker's signal handler: Clang rejects the unused
`postgres_signal_arg` parameter with warnings treated as errors. This prevents
assembly initialization, producing 3,332 failed integration cases and two
Linux-only skips before backend tests can run. The handler now checks `SIGTERM`
and accounts for PostgreSQL 19's additional signal metadata while retaining the
selected-header signature. The exact strict Clang command fails before the fix
and passes afterwards; all four allocation cases pass on Linux x64/PostgreSQL
18.6 (1m34.949s) and Windows x64/PostgreSQL 17.11 (3m11.515s). The subsequent
macOS ARM64/PostgreSQL 18 [job 108729659266](https://github.com/willibrandon/ankus/actions/runs/36357983412/job/108729659266)
passes the complete suite at `c01ed61`: 8,116 passed, zero failed and nine
platform-specific skips, 8,125 total, in a 52m55s job (integration 45m16.124s).
This confirms the fixture repair on macOS; that run predates the custom-scan
sample. Its later hosted validation is recorded below. No diagnostic is
suppressed or relaxed. In the original
hosted run, quality, all runtime jobs and Ubuntu's full suite (41m27s) pass;
Windows remains in progress at the 2026-09-27 22:28 UTC pre-commit check.
[Docs 36352480612](https://github.com/willibrandon/ankus/actions/runs/36352480612)
passes.
The subsequent callback milestone push superseded that run's Windows job after
50m09s; it is cancelled and supplies no complete platform result.

Prepared and parallel transaction callbacks now have ten real PostgreSQL cases,
passing on Linux x64/PostgreSQL 18.6 (2m11.216s) and Windows x64/PostgreSQL 17.11
(3m50.737s), plus 17 direct callback cases on each platform. These verify
commit/rollback of prepared writes, managed/native pre-prepare rejection,
durability after a post-prepare PANIC, actual parallel worker completion/errors,
callback order, SQL capability boundaries and released captures. The final
parallel lifetime refinement passes 3/3 on Linux in 2m17.312s and Windows in
3m43.067s. The final plain full Linux suite passes **8,108 tests, zero failures
and six Windows-only skips, 8,114 total**, in 14m00.034s (integration
13m59.424s). Final Release passes with zero warnings/errors in 55.95s. API
freshness verifies 200 pages/2,437 members; the site builds 244 pages and checks
with zero errors/warnings/hints.

Worker registration allocation faults now pass four focused cases on Linux
x64/PostgreSQL 18.6 (1m38.413s) and Windows x64/PostgreSQL 17.11 (3m38.883s).
The native fixture wraps only the worker owner's selected-header allocator
methods and exercises owner, registry-entry and actual PostgreSQL handle
allocation failures, plus identity exhaustion. Each case executes twice in the
same backend and requires exact owned diagnostics, restored caller context,
released registry/context state, and a real replacement worker's startup,
argument, shutdown and process exit. PostgreSQL can publish a worker before its
handle allocation fails: an independent PID/value witness proves that worker
starts, and the fixture terminates it and observes its exit. Public API remarks
and the worker guide document this registration side effect. This tests existing
production behavior; the worker implementation and runtime patch are unchanged.
The first Linux attempt failed native compilation on a setjmp-clobbered local;
the corrected counter is volatile, with no suppressed diagnostic. The final
Windows rerun verifies that qualifier. Release passes with zero warnings/errors
in 1m21.62s; API freshness verifies 200 pages/2,437 members, the site builds
244 pages, and checking reports zero errors/warnings/hints. The final plain full
Linux suite passes **8,094 tests, zero failures and six Windows-only skips,
8,100 total**, in 14m03.413s (integration 14m02.799s).

Background-worker APIs, generated native entries, a public sample and
`ankus new --background-worker` are implemented. Static/dynamic registration,
callback-owned observation handles, native signals/latches, name/OID connections
and guarded transaction callbacks preserve PostgreSQL process and ownership
semantics. Dynamic metadata that PostgreSQL would alter is rejected; extra
payloads retain exact UTF-8. Disposing a handle releases observation storage
without terminating the worker.

The follow-up passes focused Linux x64/PostgreSQL 18.6 and Windows
x64/PostgreSQL 17.11 checks for actual SIGCHLD delivery, eight connection identity
and privilege combinations, eleven process-ending connection failures with later
healthy workers, and postmaster death during latch and tracked shutdown waits.
Worker-only libraries now enter Native AOT before enabling fork support, without
requiring a user initialization method. The runtime patch and package are unchanged.
The Windows postmaster-death rerun passes in 6m20.163s, including recovery of the
owned cluster and normal cleanup.

The preceding worker lifecycle milestone's complete Linux suite passes **8,090
tests with zero failures and six Windows-only skips, 8,096 total**, in
13m31.323s. This includes the retained
bounded slot-reclamation assertion and final postmaster-restart cleanup.
The complete generator module passes
2,100/2,100 in 26.068s. Production Release builds pass with zero warnings/errors
on Linux (1m05.38s) and Windows (1m30.16s); the final Windows test rebuild also
passes. API freshness verifies 200 pages/2,437 members; the site builds 244 pages
and checks with zero errors/warnings/hints.

The preceding [worker CI 36344340281](https://github.com/willibrandon/ankus/actions/runs/36344340281)
passes quality, every runtime-package job and the complete macOS suite (42m39s).
[Docs 36344340280](https://github.com/willibrandon/ankus/actions/runs/36344340280)
passes. Ubuntu (30m39s) and Windows (59m48s) each fail only
`BackgroundWorkersRegisterAndShareState`: a detached worker had written its final
value before it exited and PostgreSQL reclaimed its slot. Ubuntu reports 3,327
passing integration cases and one failure; Windows reports 3,325 passing, one
failure and two Linux-only skips. The repair checks capacity before detached
workers and retains the final exact three-slot check with a bounded wait for
reclamation. The final bounded check passes on Windows in 6m47.069s and in the
final full Linux suite. All jobs retain the requested one-hour limit and full
unsharded suites.

The complete PostgreSQL 13–19/platform matrix remains required, along with the
remaining full-port inventory below. This
milestone does not establish complete worker or full-port parity.

The preceding mutation [CI 36338481778](https://github.com/willibrandon/ankus/actions/runs/36338481778)
passes quality, all runtime jobs and the complete Linux (36m22s), macOS
(47m17s) and Windows (56m50s) suites.
[Docs 36338481755](https://github.com/willibrandon/ankus/actions/runs/36338481755)
passes. All jobs are terminal and successful at the 2026-09-27 18:49 UTC check.
The first local worker attempts stopped during native fixture compilation;
signal parameters now match the selected headers, and the worker bridge declares
its callback type independently of the SPI bridge. These attempts provide no
worker execution evidence.

Exclusive lightweight-lock and spinlock guards now expose `Mutate` callbacks
over original protected storage. Ordinary fields, inline atomics and bounded
collection views update their actual shared bytes. Aliases cannot borrow,
replace or release the parent during mutation; overlapping child-lock access is
rejected. A separate `Read` callback permits child-lock operations while keeping
the same parent acquisition held. Writes persist after exceptions and rollback.

All 27 new direct cases and four new compiler cases pass. Complete Runtime
suites pass **1,690/1,690** on Linux and Windows. Both extended Native AOT
witnesses pass on Linux x64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11,
including exact cross-backend values, bounded queues, contended updates, owned
errors, rollback persistence, stale acquisitions and crash recovery. The final
Windows native pair passes 2/2 in 7m53.356s.

The final full Linux suite passes **8,003 tests, zero failures and six
Windows-only skips, 8,009 total**, in 12m54.774s. Release builds pass without
warnings/errors: Linux 1m30.99s and Windows 3m35.90s. API freshness verifies
192 pages/2,380 members; the site builds 235 pages and checks with zero
errors/warnings/hints. README, public shared-memory guidance and generated API
pages describe mutation, readonly access and guard lifetime rules.

The preceding lightweight-lock reader [CI 36335688901](https://github.com/willibrandon/ankus/actions/runs/36335688901)
passes every runtime build, quality and full Linux (35m12s). macOS and Windows
were superseded by the mutation push after 42m43s and 42m33s respectively; their
retained logs contain no failed test, but their full results remain unverified.
[Docs 36335688785](https://github.com/willibrandon/ankus/actions/runs/36335688785)
passes. A running job is not counted as platform proof.

The preceding scoped-spinlock [CI 36333403089](https://github.com/willibrandon/ankus/actions/runs/36333403089)
passes every runtime build, quality, full Linux (31m18s) and full macOS
(30m04s); Windows was still running at the pre-commit check and was subsequently
superseded by the lightweight-lock reader push.
[Docs 36333403092](https://github.com/willibrandon/ankus/actions/runs/36333403092)
passes. Complete hosted Windows validation was pending at that milestone;
the subsequent mutation run passes. A running or superseded job is not counted
as platform proof.

The preceding collection [CI 36325012375](https://github.com/willibrandon/ankus/actions/runs/36325012375)
passes quality, every runtime job and macOS (34m41s). Linux fails during backend
crash recovery; Windows reports a GUC FATAL transport mismatch and reaches its
one-hour timeout (60m09s). [Docs 36325012376](https://github.com/willibrandon/ankus/actions/runs/36325012376)
passes. Runtime fork commit `134b853ff766627327405b2fb1f5c0d74266e4b6`, selected
by payload `10.0.11-ankus.4`, repairs the finalizer retirement request ordering.
It passes the Linux native recovery pair, the complete Linux suite and 2,048
retirement/restart cycles. The unrepaired control also passes the stress probe;
this is a source-identified ordering race, not a deterministic stress reproduction.
The GUC repair requires the unique session's exact server-log diagnostic and a
healthy independent observer even when Windows reports a connection reset.
The later mutation run passes all three complete platform suites, including
Windows within its timeout. Every workflow job retains the requested
60-minute limit; the 56m50s Windows result leaves little margin.

A broader local Windows run on PostgreSQL 17.7 exposed six allocator failures.
The same source snapshot passes the complete integration module on PostgreSQL
17.11: **3,323 passed, zero failures, two Linux-only skips, 3,325 total**, in
23m45.280s. Focused passing evidence above does not establish full-suite support
for the older patch. Remaining worker boundaries, the full-port API/behavior
audit and the complete PostgreSQL 13–19/platform matrix remain
required work.

### Managed preload validation history

Managed `shared_preload_libraries` works through the generated Ankus SDK on
PostgreSQL 18.6/Linux x64 and PostgreSQL 18.1 on macOS ARM64 and Windows x64.
Initializers and configuration hooks run during preload. Unix backends inherit the
initialized managed graph; Windows backends initialize equivalent state in their new
process. All run tasks, timers, garbage collection, finalizers, exceptions, logging,
configuration hooks, and SPI where PostgreSQL permits it.

On Linux and macOS, the postmaster runtime retires its service threads after each
managed startup callback. A forked backend restores them on its first managed call.
On Windows, PostgreSQL starts each backend as a new process and preload initialization
runs there. Extension projects handle both paths automatically.

The runtime branch tip is `232bc9d1f`. Its retained-service probes pass timers,
queued work, waits, blocked workers, finalization, descendants, workstation GC,
and four-heap server GC. The focused PostgreSQL preload tests pass 2/2 in 54.087s
on Linux, 3m11.848s on macOS, and 1m22.803s on Windows. Isolated packaged consumers
also pass on all three platforms. The complete Linux suite passes 4089/4089 with
no skips in 4m04.670s; the Release build has zero warnings and errors.

macOS x64, PostgreSQL 13–17 and 19 beta, and public package publication remain
required before full platform and version parity is claimed. The entries below
retain the sequence of verified prototypes and their boundaries.
The first owned-runtime experiment passes on Linux x64: twelve child checks across
two rounds of forks, plus all parent controls. It preserves startup objects and
GC handles, returns each child's process ID, and runs GC, finalizers, newly started
thread-pool work, timers, and exception handling. Stock Native AOT fails the process-ID
and finalizer checks; warming its pool and timer before fork also breaks both services.
This first pass starts with only the calling thread and finalizer, uses workstation
nonconcurrent GC, and disables diagnostics. These are prototype boundaries, not the
finished requirement. Other GC modes, platforms, and Ankus SDK integration remain
unverified.

The next runtime revision also passes the parent-warmed pool/timer case. Framework
workers retire through their normal exit paths before fork; both processes resume
the preserved queues afterward. The full and minimal harness modes each pass all
twelve child checks and all parent controls, with empty stderr. CoreLib builds with
zero warnings/errors in 22 seconds. This run proves warm idle services; the later
continuity checks below cover retained pending work.
Sources/logs and the exact binary SDK are retained in
`.git/testagent/preload/service-proof/` and
`artifacts/preload/runtime-services-proof-sdk`. The actual linked diagnostics
implementation is now checked directly; these runs pass with diagnostics environment
overrides absent. Workstation nonconcurrent GC is still selected explicitly.

A further revision passes default workstation/concurrent GC with all GC and
diagnostics environment overrides absent. Six actual background-collection
observations show increasing `GCKind.Background` indices and `Concurrent=true` in
parent and child; all twelve child stages and parent controls pass. The same child
also verifies its inherited cyclic graph, GC handle, token, and retained array bytes
after collection. The probe promotes a retained 16 MiB heap first because the
collector intentionally uses blocking collection for old heaps below 4 MiB.
The patch, probe, hashes, and logs are retained in `.git/testagent/preload/bgc-proof/`,
with the exact SDK in `artifacts/preload/runtime-bgc-proof-sdk`. This proves retirement
and restart of a warmed background collector. The next probe also passes two forks
whose preparation actually observes an active collection under the collector lock;
both qualify on their first attempt. Each child and parent checks all 1,048,576
retained nodes, both edges per node, another GC handle, all 16 MiB of patterned bytes,
completed concurrent-collection metadata, a new background collection, finalization,
and the original graph/PID/task/timer/exception controls. A passive lock-free counter
records observed activity without pausing or changing GC selection. Evidence and
exact sources/binaries: `.git/testagent/preload/bgc-active-proof/` and
`artifacts/preload/runtime-bgc-active-proof-sdk`. Server GC and enabled diagnostics
were unimplemented in that revision; later server-GC evidence appears below.

Using that same frozen SDK with default GC, retained timers and queued tasks now
pass two independent forks each, with exact checks in parent and child. The native
snapshot after runtime preparation sees an unfired timer with 2998–2999 ms remaining;
the original timer, state object and GC handles survive, and its callback produces
956 exactly once in both processes. Queue snapshots see all 80 original objects
pending (16 global, 64 originating in a worker-local queue); all complete exactly
once with their expected per-index values in both processes. Tokens, object identity,
independent mutations and restored pool limits also pass. Both native supervisors
exit zero with empty stderr. Sources: `.git/testagent/preload/fork-probe/`; logs:
`/tmp/ankus-preload-retained-{timer,queue}.jsonl` and matching `.stderr` files.
Independent review, frozen sources/logs, exact commands and SDK hashes are retained
in `.git/testagent/preload/retained-proof/`.

The same binary also passes actual `shared_preload_libraries` execution in release
PostgreSQL 18.6 on Linux x64. A native probe calls managed initialization in the
postmaster, then six fresh backends pass all 36 managed stages across two server
starts. Independent server/OS PIDs, one-time initialization, inherited random token
and graph, exact callback markers, same-session SQL, and graceful shutdown are
checked. Logs and the runner's evidence are in
`artifacts/preload/pg-proof1-a5s2syh4`; sources are in
`.git/testagent/preload/postgres-probe/`. This uses a dedicated native probe, not yet
Ankus's generated `[PgInitialize]` entry point or managed configuration hooks.
The first real generated Ankus probe exposed a separate missing binding: initializers
outside a transaction could not read settings or log through the transaction-only
backend binding. The owned generator snapshot now supplies independent guarded
configuration/logging scopes while preserving the transaction requirement for SPI.
The failing evidence is retained in `artifacts/preload/pg-ankus-gavowa0s`.
After this fix, real `[PgInitialize]`, startup check/assign hooks, and backend
check/assign/show hooks pass across two server starts and six independent connections.
Exact hook ordering, owned extra bytes, inherited state, GC/finalization, fresh tasks
and timers, structured errors, managed finally, SET LOCAL rollback, RESET and same-session
SQL recovery pass. Startup NOTICE/detail independently records the actual postmaster PID.
Evidence: `artifacts/preload/pg-ankus-ns4xlz51`. This run uses the first runtime SDK with
nonconcurrent GC. The combined Ankus probe now also passes with the frozen default-GC
SDK and with tasks/timers warmed in the actual postmaster initializer. All assertions
pass across two server starts (postmasters 3889263 and 3889349) and three fresh
connections per start. Evidence: `artifacts/preload/pg-ankus-w2bvaxmk`.
The standalone test needed to export `RhEnableForkSupport` for `dlsym`, but copying
that export into an Ankus extension made its runtime call interposable under
PostgreSQL's `RTLD_GLOBAL` loading. Removing only that unnecessary export lets
Native AOT's existing export script bind the function locally. The rebuilt ELF
debug symbols show LOCAL binding and no dynamic relocation; all six Ankus connections
pass again in `artifacts/preload/pg-ankus-2ungu58j`. The two-extension witness confirms
local runtime binding and distinct generated managed exports despite identical C#
type/method names. Loading A,B passes three sessions; B,A later crashes one backend
with SIGSEGV. Both initializers ran with distinct tokens. Evidence is retained in
`artifacts/preload/pg-ankus-multi-xwujs5rr`. Native SQL breadcrumbs reproduced the
failure during image B's GC in `artifacts/preload/pg-ankus-multi-ob38edlz`.
The 92 MiB backend dump `data/core.3901061` shows the collector reading a null
method table inside an unfilled heap region. Source review found a concrete lifetime
bug: lazy recovery dereferences the vanished finalizer's `Thread`, which resides in
pthread TLS and can be reused when another runtime starts a child thread first.
The repair captures the allocation context while the finalizer is parked, then uses
that durable copy for child GC cleanup. It never reads the vanished TLS during lazy
recovery and leaves the parent's context unchanged. Both extension load orders now
pass five repeated runs: ten server starts and 30 fresh backend sessions. The repaired
runtime also passes both actively overlapping background-GC rounds and all parent
controls. Retained timer and queue regressions also pass two forks each on this
repaired SDK, with every exact result/status passing and all stderr files empty.
The retained probe also publishes from its new source location in the owned runtime
checkout, `eng/ankus/fork-probes/retained-services`. Logs:
`/tmp/ankus-preload-multi-fixed-retained-{timer,queue}.jsonl`,
`/tmp/ankus-preload-multi-fixed-repeat.log` and
`/tmp/ankus-preload-multi-fixed-bgc-full.jsonl`; SDK:
`artifacts/preload/runtime-multi-proof-sdk`. A separate ELF debugger-header
interposition issue was also identified; it is not the demonstrated GC crash cause.
GDB and required libraries were extracted locally under `artifacts/preload/debugger`;
system packages and dump settings were not changed.
Independent review in `.git/testagent/preload/multi-proof/` checks all ten starts,
30 sessions, 20 distinct parent/image tokens and 3,360 matching native SQL markers,
with no hidden server recovery. The frozen bundle retains passing/failing sources,
binaries, logs, runtime patch, and hashes for the exact SDK and original core.
The production generator and SDK have not yet been replaced by these owned snapshots.

The debugger-header issue is now repaired in the owned runtime. With the previous
binary, loading two Native AOT libraries using `RTLD_GLOBAL` leaves the second
library's exported descriptor uninitialized and overwrites the first. The new
native host reproduces that failure in both load orders. ELF protected visibility
keeps the descriptor public for debugger lookup while binding runtime writes to
the owning image; its exported name, layout and version are unchanged. Both load
orders and a separate-file copy of the same library pass with the repaired binary.
The host checks every table's owning image, exact module base, distinct runtime/GC
addresses, unchanged descriptor/table bytes after the second load, and managed GC
and reentry. ELF inspection confirms a `GLOBAL PROTECTED` export with no dynamic
symbol relocation. This covers debugger metadata isolation, not a complete debugger
session or copied-image fork safety. Sources are committed under
`eng/ankus/fork-probes/debug-headers`; evidence is frozen in
`.git/testagent/preload/debugheader-proof/`. The new SDK is
`artifacts/preload/runtime-debugheader-proof-sdk`. Actual generated Ankus extensions
also pass both preload orders and six fresh sessions with this SDK in
`artifacts/preload/pg-ankus-multi-x7u_dzpt` (release PostgreSQL 18.6/Linux x64).

The [willibrandon/pglogical](https://github.com/willibrandon/pglogical) fork is an
additional read-only reference for Windows worker attachment. Preload, child startup and
background-worker attachment must cover Linux, macOS and Windows; a Linux fork
experiment alone cannot establish the complete requirement.

The user has published the repository to GitHub and authorized CI workflows for
cross-platform verification. CI should finish within 10 minutes where possible,
with the user's current 60-minute timeout for every workflow job; cold-cache
behavior must be measured too.
Independent platforms should run in parallel and superseded runs should cancel.
Upstream runtime work is deferred until the owned patch is proven and has at least
several months of real usage; there is no immediate upstream proposal planned. The intended
consumer experience is automatic selection of a compatible patched runtime by
`Ankus.Sdk` during ordinary `dotnet publish`, with that runtime compiled into the
native extension. A local packaging prototype now proves that consumer flow on Linux
x64: a project outside the repository, with an initially empty NuGet cache and only
an `Ankus.Sdk` reference, publishes without an `IlcSdkPath` override or explicit RID.
The SDK uses the ordinary current-RID inference option for a native shared library,
pins the compatible compiler through `KnownILCompilerPack`, and restores its private
runtime payload. Package data lives under `tools/aotsdk`, keeping private CoreLib out
of compile references without suppressing NuGet warnings. The compiler response file
selects that package's CoreLib, and the resulting native symbol stays local. All six
PostgreSQL backend sessions across two starts pass with default GC and warmed services
in `artifacts/preload/pg-ankus-fod0cc17`. Sources: `.git/testagent/preload/runtime-package/`
and the owned SDK snapshot; consumer: `/tmp/ankus-preload-package-consumer-c97yfwcu`;
local feed: `artifacts/preload/package-feed`. These are unpublished prototype packages.
Production integration and hosted CI validation remain unfinished.

The package proof has also been refreshed with both the vanished-TLS allocation
repair and the debugger-header repair. Runtime payload
`10.0.11-ankus.prototype.2` and Ankus packages `1.0.0-preload.prototype.3` retain the
previous package versions unchanged. Two ordinary extension projects outside the
repository publish with an initially absent NuGet cache, without a runtime path
override or explicit RID. Their compiler response files select the packaged
CoreLib, and all 24 restored payload hashes match the source manifest. The projects
share type and method names but produce independent generated exports. Both preload
orders, all six fresh sessions, exact initialization/GUC/heap state, GC/finalizers,
tasks/timers, errors, transaction restoration and same-session recovery pass in
`artifacts/preload/pg-ankus-multi-a4d3l17c`. Consumer:
`/tmp/ankus-preload-multi-package-consumer-dhij7t2_`; frozen package/source/compiler/
SQL evidence: `.git/testagent/preload/multi-package-proof/`. Publications took 7.59
and 3.48 seconds locally; these are not hosted CI or cold runtime-build timings.

The prototype uses a separate copy of `runtime` v10.0.11
(`79d0c463f1b55624c874a11585f7e47731e8d675`) under
`artifacts/preload/runtime-10.0.11`, matching the installed ILCompiler 10.0.11.
All reference checkouts remain read-only. Exact source research is in
`.git/testagent/preload/`. The first proof's runtime patch, binary hashes, environment,
and complete stock/owned logs are retained in `.git/testagent/preload/first-proof/`;
its matching runtime SDK is retained in `artifacts/preload/runtime-proof1-sdk`.
Native/CoreLib builds and both probe publications completed without warnings or
errors. This is runtime engineering work, not a declaration-only or
delayed-initialization substitute for postmaster callbacks.

The user replaced the runtime reference with a clone of
`https://github.com/willibrandon/runtime`. The existing owned runtime working tree
now has its own Git metadata and local `ankus/fork-support` branch based on the
exact v10.0.11 commit, with that fork as `origin` and dotnet/runtime as `upstream`.
The first runtime milestone is committed locally as
`d45dd8f51d88355653e5f803978ee3b79ae6e2bb` (`Prototype Native AOT fork checkpoints
for PostgreSQL`), including runnable active-GC and retained-service probes under
`eng/ankus/fork-probes`. The runtime checkout is clean. Native Release and CoreLib
builds pass; CoreLib reports zero warnings/errors in 20.71 seconds. The Ankus Release
build also passes with zero warnings/errors in 10.51 seconds. Public site content
and generated API pages were unchanged in this research milestone.
The new reference clone is untouched. No patch commits or branches have been pushed.

Two further local runtime commits are saved: `8ce70563e` fixes debugger descriptor
binding and includes the regression host; `961301d3d` admits macOS x64/arm64 in the
existing Unix checkpoint guards and uses Mach-O linker roots in the native probes.
That commit initially had Linux validation only. Subsequent local Windows and
macOS results are recorded below. The owned Ankus generator
also omits fork enablement under `WIN32`, where PostgreSQL starts a fresh process;
managed initialization remains enabled. Windows worker attachment, actual platform
CI, server GC, enabled EventPipe and the other checkpoint requirements remain open.

After this milestone, plain `dotnet test` passes all 4077 existing Ankus tests with
zero failures/skips in 3m47.444s; the Release build has zero warnings/errors in
11.82s. These are baseline checks of the main repository; the owned runtime and
packaged preload proofs above establish the new behavior separately. Public API
and site content are unchanged while production integration remains open.

Local Windows validation now passes real generated managed preload on PostgreSQL
18.1/x64 (OS build 26200, SDK 10.0.401). Both extension orders pass across two
starts and six fresh SQL sessions. Each Windows backend runs its own initializer;
all twelve backend/image tokens are distinct and hook/initializer PIDs equal the
actual backend PID. Exact configuration callbacks, object state, GC/finalizers,
tasks/timers, owned diagnostics, SET LOCAL rollback, RESET and same-session recovery
pass. Both servers stop cleanly without crash recovery. Frozen source, binaries,
SQL results and hashes are in `.git/testagent/preload/windows-proof/`. This does
not yet verify Ankus background-worker registration/attachment or the full suite
on Windows.

The Windows probe hang was output-handle lifetime, not a failed server startup.
`pg_ctl` lets server children inherit its handles, so waiting for captured output
to reach EOF can outlive `pg_ctl` itself. One native supervisor now owns the whole
start/query/stop sequence and directs tool output to real files; it waits for the
process exit independently of inherited handles. The repository cluster fixture
already avoids capturing Windows startup output. The probe also distinguishes the
logical replication launcher's expected exit code 1 after a requested fast shutdown
from unexpected worker exits before shutdown.

Native compilation exposed two independent build defects. The bridge now includes
both PostgreSQL public and server headers, and uses shell-tokenized Unix
`pg_config --cppflags` for dependency includes and SDK paths. Empty flags are valid;
quoted and escaped paths retain their boundaries without invoking a shell.
Windows compiles the bridge with `/MT` to match Native AOT's static C runtime and
uses `/WX`; the previous `/MD` caused LNK4098. Both actual Windows extensions publish
without that conflict, and both macOS extensions compile with the reported
Homebrew dependency paths. Twelve direct compiler-argument tests pass. Plain
`dotnet test` passes 4089 cases with zero failures/skips in 3m53.236s on PostgreSQL
18.6/Linux x64; Release has zero warnings/errors (6.85s). Documentation build/type
checks and API freshness pass (114 API pages, 1212 members). Site build still reports
the existing duplicate-404 and missing-site-URL warnings.

On macOS 26.5.2/arm64 (build 25F84), the owned native runtime and CoreLib build,
active-background-GC probe, retained-timer probe and retained-queue probe all pass.
The first real PostgreSQL 18.1 preload still fails at its startup thread guard.
Apple's `pthread_is_threaded_np()` reports whether the process has ever created a
thread, including after that thread has joined; a direct native witness reports
`before=0 after_join=1`. PostgreSQL rejects that state before the runtime reaches
its fork preparation. A runtime patch alone cannot change this stock-server check.
An isolated PostgreSQL checkpoint-registration prototype is being built: registered
runtimes must prepare, report their parked thread, and match an OS inventory of all
live threads before startup admission and each fork. Unknown/unprepared threads
remain failures. No installed PostgreSQL binary or Apple thread flag is modified.
At that stage, macOS PostgreSQL success and negative controls were not yet established.
The subsequent results and repaired shutdown check are recorded below.
The source and platform evidence are kept outside public guides; personal validation
machine details are excluded from all repository documents.

The owned macOS PostgreSQL checkpoint prototype now builds with assertions and
warnings as errors through Meson. A private OpenLDAP build replaces deprecated
Apple LDAP headers; the static/shared OAuth targets also need their GSSAPI header
dependency declared. Neither fix disables a diagnostic. Generated Ankus extensions
pass both load orders and six fresh sessions against this server. Guard controls
also pass: no-runtime startup works, while an unknown native thread, an unregistered
runtime beside a registered runtime, and a thread created by a fork callback are
all rejected. A longer repeated run nevertheless exposes an intermittent
`PostmasterThreadsAreSafe()` assertion during shutdown after successful SQL checks.
The failure was retained for investigation; the correction and repeated checks follow below.

The macOS publish also exposed a separate minimum-OS mismatch: the C compiler used
the installed SDK's default (26.0), while Native AOT linked for 12.0. Ankus now waits
for Native AOT's `SetupOSSpecificProps` target and passes its exact `TargetTriple`
to the native compiler. Mach-O inspection verifies both the bridge and final library
report 12.0 by default and 13.0 with `AppleMinOSVersion=13.0`; both publications have
no compiler/linker warnings. Plain `dotnet test` again passes all 4089 cases without
failures/skips (3m48.371s); Release has zero warnings/errors (6.18s). This checks
artifact target consistency, not execution on macOS 12 or 13. Windows was also
republished with the complete header/flag fixes and passed another two starts and
six sessions. Its dedicated validation directory is removed after preserving the
sources/binaries/logs and verifying the owned clusters had stopped.

The macOS shutdown failure is fixed in the owned PostgreSQL patch. XNU snapshots
thread references before converting them to Mach ports; threads that finish during
that interval can appear as `MACH_PORT_NULL` entries. The first inventory incorrectly
counted those entries as live threads. It now counts actual thread ports and still
rejects unknown ports and `MACH_PORT_DEAD`, which can represent a policy-hidden live
thread. No retry, sleep, thread-flag rewrite or diagnostic suppression is involved.
Ten repeated runs pass both orders: 20 starts, 60 fresh sessions, 40 distinct
postmaster/image tokens and 6,720 matching native SQL markers. Four terminated-thread
entries were actually observed by the repaired path. All servers stop cleanly, with
no assertion, crash recovery or forced shutdown. A final defensive NULL-registration
check passes another two starts/six sessions plus six guard cases: ordinary startup,
invalid registration, an unknown native thread alone or beside registered runtimes,
an unregistered Native AOT image, and a thread introduced during fork preparation.

Runtime commit `817a0e193` exposes the native checkpoint validation callbacks.
The owned PostgreSQL 18.1 tree at `artifacts/preload/postgres-18.1-fork` has local
commit `217b767`, including the host registration/inventory checks, OAuth dependency
fix and native rejection fixtures. The reference clones remain clean. No installed
server, Apple thread flag, warning severity or consumer style setting was changed.
macOS requires both the runtime and PostgreSQL patches for this implementation;
stock PostgreSQL's historical thread guard remains incompatible with managed preload.
The prototype does not yet supply a normal consumer installation of that server.

Exact sources, binaries, the macOS AOT SDK, compiler/linker inputs, success/failure
logs and six guard results are frozen in `.git/testagent/preload/macos-proof/`.
All 3,101 archive entries match their file hashes or link targets. The compressed
archive SHA-256 is `711574e4d347eb51aa9201fb888d24453b20b8f693d5095f60d9707e7384c9db`.
The final source files match the tested bundle. Dedicated Windows and macOS validation
directories were removed after evidence capture and ownership/process checks; shared
user caches and installed tools were preserved. Main commits `548228c` and `145509f`
contain the verified header/CRT/preprocessor and deployment-target fixes. Public
preload guides still describe the shipped implementation while runtime/server
packaging and generator integration remain unfinished. Server GC, enabled EventPipe,
remaining wait/callback cases, user-thread/lock behavior, worker attachment, the complete
platform/version matrix and all other full-port requirements remain open.
The preload priority is still active.

Further Linux x64 checks found a child-to-grandchild failure in the runtime patch.
A child that had already entered managed code could fork again, but a native child
that forked before its first managed call exited with status 198: its runtime was
still in `ChildPending`, and preparation required `Idle`. Fork preparation now
checks the native caller and completes the existing child recovery before preparing
another checkpoint. The minimal child-atfork handler remains unchanged; initialization
is not replayed, and caller/managed-stack checks remain enforced.

The retained-services host now has `--descendants` and `--descendants-pending`
regressions. The old runtime passes the first and fails the second; the repaired
runtime passes both. Ten repeated runs cover 20 children and 40 grandchildren,
checking the original token and initializer count, cyclic graph and GC-handle
identity, every retained array value, inherited mutations, ancestor isolation,
current process identity, GC/finalizers, pool work, timers and exceptions. All
3,930 native records pass independent review with empty stderr. Existing retained
timer and queue checks and both actively overlapping background-GC rounds also pass.
Two generated extensions using the new runtime pass both preload orders and six
fresh sessions on release PostgreSQL 18.6/Linux x64, with clean shutdowns.
Runtime commit `2d8c0193c` contains this fix and its regression checks. The new SDK
is `artifacts/preload/runtime-descendants-proof-sdk`; exact sources,
before/after binaries, logs and hashes are retained in
`.git/testagent/preload/descendants-proof/`. This additional runtime change has been
executed on Linux x64 only; the earlier macOS evidence describes the preceding
runtime commit. Plain `dotnet test` passes 4,089 tests with zero failures/skips
(3m50.171s); the Release build has zero warnings/errors (11.30s). Production
packaging/integration and the other runtime/platform requirements above remain
unfinished.

Registered waits now participate in the owned runtime's fork checkpoint. Previously,
any existing portable wait thread caused managed service preparation to fail, even
when all registrations were idle. Wait threads now finish their normal wait/cleanup
paths before fork. The original registrations, callback contexts, handles and timeout
deadlines remain intact; fresh threads resume them in parent and child. Unregister
requests made after a wait thread stops are completed under the existing registration
lock, including registrations created while thread activation is closed.

Runtime commit `0f7e1d164` includes two regression modes, `--retained-waits` and
`--retained-wait-retirement`. The old SDK exits with status 198 on the first mode;
the repaired SDK passes six repeated runs, 12 forks, 24 parent/child observations
and 1,776 exact callbacks. Each setup spans two wait threads and checks safe/unsafe
execution-context behavior, original identities, one-shot/repeating signals, a finite
timeout without rearming, cancellation, unregister completion and independent state.
The retirement mode observes both native wait threads exit before an active worker
unregisters an existing wait and adds/removes another; both operations complete while
activation remains closed. Existing retained timer/queue, recovered/pending descendant
and active background-GC regressions pass on the same SDK.

Two generated PostgreSQL extensions also create waits in their actual managed preload
initializers. Both preload orders and six fresh sessions pass on release PostgreSQL
18.6/Linux x64: twelve inherited registrations independently signal and unregister,
with exact results, pristine state in every backend, opposite-image isolation and
clean server shutdowns. The final CoreLib Release build has zero warnings/errors
(21.95s). SDK: `artifacts/preload/runtime-waits-verified-sdk`; sources, before/after
binaries, logs and hashes: `.git/testagent/preload/waits-proof/`.
These additions are verified on Linux x64 only. Other wait-object kinds, callbacks
already queued at the checkpoint, blocking unregister with queued callback dependencies,
and finalizer-driven unregister still need direct evidence. The full runtime/platform
and consumer integration requirements remain active.
The first root `dotnet test` attempt stopped before test execution with MSB4166
(an MSBuild child node exited). The reported diagnostic directory was absent and
cgroup OOM counters remained zero; no cause is claimed. With the runtime build
finished, plain `dotnet test` passed all 4,089 cases, zero failures/skips, in
3m43.058s. The failed build log is retained with the successful run.
The main Release build also passes with zero warnings/errors (12.63s).

Queued wait callbacks and finalizer-driven cleanup now have direct fork evidence.
`--retained-queued-waits` consumes seventy original wait signals before fork but
holds their callbacks behind one occupied worker. Each unregister request is already
accepted and its original notification remains unsignaled. Native preparation confirms
no callback has run; parent and child must then execute every original callback and
complete every notification without resignal or reregistration. The checks preserve
callback context, handle and object identity, token, exact indexed values and process
isolation, then restore pool limits and require fresh work and collection to succeed.

`--retained-finalizer-wait` uses an actual unreachable object's finalizer to perform
blocking unregister after both native wait threads exit. It also adds and removes a
registration while activation is closed. A cleared weak reference and completed
cleanup prove that finalization ran. Both modes pass three repeated runs each:
12 forks, 24 parent/child observations and 1,728 exact callbacks, with empty stderr.
The earlier idle-wait and worker-unregister modes also pass after their shared host
and helper changes. These are Linux x64 native-host checks using the already verified
`runtime-waits-verified-sdk`; no further runtime implementation change was needed.
Runtime commit `d2b121570` contains the new regressions and host checks.
Sources, the published probe, logs and payload hashes are retained in
`.git/testagent/preload/wait-boundaries-proof/`. No new PostgreSQL or other-platform
execution is claimed by these two checks. Monitored wait-object kinds, blocking
unregister with queued callback dependencies, callback handoff races and the broader
runtime/platform/consumer integration requirements remain active.
Plain `dotnet test` passes all 4,089 tests, zero failures/skips, in 3m48.173s.
The Release build passes with zero warnings/errors (11.33s).

Blocking unregister exposed two deadlocks in fork preparation: an active pool
worker could wait for a queued callback after dispatch had stopped, and an active
finalizer could wait for that same unavailable service. Both old-runtime probes
exit with status 198. Keeping only workers alive exposed another failure: the
callback itself could need a timer and a new registered wait that had already
been stopped.

Runtime commit `1fe18de54` fixes the shutdown order. Pool callbacks, their execution-
context cleanup, and complete finalizer passes share an atomic activity count.
Preparation keeps workers, timers, and wait threads available until active work
finishes. The last active callback closes dispatch atomically; only then do service
threads retire. Remaining queued work stays available to both processes after fork.
Lifecycle callbacks now register during CoreLib initialization, after its class-
constructor machinery is ready, so first-use service initialization cannot miss
an already-started checkpoint.

`--retained-blocking-worker` and `--retained-blocking-finalizer` each require an
original queued callback to complete before synchronous unregister returns. That
callback also requires a new timer, registered wait and another worker. The final
binary passes three runs of each mode: 12 forks and 24 parent/child observations.
Native snapshots require completed cleanup, zero surviving wait threads, and 69
other original registrations still unfired. Parent and child then execute those
registrations independently and verify fresh work and restored pool limits.
The earlier cleanup probes now observe the beginning of callback draining; their
final native snapshot still requires actual wait-thread exit. Active cleanup no
longer runs after service retirement. These test changes follow the corrected
shutdown order rather than retaining that unsafe intermediate state.

Retained timer/queue/wait, pending callback, finalizer, minimal-service and both
descendant-fork regressions pass on the same runtime. Active background-GC checks
also pass. Two generated extensions pass both preload orders and six fresh sessions
on release PostgreSQL 18.6/Linux x64, with exact managed initialization, hooks,
retained waits, GC, timers, object isolation and clean shutdown. This revision has
not been run on macOS or Windows. The production generator/SDK still use their
existing implementation; these remain owned runtime and integration snapshots.
SDK: `artifacts/preload/runtime-blocking-verified-sdk`; frozen sources, binaries,
reproductions and logs: `.git/testagent/preload/blocking-proof/`.

CoreLib Release builds with zero warnings/errors (17.80s), and the native runtime
build passes. `dotnet test` passes all 4,089 cases, zero failures/skips (3m52.069s),
with MSBuild communication and failure logging retained in a dedicated directory.
The main Release build has zero warnings/errors (7.60s). No warning suppression was
added. The earlier MSB4166 build-worker exit did not recur, including six Ankus
builds overlapping three runtime builds with communication logging enabled. Older
available failure dumps belong to other runs and do not establish its cause. It remains unresolved,
with no guessed configuration workaround applied. The broader runtime/platform,
background-worker, consumer packaging and full-port requirements remain active.

Runtime commit `b7ec0ca9c` adds server GC to the owned Native AOT fork protocol. Its collector
threads previously used a detached, non-suspendable lifetime and could not retire
or restart safely at a checkpoint. The runtime now retains native join handles,
stops the dynamic heap coordinator before its peers, joins every server collector,
and restores active and inactive heaps before restarting the coordinator. The
original GC mode and heap data remain intact. Background collectors retain their
shared wake event until all have exited; resetting it in each exiting thread could
strand another waiter. Successful native thread startup also frees its copied name.

The new checks exposed an existing background-GC metadata defect. `do_pre_gc`
selected a new background result before collector startup could fall back to a
blocking collection. The abandoned result then appeared in `GCKind.Background`
with a nonzero index and `Concurrent=false`. The repair restores the previous
completed background result on fallback. The same metadata assertion fails against
the SDK before that repair (marker 36, index 7) and passes afterward. Warm-up permits
bounded startup fallbacks but still requires a real concurrent collection. A fork
attempt without observed concurrent overlap checks values and finalization, then
retries; it never counts toward the required active-collection rounds.

Linux x64 verification uses the exact SDK in
`artifacts/preload/runtime-server-gc-v4-sdk`. Three active-collection runs per mode
pass: 13 forks, 12 qualifying overlaps and 26 parent/child observations. Native
checkpoints observe zero server/background collector threads; recovery restores
four server threads. Dynamic sizing shows one, three and four active heaps while
the maximum stays four. Fixed sizing stays at four. Each process checks every
retained node, both edges, GC handles, all patterned bytes, finalizers and fresh
managed work, with server GC still selected. `GCMaxHeapCount=4` preserves dynamic
sizing; `GCHeapCount=4` disables it, so those are distinct executed configurations.

All twelve retained-service/baseline/descendant modes pass with both server-GC
configurations. Background-disabled server-GC controls pass too. The workstation
collector passes those twelve modes plus the active-collection probe on the same
native revision. No test relaxes diagnostics or changes runtime GC settings to
make fork preparation succeed.

Two separately generated extensions pass shared preload with both fixed and dynamic
server GC on release PostgreSQL 18.6/Linux x64: four server starts, both load orders
per configuration, and twelve fresh sessions. SQL asserts actual server GC in the
postmaster and backend, one initialization, inherited state, hooks, GC/finalizers,
timers, retained waits, errors and image isolation. Both clusters shut down cleanly;
all 257 recorded native-probe process IDs have exited. Fork-observation exports are
absent from the extension libraries. Sources, before/after SDKs, binaries, logs,
PostgreSQL results and payload hashes are frozen in
`.git/testagent/preload/server-proof/`.

This is still an owned runtime/generator snapshot, not the production SDK. The new
server-GC revision has not run on macOS or Windows. Enabled diagnostics/EventPipe,
arbitrary application threads and locks, background-worker attachment, consumer
packaging/integration and the full PostgreSQL/platform matrix remain required.
The earlier MSB4166 failure still lacks a reproducible cause; the runtime repairs
do not establish a fix for that separate build-worker exit.
The native Release build passes; CoreLib is unchanged from the preceding verified
SDK. Main `dotnet test` passes all 4,089 cases, zero failures/skips (3m47.217s), and
the Release build has zero warnings/errors (7.45s). Dedicated MSBuild communication
and failure logging captured another clean run. No warning suppression was added.

Runtime commit `d26aacdcb` fixes two native endpoint cleanup defects found during
the enabled-diagnostics investigation.
Closing a forked child's inherited diagnostic listener unlinked the creating
parent's Unix socket path. A real `ProcessInfo2` query succeeds before child
shutdown and fails with `ENOENT` immediately afterward on the old runtime.
Listener objects now retain their creator PID, and only that process removes
the endpoint path. Descriptor close still applies to the current process's copy.

The listen-error path separately closed its descriptor without marking the
listener closed. A later free could close an unrelated file that reused that
number. It now calls the common close function. The native regression forces
`listen` to fail with a non-socket descriptor, opens a new file at the released
number, then frees the listener. The old runtime closes the new file (result 5);
the corrected runtime preserves it (result 0).

The final Linux x64 component probe passes three runs. Each run closes inherited
endpoints in three native children through shutdown and three through ordinary
close, then queries the original parent's PID, runtime cookie and module identity
after every close. The creating parent's own shutdown removes the endpoint.
All eighteen children are reaped. The descriptor-reuse regression also passes in
each run. These use actual enabled Native AOT diagnostics and the wire protocol,
with no managed child reentry. The exact SDK is
`artifacts/preload/runtime-diagnostics-cleanup-sdk`; sources, binaries, failing and
passing evidence and payload hashes are retained in
`.git/testagent/preload/diagnostics-proof/`.

The upstream native-library EventSource build warning remains enabled and appears
in probe publish logs. This component repair does not resolve that warning's full
scope. Enabled-diagnostics fork admission remains guarded: listener retirement and
restart, in-flight commands, trace-stream ownership, sampling, managed listeners,
multiple runtime instances and actual managed backend tracing still need repair
and verification. No macOS/Windows or enabled-diagnostics PostgreSQL execution is
claimed here. Production SDK/generator integration and all remaining full-port
requirements remain active. Native C/C++ builds pass with warnings treated as
errors. Main `dotnet test` passes all 4,089 cases, zero failures/skips (3m44.950s);
the Release build has zero warnings/errors (4.79s). The earlier MSB4166 worker exit
did not recur with communication logging enabled and its cause remains unresolved.

Runtime commit `182710e9f` repairs another diagnostics hang: socket reads and writes
only applied their timeout to the initial readiness check, then used blocking
transfers. A client that sent part of a request or stopped reading could hold the
operation forever. The socket PAL now uses nonblocking streams and one monotonic
deadline across readiness checks, partial transfers and interrupted system calls.
Zero-byte operations succeed without waiting. Infinite waits remain infinite, and
finite poll intervals are bounded before conversion to the signed OS timeout.
Descriptor passing waits for readiness on the nonblocking socket. Stream creation
also releases its accepted/connected descriptor if initialization fails; allocation
failure itself has not been fault-injected in this component probe.

The Linux x64 probe runs the actual Native AOT-linked socket implementation with
an external client. Eighteen cases check partial and slowly progressing reads,
blocked and slowly drained writes, signals directed at the transferring native
thread, complete byte sequences, EOF, zero-byte and zero-timeout operations,
infinite waits, and delayed descriptor delivery. Three final runs pass all 54
cases. The same fixture against the preceding SDK exceeds the three-second
supervisor deadline in all six read/write timeout regressions despite requesting
300 ms. On the repaired runtime those operations finish near their requested
deadline. Three endpoint-cleanup runs also pass, including eighteen native child
closes and the descriptor-reuse regression. The final SDK is
`artifacts/preload/runtime-diagnostics-io-v2-sdk`; sources, SDKs, binaries, failing
controls, passing runs and hashes are retained in
`.git/testagent/preload/diagnostics-io-proof/`.

This fixes the transport's requested timeout contract. It does not add a timeout
to diagnostic commands that request an infinite wait, implement listener
retirement/restart, or admit enabled-diagnostics forks. Pending commands, active
trace writers, sampling, managed listeners and multiple runtime instances remain
required. Source review also identified follow-up checks for a full Unix listener
backlog during reverse connection and malformed descriptor messages; those cases
are not covered by the current passing evidence. This revision has not run on
macOS or Windows. The native-library EventSource warning remains visible and no
warning suppression was added. Main `dotnet test` passes 4,089 cases with zero
failures/skips (3m51.093s); the Release build has zero warnings/errors (12.56s).
The separate MSB4166 exit did not recur and remains unexplained. Public behavior
and guides are unchanged; production integration and full-port scope remain open.

Runtime commit `1257da736` fixes the reverse-connection stall and descriptor leaks
identified above. Unix socket connection attempts incorrectly assumed that a local
listener could never block. A full connection queue disproves that assumption.
Finite attempts now use nonblocking sockets and the existing operation deadline;
a full Unix queue returns `EAGAIN` to the diagnostic server's reconnect loop,
matching .NET's managed socket handling. Errors survive descriptor cleanup and
the timeout output is initialized on every attempt.

The old runtime hangs in a direct connection attempt and also stops answering
ordinary `ProcessInfo2` queries when its configured reverse port is full. With the
repair, the default port answers while that queue remains full. Once the monitor
accepts queued connections, the runtime reconnects automatically. Three final
runs each verify three successive reverse connections, their advertisement PID
and cookie, complete `ProcessInfo2` replies, default-port service and clean exit.
Six direct connection cases pass in each run, including full/zero-timeout queues,
retry after recovery, missing/refused endpoints, and an infinite wait released by
the listener. Descriptor counts stay unchanged after each attempt.

The descriptor receiver also accepted two or three transferred file descriptors
as if the message contained only one, leaking one descriptor on Linux x64. It now
rejects extra/truncated lists and closes every delivered descriptor on failure.
Valid descriptors are marked close-on-exec. The native probe verifies exact file
contents and launches a native child through `exec` to prove the descriptor is
closed there; no managed child reentry is involved. The old runtime retains the
descriptor across exec and grows its descriptor count on both malformed inputs.
The corrected runtime passes all 21 I/O/descriptor cases in three runs, including
a real diagnostic query after each operation. Existing endpoint ownership and
descriptor-reuse checks also pass in three runs, including eighteen native child
closes. Each supervisor reaps its owned processes.

The exact SDK is `artifacts/preload/runtime-diagnostics-transport-sdk`. Sources,
SDKs, native binaries, failing controls, successful runs and hashes are retained
in `.git/testagent/preload/diagnostics-transport-proof/`. These are Linux x64
results; no macOS or Windows execution of this revision is claimed. Enabled
diagnostics still needs listener retirement/restart, pending-command preservation,
active tracing/sampling and multiple-runtime support before fork admission can
be enabled. The upstream native-library EventSource warning is still present;
no warnings were suppressed. Production integration and the complete full-port
and PostgreSQL/platform requirements remain active.
Main `dotnet test` passes 4,089 cases with zero failures/skips (3m44.930s) on
PostgreSQL 18.6/Linux x64; the Release build has zero warnings/errors (12.09s).
MSBuild communication logging captured another clean run. The earlier MSB4166
exit remains unexplained and is not claimed fixed by these runtime changes.

Runtime commit `aa36934c4` adds a restartable Native AOT Unix diagnostic listener.
Previously its detached thread could wait indefinitely for a connection or the
rest of a request. The listener now owns a joinable thread and a wake pipe.
Pausing wakes its socket wait and joins the thread, including native thread
cleanup. Restarting creates a new thread. Accepted connections, request headers,
payload buffers and consumed-byte counts remain owned by the server throughout.
No client reconnect or repeated request is required.

The Linux x64 `listener.py` probe passes nine cases in three final runs, with
186 observed thread-retirement checkpoints. It verifies actual kernel thread
absence, exact consumed-byte boundaries, unchanged endpoint identity, and replies
through the real diagnostic protocol. Cases cover idle requests, fragmented
headers and Unicode payloads, EOF during header/payload input, invalid size/magic,
and reverse connections. Completing or closing a request while paused produces
no response until restart. A completed environment-setting request applies the
exact value; incomplete/error requests leave it unset and return exact protocol
errors. Reverse clients can reconnect afterward. Twenty idle cycles per run
preserve descriptor counts, and all cases finish with a live identity query.

The earlier I/O, descriptor, connection and endpoint-cleanup suites also pass
three runs against this SDK. Fixed and dynamically sized server GC each pass
active-background-collection, callback-dependency and pending-child descendant
checks after the shared joinable-thread helper was renamed. All 129 recorded
native-probe process IDs have exited. The exact SDK is
`artifacts/preload/runtime-diagnostics-listener-sdk`; sources, binaries, commands,
results and hashes are retained in `.git/testagent/preload/diagnostics-listener-proof/`.

This component stops the listener while it is waiting for input. A command already
sending a response or waiting for a tracing descriptor can still delay the join;
those paths, active tracing/sampling, child recovery and multiple runtime images
remain required before enabled-diagnostics fork admission. The listener tests do
not fork, and the existing enabled-diagnostics fork rejection remains in place.
No macOS/Windows or enabled-diagnostics PostgreSQL execution is claimed for this
revision. Production integration, application-thread handling and the full port
and PostgreSQL/platform matrix remain unfinished. The native-library EventSource
warning remains visible; no warning suppression was added.
The native Release build passes. Plain `dotnet test` passes 4,089 cases, zero
failures/skips (3m52.643s), on PostgreSQL 18.6/Linux x64. The main Release build
has zero warnings/errors (9.91s). MSB4166 did not recur and remains unexplained.
Public behavior and guides are unchanged.

Runtime commit `531646d40` preserves ordinary diagnostic responses while the
listener retires. Previously the command handler itself waited for every socket
write, so a client that stopped reading could prevent the join indefinitely.
Handlers now create an owned response, and the listener sends its chunks through
an interruptible wait. The remaining bytes and offsets survive restart. A
disconnected client releases the queued buffers and connection. Commands are not
executed again to reconstruct their replies.

The Linux x64 `response.py` probe passes eight cases in three final runs. It checks
large Unicode environment replies, repeated pauses, half-closed/disconnected
clients, reverse connections, completed replies, allocation lifetime and native
child endpoint cleanup. Each paused observation accounts for bytes already received
plus bytes still queued. Changing an environment value while paused preserves the
old value in that reply and exposes the new value in the next query. Native
children perform no managed calls; their cleanup preserves the parent's pending
reply and endpoint. The preceding SDK times out at the same pause operation.

Allocation accounting exposed a separate command-line leak. Native AOT returned
a newly allocated string through an interface whose callers treated it as borrowed.
The common interface now returns an owned snapshot, released after conversion or
process-info event emission. CoreCLR returns a current snapshot without its old
shared/leaked cache; Mono copies its borrowed value. Linux also releases a buffer
left by a failed `getline`. All three process-info wire formats retain their exact
values and boundaries. Across 64 measured success/disconnect cycles, the old
Native AOT build grows live native allocations by 16,144 bytes; all three final
runs show zero growth. This is component allocation evidence, not a claim about
every runtime allocation path.

The probes now wait for process-info EOF before counting descriptors. A separate
native pthread experiment also reproduces Linux returning from `pthread_join`
while the exiting task remains visible in `/proc` (`PF_EXITING` set). Listener
checks require that flag for any residual entry, then require its disappearance
within a bounded wait. They still require the thread to be gone at the checkpoint.
The earlier listener, I/O, connection and endpoint-cleanup suites pass three runs
against the final SDK. All 462 recorded process IDs across these experiments have
exited. SDKs, sources, failing controls, final results and hashes are retained in
`.git/testagent/preload/diagnostics-response-proof/`; the SDK is
`artifacts/preload/runtime-diagnostics-response-sdk`.

Native AOT Release and the CoreCLR EventPipe object targets compile successfully.
CoreCLR execution, Mono execution and macOS/Windows validation are not claimed.
Plain `dotnet test` passes 4,089 cases, zero failures/skips (3m52.497s), on
PostgreSQL 18.6/Linux x64; the main Release build has zero warnings/errors (11.75s).
The upstream native-library EventSource warning remains visible and no warning
suppression was added. MSB4166 did not recur and remains unexplained.
Tracing commands still need connection handoff and interruptible descriptor
receipt; active trace/sampling workers, child recovery and multiple runtime images
remain required. Enabled-diagnostics managed forks are still guarded. Production
integration, application threads and the full PostgreSQL/platform/port requirements
remain unfinished. Public behavior and guides are unchanged.

Runtime commit `8117d314b` establishes explicit ownership for EventPipe tracing
requests and sessions. Malformed CollectTracing requests now release their
connections, StopTracing validates its exact payload length, and failed user-events
descriptor receipt or session admission releases every received resource. A session
takes its continuation stream and descriptor only after all fallible enable work
succeeds. File/serializer construction follows the same rule. Partial buffer-manager,
provider, event-source, sample-profiler, metadata and Native AOT spin-lock failures
also clean up safely. A dropped provider callback now completes its pending-callback
bookkeeping, preventing later provider deletion or shutdown from hanging.

The Linux x64 probe sends real diagnostic protocol traffic. Three final runs each
pass 17 cases: all five malformed CollectTracing versions; empty, short, long and
unknown StopTracing requests; missing, extra and invalid descriptors; twenty
successive trace sessions; and 64 simultaneous sessions followed by rejected
admission. Every successful trace has a complete NetTrace header and terminator,
endpoint identity remains stable, descriptor counts return to their baseline, the
listener pauses and restarts, and shutdown succeeds with empty stderr.

An opt-in link-time allocation probe fails every observed allocation independently:
18 file-construction points, two serializer-start points and 61 session-enable
points. Each of the 81 failures runs in a fresh process, retains caller ownership,
performs exactly one cleanup, recovers with a successful operation in the same
process, answers another diagnostic query, restarts its listener and shuts down.
All points pass in three complete runs. The preserved earlier runtime frees file and
serializer resources at the wrong time and crashes with `SIGSEGV` during session
construction, so the controls distinguish the repair from a test that merely avoids
the paths. The earlier response, listener, I/O, connection, reverse-connect and
lifecycle suites also pass three runs against the final binary.

Native AOT Release and the CoreCLR EventPipe object targets compile successfully;
the native fixture compiles with warnings as errors. Main `dotnet test` passes all
4,089 cases with zero failures/skips in 3m44.215s, and the Release build has zero
warnings/errors. Exact result directories are retained under `artifacts/preload/`,
with review notes under `.git/testagent/preload/diagnostics-tracing-work/`. This
milestone does not yet pause an active trace writer or sampling thread across fork.
Tracing connection handoff, interruptible descriptor receipt, active worker
checkpoint/recovery, managed listeners, multiple runtime images and macOS/Windows
execution remain required before enabled-diagnostics fork admission. The guard and
public guides therefore remain unchanged.

Runtime commit `68a1418bc` makes the remaining tracing command handoff restartable.
The diagnostic listener can now stop while CollectTracing waits for a Linux
UserEvents descriptor or while a successful start response is blocked. Parsed
payloads, the connection and every unsent response byte remain owned across the
pause. A trace session does not start its writer until all 28 response bytes reach
the client. If delivery fails, the runtime disables the session and releases the
connection instead.

The previous runtime times out while joining the listener at both boundaries. The
repaired runtime passes three final 19-case tracing runs. Each run pauses twice
during descriptor receipt, pauses with exactly 28 response bytes queued, resumes,
returns the session identifier, drains a 738-byte NetTrace stream, stops the
session, and restores the descriptor count from seven to seven. The 81 independent
allocation failures and all earlier tracing cases also pass in every run. The
response, listener, I/O, connection, reverse-connection and lifecycle suites pass
three more rounds with empty stderr and no remaining processes.

Native AOT Release and the CoreCLR EventPipe object targets compile successfully;
the native fixture compiles with warnings as errors. The exact SDK is
`artifacts/preload/runtime-diagnostics-tracing-v7-sdk`; the final probe is
`artifacts/preload/probe-diagnostics-tracing-handoff-v2`. Controls, final results,
artifact hashes and 642 verified evidence files are retained in
`.git/testagent/preload/diagnostics-tracing-handoff-proof/`. Active trace and
sampling workers, child recovery, managed listeners, multiple runtime images and
macOS/Windows execution remain required before enabled-diagnostics fork admission.
The guard and public guides remain unchanged. Main plain `dotnet test` passes all
4,089 cases with zero failures/skips in 3m45.478s; the Release build has zero
warnings/errors in 12.21s.

Runtime commit `68e7761ad` adds cooperative checkpoints for live EventPipe trace
writers and the sample profiler. Preparation stops each worker through its normal
exit path while keeping the session, providers, buffers, serializer and trace stream
alive. Parent recovery creates new workers without writing a second NetTrace header.
Every recreated helper also receives the normal `.NET EventPipe` thread name.

The Linux x64 probe keeps one writer session and one sampling session alive through
20 stop/restart cycles each. Every pause leaves only the host and finalizer tasks;
every resume creates the expected listener, writer and optional sampler with new
thread IDs. The sampling case runs bounded managed work and produces a valid
7,950-byte NetTrace with actual samples. Both cases keep the same session identifier,
return descriptor counts from seven to seven and exit cleanly. The corrected
CollectTracing5 fixture now encodes an empty exclusion filter, rather than an empty
inclusion filter that silently disabled every provider event.

The complete 21-case tracing suite passes, including all 81 independent allocation
failures. The response, listener, I/O, connection, reverse-connection and lifecycle
suites also pass against the same binary. Native AOT Release, the CoreCLR EventPipe
sources and the native fixtures compile successfully; fixture C/C++ warnings are
errors. The exact SDK is
`artifacts/preload/runtime-diagnostics-tracing-v9-sdk`; the probe and host are
`artifacts/preload/probe-diagnostics-tracing-workers-v3` and
`artifacts/preload/fork-diagnostics-tracing-host-v6`. Results are retained in the
owned runtime checkout under `.git/testagent/preload/diagnostics-tracing-workers-v3-*`
and `.git/testagent/preload/diagnostics-workers-v3-*`.

A trace writer blocked inside socket output still needs an interruptible, resumable
handoff. Child recovery, managed listeners, multiple runtime images and macOS/Windows
execution also remain required. The enabled-diagnostics fork guard and public guides
remain unchanged.

Runtime commit `71570d91d` makes blocked EventPipe output interruptible and
resumable. Each IPC trace session creates an anonymous close-on-exec file before it
starts. If a checkpoint interrupts a full or partial socket write, the writer appends
only the unsent bytes with `pwrite`; this path does not allocate from the runtime heap.
The replacement writer sends the retained range first and advances an explicit cursor.
A later checkpoint can interrupt that replay without duplicating bytes. Parent resume
drains the listener interrupt before creating replacement writers.

The final Linux x64 blocked-output case permits exactly 32 bytes, blocks the next
send, retires the listener, writer and sampler, and then repeats five more checkpoints
while replay remains blocked. Every cycle creates new workers and leaves only the host
and finalizer while paused. The original session then resumes, records more managed
samples and stops normally. Its 7,666-byte trace opens successfully with
`dotnet-trace report`; descriptor counts return from seven to seven and stderr is
empty. The complete 22-case tracing suite passes, including all 81 independent
allocation failures. The full response, listener, I/O, connection, reverse-connection
and lifecycle suites pass against the same binary.

Native AOT, CoreCLR EventPipe, the standalone diagnostics PAL and the complete
configured runtime build compile successfully. Fixture C/C++ warnings are errors.
The exact SDK, probe and host are
`artifacts/preload/runtime-diagnostics-tracing-v15-sdk`,
`artifacts/preload/probe-diagnostics-tracing-workers-v11` and
`artifacts/preload/fork-diagnostics-tracing-host-v8`. Final evidence is retained under
`artifacts/preload/runtime-10.0.11/.git/testagent/preload/diagnostics-*-v11-all`.
Child recovery, managed listeners, multiple runtime images and macOS/Windows execution
remain required before enabled-diagnostics fork admission. The guard and public guides
remain unchanged.

Unfinished allocation-exhaustion tests are preserved in stash
`d9d1da45c924b8bdb15fd3f459450873742861ef`; the unverified initialization/configuration
guide simplification is preserved separately in stash
`d5b5bd1d15f722d98fbb16ae59603dd153a67cbd`. Neither task should resume before shared
preload works. The previous verified milestone follows.

The latest milestone verifies actual allocation above PostgreSQL's ordinary limit and growth
that remains above the limit. Five real >1 GiB cases cover ordinary, no-OOM, aligned, aligned no-OOM,
and zeroed storage on release PostgreSQL 18.6/Linux x64. All ten cases in the dedicated resource run
pass, including five small controls. The default assertion-enabled suite passes all 4077 cases
without failures/skips. Native/catalog accounting proves individual reclamation before context
deletion; checked views and same-session recovery also pass. The largest observed backend RSS peak
was 1,101,213,696 bytes, with original resource limits restored and no new cgroup OOM events.
IDE0004 and IDE0300 remain enforced as errors. Datum/node APIs, full memory/GUC/preload parity,
the remaining inventory, and the version/platform matrix are incomplete.

The non-incremental Release build has zero warnings/errors. Analyzer verification, XML documentation,
source style, documentation build/type checks and generated API freshness checks all pass.

- `Ankus.slnx` contains the runtime, source generator, native build tool, native sample,
  PostgreSQL discovery, test infrastructure, and five developer-visible MSTest projects.
- `dotnet pack` produces `Ankus.Sdk`, `Ankus.Runtime`, `Ankus.Generators`, `Ankus.PgConfig`,
  `Ankus.Testing`, and `Ankus.Tool`. The NuGet SDK supports cold restore and native publishing
  without repository imports; isolated consumers exercise the installed tool, direct publishing,
  Central Package Management, and package-backed MSTest discovery.
- `ankus new` creates a package-based extension solution with CPM, matching Ankus versions, and MSTest/MTP
  discovery. The generated five-case suite verifies managed and native calls plus same-connection error recovery.
  `PostgresExtensionTest` publishes against the selected headers and loads into an isolated PostgreSQL 18+ cluster.
  Publishing from a generated solution selects its sole Ankus SDK project; ambiguous solutions require `--project`.
  Mutation checks prove native code is rebuilt, and initialization-failure checks prove build/SQL errors fail tests
  and clean up owned cluster/publish directories. PostgreSQL logs and binlogs are retained.
- **`dotnet test`**: **4077 passed, 0 failed, 0 skipped** on Linux x64 with PostgreSQL 18.6.
  The separately selected resource run additionally executes five >1 GiB cases; those are not part of the default total.
- The public testing package lives in `src/Ankus.Testing`; repository-specific fixtures and executable tests live in
  `tests/Ankus.IntegrationTests`, `tests/Ankus.Examples.Hello.Tests`, `tests/Ankus.PgConfig.Tests`,
  `tests/Ankus.Generators.Tests`, and `tests/Ankus.Runtime.Tests`.
- The testing-package move preserves its assembly, namespace, NuGet identity, and author APIs. Solution references,
  documentation generation, and isolated package-consumer tests use the new path. The post-move Release build has
  zero warnings/errors, and plain `dotnet test` still passes all 995 cases, including installed-package and generated-solution tests.
- XML summary tags use separate opening, text, and closing lines. CA1000 is an error in the repository;
  generic types do not expose static members. Consumer projects choose their own coding conventions.
- Local type declarations follow the read-only `runtime`/`msbuild` references: IDE0008 errors require explicit
  built-in and non-apparent types; constructors/casts that name their type permit either spelling. The same
  rules apply only to this repository. `ankus new` does not ship a style `.editorconfig` or enable code-style builds.
  `AGENTS.md` records repository conventions and read-only reference names without personal paths.
- IDE0290 enforces primary constructors in the repository; eligible public constructors retain their signatures
  and initialization behavior. Warning suppressions are prohibited, and both prior test pragmas were removed
  while preserving direct `ToArray()` copy-mutation assertions. Consumer templates contain no repository style rules.
- IDE0004 is enforced as an error throughout repository builds. Redundant casts are removed with
  Roslyn's diagnostic-specific code fix; consumer templates remain free to choose their style.
- IDE0300 enforces collection expressions as an error throughout repository builds. Roslyn simplified
  16 array initializers across eight test files while preserving their expected values and element types.
  Consumer templates contain no repository style enforcement.
- IDE2003 enforces a blank line after closing blocks before the next statement. Existing C#, embedded native
  code and emitted dispatchers follow the rule. Connected clauses and enclosing closing braces remain together.
  A negative build probe fails on missing separation and passes after the blank line is inserted.
- Internal declarations also carry XML documentation. A Roslyn scan across sources, tests, samples and bundled
  templates found 101 omissions; all are documented, including enum members and internal interface contracts.
  The follow-up scan reports zero omissions. Private declarations are outside that scan's scope.
- The sample contains ordinary `[PgFunction]`-attributed `Add` and `Greet` methods. Ankus generates
  managed dispatchers, native entry points, module magic, finfo, datum conversions, and SQL.
- Function declarations support named arguments, C# optional constants, explicit SQL defaults, fixed schemas,
  volatility, parallel safety, NULL policy, owner/caller security, leakproofness, cost, planner support, replacement,
  and scoped search paths. `[PgSchema]` creates extension-owned schemas; `Create = false` targets an existing schema.
  Fixed schemas generate non-relocatable control files. Schema-only extensions also publish as native libraries.
- `[assembly: PgSql]` and `PgSqlFile` add installation SQL with named dependencies, before constraints,
  bootstrap/final positioning and per-block relocation promises. `Id`/`Requires` connect generated functions
  and schemas to the same deterministic SQL graph. `ANKUS005` rejects missing/duplicate IDs, cycles and invalid
  file inputs. SQL files are tracked Roslyn AdditionalFiles; file-only changes invalidate generation.
- `PgInet`/`PgCidr` retain IPv4/IPv6 identity and prefixes with immutable address-bit storage. `IPAddress`
  and `IPNetwork` mappings use checked conversions; scoped IPv6 and lossy host-prefix casts are rejected.
  Native binary conversion supports scalar/array function signatures and every typed SPI ownership path.
  Parsing uses guarded PostgreSQL routines; masks, network derivation, comparison and formatting work detached.
- Seven geometric types now map to immutable fixed-size records and owned path/polygon collections. Binary
  conversion preserves coordinate bits across functions, arrays and SPI. Box normalization and polygon bounds
  use PostgreSQL float ordering; parsing and binary validation remain inside native guards. Empty owned
  paths/polygons retain pgrx's representation, using header-derived native storage for zero vertices.
- `PgRange<T>` represents six built-in range families with owned bounds, explicit inclusion flags, empty values
  and unbounded ends. It supports full-range PostgreSQL values and checked .NET aliases, scalar/array function
  signatures and typed SPI. PostgreSQL performs canonicalization, parsing, formatting, containment, adjacency,
  overlap, union, intersection, difference and merge through guarded native calls.
- `[PgEnum]`/`[PgEnumLabel]` generate ordered PostgreSQL types, exact label mappings, schema/type/function
  dependencies and Native AOT conversions for scalars, nullable values, vectors and shaped arrays. `PgEnums`
  resolves current type/value OIDs and returns owned catalog metadata without retaining identities across DDL.
  The enum sample exercises extension relocation/reinstallation; installation scripts declare UTF-8 encoding.
- `[PgOperator]` generates binary/prefix operators with commutator, negator, selectivity and hash/merge options.
  `[PgCast]` generates explicit, assignment and implicit casts, including PostgreSQL typmod/explicitness arguments.
  Both imply a backing function, accept optional `[PgFunction]` configuration, and expose separate SQL dependency IDs.
- `IEnumerable<T>` generates SETOF for supported values and TABLE for named tuple elements, with explicit
  column-name overrides, planner row estimates, streaming and PostgreSQL tuple-store materialization.
  Iterator ownership survives suspension and releases on completion, LIMIT, portal closure, native errors and
  cancellation. Restricted abort cleanup frees owned plans and safely defers cursor closure past portal scans.
- Generated native code compiles against the discovered PostgreSQL server headers, then links
  into the Native AOT library. Export inspection confirms magic, finfo, and the SQL entry point.
- Managed exceptions return to the native wrapper before it raises PostgreSQL ERROR.
  Both checked-overflow cases return SQLSTATE `38000`; rollback and subsequent queries succeed
  on the same backend. No PostgreSQL error is raised through a managed frame on this path.
- Generator tests cover compilable wrappers and diagnostics for unsupported signatures,
  inaccessible/generic types, async methods, invalid SQL names, and duplicate SQL signatures. Runtime tests verify
  UTF-8 truncation, buffer guards, null termination, and a throwing exception-message accessor.
- PostgreSQL discovery checks `~/.ankus/config.json`, Ankus-managed installations,
  PATH, and conventional Windows/Linux/macOS installation directories.
- Validation uses an assertion-enabled PostgreSQL 18.6 built from the official source archive.
- Integration setup publishes the native sample for the host RID, reserves a dynamic port,
  initializes fresh PGDATA, starts `pg_ctl`, creates a test database, and runs `CREATE EXTENSION ankus_hello`.
- Publishing emits the native library, generated SQL, and `extension/` control and versioned SQL files.
  PG18 fixtures use per-cluster `extension_control_path` and `dynamic_library_path` settings to
  load the actual published files without copying into the shared PostgreSQL installation.
- Integration tests verify extension-owned function catalog entries, schema relocation,
  DROP EXTENSION removing the function, and reinstallation into a requested schema.
- The integration cases include aggregates, triggers/transition tables, composites/heap tuples, SETOF/TABLE, operators/casts, enum/range/geometric/network/array/JSON conversions, custom SQL/dependency checks, declaration/catalog/ownership checks, isolated NuGet consumers, installed-tool workflows, arrays and variadics, numeric/temporal storage and operations, scalar bounds, signed zero and NaN bit patterns,
  nullable contracts, SQL overloads, Unicode, bytea, packed headers, compressed/external TOAST,
  LATIN1 conversion, and recovery from native output-encoding errors on the same backend.
- `Spi.Execute` runs SQL inside a guarded native subtransaction. Tests verify writes, row counts,
  rollback after errors, recursive calls between AOT extensions, backend-thread enforcement,
   and managed finally execution after native errors and statement cancellation.
- `SpiParameter`, `Spi.Query`, and `Spi.ExecuteScalar<T>` support typed scalar/text/bytea parameters,
  typed NULLs, owned result rows, column names/OIDs, domains over supported base types, read-only execution,
  and row limits. Scalar materialization does not truncate command writes. Tests exercise conversion-error
  rollback, results surviving subsequent SPI calls, parameter binding, and empty/zero-column results.
- `Guid`, `PgJson`, and `PgJsonb` map to `uuid`, `json`, and `jsonb` in generated function signatures
  and all typed SPI paths. UUID transport uses explicit network byte order. JSON wrappers own exact or
  server-normalized text, preserve numeric precision, distinguish SQL/JSON null, and expose disposable DOMs
  plus `JsonTypeInfo<T>` serialization. Native AOT tests exercise source-generated contracts containing
  nested wrappers, domain conversion, packed/compressed/external values, deep JSON, LATIN1 encoding,
  and guarded recovery from invalid syntax, jsonb Unicode restrictions, and numeric overflow.
- Arrays use ordinary `T[]` vectors or `PgArray<T>` with row-major values, dimensions and lower bounds.
  All supported scalar families work in attributed functions and typed SPI; `byte[]` remains scalar bytea,
  while `byte[][]` is bytea[]. NULL elements require reference or nullable value types. Vectors reject
  shape/lower-bound loss, and `ToArray()` explicitly flattens. C# `params T[]` generates SQL `VARIADIC`.
  The native bridge uses one pointer-free transport buffer with scalar element conversion and allocator-matched
  cleanup. Typed managed decoding avoids an intermediate object array. Backend cases verify PostgreSQL binary
  output, six dimensions, lower bounds, domains, TOAST, LATIN1, native output failures and session recovery.
- `PgNumeric` owns canonical numeric output with full precision and display scale. It supports PostgreSQL
  arithmetic, rescaling, transcendental routines, NaN/infinities, and backend-independent equality/order/hash.
  Generated decimal adapters and typed SPI conversions reject overflow, rounding, and underflow; finite integer
  conversion uses `BigInteger`. The native scalar signature dispatcher is shared with temporal routines.
  PostgreSQL 14+ temporal `Extract` returns numeric directly; PG13 retains its floating-point extraction limits.
- `PgNumericPrecision` constrains generated numeric/decimal parameters and returns through guarded PostgreSQL
  rescaling. Input constraints precede user code and checked decimal narrowing; return constraints follow managed
  method unwinding. `ANKUS003` rejects invalid declarations. Generic checked integer conversion covers all .NET
  binary integer widths, including UInt128 and BigInteger; server integer/float casts retain PostgreSQL rounding and
  range errors. Exact primitive conversions, generic operator interfaces and one-pass `Sum` complete the numeric
  convenience surface. Recovery tests preserve prior writes/plans and retain zero extra native contexts.
- `DateOnly`, `TimeOnly`, `DateTime`, `DateTimeOffset`, and `TimeSpan` have checked temporal mappings.
  `PgDate`, `PgTime`, `PgTimeTz`, `PgTimestamp`, `PgTimestampTz`, and `PgInterval` preserve PostgreSQL's
  full finite range, infinities, 24:00, second-resolution offsets, and independent calendar components.
  All generated function and SPI paths share field-wise native conversions. Tests compare PostgreSQL binary
   storage, independently check epoch/offset fields, and verify daylight-saving semantics and error recovery.
- Temporal `Parse/TryParse`, server/ISO text output, calendar arithmetic, symbolic age, field extraction,
  truncation, named-zone conversion, native constructors, and server clocks use an allowlisted native dispatcher.
  The guard copies nullable results out of disposable contexts without opening or replacing an SPI connection.
  Date/time/timestamp comparisons are backend-independent; interval comparison explicitly uses server semantics.
  Tests verify session settings, DST transitions, native SQLSTATEs, write preservation, managed finally execution,
  and zero retained operation/diagnostic/transaction contexts after repeated successful and failed calls.
- Temporal component factories, arithmetic operators, precision-rounded current/local clocks and explicit-zone
  ISO formatting use the same guard. Explicit-zone output resolves the offset at the stored instant, including
  historical seconds and DST overlaps. Timestamp rounding rejects results beyond the finite range before returning
  to managed code. Interval unit factories, checked component-wise absolute value, and Int128 comparison-duration/sign
  helpers retain the distinction between calendar components and elapsed time.
- Full-range temporal and numeric JSON converters are statically registered and exercised through source-generated
  contracts in Native AOT. Temporal strings preserve BC/infinity/24:00 and offsets; numeric strings preserve full
  precision and scale and also accept exact unquoted numeric input. Invalid values include JSON property paths
  and native exception causes; repeated failures preserve writes, execute finally and retain no native contexts.
- `Spi.Prepare` creates explicitly disposable `SpiPreparedStatement` instances using declared CLR parameter
  types and native `SPI_keepplan`. Tests verify reuse across callbacks and committed/rolled-back transactions,
  schema/search-path invalidation, argument validation, recursive execution, reentrant-disposal rejection,
  worker-thread rejection, full write effects, error recovery, and native memory cleanup after errors/timeouts.
- `Spi.OpenCursor`, prepared-plan cursors, `Spi.FindCursor`, and `SpiCursor` provide owned batches,
  forward/backward fetch, detach/find, and guarded disposal. Native memory-context callbacks invalidate
  portal identities on close/commit/rollback; tests reject stale objects even after portal-name reuse.
  Tests also cover external scrollable/holdable cursors, plan-independent cursor lifetime, savepoints,
  read-only execution, reentrant-disposal rejection, and cleanup after fetch errors/cancellation.
- `Spi.Connect` provides synchronous scoped `SpiSession` callbacks with one native connection, nested scopes,
  session-bound plans, and `SpiPreparedStatement.Keep()` ownership transfer. Native session cleanup frees
  unretained plans and materialized tuple tables. Session plans are registered with the plan cache so schema
  invalidation works before retention. Tests cover expired owners, worker/reentrant access, nested errors,
  native/managed failures, cancellation, cursor/result survival, and kept plans across commit/rollback.
- `SpiRow.Set`, named indexing, `GetOrdinal`, and per-cell `GetTypeOid` provide local tuple edits,
  including type changes and typed NULL, while retaining original result metadata. `Spi.QuoteIdentifier`,
  `QuoteQualifiedIdentifier`, and `QuoteLiteral` use PostgreSQL's native rules and encoding. `Spi.Explain`
  and `SpiSession.Explain` return owned JSON plans with typed parameters and single-statement validation.
- Scoped parameter conversion and quotation now use disposable per-operation native memory contexts.
  A regression reproduced 100 retained `CurTransactionContext` instances after 100 calls before the fix;
  both paths now retain zero additional transaction contexts before the caller's transaction ends.
- IDE1006 is an error during builds. A negative build verified field-prefix violations are rejected;
  the corrected runtime and the full solution pass with naming enforcement enabled.
- `PgException` transports SQLSTATE, full message/detail/hint/context, object names, query positions/text,
  source file/line/routine, server-only detail, and backtrace. Owned UTF-8 buffers preserve long diagnostics
  and null/empty distinctions. Native rethrow preserves context without replaying callbacks; tests verify
  recursive propagation, retained exceptions across transactions, actual constraint errors, LATIN1 conversion,
  conversion-failure cleanup, and repeated error-context cleanup. A bounded primary-message fallback remains
  available if diagnostic allocation/encoding fails.
- `tests/Ankus.TestExtension` supplies backend test functions. The fixture publishes and installs
  this separate extension alongside the minimal sample, exercising multiple AOT libraries in one backend.
- Backend test functions run in individual rollback-only transactions. Tests prove rollback
  after success, failure, and cancellation; expected-error matching; retained session logs;
  independent concurrent clusters; failed-start cleanup; and idempotent shutdown.
- Normal process exit attempts shutdown. Forced process termination cannot guarantee cleanup.
- Windows and macOS code paths are implemented but have not yet been executed on those hosts.
- One full-suite attempt failed during fixture publishing with MSB4166 (an MSBuild child exited prematurely).
  The reported diagnostic directory was unavailable. A subsequent plain `dotnet test` run passed all 267 cases;
  the build-worker failure's cause is undetermined.

### Error diagnostic API evidence

Reference surface: `pgrx-pg-sys/src/submodules/{panic,ffi,pg_try,elog}.rs` and PostgreSQL
`src/include/utils/elog.h` / `src/backend/utils/error/elog.c`. The error-level behavior below is
verified on PostgreSQL 18.6 / Linux x64; remaining platform/version validation is required.

| Source behavior | Ankus API/implementation | Concrete test evidence |
|---|---|---|
| ErrorData message/detail/hint and object names | `PgException` primary diagnostics, `SchemaName`, `TableName`, `ColumnName`, `DataTypeName`, `ConstraintName` | `PgDiagnosticTests.NativeObjectDiagnosticsSurviveCatchAndRethrow`, `ConstraintViolationPreservesCatalogMetadata` |
| ErrorData cursor/internal positions and query | `Position`, `InternalPosition`, `InternalQuery` | `PgDiagnosticTests.SyntaxPositionPreservesUnicodeQueryAndOriginalLocation` |
| ErrorReport location and native context | `File`, `Line`, `Routine`, `Context`; `NativeErrorBridge` rethrow | `PgDiagnosticTests.RecursiveRethrowDoesNotDuplicateContextFrames`, `NativeObjectDiagnosticsSurviveCatchAndRethrow` |
| Server-only detail and backtrace | `DetailLog`, `Backtrace` | `PgDiagnosticTests.ServerOnlyDiagnosticsRemainSeparateFromClientDetail` |
| Long/optional text ownership | Allocator-specific diagnostic buffers | `PgDiagnosticTests.LongNativeDiagnosticsAreNotTruncated`, `ManagedDiagnosticsReachClientWithoutTruncation`, `EmptyNativeDiagnosticsRemainPresent` |
| Encoding, secondary failures, and recovery | Server encoding conversion, `DiagnosticsIncomplete`, emergency message, temporary error context | `PgDiagnosticTests.Latin1DiagnosticsAndEncodingFailurePreserveBackend`, `RepeatedFailuresReleaseDiagnosticContexts`; `NativeDiagnosticTests.InvalidSecondaryDiagnosticUsesPartialFallback`, `BrokenMessageProducesEmergencyDiagnostic` |
| DEBUG5–DEBUG1, LOG, LOG_SERVER_ONLY, INFO, NOTICE, WARNING | `PgLog.Write`, `PgLogLevel`, native severity mapping | `PgLogTests.NonterminalLevelsUsePostgresRouting` |
| Message filtering and LOG/INFO ordering | `PgLog.IsEnabled`, server and client thresholds | `PgLogTests.FilteringMatchesClientAndServerRules` |
| Structured nonterminal reporting and server-only detail | `PgDiagnostic`, shared diagnostic transport | `PgLogTests.StructuredNoticePreservesFieldsAndLongUnicode` |
| ERROR catch and managed unwinding | `PgException`, generated native reporting boundary | `PgLogTests.ErrorUnwindsAndRemainsCatchable` |
| FATAL and PANIC | Internal terminal report exception, native termination after managed unwind | `PgLogTests.TerminalLevelsUnwindBeforeNativeTermination` (isolated clusters; peer survival for FATAL, crash recovery for PANIC) |
| Reporting validation, encoding and temporary memory | Backend-thread checks, guarded `ThrowErrorData`, disposable native context | `PgLogTests.InvalidReportsPreserveBackend`, `Latin1ReportingRecoversFromUnrepresentableText`, `ReportingReclaimsOperationContexts` |

### Scoped SPI API evidence

Reference surface: `pgrx/src/spi.rs` (`connect`, `connect_mut`), `spi/client.rs`
(`prepare`, query/cursor operations), and `spi/query.rs` (`PreparedStatement`, `keep`).
Native lifecycle references are PostgreSQL `executor/spi.c` and `utils/cache/plancache.c`.

| Source behavior | Ankus API/implementation | Concrete test evidence |
|---|---|---|
| Scoped connection, queries, and owned output | `Spi.Connect`, `SpiSession.Execute/Query/ExecuteScalar` | `SpiSessionTests.SessionOperationsPreserveScopeAndResults` (`session_owned_result`, `session_writes`, `session_tuple_cleanup`) |
| Nested connection lifetimes and error recovery | Native session identity, saved resource owner/nesting level, dispatcher-depth checks | `SpiSessionTests.SessionOperationsPreserveScopeAndResults` (`session_nested`, `session_nested_recovery`, `session_recursive`) |
| Session-bound plans and escape rejection | `SpiSession.Prepare`, scope invalidation | `SpiSessionTests.ExpiredSessionOwnershipIsRejected` |
| Plan ownership transfer and explicit cleanup | `SpiPreparedStatement.Keep/Dispose` | `SpiSessionTests.KeptSessionPlanSurvivesTransactionEnd` (commit and rollback) |
| Plan invalidation and cursor lifetime | Session-owned saved plans; independent portal ownership | `SpiSessionTests.SessionOperationsPreserveScopeAndResults` (`session_replan`, `session_cursor`) |
| Failure, cancellation, and thread-affinity cleanup | Native scope cleanup and managed access guards | `SpiSessionTests.FailedCallbackClosesSessionAndPlans`, `CancellationClosesSessionAndPlans`, `WorkerThreadCannotAccessSession` |

Additional source mappings: `spi/tuple.rs` (`set_by_ordinal`, `set_by_name`, entry `oid`),
`spi.rs` (`quote_identifier`, `quote_qualified_identifier`, `quote_literal`, `explain_with_args`),
and PostgreSQL `access/transam/xact.c` (`AtSubCommit_Memory`).

| Source behavior | Ankus API/implementation | Concrete test evidence |
|---|---|---|
| Mutable tuple values and entry type OIDs | `SpiRow.Set`, `GetTypeOid`, `GetOrdinal`, named indexing | `SpiRowTests.ReplacementUpdatesOnlyTheSelectedRowAndCellType`, `TypedNullAndJsonNullKeepDistinctTypesAndValues`, `NameLookupIsOrdinalAndChoosesFirstDuplicate`, `InvalidEditsLeaveTheRowUnchanged`, `EmptyRowRejectsAllEdits`; `SpiHelperTests.RowEditsRemainLocalAfterSessionEnds` |
| Identifier, qualified identifier, and literal quoting | `Spi.QuoteIdentifier/QuoteQualifiedIdentifier/QuoteLiteral`; direct native helpers | `SpiHelperTests.IdentifierQuotingUsesServerRules`, `QualifiedIdentifiersPreserveComponentBoundaries`, `LiteralQuotingRoundTripsUnderBothEscapeSettings` |
| Quoting validation and server encoding | Managed input checks, native encoding under guard | `SpiHelperTests.QuotingValidationErrorsPreserveBackend`, `Latin1QuotationAndEncodingFailurePreserveBackend` |
| JSON EXPLAIN, parameters, and owned output | `Spi.Explain`, `SpiSession.Explain`; parser statement-count check | `SpiHelperTests.ExplainUsesTypedParametersAndOwnedJson`, `ExplainPlansWritesWithoutRunningThem`, `ExplainRejectsInvalidOrMultipleStatements` |
| Temporary buffer cleanup before transaction end | Disposable native operation context | `SpiHelperTests.HelperAndSessionBuffersDoNotAccumulate` (quotation and session parameters) |

### UUID and JSON API evidence

Reference surface: `pgrx/src/datum/{uuid,json}.rs` (`Uuid`, `Json`, `JsonB`, `JsonString`,
`FromDatum`, `IntoDatum`, serialization), PostgreSQL `utils/uuid.h`, and native `json_in`,
`jsonb_in`, `jsonb_out`. Verified on PostgreSQL 18.6 / Linux x64.

| Source behavior | Ankus API/implementation | Concrete test evidence |
|---|---|---|
| UUID bytes and SQL representation | `Guid`, `NativeValue.ReadGuid/FromGuid`, native `pg_uuid_t` | `ExtendedDatumTests.UuidUsesNetworkByteOrder` verifies both directions independently |
| JSON text and JSONB normalization, numeric precision, NULL | `PgJson`, `PgJsonb`, native input/output routines | `ExtendedDatumTests.ExtendedTypesSurviveEverySpiPath`, `JsonValuesPreserveTypeSemantics` |
| Typed SPI parameters/results, domain base conversion, ownership | `SpiType`, shared typed buffer helpers | `ExtendedDatumTests.ExtendedTypesSurviveEverySpiPath`, `DomainResultsResolveBaseTypesAndOwnTheirValues` |
| Managed parsing, equality, and validation | Owned text, independent DOM, ordinal equality/hash | `PgJsonTests.ValidTextPreservesSpellingAndOwnership`, `InvalidSyntaxIsRejected`, `NullAndInvalidUtf16AreRejected`, `DefaultNullAndTextEqualityHaveConsistentHashes` |
| Serialization into application contracts | `Serialize/Deserialize` with `JsonTypeInfo<T>`, static wrapper converters | `ExtendedDatumTests.SourceGeneratedJsonContractsRunInsideNativeAot` |
| Packed/TOAST/deep/encoded JSON | Native detoasting and encoding; managed depth configuration | `ExtendedDatumTests.PackedJsonValuesUseCorrectVarlenaLayout`, `ToastedJsonValuesRetainExactContent`, `DeepJsonSurvivesNativeAndManagedBoundaries`, `Latin1JsonConversionPreservesTextAndNativeErrors` |
| Native conversion errors and recovery | Native output cleanup and guarded SPI parameter conversion | `ExtendedDatumTests.JsonFailuresPreserveTheBackend`, `JsonbParameterErrorsRecoverWithinSession` |

### Temporal API evidence

Reference surface: `pgrx/src/datetime/`; PostgreSQL `datatype/timestamp.h`, `utils/date.h`, and
`utils/timestamp.h`; .NET `DateOnly`, `TimeOnly`, `DateTime`, `DateTimeOffset`, and `TimeSpan` contracts.
Verified on PostgreSQL 18.6 / Linux x64. Field/epoch accessors, named-zone timetz construction and offset lookup,
modular/saturating raw factories, interval zone conversion and the timeofday text helper are implemented below.
Other server versions/platforms remain unvalidated. PG13 date extraction uses its narrower timestamp cast;
PG14+ uses native date extraction. Managed interval equality remains component-based; Sign uses PostgreSQL's
comparison approximation and Abs takes the checked absolute value of each component.

| Source behavior | Ankus API/implementation | Concrete test evidence |
|---|---|---|
| Full finite ranges, BC dates, infinities, end of day, SQL NULL | Six `Pg*` temporal structs; shared native datum helpers | `TemporalDatumTests.TemporalStorageSurvivesEveryPath` (eight function/SPI ownership paths, binary send comparison) |
| Epoch, microseconds, offset sign and second resolution, interval field order | Field-wise `NativeValue` transport and PostgreSQL header macros | `NativeInputUsesPostgresEpochAndIndependentFields`, `ManagedConstructionMatchesNativeStorage` |
| Idiomatic .NET inputs/results without lossy conversions | Generated adapters, `SpiTemporal`, exact precision/kind/range checks | `DotnetTemporalTypesWorkInsideNativeAot`, `UnrepresentableTemporalValuesUnwindSafely`; `PgTemporalTests` |
| Timezone/display independence, calendar day versus elapsed hours | UTC instant transport and separate interval components | `SessionSettingsDoNotChangeStoredTemporalValues`, `CalendarDaysRemainDistinctFromElapsedHours` |
| Interval infinity and version-gated native writes | Explicit discriminator separate from finite component combinations; PG17+ macros | `IntervalInfinityIsExplicitAndNativeErrorsRecover`; pre-PG17 guard is implemented but not yet executed |
| Domain conversion and owned result storage | Base-OID resolution and copied scalar fields | `TemporalDomainsRemainOwnedAfterSpiCleanup` |
| Conversion failures, prior-write preservation, native cleanup | Existing managed unwind/native subtransaction boundaries | `UnrepresentableTemporalValuesUnwindSafely`, `IntervalInfinityIsExplicitAndNativeErrorsRecover`, `SessionSettingsDoNotChangeStoredTemporalValues` |
| Shared SQL signatures for .NET and full-range aliases | `FunctionType` and duplicate signature diagnostics | `PgFunctionGeneratorTests.ClrAliasesShareSqlSignatures`, `SupportedFunctionsCompile` |
| Text input, special values, session DateStyle/IntervalStyle, ISO output | `Parse`, `TryParse`, `ToPostgresString`, `ToIsoString` | `TemporalOperationTests.ParsingAndFormattingHonorServerSettings`, `TryParseDistinguishesInvalidInputFromValidValues`, `TryParseRejectsInvalidManagedEncoding` |
| Calendar arithmetic, age, extraction, truncation, justify and interval scaling | Allowlisted `NativeTemporalOperations`, `PgDateTimePart`, typed runtime methods | `TemporalOperationTests.TemporalMethodsMatchServerSemantics` compares independently written SQL expressions, including month-end, BC, full-range date fields, and NULL/infinity results |
| Named timezone conversion and DST gap/overlap resolution | `AtTimeZone`, `PgTimestampTz.Truncate(part, zone)` | `TemporalMethodsMatchServerSemantics`, `ConstructorsClocksAndSessionsUseNativeSemantics` (explicit New York midnight is 05:00 UTC on the spring transition date) |
| Native field constructors and PostgreSQL transaction/statement/wall clocks | `PgDate.Create`, `PgTime.Create`, three clock properties, `FromUnixTimeSeconds` | `ConstructorsClocksAndSessionsUseNativeSemantics` verifies BC leap day, 24:00, Unix microseconds, server clock identity/order and plan survival |
| Backend-independent ordering and interval comparison distinction | `IComparable<T>`, relational operators, `CompareInPostgres` | `PgTemporalTests.TemporalOrderingWorksWithoutBackendAccess`, `OffsetTimeOrderingMatchesPostgresTieBreaking`; SQL comparator cases in `TemporalMethodsMatchServerSemantics` |
| Invalid input, unsupported units, range errors, preserved writes and cleanup | Native guarded subtransactions, managed TryParse filters, owned scalar transport | `TemporalErrorsPreserveWritesAndManagedUnwinding`, `ConstructorsClocksAndSessionsUseNativeSemantics` (100 success/failure cycles, zero additional contexts), `PgTemporalTests.TemporalParsingValidatesTextAndPreservesAccessErrors` |
| Exact numeric field extraction on PostgreSQL 14+ | `Extract(PgDateTimePart)` on all six temporal types, native extract routines | `NumericTests.TemporalExtractionPreservesNumericPrecision` verifies high-range epoch/fractional fields, offsets, interval components, and infinity/NULL. PG13's floating-point fallback remains unexecuted |
| Multi-field timestamp/offset/interval constructors and unit factories | `Create`, interval `FromYears` through `FromMicroseconds`; native scalar dispatcher supports seven typed arguments | `TemporalConvenienceTests.TemporalFactoriesMatchSql` covers BC leap day, timestamp maximum, 24:00, DST gaps/overlaps, mixed interval signs and exact Int64 microseconds |
| Arithmetic operator families and commuted overloads | Operators delegate to guarded temporal routines | `TemporalOperatorsMatchSql` checks every operator route against independent SQL, including month-end and daylight-saving differences |
| Precision modifiers and current/local clocks | `Round(0..6)`, `CurrentDate`, `GetCurrentTime/GetLocalTime/GetCurrentTimestamp/GetLocalTimestamp` | `PrecisionRoundingMatchesNativeModifiers` checks before/at/after positive and negative timestamp ties, rollover and infinities; `CurrentClocksMatchSqlWithinTransaction` checks SQL identity at precisions 0/3/6 |
| Explicit-zone ISO without altering session state | Native zone conversion and ISO encoder, offset derived at the stored instant | `ExplicitZoneIsoUsesInstantOffset` checks seasonal and overlapping offsets, historical seconds, BC and infinities; `CurrentClocksMatchSqlWithinTransaction` verifies TimeZone and DateStyle |
| Exact interval units, comparison duration/sign and component absolute value | `FromMicroseconds`, `ToComparisonMicroseconds`, `Sign`, `Abs` | `IntervalSignMatchesPostgresComparison`; `PgTemporalConvenienceTests.IntervalComponentConveniencesPreserveExactStorage` checks cancellation, signed limits, overflow, and Int128 range |
| New constructor/round/format failures and cleanup | Guarded operations and explicit rounded-timestamp finite-range check | `TemporalConvenienceErrorsPreserveState` checks SQLSTATE, 50 finally executions, zero context growth, active plan and prior-write survival; `TemporalPrecisionRejectsInvalidDigits` checks invalid managed precision |
| Native AOT temporal JSON contracts | Six statically registered `JsonConverter<T>` implementations and source-generated metadata | `ScalarJsonTests.ScalarJsonPreservesFullRangeAndScale`, `IntervalJsonHonorsStyleAndRetainsComponents` compare text and binary values; `ScalarJsonFailuresPreservePathsAndBackend` checks paths, inner SQLSTATE, finally and cleanup; `ScalarConvertersPreserveBackendAccessErrors` checks detached access |

### Temporal field, raw-value and timezone parity

Completed the remaining safe temporal conveniences against the local read-only pgrx datetime
surface and PostgreSQL's native calendar/timezone routines:

- Detached full-range date/time/timestamp fields and named tuples, including BC years without year zero,
  fractional-only microseconds, signed offset components and finite epoch conversions. Timestamptz
  fields use native session-zone extraction directly, preserving endpoint instants whose local timestamp
  cast would overflow. Both infinity predicates and explicit finite-field rejection are available.
- Saturating date/timestamp/timestamptz raw factories and Euclidean time/raw-timetz wrapping. Raw timetz
  explicitly accepts PostgreSQL seconds west of UTC; ordinary checked constructors retain seconds east.
- `PgTimeZone.GetOffset` resolves server names/abbreviations/POSIX zones at transaction start or an explicit
  finite instant. Named timetz construction attaches the offset without shifting supplied clock fields.
  Interval `AtTimeZone` overloads use native conversion, and detached `ToUtc` conveniences preserve exact values.
- PostgreSQL `timeofday()` returns owned live clock text. Explicit record `ToString` implementations retain
  detached diagnostic formatting without invoking new finite/local field accessors.

The new scalar operations use the existing native guard and owned result/diagnostic transport. The timezone
helper follows the PG13–15 versus PG16+ native decoder signatures; declarations were checked against local
PG13–19 bindings, which is compatibility intent, not execution evidence. Offset lookup can resolve wider
POSIX offsets than the existing checked timetz type; named construction and converted results reject offsets
outside that type's strict ±16-hour bound. Fractional interval-zone offsets follow PostgreSQL's whole-second
truncation. Existing GetPart/Extract(Microseconds) remains second-inclusive, unlike MicrosecondsWithinSecond.

| Contract | Concrete evidence |
|---|---|
| Full-range Gregorian/epoch values and negative-epoch floor division | `PgTemporalFieldsTests.DateFieldsAndEpochsPreserveFullRange`, `TimestampFieldsUseFloorDivision`; backend `DateFieldsMatchPostgresAcrossFullRange`, `TimestampFieldsMatchPostgres` compare independent SQL extraction |
| Local endpoint fields, BC, seasonal/DST and historical seconds | `TimestampFieldsFollowSessionZoneAtFiniteEndpoints`, including actual failing local timestamp casts beside successful native field reads |
| Whole/fractional seconds, 24:00, offset fields and UTC wrapping | `TimeFieldsPreserveEndOfDayAndFractions`, `OffsetFieldsAndUtcPreserveSeconds`, `TimeFieldsMatchPostgresIncludingEndOfDay`, `UtcConveniencesMatchNativeValues` |
| Saturation, raw Euclidean modulo and unchanged checked constructor bounds | `RawDateSaturationPreservesBoundaries`, `RawTimestampSaturationPreservesBoundaries`, `RawTimeWrappingUsesEuclideanRemainders`, `RawOffsetWrappingUsesPostgresWestConvention` use independent literals, adjacent boundaries and signed extremes; `RawTemporalFactoriesMatchNativeBinaryValues` compares native bytes |
| Native timezone identity and clock preservation | `NamedZoneOffsetsUseSpecifiedInstant`, `NamedZoneOffsetsUseTransactionStart`, `NamedZoneOffsetsSupportFiniteEndpoints`, `NamedZoneTimeConstructionPreservesWallClock`, `IntervalZoneConversionsMatchPostgres` |
| Owned live clock text and unchanged settings | `TimeOfDayReturnsOwnedServerClockText` brackets parsed server text with native clock samples after further native allocations and managed GC |
| Errors and cleanup | `TemporalParityErrorsPreserveState` checks exact SQLSTATE/managed exception, 50 failures/finally/successes, zero context growth, prepared plan and prior-write survival; direct `PgTimeZoneTests` check detached validation/access |
| Safe diagnostics and infinities | `DiagnosticStringsDoNotRequireBackendOrFiniteValues`, `InfiniteDateFieldsAndEpochsAreRejected`, `InfiniteTimestampFieldsAreRejected`, `ZonedTimestampFieldsRequireFiniteBackendContext` |

Focused validation: 115 runtime cases and 148 PostgreSQL cases passed; the strengthened same-transaction
clock check passed all three cases. The non-incremental Release build had zero warnings/errors. Site build/check
passed (89 API pages/1033 members, 116 site pages); the XML scan found no omissions in 678 internal declarations.
Plain `dotnet test` passed **3172 cases, zero failures/skips**, including the final repeated-clock oracle
and isolated package consumers. API freshness checking passed. Existing site warnings for the duplicate 404 route
and missing public site URL remain visible. No warnings were disabled or lowered. Backend evidence is
PostgreSQL 18.6 / Linux x64. Updated the public temporal guide, README and generated API.
Research/planning/static source pairing and final assertion/gap reviews are recorded in
nonstageable `.git/testagent/temporal-parity/`; no empirical mutation or coverage percentage is claimed.
Full raw bindings and the complete PostgreSQL/platform matrix remain required full-port work.

### Numeric API evidence

Reference surface: `pgrx/src/datum/{numeric.rs,numeric_support/}` and PostgreSQL
`src/backend/utils/adt/numeric.c`. The decimal conversion guard accounts for the documented
round-to-nearest behavior of `Decimal.Parse/TryParse` when an input exceeds decimal precision;
a successful parse alone does not establish an exact conversion.
Verified on PostgreSQL 18.6 / Linux x64, including declarative precision/scale constraints, primitive casts,
generic checked integer conversions, mixed arithmetic and summation. Other server versions/platforms remain
unvalidated. Exact .NET integer narrowing rejects fractions and signed-to-unsigned overflow; PostgreSQL-style
integer casts are exposed separately as `ToInt16`, `ToInt32`, and `ToInt64`.

| Source behavior | Ankus API/implementation | Concrete test evidence |
|---|---|---|
| Full numeric range, scale, NULL, NaN and infinities | `PgNumeric`, owned canonical numeric text, guarded numeric input/output | `NumericTests.NumericStorageSurvivesEveryPath`, `FullRangeParsingAndScaleStayExact` (131072 integer digits and 16383 fractional places) |
| Ordinary .NET decimal function and SPI support | Generated decimal adapters, numeric OID mapping, `SpiRow` exact conversions | `DecimalAdaptersRemainExactAcrossSpi`, `GeneratedDecimalAdaptersRejectLossyInput`; `PgNumericTests.DecimalConversionsPreserveValueAndScale`, `UnrepresentableDecimalsAreRejected`, `RowConversionsUseExactNumericSemantics` |
| Arithmetic, rounding, roots/logarithms/powers, GCD/LCM and rescaling | Numeric operators/methods; guarded native routines; server typmod input with cstring[] | `ArithmeticMatchesPostgresNumericSemantics` compares native binary output, including scale, negative scale and scale above precision |
| Scale-independent comparison/hash and special-value order | Managed normalized-span equality/comparison/hash | `ManagedComparisonMatchesNativeValues`; `PgNumericTests.EqualityAndHashesIgnoreDisplayScale`, `OrderingMatchesNumericMagnitudeAndSpecialValues` |
| Arbitrary-precision integer conversion and exact decimal narrowing | `FromBigInteger/ToBigInteger`, `FromDecimal/ToDecimal` | `PgNumericTests.BigIntegersAndRedundantFractionalZerosRemainExact` verifies the integer digit limit and adjacent invalid magnitudes |
| Packed headers, compressed/external TOAST and owned domain cells | Native detoasting, allocated numeric output, copied managed text and base-OID conversion | `StoredNumericPayloadsSurviveDetoasting` verifies storage conditions and exact text; `NumericDomainsConversionsAndCleanupRemainOwned` |
| Error recovery, prior-write preservation, managed finally and cleanup | Shared guarded scalar boundary, disposable operation/diagnostic contexts | `NumericErrorsPreserveSessionState`, `NumericDomainsConversionsAndCleanupRemainOwned` (100 successful/failing operations, zero extra contexts and live prepared plan) |
| Compile-time alias handling and Native AOT dispatch | `FunctionType`, numeric buffers, shared scalar signature dispatcher | `PgFunctionGeneratorTests.SupportedFunctionsCompile`, `ClrAliasesShareSqlSignatures`; all numeric integration cases publish and load the actual Native AOT extension |
| Lossless numeric JSON strings and exact unquoted input | Statically registered `PgNumericConverter`; raw number text passed to PostgreSQL | `ScalarJsonPreservesFullRangeAndScale`, `ScalarJsonNumbersAndNullsUseExactContracts`, `ScalarJsonFailuresPreservePathsAndBackend`; `ScalarConvertersPreserveBackendAccessErrors` checks detached numeric writes and backend-only reads |
| Numeric precision and scale declarations | `PgNumericPrecisionAttribute`, semantic validation and generated guarded `Rescale` calls; SQL signatures remain unconstrained numeric | `NumericContractTests.NumericBoundariesMatchPostgresTypmods`, `DecimalConstraintsAndNullableValuesKeepTheirContracts`, `ExtendedScalesMatchPostgres`, `ConstraintOverflowLeavesBackendUsable`; `PgFunctionGeneratorTests.InvalidNumericConstraintsAreRejected`, `NumericConstraintsDoNotCreateSqlOverloads` |
| Primitive conversions using PostgreSQL routines | Native `float4_numeric`, `numeric_float4`, `numeric_int2/int4/int8`; explicit floating-point operators | `NumericContractTests.PrimitiveCastsMatchServer` compares signed integer results and floating-point bits, including subnormals and special values; `SinglePrecisionInputUsesServerPrecision` compares numeric binary output |
| Exact generic integer conversion and primitive operators | `FromInteger<T>/ToInteger<T>` with `IBinaryInteger<T>`, checked BigInteger conversion; exact implicit integer/decimal operators | `PgNumericTests.GenericIntegerConversionsAreExactAndChecked` tests all 12 fixed/native integer widths at and just beyond bounds; `PrimitiveConversionOperatorsPreserveValues`; `NumericContractTests.GenericIntegersExecuteInNativeAot` covers 13 closed generic integer types |
| Generic arithmetic and sequence summation | Fine-grained .NET operator/identity interfaces; `PgNumeric.Sum` | `NumericContractTests.GenericArithmeticPreservesScale`; `PgNumericTests.SumPreservesSingletonScaleAndDisposesOnFailure` checks empty/singleton behavior and iterator cleanup |
| Generated constraint/cast failure recovery | Shared native guard and existing exception transport | `NumericContractTests.NumericContractFailuresPreserveSession` repeats five failure routes 50 times each; input failures never invoke user code, return failures follow user finally, outer finally always runs, writes/plans survive and native context growth is zero |

### NuGet consumer evidence

Verified with .NET SDK 10.0.400, PostgreSQL 18.6 and Linux x64. `dotnet pack -c Release -o artifacts/packages`
produces six independently consumable packages. Public publication and other platforms remain pending.
All consumer tests live in `ToolCommandTests`; consumer projects and their initially empty NuGet cache are outside
the repository, with spaces in their paths. The external MSTest project contributes one additional passing test
inside the host integration case.

| Requirement | Implementation | Concrete test evidence |
|---|---|---|
| Extension-author SDK with automatic runtime, generator and native-build integration | `Ankus.Sdk` MSBuildSdk package, implicit version-matched references, embedded `Ankus.Build` tool | `SdkRestoresWithoutRepositoryReferences` checks package-only assets, zero project references, analyzer/helper paths and AOT settings; `PublishedAndInstalledExtensionExecutesInPostgres` loads the resulting native library |
| Direct .NET publishing and central version management | `Sdk.props`/`Sdk.targets`, CPM-compatible implicit references, native artifact targets | `SdkSupportsDirectPublishWithCentralPackages` uses `global.json` SDK selection and `Directory.Packages.props`, then verifies the SQL result and extension version |
| Native-only publish contract | SDK validation before publishing | `SdkRejectsNonExtensionPublishSettings` rejects disabled AOT and static-library output without an installable manifest |
| Reusable testing package and normal discovery | Packed `Ankus.Testing` with PgConfig/Npgsql dependencies | `TestingPackageRunsInIndependentMSTestProject` runs ordinary `dotnet test`; its TRX proves the discovered `PackagedClusterLoadsNativeExtension` passed, including checked-overflow SQLSTATE and same-connection recovery |
| Installed .NET tool, staging and artifact consistency | Packed `Ankus.Tool`, registry, publish driver and installer | Existing 23 tool cases now use the packaged SDK; installed payload bytes, SQL results, invalid-artifact rejection and failed-build manifest invalidation remain verified |
| Package-based project creation and normal test discovery | `ankus new`, bundled solution/source templates, matching SDK/testing versions, CPM and MSTest/MTP global.json | `ToolCommandTests.NewSolutionRunsManagedAndBackendTests` creates an external solution, discovers five managed/native tests, publishes from the solution root, and proves a native function change fails its SQL assertion |
| Portable project names and preservation of user files | One-pass template expansion, escaped keyword namespaces, validated SQL names, atomic directory move | `NewSolutionSupportsKeywordsAndExplicitNames`, `NewRejectsInvalidNamesWithoutFiles`, `NewPreservesExistingFiles`, `NewSolutionWithMultipleExtensionsRequiresSelection` |
| Reusable publish/load fixture and initialization cleanup | `PostgresExtensionTest`, selected pg_config forwarding, native manifest check, per-cluster search paths and asynchronous disposal | `NewSolutionRunsManagedAndBackendTests` verifies five passing tests and cleanup after a SQL assertion failure; `NewSolutionReportsInitializationFailuresAndCleansUp` verifies Release-only compile errors and CREATE EXTENSION division-by-zero failures, zero leftover cluster/publish directories and retained binlogs |
| NuGet cache paths with spaces | Ordered quoting of native file arguments before the Unix linker | Both publish tests use an isolated cache path containing spaces; this reproduced an unquoted .NET 10 Native AOT library-path failure before the fix |

### Array API evidence

References: `pgrx/src/datum/array.rs` (`Array`, `VariadicArray`, iteration and NULL handling),
`pgrx/src/array/`, and PostgreSQL `utils/adt/{arrayfuncs,arrayutils}.c`. These cases run on PostgreSQL 18.6/Linux x64.
The public owned-array API is implemented; raw borrowed array views, polymorphic arrays and arrays of future
custom/composite types remain part of the wider port; generated enum elements are implemented below.

| Requirement | Implementation | Concrete test evidence |
|---|---|---|
| Scalar element families, NULL arrays/elements, empty arrays, all SPI owners | Closed `SpiArray` type mappings, `NativeArray`, `NativeArrayBridge`, generated wrappers | `ArrayDatumTests.ArraysPreserveBinaryValuesAcrossOwners` compares `array_send` for 20 scalar families across eight ownership paths; `VectorsAndDotnetElementsUseExactConversions` covers ordinary .NET adapters |
| Dimensions, lower bounds, six-dimensional limit, independent input/output | `PgArray<T>` constructors, `GetValue`, flat indexing, enumeration and explicit flattening | `PgArrayTests.ShapeOwnsInputsAndUsesPostgresSubscripts`, `VectorAndEmptySemanticsAreExplicit`, `ShapeValidationRejectsOnlyInvalidBoundsAndCounts`; `ArrayDatumTests.ShapeAndIndexingMatchPostgresSubscripts` |
| Shape/NULL/precision loss rejection | `ToVector`, per-element scalar conversion | `PgArrayTests.TypedRowsRejectNullAndPrecisionLoss`; `ArrayDatumTests.LossyArrayConversionsRaiseManagedErrors` |
| Binary elements, embedded zeroes, independent wire contract | Length-delimited bytea transport; big-endian field headers | `PgArrayTests.NativeTransportHasExpectedIndependentLayout`, `NativeReaderAcceptsIndependentWireValues`; `ArrayDatumTests.BinaryArrayOutputPreservesEmbeddedZeroBytes` |
| Buffer ownership and malformed input | Typed materialization, shape/count/buffer checks, one owned native buffer | `PgArrayTests.BufferedElementsOutliveNativeTransport`, `MalformedArrayTransportIsRejected`, `MalformedArrayEnvelopesAreRejected` |
| Domains, text aliases, compressed/external storage | Base-type resolution, server detoasting and metadata | `ArrayDatumTests.DomainAndTextAliasArraysRemainOwned`, `ToastedArrayPayloadsSurviveNativeCleanup` |
| Encoding and native failure cleanup | Per-element scalar conversions in native frames; output release in `PG_FINALLY` | `ArrayDatumTests.Latin1ArraysConvertElementsAndRecoverFromOutputFailure`; `ArrayFailuresPreserveWritesPlansAndCleanup` verifies prior writes, prepared plan, 30 managed unwinds and zero retained operation contexts |
| SQL variadics and declaration validation | C# `params T[]`; generator rejects scalar-byte and unsupported params signatures | `ArrayDatumTests.ParamsArraysDeclareSqlVariadicFunctions` verifies dispatch, explicit empty/NULL arrays, strictness and `provariadic`; `PgFunctionGeneratorTests.SupportedFunctionsCompile`, `UnsupportedSignaturesAreRejected`, `ClrAliasesShareSqlSignatures` |
| Package consumption | Packed SDK/runtime/generator, CPM and cold package-only restore | `ToolCommandTests.SdkSupportsDirectPublishWithCentralPackages` publishes and executes shaped SPI arrays and variadic binary arrays outside the checkout |

### Function declaration evidence

References: `pgrx-sql-entity-graph/src/{extern_args.rs,pg_extern/,schema/}`, `pgrx-macros/src/lib.rs`,
and PostgreSQL `commands/{functioncmds,extension,schemacmds}.c`. Verified on PostgreSQL 18.6/Linux x64.
This milestone adds 28 generator cases and 27 backend/package cases. Entity dependency ordering, custom/disabled
SQL, polymorphic/raw signatures, and schema support for future type families remain pending.

| Requirement | Implementation | Concrete test evidence |
|---|---|---|
| Function volatility, parallel modes, strictness, security, cost, leakproofness, support | `PgFunctionAttribute`, typed enums, `FunctionDeclaration` SQL emission | `FunctionDeclarationTests.CatalogRetainsPlannerAndArgumentContracts` reads `pg_proc`; `SqlDispatchHonorsDeclarations` checks explicit STRICT and called-on-NULL dispatch and supported prefix execution |
| Named arguments and exact C# optional constants | `PgParameterAttribute`, snake-case argument names, `ParameterDefault` | `SqlDispatchHonorsDeclarations` checks named/reordered/omitted arguments, decimal scale, NULL versus zero, signed minima, uint maximum, NaN and independent negative-zero/char binary output |
| Server-evaluated defaults and variadic defaults | Explicit SQL expressions override C# constants; all following input arguments require defaults | `ServerDefaultsAndScopedSettingsUseTheCallingSession` checks `current_date` in a non-UTC zone and a named override; `SqlDispatchHonorsDeclarations` checks empty/default and expanded variadic calls |
| Value-type defaults and encoding | Explicit epoch/default mappings and PostgreSQL E-string literals | `SqlDispatchHonorsDeclarations` checks Guid, PgDate/DateOnly, timestamp/timetz, PgNumeric, PgJson and interval defaults; `FixedSchemasParticipateInExtensionLifecycle` reinstalls under `standard_conforming_strings=off` and checks escaped Unicode text |
| Fixed, inherited, overridden and standalone schemas | `PgSchema`, `Create=false`, per-function `Schema`, schema-first SQL | `PgFunctionGeneratorTests.SchemaInheritanceAndOverridesUseQualifiedSignatures`, `EmptySchemasHaveValidatedStandaloneMetadata`, `ExistingSchemasHaveNoCreationStatement`; backend nested/quoted/Unicode/public schema calls |
| Schema ownership and relocation | `Ankus.Relocatable` metadata read without executing assemblies; control-file flag | `FixedSchemasParticipateInExtensionLifecycle` checks `pg_depend`, rejected relocation, rejected adoption of an unrelated schema, uninstall/reinstall and survival of shared public schema |
| Scoped search paths and execution identity | Native PostgreSQL function configuration/security clauses; unchanged guarded callback ABI | `ServerDefaultsAndScopedSettingsUseTheCallingSession`, `EmptySearchPathUsesNativeSettingSemantics`, `SecurityModeControlsPrivilegesAndRestoresContext` check restored path/role and owner-only access versus permission failure |
| CREATE OR REPLACE semantics | Generated replacement DDL | `GeneratedReplacementPreservesDependentObjects` reapplies actual published SQL and verifies retained function OID and working dependent view |
| Compile-time diagnostics and identifier handling | `ANKUS004`, enum/cost/default-order/Unicode/UTF-8-length validation | `InvalidDeclarationOptionsAreRejected`, `EmptySchemasHaveValidatedStandaloneMetadata`, `FunctionDeclarationsPreserveOptionsAndConstants` check errors and compilable generated sources |
| Package-only and schema-only Native AOT consumption | Packaged SDK/runtime/generator; magic-only native source for schema-only assemblies | `ToolCommandTests.SdkSupportsDirectPublishWithCentralPackages` checks fixed-schema/default/named calls and control metadata; `SchemaOnlyPackageCreatesOwnedNamespace` publishes, explicitly loads the native library, and checks schema creation/removal |

Full-suite evidence: `artifacts/declarations-all-output.txt` (1119 passed, zero failures/skips).
The internal XML documentation scan remains clean: `artifacts/declarations-internal-docs.txt`.
`artifacts/declarations-schema-only-output.txt` verifies the added explicit native `LOAD` check.
The Release build, site build/type check, and API freshness check pass; see the corresponding
`artifacts/declarations-{build,docs-build,docs-check,api-check}-output.txt` files.

### Custom installation SQL evidence

References: `pgrx-sql-entity-graph/src/extension_sql/`, `pgrx-examples/custom_sql/`, and the pgrx SQL
entity graph. `PgSql`/`PgSqlFile` use assembly attributes; generated functions and schemas expose dependency
`Id`/`Requires` options. Microsoft Learn's `AdditionalTextsProvider` contract and the installed Roslyn APIs
provide tracked non-code inputs without runtime reflection or direct generator filesystem reads.

| Requirement | Implementation | Concrete test evidence |
|---|---|---|
| Inline SQL and ordering around generated declarations | `CustomSql`, shared `SqlEntity`/`SqlGraph`, automatic schema edges and explicit Requires/Before edges | `PgFunctionGeneratorTests.SqlGraphOrdersAllDeclarationKinds`; `CustomSqlTests.InstallationFollowsDeclaredDependencyOrder` records bootstrap/support/file/view/final order and executes a view calling a generated function with a SQL-created default routine |
| Stable output and verbatim SQL | Stable graph keys, dependency ordering, newline separation without SQL rewriting | `CustomSqlIsDeterministicAndPreservesStatementText` compares reordered source attributes and exact output; backend view verifies dollar-quoted semicolons, quotes, backslash and Unicode |
| Dependency aliases and shared schemas | One schema node with merged aliases/dependencies | `RepeatedSchemaDeclarationsShareOneGraphNode` requires both aliases and verifies exactly one schema creation |
| Missing/duplicate IDs, cycles and invalid boundaries | `ANKUS005`, named edge resolution, bootstrap/final edges and topological cycle detection | `InvalidSqlDependenciesAreDiagnosed` covers missing Requires/Before, self/transitive/schema-function cycles, duplicate function/SQL IDs, repeated bootstrap/final blocks, contradictory boundary edges, case-sensitive names and invalid inputs |
| File inputs and incremental invalidation | Roslyn AdditionalFiles, SDK CompilerVisibleProperty for MSBuildProjectDirectory, normalized registered paths | `SqlFilesUseTrackedProjectRelativeInputs`, `InvalidSqlFilesAreDiagnosed`, `SqlFileChangesInvalidateIncrementalOutput`; no source-code edits are made between the package consumer's successful republish cases |
| Extension ownership | PostgreSQL installation transaction and pg_depend registration | `CustomSqlTests.CustomObjectsAreExtensionMembers` checks custom table/view/function plus generated function membership |
| SQL-only Native AOT package and relocation | Magic-only native source, packed SDK/runtime/generator, per-block Relocatable opt-in | `ToolCommandTests.SqlFilePackageRebuildsAndRollsBackFailedInstallation` publishes outside the checkout, explicitly LOADs the library, checks changed SQL results, relocates table/view and verifies uninstall removal |
| Failed SQL installation cleanup | PostgreSQL transactional extension installation | The same package test introduces division by zero after CREATE TABLE, checks SQLSTATE 22012, and independently verifies absence of both the partial table and pg_extension entry |

Function, type/enum, aggregate and ordering/hash family SQL controls now expose
`Sql`, `GenerateSql`, and `SqlRelocatable`; the later SQL-generation entries record their
distinct ownership contracts and execution evidence. Declared custom-type providers,
future type-family edges, and standalone schema extraction remain required work. These tests establish the
implemented installation graph, not full parity with every pgrx SQL-entity feature.

Evidence: `artifacts/sql-graph-all-output.txt` records 1152 passing tests with zero failures/skips.
`sql-graph-{generator,backend,package}-output.txt` records the focused runs; `sql-graph-build-output.txt` records
the zero-warning Release build. Site build/type check, API freshness and internal XML scans are retained in
`artifacts/sql-graph-{docs-build,docs-check,api-check}-output.txt` and `artifacts/sql-graph-internal-docs.txt`.

### Network value evidence

References: `pgrx/src/datum/inet.rs`, PostgreSQL `src/backend/utils/adt/network.c` and `src/include/utils/inet.h`,
and Microsoft Learn's `IPNetwork(IPAddress, Int32)` constructor contract. Managed storage owns numeric address
bits; mutable `IPAddress` instances never back a `PgInet`. The native boundary normalizes PostgreSQL's socket-family
byte to a portable 4/6 marker and uses the selected backend's binary send/receive and text input routines.

| Requirement | Implementation | Concrete test evidence |
|---|---|---|
| IPv4/IPv6, prefixes, ownership and detached construction | `PgInet`, `PgCidr` | `PgNetworkTests.AddressesAreCopiedAndFamilyIsPreserved`, `NetworkMasksPreservePrefixAndFamily`, `ConstructionRejectsInformationLoss`; `NetworkDatumTests.NetworkConstructionHasCorrectWireBytes` checks independently constructed IPv4/IPv6 wire bytes and SQL defaults |
| Exact .NET address/network mapping | `IPAddress` maps to full-prefix inet, `IPNetwork` to cidr; checked scalar and element conversions | `HostMappingsRejectPrefixLoss` verifies scalar/vector callback and SPI-result narrowing failures; `NetworkOwnershipPathsPreserveValues` executes .NET scalar/vector conversions under Native AOT |
| Binary validation | Network transport header/length checks, native receive validation, zero-host-bit cidr constructors | `BinaryTransportRetainsNetworkBits`, `InvalidBinaryHeadersAreRejected`, `BinaryCidrRejectsHostBits` |
| Generated declarations and arrays | `FunctionType`, buffered `NativeValue` conversion, statically closed SPI array registry | `NetworkSignaturesCompile` compiles 12 scalar/nullable/array contracts and checks the SQL argument/result types |
| SPI lifetime paths and SQL NULLs | Owned network buffers through direct calls, queries, plans, sessions, cursors, retained plans and edited rows | `NetworkOwnershipPathsPreserveValues` verifies eight paths for each scalar/array input, including NULLs, zero-rank arrays, multidimensional arrays and negative/zero lower bounds |
| Packed, domain and TOAST input | Native network send/receive in existing buffer ownership scopes | `PackedNetworkStorageAndDomains`, `NetworkArrayToastingAndDomains` verify packed scalar storage, domains and compressed 10,000-element network arrays |
| PostgreSQL network semantics | Detached masks, prefix changes, subnet containment and network ordering | `NetworkOperationsMatchEveryPrefix` compares all legal IPv4/IPv6 prefixes with native SQL; `NetworkOrderingMatchesPostgres` compares 169 pairs including IPv4-mapped IPv6 |
| Backend parsing and recovery | Allowlisted network input operations in guarded subtransactions | `NetworkParsersMatchPostgres` includes abbreviated IPv4/cidr input; `NetworkInputFailureRecovery` verifies 50 finally executions, retained writes/plan and zero context growth after repeated native errors; `ParsingRequiresBackendAccess` checks detached access failure |
| AOT JSON | Static converter attributes and source-generated metadata | `NetworkJsonUsesAotMetadata` verifies prefix/family retention, canonical output and JSON error paths wrapping native SQLSTATE 22P02 |

Evidence: `artifacts/network-{runtime,generators,backend}-output.txt` records 21 detached, 12 generator and
40 backend cases. `artifacts/network-all-output.txt` records 1225 passing tests with no failures/skips; the
Release build in `artifacts/network-build-output.txt` has zero warnings/errors. The internal XML scan in
`artifacts/network-internal-docs.txt` reports zero omissions.
The documentation build, type check and API freshness outputs are retained as
`artifacts/network-{docs-build,docs-check,api-check}-output.txt`; 47 API pages document 665 members.

### Geometric value evidence

References: `pgrx/src/datum/geo.rs`, PostgreSQL `geo_ops.c` and `geo_decls.h`, and Microsoft Learn's readonly
record-struct/value-equality guidance. Fixed shapes fit in small immutable records; variable-length collections
copy their vertices rather than relying on shallow record immutability. Binary I/O avoids native layout assumptions.

| Requirement | Implementation | Concrete test evidence |
|---|---|---|
| All pgrx geometric datum families | `PgPoint`, `PgLine`, `PgLineSegment`, `PgBox`, `PgCircle`, `PgPath`, `PgPolygon` | `GeometrySignaturesCompile` compiles each type as required/nullable scalar, vector and shaped array (28 contracts) and checks exact SQL types |
| Owned vertices, order and closure | Copied collections, read-only point spans, `WithClosed` | `GeometricCollectionsOwnTheirVertices`, `EmptyAndSingletonGeometry`, `CollectionBinaryLayouts` verify input mutation isolation, indexing, closure and validity after buffer release |
| Exact coordinates and binary layout | Big-endian double protocol, fixed-length and point-count validation | `FixedGeometryBinaryLayouts`, `InvalidGeometryFramesAreRejected`; `GeometryConstructionUsesExactCoordinateBits` independently constructs NaN payload/signed-zero coordinates and checks server wire bytes for points, paths and polygons |
| PostgreSQL box normalization and polygon bounds | Total float ordering (NaN highest) and stable equal-coordinate handling | `BoundsUsePostgresFloatOrdering`, `GeometryBoundsMatchPostgres` compare corners/bounds, infinities, NaN, singleton vertices and signed zero with backend results |
| All SPI ownership paths, arrays and SQL NULL | Existing owned buffered datum/array channels extended for seven geometric OIDs | `GeometryOwnershipPathsPreserveBinaryValues` compares native binary sends through eight paths, including nullable scalars/elements, vector and multidimensional/lower-bound-preserving arrays |
| Empty pgrx collections | Native zero-vertex headers built from selected server `offsetof` values, preserving path closure and zero polygon bounds | `EmptyGeometricCollectionsRemainRepresentable` exchanges both empty path forms and empty polygons through all eight paths and inside arrays |
| Domain, compressed and external storage | Explicit detoast ownership before native binary send | `GeometryToastedStorageAndDomains` verifies 10,000-vertex compressed/external values, storage sizes, domain values and variable-element arrays |
| Native parsing and detached text | Allowlisted native input routines, invariant round-trip double formatting | `GeometryParsingMatchesPostgres`, `GeometryFormattingAndEqualityAreDetached`, `GeometryParsingRequiresBackend` |
| Input/output failure boundaries | PostgreSQL line/circle validation, native receive framing and guarded subtransactions | `InvalidGeometryOutputRaisesNativeError` checks SQLSTATE 22P03 after managed callbacks; `GeometryFailureRecoveryPreservesSession` verifies 50 finally runs, retained writes/plan and zero context growth after native text and binary failures |

PostgreSQL geometric predicates remain available through SPI; dedicated wrappers for the broader geometric
operation catalog are pending. Record equality follows exact .NET coordinate/coefficient semantics rather than
PostgreSQL's tolerance- or area-based predicates.

Evidence: `artifacts/geometry-{runtime,generators,backend}-output.txt` records 15 detached, seven generator
and 47 backend cases. `artifacts/geometry-all-output.txt` records 1294 passing tests with no failures/skips;
`artifacts/geometry-build-output.txt` records the zero-warning Release build. Internal XML documentation,
site build/type checks and API freshness pass in `artifacts/geometry-{internal-docs,docs-build-output,
docs-check-output,api-check-output}.txt`. The API reference contains 54 pages and 744 members.

### Range value evidence

References: `pgrx/src/datum/range.rs`, PostgreSQL `rangetypes.c`/`rangetypes.h`, and Microsoft Learn's
`System.Range.GetOffsetAndLength` contract. `PgRange<T>` has no static members; inference, parsing and empty/
unbounded factories live on the non-generic `PgRange` class. C# index-range conversion requires a collection
length, explicitly rejects negative lengths, and resolves from-end indices before creating integer bounds.

| Requirement | Implementation | Concrete test evidence |
|---|---|---|
| Six built-in range families and idiomatic .NET aliases | `PgRange<int/long/PgNumeric/PgDate/PgTimestamp/PgTimestampTz>`, with decimal/DateOnly/DateTime/DateTimeOffset adapters | `RangeSignaturesCompile` compiles 40 scalar/nullable/vector/shaped-array contracts with exact SQL type assertions; `UnsupportedRangeSubtypesAreDiagnosed` rejects unsupported subtypes |
| SQL NULL, empty, unbounded and included/excluded bounds | Nullable reference for SQL NULL; nullable bound values for unbounded ends; separate `IsEmpty` and inclusion flags | `RangeStatesRetainRequestedBounds`, `RangePropertiesExposeNativeState`, `RangeArrayTransportPreservesStates` independently inspect states and preserve NULL/empty/unbounded array elements |
| Managed ownership and structural equality | Immutable owned bounds; equality/hash without native access or canonicalization | `RangeEqualityIsDetachedAndStructural`, `RangeTransportOwnsBoundsAndConvertsAliases` verify flags, values, hashes, numeric scale and values surviving native buffer release |
| .NET index range conversion | `PgRange.FromRange(range, length)` with checked length/offset resolution | `IndexRangeConversionResolvesLength` covers from-end indices, entire and empty slices, reversed/out-of-bounds indices and negative length |
| Pointer-free transport and validation | Built-in range OID/flags, length-delimited scalar bound records, outer range marker | `RangeReaderAcceptsIndependentFrame` decodes a hand-authored frame; `MalformedRangeTransportIsRejected` rejects truncated/missing markers, SQL NULL, contradictory flags, null bounds, lengths, trailing bytes and integer overflow |
| Backend canonicalization and checked narrowing | Version-aware `make_range`, statically closed scalar bound adapters | `ManagedRangeConstructionCanonicalizes`, `ManagedCanonicalizationAndOffsetConstruction`, `InvalidRangeConversionsRaiseErrors`, `RangeAliasesRejectLossyBounds` verify discrete successors, equal ends, reversed bounds, overflow, UTC normalization, infinities, numeric precision and sub-microsecond/kind rejection |
| Scalar and array SPI lifetimes | Range converters reused by every existing SPI ownership path and nested inside array transport | `RangeOwnershipPathsPreserveBinaryValues` checks all ten managed bound types, NULL/empty/unbounded states, full-range/special values, numeric display scale and shaped arrays through eight paths against PostgreSQL binary sends |
| Parsing, output and subtype-aware operations | Guarded OID input/output calls and allowlisted scalar dispatch with initialized `FmgrInfo` | `RangeTextOperationsMatchPostgres`, `RangeSetOperationsMatchPostgres`, `RangeSubtypePredicatesMatchSql`, `RangePredicatesRespectBoundaries`, `RangeSetOperationBoundaryResults` compare SQL semantics across all six families, session timezone/DateStyle and boundary/empty/disjoint cases |
| Domains, packed and toasted storage | Detoast ownership before range deserialization, owned numeric bounds | `RangeDomainsAndToastedStorage` checks domain reads and 10,000-element compressed/external arrays; `LargeNumericRangeBoundsOwnDetoastedStorage` checks individually toasted 32,001-digit numeric bounds and display scales |
| Native error unwinding and cleanup | Existing guarded subtransactions and allocator-matched output ownership | `RangeFailureRecoveryPreservesSession` verifies repeated parse/union/canonicalization failures, 40 managed finally executions, two retained writes, a usable prepared plan and zero retained-context growth |

Mapped value-type range subtype registration is implemented in the 2026-09-25
milestone below. Reference-type bounds, direct PgType/PgEnum range derivation,
multiranges and range-specific JSON converters remain pending.

Evidence: `artifacts/range-{runtime,generators,backend}-output.txt` records 18 detached, 14 generator and
96 backend cases. `artifacts/range-all-output.txt` records 1422 passing tests with no failures/skips;
`artifacts/range-build-output.txt` records the zero-warning Release build. The XML scan in
`artifacts/range-internal-docs.txt` reports zero missing internal summaries.
Documentation build, type checking and API freshness pass in
`artifacts/range-{docs-build,docs-check,api-check}-output.txt`; 56 API pages document 772 members.

### Enum value evidence

References: `pgrx/src/enum_helper.rs`, the `PostgresEnum` derive and enum SQL entities,
`pgrx-unit-tests/src/tests/enum_type_tests.rs`, and PostgreSQL `enum.c`/`pg_enum` catalogs.
Generated module initialization registers closed generic mappings without reflection or backend access.
SQL declaration order determines PostgreSQL ordering; C# numeric values remain independent.

| Requirement | Implementation | Concrete test evidence |
|---|---|---|
| Exact labels, integral widths, unknown values and detached ownership | `PgEnum`, `PgEnumLabel`, `PgEnums`, generated registration | `PgEnumTests` checks case/empty/escaped labels, signed/unsigned extrema, undefined values, copied registration, failure atomicity and worker-thread reads; `EnumModuleInitializerRegistersExactClosedConversions` executes the generated initializer |
| Schema/type/function ordering and valid SQL contracts | `EnumDeclaration`, SQL graph edges, `ANKUS006` diagnostics | `PgEnumGenerationTests` compiles scalar/nullable/vector/shaped/variadic signatures, verifies defaults and all eight underlying types, byte-length/Unicode boundaries, flags/alias/access rejection, real dependency cycles and deterministic DDL |
| SQL label order independent of numbers, defaults, variadics and ownership | Generated enum and function DDL | `EnumDeclarationOrderAndValuesAreIndependent` checks `pg_enum`, extension membership, numeric values, defaults and variadic execution |
| Type identity, NULLs, dimensions, bounds and every SPI lifetime | Closed enum scalar/array mappings and owned label transport | `EnumOwnershipPathsPreserveIdentity` compares native binary sends across eight paths; detached tests reject foreign/underlying enum-array casts and distinguish byte-backed enums from bytea |
| Domains and large toasted arrays | Native base-type resolution and detoast ownership | `EnumDomainsRetainBaseTypeAndShape`, `EnumToastedArraysRemainOwned` cover enum/array/element domains and 10,000-element EXTENDED/EXTERNAL arrays |
| Fresh OIDs, relocation, search-path independence and non-extension functions | Active function identity, live extension/schema lookup, no process-global OID cache | `EnumTypeRecreationUsesFreshCatalogIdentities`, `EnumExtensionRelocationAndReinstallationFollowCatalogIdentity` verify new scalar/array OIDs, relocated and reinstalled samples, shadow types and function-namespace fallback |
| Live enum catalog metadata and transaction visibility | Guarded type/value OID lookup and owned `PgEnumInfo` | `EnumCatalogHelpersMatchPostgresEntries` checks label/type/value/sort order, fractional sort positions, unmapped catalog enums, multiple columns/rows with leading NULLs, mixed interval/range values and recursive callbacks; `EnumUncommittedLabelsRetainPostgresSafetyChecks` verifies SQLSTATE 55P04 |
| Missing schemas/types, wrong type kind, rename and native failures | Optional registry scans, native subtransactions and output cleanup | `EnumCatalogLookupRejectsMissingAndNonEnumTypes`, `EnumMissingFixedSchemaDoesNotPoisonOtherMappings`, `EnumNativeOutputAndCatalogErrorsRecover`, `EnumFailuresRecoverOnTheSameBackend` assert SQLSTATEs and same-session recovery; the existing unsupported-result test still proves command rollback |
| Prior writes, retained plans, managed finally and native context cleanup | Shared guarded backend boundary | `EnumGuardedRecoveryPreservesStateAndCleansContexts` pins 40 successful probes, 20 finally executions, two retained writes and zero extra contexts |
| Non-UTF8 databases, labels and identifiers | UTF-8 installation control metadata and native label/name transcoding | `EnumLatin1LabelsUseServerEncoding` installs into LATIN1 and verifies exact labels plus Unicode type/schema names |
| Enum-only and empty extensions with package-only AOT builds | Standalone enum DDL and magic-only native emission | `ToolCommandTests.EnumOnlyPackageCreatesOwnedType` publishes, explicitly loads, installs, checks labels and verifies DROP ownership for inhabited/empty enum declarations |

This milestone adds 18 runtime, 72 generator and 40 backend/package cases. The full suite passes
1552 cases without failures/skips, and the Release build has zero warnings/errors. A Roslyn scan finds
zero missing XML comments among 493 internal declarations. The enum guide, native-boundary notes and
generated API reference pass site build/type/freshness checks; 60 API pages document 796 members.
Evidence is retained under `.git/testagent/enums/`: `full-tests-final.log`, `release-build.log`,
`internal-docs.log`, `docs-build.log`, `docs-check.log`, `api-check.log`, and the bounded review/status notes.
Validation remains PostgreSQL 18.6/Linux x64; the full version/platform matrix is still required.

### Operator and cast evidence

References: pgrx `pg_operator`/`pg_cast` macros, `pg_extern/entity`, `pg_operator_tests.rs`,
`pg_cast_tests.rs`, the operators example, and PostgreSQL `pg_operator.c`, `operatorcmds.c`,
`functioncmds.c` and CREATE OPERATOR/CAST reference sources. Ordinary static methods use the existing
generated native function/error boundary. No new unguarded backend calls are introduced.

| Requirement | Implementation | Concrete test evidence |
|---|---|---|
| Standalone attributes, optional function settings, one native export per method | Semantic discovery merged by symbol identity; ordinary `FunctionDeclaration` defaults | `OperatorCastAttributesShareOneBackingFunction`, `OperatorOptionsPreserveQualifiedReferencesAndFunctionOptions`, `CastUsesQualifiedBackingFunctionAndExecutionOptions` compile generated sources and assert exact SQL |
| Binary/prefix operands, type order, SQL NULLs and arrays | Shared function contracts and wrappers; unary RIGHTARG | `OperatorsExecuteTheirDeclaredSignatures` checks asymmetric/mixed-width operands, prefix syntax and nullable dispatch; `OperatorsPreserveEnumArrayIdentityAndBounds` compares shaped/null/empty enum arrays across eight SPI paths |
| Names, planner constraints and diagnostic boundaries | `ANKUS007`, PostgreSQL punctuation/length/grammar checks, normalized !=, self-negator rejection | `ValidOperatorNamesPreserveEveryToken`, `OperatorNameLengthUsesPostgresBoundary`, `InvalidOperatorNamesAreDiagnosed`, `OperatorReferenceNamesRespectEncodedLengthLimits`, `InvalidOperatorSignaturesAndPlannerOptionsAreDiagnosed`, `OperatorSelfNegatorsAreDiagnosed` |
| Commutators, negators, estimator functions, hashes/merges and shell filling | Quoted schema names and OPERATOR-qualified references; native PostgreSQL operator catalog semantics | `OperatorCatalogRetainsOptionsAndFillsShells` independently checks types, procedure OIDs, reciprocal links, estimator OIDs, planner flags and backing-function volatility/strictness/cost |
| Actual planner execution | Hashes/Merges declarations plus test-supplied compatible operator classes | `DeclaredPlannerOptionsEnableCompatibleJoinPlans` verifies Hash Join and Merge Join plan nodes and every expected result pair |
| Explicit, assignment and implicit conversion contexts | `PgCastContext`, generated CREATE CAST with exact function signature | `CastContextsGenerateExactSql`, `CastContextsControlAssignmentAndFunctionResolution`, `OperatorAndCastFailuresRecoverInTheSameSession` prove accepted and rejected SQL resolution contexts |
| Nullable conversions, target typmods, explicit flag and direct array casts | One-to-three-parameter cast signatures with non-nullable int/bool metadata | `CastsExecuteTheDeclaredConversion` distinguishes NULL-input invocation from NULL output, checks packed numeric modifiers/explicitness, and verifies a declared shaped-array conversion instead of element-wise fallback; `CastCatalogRetainsFunctionAndContext` checks all six catalog contracts |
| Schema/type/function dependencies, separate IDs, duplicates and cycles | Independent operator/cast SQL entities depend on backing functions and their transitive type/schema prerequisites | `OperatorCastEnumArraysPreserveIdentityAndTypeDependencies`, `OperatorCastGraphOrdersSeparateEntityDependencies`, `InvalidOperatorCastGraphsAreDiagnosed`, `DuplicateOperatorCastSqlIdentitiesAreDiagnosed`, `OperatorCastOutputIsDeterministicAcrossDeclarationOrder` |
| Extension ownership, relocation, removal and fresh reinstallation | `Ankus.Examples.Operators`, PostgreSQL catalog dependencies | `OperatorCastSampleRelocatesAndReinstalls` verifies seven owned objects, qualified operations with a shadow search path, moved casts/operators, complete cleanup and fresh enum OID on reinstallation |
| Native error unwinding, command rollback and retained state | Existing guarded SPI/managed unwind paths | `OperatorsAndCastsRecoverInsideGuardedSpi` verifies 40 errors after CTE writes, 20 finally executions, two surviving writes, usable kept plan and zero extra native contexts; direct failures assert SQLSTATEs and same-session success |
| Package-only consumers and independent style choices | Packed SDK/generator/runtime and style-free `ankus new` scaffold | `SdkSupportsDirectPublishWithCentralPackages` publishes and executes an enum operator/cast outside the checkout; `NewSolutionRunsManagedAndBackendTests` checks no generated .editorconfig and runs the scaffold's managed/backend suite |

This milestone adds 170 generator and 37 backend cases. Plain `dotnet test` passes all 1759 cases without
failures or skips on Linux x64 with PostgreSQL 18.6; the Release build has zero warnings/errors. The internal
XML scan checks 495 declarations with zero omissions. The generated API contains 63 pages and 813 members.
Site build, type checks, API freshness and IDE0008/IDE0290 verification pass. Duplicate analyzer release-file
entries were removed from the generator project; the analyzer package supplies each file once, and the
formatter no longer reports a workspace warning. Sitemap generation still awaits the public site URL.

The declarations cover supported input/output types. Composite/custom-type operands, automatic equality/order/hash
operator-class generation, custom SQL translation hooks, and the full PostgreSQL/platform matrix remain active work
in their respective inventory rows. Validation artifacts are under `.git/testagent/operators/`.

### Set-returning function evidence

References: pgrx `iter.rs`, `srf_tests.rs`, and the `srf`/`spi_srf` examples; PostgreSQL `funcapi.c`,
`execSRF.c`, `nodeProjectSet.c`, expression/memory cleanup, portal management and transaction cleanup.
Ordinary `IEnumerable<T>` declarations use shared scalar converters and the existing guarded backend boundary.

| Requirement | Implementation | Concrete test evidence |
|---|---|---|
| SETOF values, empty/null sequences and nullable elements | Typed enumerable callbacks; SQL NULL is a row, distinct from end-of-set | `SetElementConversionFamiliesCompile`, `SetSequenceAndElementNullabilityCompileIndependently`, `ScalarSetsPreserveEmptyAndNullSemantics`, `TypedSetColumnsPreserveExactNativeValues` |
| Named TABLE columns, one-column tables and long tuples | Named flat C# tuples or `[return: PgColumnNames(...)]`; PostgreSQL's 1664 record-column limit | `TableColumnsPreserveNamesTypesAndValues`, `TableColumnOverridesSupportScalarAndTupleRows`, `LongTableTuplesCompileEveryOutputColumn`, `TableTupleBeyondPostgresRecordLimitIsDiagnosed` |
| Identifier, shape, options and graph validation | ANKUS008 result diagnostics; existing declaration/graph validation; per-column enum dependencies | `TableColumnNamesUseUtf8LengthLimits`, `InvalidSetRowShapesAndNamesAreDiagnosed`, `SetRowsAcceptPositiveRepresentableBoundaries`, `InvalidSetOptionsAreDiagnosed`, `TableGraphDependsOnEveryOutputEnumAndCustomSql`, `SetReturnEnumDependencyCyclesAreDiagnosed`, `SetOutputIsDeterministicAcrossDeclarationOrder` |
| Planner rows, strictness and execution modes | `PgFunction.Rows`, `PgSetMode`, required-argument handling and executor mode negotiation | `SetCatalogRetainsRowsAndTableContracts`, `ExecutorModesDisposeExactlyOnce`, `EmptyExecutionDoesNotLeakEnumerators` distinguish lazy SELECT-list LIMIT from eager FROM/forced materialization |
| Exactly-once managed ownership | `NativeSet` owns a GCHandle, clears it before user disposal and frees it even if Dispose throws | `EmptySequenceOwnsAnIteratorUntilExplicitDisposal`, `SingletonNullIsARowBeforeCompletion`, `MultipleRowsPreserveValuesAndReadCurrentOncePerRow`, `EarlyDisposalClearsTheHandleBeforeCallingUserCode`, `DisposalFailureStillReleasesTheManagedRoot` |
| Independent factory/GetEnumerator/MoveNext/Current/Dispose errors | Managed callback catches errors before returning to native; reset cleanup preserves primary errors | `IteratorFailuresPreserveOwnershipAndRecover`, `ExecutorErrorsAbortEnumeratorsWithoutReplacingTheError`, `EarlyDisposalFailureIsReportedExactlyOnce`, `RowConversionFailuresReleaseEnumerator` |
| Suspended state, ordinary disposal with SPI and native abort | Expression shutdown supplies a snapshot; memory reset denies new SQL while allowing owned resource release | `ClosingPortalDisposesAbandonedSequence`, `SuspendedArgumentsAndSpiPlansRetainValues`, `InterleavedPortalsResumeIndependentEnumerators`, `NestedSetFailuresRecoverInsideSpi` |
| Repeated plan/cursor cleanup, including adopted parent cursors | Stable cursor registry queues abort closes; transaction callbacks and memory reset drain after PostgreSQL portal scans | `AbortCleanupReleasesOwnedPlansAndCursors` repeats twenty failures and checks resource baselines; `AbortCleanupReleasesAdoptedParentCursor` verifies ownership across rollback to a savepoint |
| Cancellation between native row steps | Native CHECK_FOR_INTERRUPTS, managed iterator cleanup and same-session recovery | `CancellationDisposesSuspendedSequence`, `PureManagedMaterializationObservesCancellation` (no backend call inside the materialized iterator) |
| Materialization spill and bounded row allocations | PostgreSQL tuplestore and per-row context reset | `MaterializedTuplestoreSpillsAndPreservesEveryRow`, `MaterializedRowContextsRemainBoundedAcrossSpill` verify exact values, temporary disk blocks and bounded context storage across 16 MiB of rows |
| Numeric precision for each yielded element | Existing `[return: PgNumericPrecision]` rescaling in the set writer | `SetNumericPrecisionRoundsNullsAndRecoversFromOverflow` checks rounding, NULL, overflow SQLSTATE and recovery |
| Relocation, removal/reinstallation and package-only consumption | `Ankus.Examples.Sets`, shared SDK packaging and SQL graph | `SetSampleRelocatesAndReinstalls`, `SdkSupportsDirectPublishWithCentralPackages` |

This milestone adds 161 generator, 12 direct iterator and 52 backend/sample cases. Plain `dotnet test` passes
all 1984 cases on PostgreSQL 18.6/Linux x64, including cold package consumption. The non-incremental Release
build has zero warnings/errors; the internal XML scan checks 525 declarations with zero omissions.
These regressions exposed and verified fixes for missing disposal snapshots and unsafe cursor deletion during
PostgreSQL's abort scan. IDE0008/IDE0290/IDE2003 verification passes; consumer templates retain independent
style choices. The site build, type checks and API freshness check pass; the generated API contains 65 pages
and 820 members. The site build still reports its existing duplicate `/404` route and missing public site URL
warnings; neither warning is disabled. Validation artifacts are under `.git/testagent/sets/`.

### Composite and heap-tuple evidence

References: pgrx `heap_tuple.rs`, `tupdesc.rs`, `composite_type!`, and composite/heap-tuple
unit tests; PostgreSQL tuple formation/deformation, record descriptors, array construction,
assignment coercion, domains, and set execution. `PgHeapTuple` owns managed cells;
`PgTupleDescriptor` and `PgTupleAttributeInfo` copy catalog metadata without retaining native pointers.

| Requirement | Implementation | Concrete test evidence |
|---|---|---|
| Names, physical slots, dropped attributes and strict edits | Zero-based ordinals, exact-name lookup, immutable metadata and atomic checked replacement | `NamesAndOrdinalsDistinguishDroppedSlotsAndNullValues`, `EditsPreserveMetadataAndRejectIncompatibleTypesAtomically`, `DescriptorMetadataMatchesCatalogs`, `DroppedAttributesRemainNullAndUnwritable` |
| Independent ownership and transport | Copied cell slots, documented shallow clone, recursive pointer-free envelopes and allocator-matched datum reconstruction | `NativeReaderAcceptsIndependentTupleEncoding`, `NativeWriterMatchesTheIndependentTupleEncoding`, `NestedValuesRemainOwnedAfterNativeTransportRelease`, `StoredCompositeValuesSurviveSourceDeletion` |
| Empty/NULL/all-null/first-null arrays and domain identity | Explicit descriptor arrays retain declared/base OIDs; ordinary vectors use record transport and validate each named output element | `CompositeDomainsRetainDeclaredAndUnderlyingIdentities`, `CompositeArraysKeepNullElementsDimensionsAndIdentity`, `OrdinaryCompositeArraysUseAnnotatedIdentityWithoutInferringFromElements` |
| Scalar and nested value families across SPI owners | Shared tuple/array/scalar converters; static/session `PrepareWithTypeOids`; typed-null descriptor bindings | `NamedAndNestedTuplesPreserveExactValuesAcrossOwners`, `PrimitiveTupleCellsRetainTheirBinaryRepresentation`, `AnonymousRecordsKeepShapeAndValues`, `SpiRowEditsRetainConcreteTupleIdentity` |
| Domains, type modifiers and stale catalog layouts | PostgreSQL assignment coercion, domain checks including NULL, live identity/name/type/typmod/collation validation | `TupleOutputAppliesAttributeTypeModifiers`, `DomainDescriptorsRetainBaseIdentityAndValidateConstructedValues`, `StaleTupleOutputFailsWithoutPoisoningBackend`, `DomainFieldsValidateAndRecoverAfterNativeErrors`, `NotNullCompositeDomainsRejectNullDatumsAndRecover` |
| Named/anonymous SQL signatures and TABLE bindings | `[PgCompositeType]`, per-column annotations, explicit custom-SQL dependencies and ANKUS009 diagnostics | `CompositeSignaturesCompileWithNamedAndAnonymousTypes`, `CompositeTableBindingsSelectFinalColumnNames`, `InvalidCompositeBindingsAreDiagnosed`, `CompositeNullResultValidatesItsDeclaredDomain` |
| Composite sets and caller descriptors | Separate transported-cell and result-row descriptors; PostgreSQL materialized NULL-row semantics | `StreamingCompositeSetDistinguishesNullRows`, `MaterializedCompositeSetUsesNamedTupleDescriptor`, `AnonymousRecordsRejectCallerDescriptorMismatch`, `TableColumnsCarryTheirIndividualCompositeTypes`, `MaterializedAnonymousSetRequiresCompatibleCallerContext` |
| Encoding, nested enums, nominal identity and operators/casts | UTF-8 transport of server catalog names, guarded function identity during input conversion, existing declaration bindings | `Latin1CompositeNamesAndCellsPreserveEncodingAndRecover`, `AnonymousRecordsKeepShapeAndValues`, `CompositeOperatorsAndCastsPreserveFieldsAndNullBehavior` |
| Malformed metadata and recursive values | Shape/count/flags/identity/UTF-8 checks, execution-stack checks and managed reference-cycle rejection | `MalformedTupleTransportsAreRejected`, `DescriptorAndNameCapacityBoundariesAreExact`, `DeepValidTuplesWorkAndCyclicValuesDoNotPoisonLaterConversions` |
| Public sample and ordinary package consumption | `Ankus.Examples.Composites`, relocation/reinstallation, package-only typed plans and arrays | `CompositeSampleRelocatesAndReinstalls`, `SdkSupportsDirectPublishWithCentralPackages` |

Focused validation passes 56 generator, 38 runtime, and 85 integration/sample cases on
PostgreSQL 18.6/Linux x64. Native regressions found and fixed an interior tuple-pointer free,
NULL composite-domain output bypass, and enum lookup before the function context was set.
Domain array test oracles explicitly cast the whole array, because PostgreSQL's common-type
inference can otherwise strip a domain from an array containing an untyped NULL.
Plain `dotnet test` passes all 2163 cases, including 1261 integration cases and the
isolated package consumer. The non-incremental Release build has zero warnings/errors.
IDE0008/IDE0290/IDE2003 verification passes; the XML scan checks 548 internal declarations
with zero omissions. The API reference contains 69 pages and 859 members; the site builds
93 pages. `pnpm check` and API freshness verification pass. The existing duplicate `/404`
route and missing public site URL warnings remain visible during the site build; no warnings
are disabled. Artifacts and bounded test-gap/assertion reviews live under `.git/testagent/composites/`.
Raw heap interfaces, custom base types, trigger callbacks and the required version/platform
matrix remain in the full-port inventory; this milestone does not claim those capabilities.

### Trigger evidence

References: pgrx `trigger_support/`, `trigger_tests.rs`, and `pgrx-examples/triggers`;
PostgreSQL trigger execution, SPI transition registration, portal ownership and generated-column rules.
`[PgTrigger]` exports a synchronous static callback taking `PgTriggerContext` and returning
`PgHeapTuple` or `PgHeapTuple?`. Optional `[PgFunction]` supplies ordinary declaration options;
custom SQL attaches the trigger with explicit table/function dependencies.

| Requirement | Implementation | Concrete test evidence |
|---|---|---|
| Row/statement event decoding and exact metadata | Owned names, arguments, OIDs, relation descriptor and OLD/NEW rows; separate operation/timing/level enums | `EventsDecodeExactOperationTimingAndLevel`, `EventsExposeExactRowsMetadataAndCatalogIdentities`, `ContextOwnsMetadataAndRowsAfterTransportRelease`, `RelationSchemaComesFromTargetRatherThanFunction` |
| Correct replacement, NULL skip, ignored results and INSTEAD OF behavior | HeapTuple pointer return with `fcinfo->isnull=false`; INSERT/UPDATE reconstruction preserves tuple identification; ignored AFTER/DELETE payloads bypass serialization | `BeforeRowsReplaceStoredAndReturnedValues`, `BeforeNullSkipsRowsAndLaterCallbacks`, `AllNullAndOldReturnsAreNotSkipSignals`, `IgnoredReturnPayloadsDoNotApplyForeignTupleValidation`, `InsteadOfViewWritesHonorReturnedRowAndSkip`, `ZeroColumnRowsKeepPhysicalTupleIdentity` |
| Domain/typmod/table constraints and undefined generated values | PostgreSQL coercion; generated availability flag distinct from NULL; forbidden reads/writes and ordinary composite conversion | `ReplacementRowsEnforceActualRelationConstraints`, `GeneratedAndDroppedFieldsRespectAvailability`, `UnavailableColumnsCannotBeReadOrReplaced`, `UnavailableTransportUsesAnIndependentFlagAndNullEncoding` |
| Transition tables across SPI owners and nested triggers | Registration once per SPI connection; callback-owned query environments outlive SPI_finish; portals close before borrowed transition storage expires | `TransitionInsertRowsMatchActualWritesAcrossOwners`, `TransitionOldAndNewSetsHaveExactImages`, `EscapedTransitionCursorsExpireAndBackendRecovers`, `RetainedTransitionPlanRebindsAndRejectsOutsideUse`, `NestedTriggersRestoreParentContextAndTransitionRelations`, `SuspendedCursorCleanupKeepsTransitionEnvironmentUntilAllOwnersEnd` |
| SQLSTATEs, rollback, recovery and native invocation guard | Native-only PostgreSQL errors; restored trigger/function contexts and normal managed exception transport | `InvalidReturnsAndErrorsRollBackAndRecover`, `CaughtSpiErrorsLeaveTriggerAndBackendUsable`, `OrdinaryInvocationIsRejectedBeforeManagedDispatch`, `CancelledTriggerRollsBackAndRestoresBackendContext` |
| Deferred/partition/filter/conflict behavior and server encoding | Actual PostgreSQL callback metadata and UTF-8 owned transport | `DeferredAndPartitionTriggersRetainActualRelationIdentity`, `DeferredTriggerCanUseSpiDuringCommit`, `NativeTriggerFilteringAndConflictOrderingArePreserved`, `Latin1TriggerNamesArgumentsAndTransitionsUseServerEncoding` |
| Declaration diagnostics, nullable contracts and ordered SQL | ANKUS010, zero SQL arguments, shared schema/options graph; trigger-only helpers emit only when required | `InvalidTriggerSignaturesAreDiagnosed`, `ConflictingTriggerMetadataIsDiagnosed`, `TriggerSqlSignaturesCollideOnlyOnSchemaNameAndZeroArguments`, `TriggerSqlDependenciesOrderSchemaTableFunctionAndAttachment`, `NonTriggerExtensionsDoNotEmitUnusedTriggerEntryPoints` |
| Public example and extension lifecycle | `Ankus.Examples.Triggers` normalizes names using trigger arguments and skips blank/NULL rows | `TriggerSampleRelocatesAndReinstalls` |

Focused validation passes 70 new generator, 102 new runtime, and 79 new backend/sample cases.
The complete generator suite passes 718 cases; plain `dotnet test` passes all 2414 cases,
including 1340 integration cases and the package/tool consumers, with zero failures or skips.
The non-incremental Release build has zero warnings/errors. IDE0008/IDE0290/IDE2003 verification
passes, no extra opening-brace blank lines remain, and the XML scan checks 559 internal declarations
with zero omissions. The API reference contains 74 pages and 885 members; the site builds 99 pages.
`pnpm check` and API freshness verification pass. Existing duplicate `/404` and missing-public-site-URL
warnings remain visible; no warnings are disabled.

Native publication caught unconditional trigger helper emission in extensions without triggers;
helpers now emit only when needed. Review caught optional-context validation and cursor cleanup
ordering/lifetime gaps: suspended iterators retain the current trigger environment during disposal,
and an early close error leaves borrowed storage alive for PostgreSQL abort cleanup. Backend tests
verify both successful disposal and first-close failure with another suspended owner. An escaped
retained transition plan preserves PostgreSQL's actual XX000 missing-tuplestore diagnostic, while
queries within subsequent callbacks bind fresh transition data. No SQLSTATE normalization was added.

The runtime/generator/native/backend/sample reviews and exact logs are under `.git/testagent/triggers/`.
This is PostgreSQL 18.6/Linux x64 evidence, including UTF8 and LATIN1; no other platform/version is claimed.
Full relation/raw heap APIs, unsupported datum families, other inventory rows and the
PostgreSQL/platform matrix remain active full-port work.

### Event trigger evidence

References: the read-only pgrx checkout exposes `EventTriggerData` through `pgrx-pg-sys`,
with no safe `pg_event_trigger` macro. PostgreSQL's event trigger implementation, headers,
metadata helpers and regression tests define this managed API's native behavior.
`[PgEventTrigger]` exports a synchronous static `void` callback with one `PgEventTriggerContext`.
Optional `[PgFunction]` supplies common SQL settings; custom SQL attaches database-wide event triggers.

| Requirement | Implementation | Concrete test evidence |
|---|---|---|
| Event kinds, tags and catalog timing | Header-derived `EventTriggerData`/`CommandTag`; owned event/tag strings, zero SQL arguments and `RETURNS event_trigger` | `EventsRetainExactKindAndCommandTag`, `EventsExposeExactKindsTagsAndCatalogTiming`, `EventTriggerMarkerEmitsOneCompilableZeroArgumentFunction` |
| DDL metadata, NULL identities and extension origin | Explicit public-column projection; immutable `PgDdlCommand` snapshots preserve nullable identities and empty results | `DdlCommandsPreserveCatalogIdentityAndEmptyResults`, `PrivilegeSnapshotsKeepNullAddressFields`, `ExtensionCommandsMarkTheirOrigin`, `DdlCommandsPreserveNullableObjectAddresses` |
| Dropped dependencies and object addresses | All twelve metadata fields, independent root/normal flags, owned address arrays and canonical temporary names | `DroppedObjectsPreserveDependencyAndAddressMetadata`, `DropSnapshotsKeepColumnsFunctionsAndTemporaryNames`, `DroppedObjectsDistinguishNullFromEmptyAddresses` |
| Rewrite identity, reason bitmaps and exclusions | Owned relation OID and flags, preserving combinations and future nonnegative bits | `TableRewriteReportsRelationAndReasonBitmap`, `NonEventRewritesAndUnchangedPersistenceDoNotFire`, `RewriteReasonsPreserveKnownAndFutureBits` |
| Detached snapshots and active helper scope | Copied immutable results; exact current context/phase/thread checks; parent references detached on exit | `HelpersReturnOwnedImmutableOrderedSnapshots`, `NestedScopesRestoreParentsAndReleaseTheirLinks`, `RetainedSnapshotsOwnValuesAndRejectFreshHelpers`, `MetadataQueriesWorkAcrossSessionPlanAndCursorOwners` |
| Nested event, function and row scopes | Restored managed context, function schema and native transition environment on success/error | `NestedDdlRestoresParentContextAndSnapshot`, `NestedEventRestoresFunctionSchemaForEnumResolution`, `RowTransitionScopeIsIsolatedAndRestoredAroundEvents`, `EventContextSurvivesNestedRowTrigger` |
| Protocol, errors, cancellation and recovery | Native 39P03 guard; owned diagnostics raised after managed return; callback state restored on every exit | `OrdinaryInvocationRejectsEventProtocolBeforeDispatch`, `ErrorsRollBackDdlAndRecoverOnSameConnection`, `FailedCommandsDoNotRunEndAndBackendRecovers` |
| Login callbacks | PostgreSQL 17+ login support; no parse-tree or active-portal assumption | `LoginCallbacksCommitExactMetadataForEachPhysicalConnection`, `LoginFailuresRollBackAndAllowAdministrativeRecovery`, `LoginFiltersAndConnectionBypassFollowPostgresRules` |
| Server-owned firing rules, encoding and lifecycle | Native filters/order/enable settings; UTF-8 transport and database-wide extension attachment | `EventOrderingFiltersAndEnableModesFollowPostgres`, `Latin1EventSnapshotsPreserveNamesArraysAndRecover`, `EventTriggerSampleRelocatesAndReinstalls` |
| Declaration validation and dependencies | ANKUS011, shared function options/SQL graph, compiled nested dispatch and conditional native helpers | `InvalidEventTriggerSignaturesAreDiagnosed`, `ConflictingEventTriggerMetadataIsDiagnosed`, `EventTriggerSqlDependenciesOrderSchemaTableFunctionAndAttachment`, `EventTriggerMixedDeclarationsPreserveDiscoveryAndDeterministicOutput` |

Focused checks pass 71 runtime cases, 80 event generator cases, and the complete 798-case generator suite.
The final focused backend run passes 44 event/sample/login cases on PostgreSQL 18.6/Linux x64,
including function-schema restoration through nested success and caught errors. Plain `dotnet test`
passes all 2609 cases, including 1384 integration cases and the package/tool consumers, with zero failures
or skips. The non-incremental Release build has zero warnings/errors. IDE0008/IDE0290/IDE2003 verification
passes; the brace scan checks 318 C# files with zero extra opening-brace blank lines, and the XML scan
checks 573 internal declarations with zero omissions.
The independent C review found mutable automatic result/error headers read after `longjmp` in both
event and row callbacks. These headers now live in callback memory contexts, reached through stable
pointers assigned before `PG_TRY`; allocator-matched cleanup and row cursor lifetime ordering are preserved.
The API reference has 81 pages and 924 members; the site builds 107 pages. Site type checking and API
freshness verification pass. Existing duplicate `/404` and missing-public-site-URL warnings remain
visible; no warnings are disabled.

The native, runtime, generator and backend review evidence lives under `.git/testagent/event-triggers/`.
This managed surface exposes owned descriptive metadata; raw parse trees and opaque `pg_ddl_command`
objects remain part of the full raw-binding inventory. Other datum/runtime/tooling requirements and
the PostgreSQL/platform matrix remain active full-port work.

### PostgreSQL aggregates with owned managed state

`[PgAggregate]` now declares typed static transition, final, combine, serialization and moving
callbacks. Ordinary SQL state uses the existing exact converters; `PgAggregateState<T>` maps to
`internal`, roots an owned payload and releases it once when PostgreSQL resets its memory owner.
`PgAggregateContext` supplies owned metadata and guarded PostgreSQL comparison, including explicitly
typed NULL composite/array operands. The public average and discrete-percentile examples are in
`samples/Ankus.Examples.Aggregates`, with the author guide at `docs/src/content/docs/aggregates.md`.

The implementation follows `pgrx/src/aggregate.rs`, pgrx's aggregate examples
and SQL entity graph, and PostgreSQL's `nodeAgg.c`, `nodeWindowAgg.c`, `pg_aggregate.c`,
`aggregatecmds.c`, and `orderedsetaggs.c`. PostgreSQL's actual `(bytea, internal) -> internal`
deserializer contract takes precedence over the incompatible pgrx wrapper shape found during
reference research. Local reference checkouts remained read only.

| Boundary | Implementation and independent evidence |
|---|---|
| Declaration and SQL graph | Conventional/overridden callback names, shared helpers, exact SQL signatures, schema/type/dependency ordering, common function options and ANKUS012 validation; 114 generator cases require warning-free positive compilation |
| NULL, strictness and seeding | Empty versus all-null inputs, skipped required inputs, strict first-value seeding, nullable-state recovery, zero arguments and variadics; exact values and callback counts, including binary-compatible integer→OID and CIDR→INET seeds |
| SQL state values | No-final enum labels, shaped arrays, TOAST-sized named composites and composite domains retain values and identity; domain violations preserve SQLSTATE and same-session recovery |
| Managed state ownership | A checked root-ID map plus native address registry validates before dereference; release removes roots and invalidates wrappers before disposal; GC, replacement, wrong payload types, foreign native pointers, worker access and stale handles have direct/backend witnesses |
| Executor lifetimes | Grouping sets, actual hash spilling, sorted groups, rescans, suspended cursor CLOSE/LIMIT/COMMIT/ROLLBACK, cancellation and errors release every observed owner; throwing Dispose emits warnings while preserving the original error and releasing other owners |
| Parallel transport | Actual launched workers, Partial/Finalize plans, foreign backend PIDs and transport counters prove combine/serialize/deserialize execution; temporary deserializer owners require copied destination state; ordinary array INITCOND is independently observed per partial/final state |
| Moving windows | Separate ordinary/moving state types, inverse callbacks, deterministic NULL restart, singleton/nonoverlapping/excluded frames, volatile fallback, NULL/FILTER/partition behavior and stable prior text results match literal vectors and native aggregate results |
| Final contracts | Typed NULL EXTRA inputs, READ_ONLY/SHAREABLE/READ_WRITE sharing, mutable-window rejection and read-only moving fallback are checked through catalog values, exact transition counts and results |
| Native ordering | ASC/DESC, NULL placement, both sort-key metadata records, ICU case-insensitive versus C collation, hypothetical rank, percentile boundaries and custom B-tree operators use native or independent result oracles |
| Guarded comparator ERROR | A PL/pgSQL B-tree comparator raises P7823 inside SortSupport; managed code catches its owned diagnostics and successfully reuses Compare and SPI in the same callback; uncaught propagation and later recovery also pass |
| Extension lifecycle | The public sample preserves support OID bindings across relocation, drops owned aggregate/support entries and reinstalls with new OIDs and correct results |

Review found and fixed nested payload nullability loss, invalid qualified SORTOP syntax, dependency
self-edges, combined direct/input name collisions, and dropped explicit final flags. Strict ordered
seeding validates both catalog and executor input requirements. Native comparison now restores the
saved memory context after successful subtransaction commit; typed SPI operands preserve named SQL
NULL identities. No warning pragma, suppression attribute, NoWarn setting or diagnostic downgrade
was added. Consumer templates are unchanged.

Validation on PostgreSQL 18.6/Linux x64, including a separate LATIN1 database and ICU collation:

- Aggregate-specific evidence: 61 direct runtime, 114 generator and 125 Native AOT backend cases.
- `dotnet build -c Release --no-incremental`: zero warnings/errors.
- `dotnet test`: **2909 passed, 0 failed, 0 skipped**, including all 125 aggregate backend cases.
- `pnpm build`: 88 generated API pages / 972 members and 115 site pages; `pnpm check`: zero errors,
  warnings or hints. Existing duplicate `/404` and missing public site URL build warnings remain visible.
- `dotnet run --project src/Ankus.DocGenerator -c Release -- --check`: current, zero warnings/errors.
- XML scan: 666 internal declarations, zero omissions; opening-brace whitespace and diff checks clean.

An extra ad hoc GCC compile still reports existing base-bridge longjmp/clobber warnings, and a Swift
clang probe with ordinary rather than system header classification reports PostgreSQL's generated
`gnu_printf` attributes. These are not claimed as successful additional compiler validation; the
actual SDK Native AOT publishes use the unchanged repository compiler invocation. No warning flags
or reference headers were changed to make the probes pass.

This implements aggregates for the current concrete type surface. Full-port requirements remain:
polymorphic/raw and custom base-type state/input/result transport, heterogeneous ordered-set
`VARIADIC "any"`, other native extension APIs and all required PostgreSQL/platform combinations.
Customized `FUNC_MAX_ARGS`, backend invocation at the generated 99-argument boundary, and independent
post-exit worker cleanup telemetry are not claimed verified. Passing this milestone is not full pgrx parity.

### Backend initialization evidence

`[PgInitialize]` selects one public/internal, synchronous, non-generic, parameterless static void
callback. The generator emits `_PG_init`, its managed exception boundary, and a complete native
manifest even without SQL functions. `ANKUS013` rejects invalid signatures/containers, duplicate
initializers, conflicting SQL metadata, async partial implementations, static virtual interface
methods, conditional call removal and unmanaged-only callbacks. Initialization never becomes a SQL function.

The native loader guards reentrancy and records success only after the managed callback returns.
Failures reset the state for retry. Owned diagnostics and managed finally blocks use the existing
native boundary. Session preload has an initial transaction but no portal snapshot; `_PG_init`
pushes a snapshot only when needed and releases only its own snapshot on success or failure.
When no transaction exists, generated dispatch binds no transaction-dependent native entry point.
In a forking postmaster, the generated loader initializes managed code and then enables
the patched runtime checkpoint before PostgreSQL creates backends.

| Requirement | Concrete evidence |
|---|---|
| Init-only native publication and SQL/export separation | `InitializationOnlyExtensionCompilesWithNativeLoaderExports`, `InitializationOnlyExtensionLoadsWithoutSqlFunctions` |
| Declaration validation and deterministic mixed artifacts | `InvalidInitializationDeclarationsAreRejected`, `MultipleInitializationCallbacksAreRejected`, `InitializationRejectsSqlFunctionAndResultMetadata`, `InitializationRejectsAttributesThatPreventManagedInvocation`, `InitializationMixedDeclarationsPreserveSqlAndDeterministicManifest`, `InitializationCallbackSymbolsAreAssemblyScoped`, `InitializationIsAbsentUnlessExplicitlyDeclared` |
| Per-backend first-use/LOAD lifecycle | `InitializationRunsOncePerBackend` exercises two independent backends and repeated LOAD, both with explicit LOAD and first-function entry |
| Exceptions, owned long/Unicode diagnostics, finally and retry | `InitializationFailureUnwindsAndCanRetry` runs 50 managed, structured PostgreSQL, or recursive failures, followed by a successful 51st attempt with exact counter/diagnostic assertions |
| SQL rollback and managed state lifetime | `InitializationFailureRollsBackSqlAndPreservesCallerState` verifies savepoint rollback, earlier writes, successful retry, and initialized managed state after outer transaction rollback |
| Native error recovery and nested initialization | `InitializationSpiErrorsPreserveCallerState` catches division/recursive-load errors while preserving a prepared statement and transaction writes; `InitializationCanLoadAnotherExtension` proves nested Native AOT initialization and continued SPI |
| Startup snapshot and failure isolation | `SessionPreloadInitializesBeforeFirstFunction`, `SessionPreloadFailureUnwindsAndPreservesServer` verify preload before client SQL, finally logging before connection failure, a healthy existing backend, and a corrected new connection |
| Managed postmaster state and runtime services across fork | `SharedPreloadPreservesManagedRuntimeAcrossFork` loads two Native AOT extensions and verifies inherited state, tasks, timers, finalization, GC, exceptions, hooks, three backend PIDs and clean shutdown |
| No-transaction bindings, thread affinity and scope restoration | `NontransactionalInitializationBlocksBackendEntryPoints`, `NestedDisabledInitializationRestoresOuterBinding`, `InitializationBindingDoesNotFlowToWorkerThreads`, `InitializationRestoresTheOwningSpiSession`, `InitializationPreservesAbortCleanupRestrictions`, `EventQueriesHonorDisabledInitializationAndRecover` |

Focused checks pass: 7 runtime cases, all 962 generator cases (including 50 new initialization cases),
and 13 real backend cases on PostgreSQL 18.6/Linux x64. The non-incremental Release build has zero
warnings/errors. Plain `dotnet test` passes 3242 cases with zero failures/skips in 2m50.126s.
The XML scan found zero omissions in 683 internal declarations; 384 source/template files have no
extra opening-brace blank lines or warning suppressions. The sample and public initialization guide describe backend loading, retry and preload
limits; generated API documentation contains 90 pages/1034 members, and the site builds 118 pages.
Existing duplicate-404/missing-site-URL site warnings remain visible; no warnings are disabled.

Actual native loading without a transaction, standalone/EXEC_BACKEND behavior, and other PostgreSQL
versions/platforms are not claimed executed. The no-transaction branch is verified by generated
contract checks and direct runtime binding tests. The GUC work below extends this initialization
foundation; other PostgreSQL versions and operating systems remain required full-port work.

### Configuration settings evidence

Native-backed static partial getters now support Boolean, int32, double, nullable/nonnullable string,
and C# enum settings through `PgGucBool`, `PgGucInt`, `PgGucReal`, `PgGucString`, and `PgGucEnum`.
Definitions retain native metadata and storage; enum ordinals preserve wide managed values and aliases.
Contexts, symbolic options, numeric units, bounds, hidden labels and typed check/assign/show methods
are validated with `ANKUS014`. `PgGucOptions` follows the enforced .NET enum naming rule.

PostgreSQL owns parsing, source priority, placeholders, SET/LOCAL/RESET, savepoints, function settings,
permissions, file reloads and restoration. Check results can normalize values and retain copied byte
extras; numeric replacements follow native semantics without a second bounds check. Native extras
use the PG13–15 allocator or PG16+ GUC allocator, never a managed handle. Typed strings, extras and
errors cross the boundary as owned copies. Cached native encoding converters keep LATIN1 callbacks
usable during abort/restoration and out-of-transaction reporting without catalog lookup.

Registration precedes `[PgInitialize]`, detects owned definitions on retry, and never overwrites an
adopted placeholder value. Managed hooks can run before postmaster fork and continue in individual
backends. A GUC-only library and separate check-only, assign-only and
show-only libraries publish with only their required native helpers. No warning is disabled and no
unused helper is retained through a dummy reference or warning-suppression attribute.

| Requirement | Concrete evidence |
|---|---|
| Five values, native parsing, exact metadata, null/empty strings, wide enum labels and owned snapshots | `DefaultsAndMetadataPreserveNativeTypes`, `FiveTypesUseNativeParsingAndOwnedStorage`, `InvalidNativeValuesDoNotAssign` |
| Placeholder adoption, native warning severity, stacked values and retry after managed init failure | `PlaceholderAdoptionPreservesValuesAndWarnings`, `PlaceholderStacksSurviveRegistrationAndRollback`, `RegistrationSurvivesInitializerFailureAndRetry` |
| All typed hooks, display/storage separation and native numeric normalization | `AllHookTypesNormalizeAndDisplayIndependently`, `AcceptedNumericNormalizationPreservesNativeHookSemantics` |
| Error ownership, unchanged rejected state and same-session recovery | `CheckErrorsPreserveDiagnosticsAndRecover` alternates structured/default/thrown/unexpected failures 25 times and checks native/typed storage and exact callback events after each failure |
| Check source, validation without assignment, old-value assignment, restoration without another check and detached extra copies | `DefaultsAndMetadataPreserveNativeTypes`, `FunctionSettingsValidateWithoutAssignAndRestore`, `HooksRestoreValuesAndExtraAcrossLocalAndSavepoints` pin native Default/Test/Session sources and exact ordered events, including null versus empty extra |
| Hook phase capabilities and terminal failure policy | `HookSqlAvailabilityMatchesNativePhase`, `ShowErrorLeavesBackendAndStorageUsable`, `AssignFailureTerminatesOnlyItsBackend`, `RejectedBootDefaultTerminatesOnlyItsBackend`, `ReportShowFailureTerminatesOnlyItsBackend` |
| Latin1 values, cached abort/report conversions, diagnostic/result encoding failures and recovery | `Latin1HooksPreserveValuesDuringRollbackAndReporting` also sets non-UTF8 client encoding, constructs server-side accented values and repeats unrepresentable managed results 25 times |
| Flags, privileges, visibility, identifier bytes and security restrictions | `FlagsPreserveNativeListingResetAndIdentifierRules`, `PrivilegesAndVisibilityUseNativeChecks`, `ClientOptionsRespectBackendPrivilegeAndParameterGrant`, `DisallowInFilePreservesNativeFileAndAlterSystemBehavior` |
| Every memory/time unit and server-derived block size | `UnitsUseServerConversionsAndBlockSizes` |
| Native preload, managed backend lifetime, reload/reset priority, startup-only contexts and client defaults | `SharedPreloadPreservesNativeStorageAndBackendRuntime`, `ReloadPreservesSourcePriorityAndConnectionContexts`, `ClientDefaultsAndBackendSettingsRetainSources`, `LatePostmasterRegistrationRejectsWithoutTerminatingBackend` |
| Small library publication and managed postmaster hooks | `GucOnlyLibraryLoadsAndRegisters`, `HooksOnlyLibraryRunsItsCheckInBackend`, `AssignOnlyLibraryPreservesOldValueAndRestoration`, `ShowOnlyLibraryReadsNativeStorageWithoutRecursion`, `SharedPreloadRunsManagedHooksAcrossFork`, `SharedPreloadRejectsMetadataWithoutDatabaseEncoding` |
| Managed ownership, malformed transport, thread affinity and nested scope cleanup | 52 direct `GucRuntimeTests` cases, including allocator-release checks for successful and failed reads |
| Compiler contracts and diagnostics | 114 `GucGeneratorTests` cases compile generated declarations and verify exact contracts; the complete generator suite passes 1076 cases |

The initial configuration milestone passed 39 focused backend cases on PostgreSQL 18.6/Linux x64.
Plain `dotnet test` passed 3447 cases, zero failures/skips. Non-incremental Release build: zero warnings/errors. Public
configuration/initialization guides, README and the native preload sample are updated. Documentation
build, type check and API freshness pass; 104 API pages contain 1118 members and the site builds 133 pages. XML review finds zero omissions in 755 internal declarations; 415 C# source/template
files have no opening-brace blank lines or warning suppressions. Existing duplicate-404/missing-site-URL
site warnings remain visible.

Assign/show have typed reads but no SQL executor, since PostgreSQL cannot reliably distinguish normal
SET from every restoration phase. Unexpected assign failures are FATAL; show failures are ERROR in a
transaction and FATAL outside it. The lifecycle work below adds independent logging in all hook phases. Shared
preload metadata/defaults/labels must currently be ASCII. Managed hooks and initializers run in the
postmaster and continue in forked backends. PG18's native DisallowInFile flag blocks ALTER SYSTEM
but does not by itself reject manually supplied custom UserSet file values; tests and public docs
preserve this observed behavior.

Remaining full-port work is explicit: raw placeholder behavior,
mixed-encoding shared-preload metadata, complete source
and placeholder-privilege combinations, parallel-worker propagation, allocation/root measurements,
packaged GUC consumer and drop/reinstall cases, and actual PostgreSQL 13–19 beta plus Windows/Linux/macOS
execution. The broader G01–G60 inventory and review dispositions are retained in `.git/testagent/guc/`;
passing this milestone is not full GUC or pgrx parity.

### Configuration lifecycle and logging evidence

`[assembly: PgGucPrefix("name")]` now composes native prefix checks after all settings register and
before optional managed initialization, including libraries with no settings or managed callbacks.
`ANKUS015` rejects null, embedded-zero and malformed-Unicode transport; literal case, empty/dotted
prefixes, duplicate coalescing and deterministic ordering preserve native semantics. PostgreSQL
13–14 warn about matching placeholders; PostgreSQL 15+ removes them and reserves future first
components. Non-ASCII backend prefixes convert through the database encoding; native-only shared
preload currently requires ASCII.

GUC hooks receive independent read/log/SQL capabilities. `PgLog` can filter and report structured
messages in check/assign/show, including reload before transaction startup, abort restoration and
ParameterStatus reporting, without granting SQL access. Native guards contain conversion/reporting
ERROR beneath managed frames; owned diagnostics and temporary contexts are released. Explicit
FATAL/PANIC retains terminal severity after managed finally, including when diagnostic encoding
fails. Unexpected hook errors retain the existing phase-dependent failure policy.

Review identified diagnostic source-pointer ownership differences: PostgreSQL 13–16 CopyErrorData
borrows five source/translation strings; 17+ copies them, but FreeErrorData treats them as constants.
Shared native copy/free helpers now retain and release those strings across GUC, SPI and aggregate
error guards. Exact supported-version source tags were inspected; execution below is only PG18.6/Linux.

| Requirement | Concrete evidence |
|---|---|
| Prefix-only native load, placeholder adoption/removal, literal case/dotted prefixes, rollback and independent preload backends | `DeclaredSettingsAreAdoptedBeforeUnknownPlaceholdersAreRemoved`, `PrefixOnlyLibraryPreservesLiteralCaseAndFirstComponentReservation`, `ReservationSurvivesRollbackWithPlaceholderHistory`, `PrefixOnlySharedPreloadReservesNamesInEveryBackend` |
| UTF8/LATIN1 native prefix encoding and isolated shared-preload rejection | `Latin1PrefixReservationUsesDatabaseEncoding`, `PrefixOnlySharedPreloadRejectsUnicodeWithoutMetadataMasking` |
| Full structured logging, source fields, old values, SQL denial and abort/function restoration | `AssignmentLogsStructuredDiagnosticsDuringAbortAndFunctionRestoration` pins ordered events and client/server-specific fields |
| Filtering before conversion, reload and parameter-report phases | `ShowLoggingHonorsClientThresholdsWithoutEnablingSql`, `ReloadCheckLogsWithoutTransactionAccess`, `ParameterStatusShowLogsOutsideTransactions`, `Latin1LoggingPreservesAbortAndReportMessagesAndConversionRecovery` |
| Actual managed finally, terminal severity and healthy-peer/recoverable-session behavior | `ShowErrorPreservesDiagnosticsAndSameSessionRecovery`, `AssignmentReportsTerminateTheAffectedBackend`, `ShowFatalPreservesTerminalSeverity`, `CheckFatalPreservesTerminalSeverity`, `Latin1TerminalDiagnosticConversionKeepsFatalSeverity` |
| Thread-local scope isolation, nesting, disabled fallback, exact diagnostic transport and release counts | 47 `NativeLogTests` cases including `LoggingCapabilityDoesNotEnableTransactionApis`, `NestedAndDisabledScopesRestoreTheirExactBinding`, `NativeFailuresReleaseOwnedDiagnosticsAndAllowRecovery`, `TerminalReportsRetainManagedUnwindSemantics` |
| Prefix declaration compiler contracts and callback scope/lifetime composition | 21 new prefix generator cases; 135 focused GUC generator cases pass, including full/minimal native diagnostic ownership assertions |

Verification: 47 runtime, 135 generator, and 70 focused backend/diagnostic cases pass. Plain
`dotnet test`: 3534 passed, zero failures/skips, 3m06.420s. The 87 new cases comprise 47 runtime,
21 generator and 19 backend cases. Non-incremental Release build: zero warnings/errors.
XML scan: 775 internal declarations, zero omissions. Style scan: 426 C# source/template files,
zero opening-brace blanks or warning suppressions. Public configuration/logging/native-boundary guides,
README, sample and generated API are updated. `pnpm build`, `pnpm check` and API `--check` pass:
105 API pages, 1120 members and 134 site pages. Existing duplicate-404/missing-site-URL site warnings
remain visible. No reference checkout or consumer style template was changed.

Remaining full-port scope includes raw placeholders, mixed-encoding preload metadata,
remaining source/privilege combinations, worker propagation,
allocation/root measurements, packaged GUC consumers and the complete PostgreSQL/platform matrix.
Prefix and logging tests do not establish full GUC or pgrx parity.

### Configuration source, worker, lifetime, and package evidence

The existing native configuration bridge now has additional direct backend evidence for startup
priority, original setter privileges, actual parallel workers, allocation ownership and cold SDK
consumers. These checks required test/sample additions and documentation, with no runtime or native
bridge changes.

| Requirement | Concrete evidence |
|---|---|
| File, database, role, database-role and client priority; session/local overrides and reset defaults | `StartupSourcesPreservePriorityAndResetValues` asserts exact native current/reset/source metadata, typed reads, rollback and unchanged previously connected sessions |
| Original startup check sources after deferred registration | Both `PlaceholderStartupSourcesReachTypedCheckHooks` cases exercise all five sources through backend function loading and session preload, including boot checks and RESET |
| Original setter identity and replay-time parameter grants | Four `PlaceholderAdoptionRechecksOriginalSetterGrant` cases cover grant absence/presence/revocation/addition; both `PlaceholderMaskedAndLocalStatesRecheckTheirOwnRoles` cases check independently authorized SET/LOCAL histories, exact native warnings, COMMIT/ROLLBACK and recovery |
| Worker values, enum aliases/hidden labels, NULL versus empty, exact real bits, regenerated extras and independent managed initialization | Four `BackendLoadedWorkersRestoreTypedValuesAndRegenerateExtras` cases require launched workers in native EXPLAIN, foreign PIDs for all 30,000 result rows, exact typed values and worker-created extra data; both `NativePreloadWorkersRestoreValuesWithoutManagedPostmasterState` cases independently verify native shared preload |
| Worker mutation denial, function-local restoration and failed worker startup recovery | Both `WorkerSetRejectsAndLeaderRecovers` cases, `WorkerFunctionSettingsRestoreValueAndExtra`, and `WorkerRestoreFailurePreservesLeaderAndRecovers` pin SQLSTATE/native diagnostic identity, preserved leader state, restored value/extra and subsequent worker execution |
| Copied managed objects and native current/reset/prior/masked allocation lifetimes | `ManagedSnapshotsCollectWhileNativeStackRetainsBytes` observes all eight weak-reference categories collecting while 64 KiB native strings/extras remain exact across savepoint/commit/rollback/reset |
| Warmed native allocation and diagnostic cleanup | UTF8 and LATIN1 `WarmedNativeAllocationsStabilizeAcrossStateAndErrorPaths` cases execute three measured batches of 64 complete cycles after warmup, covering accepted and validation-only values, repeated rejections/exceptions, successful accented payloads, result/diagnostic/log encoding failures, and recovery |
| Cold SDK packages without repository imports/style, with SQL drop/reinstall lifetime | `PackedGucConsumerPreservesHooksAndNativeStateAcrossReinstall` and `PackedNativeGucConsumerPreloadsWithoutSqlExports` each restore into an initially empty package cache and publish; full typed hooks/extras/normalization/diagnostics and minimal native-only shared preload survive their separate SQL lifecycle checks |

PostgreSQL serializes a nondefault NULL configuration string as empty text; an untouched default
NULL remains NULL in workers. Check hooks rebuild extra data in the worker instead of transporting
managed objects. Ordinary worker SET remains forbidden, while native function-local SAVE settings
restore the previous value and extra. The public guide records these native distinctions.

Allocation assertions require identical retained GUCMemoryContext used bytes after each batch and
zero surviving Ankus configuration read/check/assign/show/logging contexts. A Linux/glibc-specific
supplement measures allocated arena plus mmap bytes, validates that counter against a retained and
released 4 MiB allocation, and limits growth to 512 KiB across measured batches. That allowance is
one eighth of one batch leaking a single 64 KiB payload per cycle; it does not establish the absence
of arbitrarily small leaks. The host has glibc 2.41; the supplemental probe requires glibc 2.33+ and
is not cross-platform allocation evidence. Successful-test context output is not retained by the
default runner, so absolute byte totals are not claimed. Managed weak-reference/state assertions
run independently of that platform-specific allocator probe.

The source tests serialize their shared parameter ACL catalog mutations. Review also corrected a
fixture that batched its initial SET with BEGIN: the initial session value must commit before
constructing the transaction history under test. Neither correction changes PostgreSQL semantics.
Static assertion and behavior-gap reviews are recorded in `.git/testagent/guc-state/`; no executed
mutation coverage is claimed. The focused source command passes nine cases with zero failures/skips;
all ten worker, three lifetime and two package cases also passed their focused executions.
Final verification: plain `dotnet test` passes 3558 cases with zero failures/skips in 3m37.716s on
PostgreSQL 18.6/Linux x64. Non-incremental Release build: zero warnings/errors. XML scan: 789 internal
declarations, zero omissions. Style scan: 433 C# source/template files, zero opening-brace blanks or
warning suppressions. README/configuration guide updates, `pnpm build`, `pnpm check`, and API `--check`
pass; the API remains 105 pages/1120 members and the site builds 134 pages. Existing duplicate-404 and
missing-site-URL warnings remain visible. No reference checkout or consumer style template changed.

Remaining full-port work includes safe explicit treatment of raw placeholder storage,
mixed-encoding shared-preload metadata, the rest of the pgrx runtime
and tooling inventory, and actual PostgreSQL 13–19 beta validation on Windows/Linux/macOS. The native
CUSTOM_PLACEHOLDER flag assumes PostgreSQL-owned string-placeholder layout and lifetime; exposing it
on arbitrary typed declarations would violate those assumptions. Existing typed configuration
options continue to reject that internal flag. This milestone does not establish full GUC or pgrx parity.

### Work in progress

The owned Native AOT runtime now checkpoints its managed services before PostgreSQL fork
and repairs them in each child. Generated initialization and configuration callbacks use
that runtime automatically. Linux x64 has direct PostgreSQL evidence above. macOS, Windows,
the remaining PostgreSQL matrix, and packaged consumer validation are still in progress.

The generated API currently supports accessible, synchronous static methods with by-value
`bool`, `sbyte`, `short`, `int`, `long`, `uint` (OID), `float`, `double`, `decimal`, `string`, `byte[]`, `Guid`, `PgJson`, `PgJsonb`, `PgNumeric`,
the .NET/full-range PostgreSQL temporal types, network/geometric values, typed ranges, generated enums and composite/record tuples. Arrays use `T[]` or `PgArray<T>`; `params T[]` declares
SQL variadic parameters. Nullable forms and `void` results are supported. Strictness follows argument nullability
unless overridden by `PgNullInput`. Named/defaulted arguments and PostgreSQL execution options are supported.
SETOF and TABLE cover these supported value families through `IEnumerable<T>` and named tuple elements.
Custom installation SQL strings/files and generated declarations, including operators and casts, share a dependency-ordered graph.
The native library, control file, and versioned SQL are published and installed through PostgreSQL's extension mechanism.
Full `[PgTest]` generation, provisioning/lifecycle/package tooling, extension upgrade scripts, more data types,
the remaining SPI and PostgreSQL APIs, and the PG13–19 matrix remain pending. `Ankus.Sdk` is now a
consumable NuGet project SDK; repository development uses project references with the same native targets. PostgreSQL discovery is
available to non-CLI callers; the CLI uses registered installations or an explicit override.
Prerequisite installation is currently manual.

### Read-only reference repositories

- **[pgrx](https://github.com/pgcentralfoundation/pgrx)** — the port's reference implementation. Key paths:
  - `pgrx/src/` — runtime modules (`spi.rs`, `datum/`,
    `guc.rs`, `memcx.rs`, `trigger_support/`, `iter.rs`, `bgworkers.rs`, …)
  - `pgrx-macros/src/lib.rs` — the proc macros (`pg_extern`, `pg_trigger`, `pg_aggregate`, …)
  - `cargo-pgrx/` — CLI model to mirror (`new/init/build/schema/test/run/package`)
  - `pgrx-examples/` — example set to mirror in `samples/`
  - `pgrx-tests/`, `pgrx-unit-tests/` — test strategy reference
  - `v18-ONE-COMPILE-CHANGELOG.md` — one-compile `.pgrxsc` schema model (our `.ankusc` analogue)
- **[postgres](https://github.com/postgres/postgres)** — ABI source of truth. Tags:
  `REL_13_23`…`REL_18_6`, `REL_19_BETA3`. Key files are `src/include/fmgr.h`,
  `src/backend/utils/fmgr/dfmgr.c`, `src/backend/utils/fmgr/fmgr.c`, and
  `src/include/utils/elog.h`.
- **[runtime](https://github.com/willibrandon/runtime)** — .NET runtime source (Native AOT: `src/coreclr/nativeaot/`, PAL: `src/coreclr/pal/src/`).
- **[roslyn](https://github.com/dotnet/roslyn)** — compiler source (function-pointer grammar, source generators).
- **[msbuild](https://github.com/dotnet/msbuild)** — build conventions and type-style rules, compared with `runtime`.
- **[sdk](https://github.com/dotnet/sdk)** — .NET SDK and CLI conventions.

### PostgreSQL memory contexts and allocations

The memory-context port now has `PgMemoryContext`, predefined native context selectors,
owned AllocSet children, borrowed handles, parent/name/liveness queries, nested `Run` scopes,
three native reset variants, and native allocation statistics. `PgAllocation` provides checked
byte and unmanaged-value copies, zeroed/no-OOM allocation, resize, clear, native-owner lookup,
deterministic free, and explicitly unsafe pointer access. Contexts and chunks are validated by
monotonic native identities; managed handles can survive callbacks while their native owners remain
alive. Backend-thread/provider checks reject detached and foreign-provider access.

An independent guarded memory capability now accompanies scalar, set, trigger, event-trigger,
aggregate/release, initializer, and GUC callbacks. It does not use SPI scratch contexts or
subtransactions. Native errors restore the context selected at operation entry and transport
owned diagnostics below the managed stack. Assign/show-only GUC fixtures allocate and read memory
while their SQL capability remains unavailable. Native-only declarations omit unused memory helpers.

Reference review covered read-only pgrx `memcxt`, `memcx`, `palloc/pbox`, `pgbox`, their examples/tests,
and PostgreSQL context implementations/headers. Backend testing found and corrected first-reset
callback re-registration, context-name storage freed by reset, diagnostic copying from ErrorContext,
callback-stack-address provider identity, and native helper emission in GUC-only libraries.
Names now survive explicit resets, convert through server encoding, and reject malformed UTF-16;
failed encoding does not leave an orphaned context.

| Contract | Exact evidence |
| --- | --- |
| Thread/provider/callback lifetime and owned diagnostics | `DetachedOperationsRequireBackendCapability`, `CapabilitiesRemainThreadBound`, `HandlesRemainUsableAcrossCallbackEnvelopesWithSameProvider`, `ForeignProviderRejectsContextAndAllocationWithoutNativeCall`, `NativeErrorsReleaseOwnedDiagnosticsAndRecover` |
| Checked byte ranges, typed copies, null/no-OOM distinction and failed resize/free | `InvalidRangesAreRejectedBeforeNativeCalls`, `NativeSizeOverflowRangesAreRejectedBeforeNativeCalls`, `ReadWriteAndClearPreserveBytesAndOffsets`, `TryAllocateReturnsNullOnlyForSuccessfulNativeNull`, `TryAllocatePropagatesNativeErrors`, `DisposeFailureRetainsAllocationForRetry` |
| Generated ABI and scope restoration | `GeneratedCallbacksBindMemoryWithinExceptionBoundary`, `SetMemoryScopeEnclosesCreationAdvancementAndBothDisposalModes`, `AggregateStateReleaseNativeAbiCarriesIndependentMemoryCapability`, `NativeOnlyDeclarationsOmitMemoryDispatch` |
| Reset/delete subtree and repeated invalidation | `ResetVariantsPreserveNamesAndInvalidateTheirExactSubtree`, `MemoryContextRoundTripPreservesOwnershipAndInvalidatesResetData` |
| Native allocator bounds, ownership, error recovery, nested scopes and catalog-visible cleanup | `AllocationBoundariesPreserveBytesAndActualNativeOwner`, `AllocationErrorsPreserveContentsAndSelectedContext`, `NestedScopesRestoreAfterFailureAndAllowDeletionRetry`, `NativeInventoryProvesOwnedAndBorrowedContextLifetimes` |
| Active native callback storage protection | `ActiveNativeCallbackStorageRejectsDestructiveResets` |
| Later callbacks, commit/rollback and subtransactions | `SavedHandlesSurviveCallbacksAndExpireAtTransactionEnd`, `SubtransactionRollbackInvalidatesOnlyItsOwnedContext` |
| Iterator retention, early shutdown and failure | `IteratorCallbacksRetainAndDisposeNativeStorage`, `IteratorFailureReclaimsNativeStorageAndRecovers` |
| UTF8/LATIN1 identifiers and failure cleanup | `ContextNamesUseServerEncodingAndRecoverWithoutLeaking` |

Development evidence: 44 focused runtime cases passed; the combined memory/GUC backend run passed
101 cases (zero failures/skips, 2m14.452s). Four final focused cases then verified protected resets,
both database encodings, and transaction-free reload observation. Plain `dotnet test` passed
3638 cases (zero failures/skips, 3m13.702s), including all 20 new memory backend cases, on PostgreSQL
18.6/Linux x64. The final non-incremental Release build has zero warnings/errors. XML inspection
covered 848 internal declarations with no omissions; 445 C# source/template files have no extra
opening-brace blank lines or warning suppressions. Documentation build/type/API freshness checks
pass: 108 API pages, 1155 members, and 138 site pages. Existing site warnings about the duplicate
404 route and missing sitemap site URL remain visible.
The public memory-context guide, execution guide, README, and native boundary documentation describe
the implemented ownership and failure contracts.

The full run also exposed a timing-dependent GUC reload test. The observed backend now receives
only Close/Sync protocol messages for statements prepared before the reload; a separate backend
signals configuration reload. This proves the exact `source=File;sql=unavailable` result without
opening a SQL transaction in the observed backend while waiting for the signal.

### One-shot memory cleanup callbacks

`PgMemoryContext.RegisterResetCallback(Action)` now returns a cancellable `PgMemoryCallback`.
Native registrations and managed roots are consumed before user code, including when the action
throws. LIFO ordering, registration during drain, pending older callbacks after ERROR, and native
payload lifetime follow pgrx/PostgreSQL. Cancellation releases managed captures immediately and
retains an inert native record until cleanup, supporting PostgreSQL 13–18 without an unregister API.
The implementation preserves PostgreSQL's native ERROR behavior after managed frames return.

Cleanup retains owned SPI plan/cursor disposal while masking SQL, logging and inherited
GUC/aggregate capabilities; the enclosing bindings are restored after success or failure.
Scoped native owner protection and reset-retention markers allow independent nested resets while
rejecting reentry into active teardown trees. An adversarial abort probe exposed a
missing guard on resetting PostgreSQL's own transaction context: GDB showed `CurrentTransactionState`
filled with PostgreSQL's freed-memory `0x7f` pattern. Infrastructure context resets/deletes are now
rejected, and tests target executor ancestry separately. Caught native errors also restore interrupt
holdoff counters before returning into abort cleanup. An adopted parent cursor exposed a late
subtransaction leak: `CurTransactionContext` had already changed to its surviving parent. Native
callback registrations now retain a transaction ancestor for deferred closes during its own drain.

ErrorContext-owned callbacks require a precise native restriction. Error recovery could recursively
delete their active owner, and PostgreSQL's private error stack cannot be isolated by changing the
ErrorContext pointer. Such callbacks retain managed cleanup but receive direct `55006` (object in use)
diagnostics for guarded memory/SPI calls before PostgreSQL's error machinery is entered.
PostgreSQL 19 also drains ErrorContext after normal outermost reporting; that version-specific path
is source-reviewed, with actual execution still required in the matrix.

| Contract | Exact evidence |
| --- | --- |
| Rooting, cancellation, failed registration, one-shot consumption and collection | `NativeRegistrationRootsWrapperAndCaptureUntilConsumption`, `FailedRegistrationReleasesCaptureAndRejectsSavedRoot`, `FailedCancellationPreservesPendingActionAndReleasesDiagnostics`, `CallbackIsConsumedBeforeUserActionAndRunsExactlyOnce`, `PendingNativeOwnershipAndTerminalCleanupHaveExactManagedRootLifetimes` |
| Provider/thread isolation and capability restoration | `ForeignProviderRejectsCancellationAndDispatchWithoutConsumingRoot`, `CallbackRootsAndCancellationRemainOnTheirOwningThread`, `CallbackMasksInheritedBackendAndLogThenRestoresEveryBinding`, `CallbackRetainsOwnedResourceCleanupBindingAfterRegistrationScopeEnds` |
| LIFO, reentrant registration/cancellation, failure retry and exact subtree ownership | `ResetCallbacksAreOneShotAndLifo`, `CallbackRegistrationAndCancellationDuringDrainPreserveNativeOrder`, `CallbackFailuresConsumeOnlyTheFailingRegistrationAndRetryPendingCleanup`, `ResetVariantsPreserveNativeCallbackTreeOrderAndPayloadLifetime` |
| Nested independent resets and active native owner protection | `NestedIndependentResetsPreserveOuterRetainState`, `TeardownProtectsOwnerAndAncestorsWhileKeepingPayloadAndIndependentMemoryUsable`, `NativeAbortCleanupProtectsOwnerOutsideCurrentContext`, `InfrastructureResetsAreRejectedAndOwnedChildrenRemainUsable` |
| Implicit query/commit/rollback/savepoint cleanup and errors | `ImplicitCommitAndRollbackCleanupRunsExactlyOnce`, `ImplicitSavepointCleanupRespectsItsOwningTransaction`, `ImplicitQueryCleanupPropagatesOwnedCallbackErrorAndRecovers`, `ImplicitCommitCleanupPropagatesOwnedCallbackErrorAndRecovers` |
| Primary plus cleanup errors, ordered native diagnostics and client recovery | `SqlFailureDrainsImplicitCallbacksAndReportsLastProtocolError` checks both server-log errors; Npgsql exposes the final callback error when cleanup throws |
| Exact native plan/portal release and disposal order | `ExplicitResetCallbacksDisposeOwnedSpiPlansAndCursors` repeats five times; `ImplicitTransactionCallbacksDisposeOwnedSpiPlansAndCursors` covers commit/rollback/SQL abort; `ImplicitSavepointCallbacksDisposeAdoptedParentCursorAndOwnedPlan` proves closure of a surviving parent cursor |
| ErrorContext scratch, reclaimed descendants and error-handler restrictions | `ErrorContextFailuresKeepOwnedDiagnosticsOutsideResetScratch`, `ErrorContextSpiFailurePreservesDiagnosticsAndRestoresCaller`, `ErrorContextChildFailureRestoresLiveAncestorAndOriginalCaller`, `ErrorHandlerCallbacksPreserveNativeErrorStackAndRetainedResources` |
| UTF8/LATIN1 diagnostics and remaining callback ownership after conversion failure | `CallbackErrorsPreserveEncodingAndPendingDrainOwnership` |

Focused validation passes 19 runtime cases (962ms), all 1113 generator cases (9.943s), and
52 backend cases (1m22.548s), with zero failures/skips. The new test files contain 39 methods,
71 data-expanded cases, and 222 assertion sites, including shared helpers. Assertion and
public-outcome reviews cover values, state transitions, exact errors, root release, native resource
inventory and same-session recovery; no numerical code coverage or executed mutation claim is made.
Plain `dotnet test` passes all 3709 cases with zero failures/skips in 3m20.988s on PostgreSQL
18.6/Linux x64. The non-incremental Release build passes with zero warnings/errors (4.98s).
The style scan covers 451 C# source/template files with no extra opening-brace blanks or warning
suppressions. README, public memory guide, native boundary guide and generated API reference are
updated; the site builds 139 pages with 109 API pages/1158 members. Existing duplicate-404 and
missing-site-URL warnings remain visible. XML inspection covers 856 internal declarations with
zero omissions; `pnpm check` and API `--check` pass. Reference checkouts and consumer style
templates are unchanged.

### Typed, aligned, and transferred native allocations

`PgAllocationOptions` adds explicit zeroed and huge size policies. Generic allocation factories
check `count * sizeof(T)` before entering native code; span copies preserve independent unmanaged
bytes. `AllocateUtf8String` copies strict UTF-8 plus one terminator without server-encoding conversion.
`PgMemoryContextOptions` carries PostgreSQL AllocSet presets and custom sizes, validated against
the selected headers before native assertions. `RunTransient` selects a fresh child, restores its
caller, and attempts deletion on every exit, preserving action/restoration/deletion diagnostics.

Allocation metadata now retains requested size, alignment, and huge policy. Native aligned storage
uses PostgreSQL's own redirect chunks, preserving `pfree` and native-owner compatibility. Checked
payload/padding arithmetic prevents overflow into undersized allocations. Ordinary throwing resize
uses `repalloc`/`repalloc_huge`; aligned and no-OOM resize allocate, copy the exact requested prefix,
and free the old chunk only after replacement succeeds. Optional tail clearing never copies old
padding into newly exposed bytes. Detach consumes the checked owner without freeing storage;
unsafe adoption rejects an already tracked pointer or wrong native owner, and does not free the
caller's chunk if registration fails.

Source review of PostgreSQL 13–19 release tags found aligned allocation starts in 16, aligned
no-OOM safety requires 16.15/17.11/18.6/19 beta 3, and PostgreSQL 16 aligned realloc loses flags.
The bridge avoids that resize path and rejects unsupported calls. PostgreSQL 19 prereleases share
numeric version 190000, so the gate also checks beta labels and rejects ambiguous development
snapshots for aligned no-OOM calls. These are source-reviewed gates, not an executed version matrix.

| Contract | Exact evidence |
| --- | --- |
| Option combinations, alignment bounds, typed overflow and independent copies | `SupportedPoliciesRemainDistinctThroughAllocation`, `ValidAlignmentPreservesExplicitRequest`, `TypedAllocationOverflowNeverReachesNativeCode`, `LargestRepresentableTypedProductReachesNativeSizeValidation`, `TypedAllocationsAndCopiesPreserveExactValuesAndCheckedCounts` |
| Strict terminated UTF-8, invalid text and UTF8/LATIN1 native bytes | `InvalidUtf8StringsAreRejectedBeforeNativeCalls`, `NativeUtf8StringsKeepExactTerminatedBytesAcrossDatabaseEncodings` |
| Thirty-two 3319-byte pgrx witnesses, grow/shrink, exact zero tail, alignment and native owner | `ThirtyTwoAlignedPgrxWitnessesPreserveBytesAndNativeOwnership` |
| Invalid ordinary/huge payload and padding limits preserve pointer, bytes, policy and session | `InvalidSizeAndAlignmentPaddingErrorsPreservePointerLengthBytesAndSession`; scripted allocator NULL is separately proved by `TryResizeNullPreservesOldHandleAndSuccessfulRetryReplacesIt` |
| Native AllocSet sizes, presets, non-power-of-two minimum blocks, malformed-size rejection and inventory | `NativeAllocSetSizingPreservesPresetsAndNonPowerOfTwoMinimumBlocks`, `NativeAllocSetValidationRejectsMalformedSizesWithoutLeakingContexts` |
| Custom initial and maximum ordinary-block growth with retained payloads | `NativeAllocSetGrowthUsesCustomInitialAndMaximumBlockSizes` retains 200 chunks, checks native block counts and growth at each boundary, and verifies reset/deletion |
| Transient selection/restoration/deletion, nested owners and preserved cleanup failures | `TransientInitialSwitchFailureDeletesUnusedChild`, `TransientActionRestoreAndDeleteFailuresRemainIndependentlyObservable`, `TransientContextsRestoreAndDeleteAcrossActionNativeAndCleanupFailures`, `NestedTransientContextsHaveIndependentIdentitiesAndExactCurrentRestoration` |
| Detach/adopt, duplicate and wrong-owner failures, reset/delete cleanup and exact raw bytes | `TransferredPointersAcquireFreshExclusiveOwnershipAndExpireWithNativeCleanup`, `DetachedStorageRemainsContextOwnedUntilNativeReset`, `FailedAdoptionLeavesRawOwnershipWithCaller` |
| Adopted aligned allocations across callbacks, commit/rollback and savepoint cleanup | `AdoptedAlignedAllocationsExpireAfterNativeTransactionCleanup`, `AdoptedAlignedAllocationsRespectSubtransactionOwnership` |

Development validation passes 67 new runtime cases (913 ms), all 1113 generator cases (9.417 s),
and 52 new backend cases (1m17.744s) on PostgreSQL 18.6/Linux x64 with assertions enabled.
Assertion review added the independent ordinary-block growth witness; its focused run passes
in 1m11.493s. The final plain `dotnet test` passes all 3829 cases with zero failures/skips in
3m13.046s, including all 53 new backend cases. The growth witness closes a gap where a wrong
maximum block size could have passed a test that only allocated dedicated large blocks.

The final non-incremental Release build passes with zero warnings/errors (4.40s). XML inspection
covers 863 internal declarations with no omissions; 457 C# source/template files have no extra
opening-brace blank lines or warning suppressions. `AGENTS.md` has no personal paths. Public memory
and native-boundary guides, README and generated API pages are updated. Documentation build,
type checks and API freshness pass: 111 API pages, 1180 members, 141 site pages; `pnpm check`
reports zero errors/warnings/hints. Existing site duplicate-404 and missing-site-URL warnings
remain visible. Reference repositories and consumer coding-style templates remain unchanged.

At this milestone, allocation above `MaxAllocSize` was still unverified. Small allocations with
the huge flag prove routing and policy retention only; rejected large/padded sizes prove bounds and recovery.
The installed PostgreSQL 18.6 build enables `RANDOMIZE_ALLOCATED_MEMORY`, which writes the entire
payload, so a sparse-endpoint test cannot avoid more than 1 GiB of resident work. A dedicated
resource-budgeted huge-allocation witness was therefore required, including a resize whose target
stays above the ordinary limit. The later resource-budgeted lifecycle section records that actual
release-server execution. Native boxes, virtual `MemCx` parameters and allocator variants were also
still required at this stage; subsequent sections record those implementations. Node allocation,
raw/custom datum contracts, broader resource witnesses and the actual PostgreSQL/platform matrix
remain required alongside the full inventory below. The subsequent borrowed-allocator section
records actual Slab/Generation/Bump cases and controlled native registry/allocator-boundary failure
witnesses. Those controlled boundaries do not claim physical resource exhaustion or huge allocation.

### Native boxes and borrowed typed references

`PgNativeBox<T>` owns individual release rights, `PgContextValue<T>` leaves reclamation to the
native context, and `PgNativeReference<T>` borrows without acquiring release rights. All expose
copied unmanaged values; they do not manufacture managed references over native lifetimes.
`ReleaseToContext` consumes only the old owning wrapper and keeps existing checked views live.
Raw detach consumes shared tracking without freeing bytes. Failed free or detach retains the
previous ownership state for retry. None of these wrappers has a finalizer or invokes a native
value's constructor, `Dispose`, or recursive pointer cleanup.

Cloning copies all `sizeof(T)` bytes, including padding and shallow pointer fields, into independent
storage in the explicit or current destination context with default allocation policies. Checked
allocation views retain their byte offset and observe resizing; shrinking below the complete value
rejects access. Unsafe raw borrows accept stack, interior, and resource-owned addresses without
reading a palloc header or creating a native allocation record. Their captured context identity
and native-width reset generation are checked before any copy or pointer extraction. Explicit
reset invalidates old generations even when the context survives; implicit native cleanup removes
the old identity. Generation exhaustion permanently retires borrowing instead of wrapping into a
previously live generation. A callback error before invalidation leaves existing references live
until the native callback drain advances.

Raw null adoption and borrowing return nullable managed references without backend access;
context-owned adoption requires a non-null palloc-compatible address. Initialized `TryCreate`
factories use null only for allocator exhaustion. Initialization failure attempts native cleanup
and preserves both diagnostics if cleanup also fails. A raw lifetime anchor does not prove
allocator ownership or detect shorter external lifetimes such as stack return or resource closure.

| Contract | Exact evidence |
| --- | --- |
| pgrx five-value, zeroed/uninitialized and raw-null construction | `NativeBoxConstructionPreservesFiveZeroAndNullPointerContracts`, `NullRawViewsAndOwnersNeedNoCapabilityOrLiveContext` |
| Immediate individual native reclamation while the context remains live | `IndividualDisposalImmediatelyReclaimsNativeChunkWithoutResettingItsContext` observes a 64 KiB block returning to baseline through both allocator accounting and the native context catalog |
| Context transfer preserves aliases and consumes only individual release rights | `ReleaseToContextPreservesExistingBorrowsAndConsumesOnlyIndividualOwnership`, `FailedContextTransferRetainsOwnerUntilExplicitDisposalOrRetry` |
| Failed initialization/free/detach preserves errors and ownership | `FailedInitializationFreesNewAllocationAndPreservesCleanupError`, `FailedFreeRetainsOwnerAndSuccessfulRetryInvalidatesBorrow`, `FailedDetachPreservesOwnerAndViewsUntilSuccessfulTransfer` |
| Raw detach invalidates all old views before exclusive re-adoption | `RawDetachInvalidatesSharedViewsBeforeFreshAdoption` |
| Exact padding, shallow embedded pointers, independent storage and selected/current destination | `NativeClonesPreservePaddingShallowPointersAndTargetOwnership`, `RawReferenceCloneCopiesExactRepresentationIntoCurrentContext` |
| No pointee disposal or native freeing from garbage collection | `NativeCleanupNeverInvokesPointeeDispose`, `ManagedCollectionLeavesNativeTypedStorageOwnedByItsContext` |
| Typed offset views follow relocation and reject narrowed ranges | `BorrowedOffsetViewsFollowResizeAndRevalidateBounds`, `BorrowRejectsPartialAndNativeOverflowRangesBeforeNativeAccess` |
| Stack/interior aliases, exact reset-tree boundaries and successive generations | `RawStackAndInteriorAliasesExpireWithTheirAnchorGeneration`, `SuccessiveRawGenerationsRespectParentAndDescendantResetBoundaries` |
| Failed reset preserves references until invalidation actually runs | `FailedResetPreservesRawGenerationUntilSuccessfulRetry` |
| Separate callbacks, commit/rollback, and savepoint cleanup | `TransactionCleanupExpiresOwnedContextAndRawNativeViews`, `SavepointCleanupRespectsTypedOwnershipAndRawAnchor` |
| Native-width tokens and backend/provider/thread restrictions | `RawReferencePreservesNativeWidthGenerationBitPattern`, `RawReferencesRemainBoundToBackendCapabilityAndProvider` |

Focused development runs pass all 34 runtime cases (863 ms), all 1113 generator cases (12.779 s),
and all 34 new backend cases (1m16.275s) on PostgreSQL 18.6/Linux x64. Every backend case checks
native context inventory and same-session recovery. Assertion review added the failed-transfer
retry/disposal and immediate individual-reclamation witnesses; stale views alone could have
missed an omitted native free. Initial IDE0305 build failures were fixed with collection expressions
without weakening byte-copy assertions. Native generation exhaustion is source-reviewed and the
token bit pattern is tested directly; no actual generation-counter wrap was executed. Absence of
per-reference native registry allocation is source-reviewed, not inferred from context byte totals.

IDE0004 is now an error across repository builds. A non-incremental build first rejected existing
redundant casts; Roslyn's IDE0004 code fix updated 19 C# files. The subsequent non-incremental
Release build passes with zero warnings/errors (5.00s). `AGENTS.md` and the development guide
record the rule, while consumer templates retain their own style choices.

Final plain `dotnet test` passes all 3897 cases with zero failures/skips (3m43.662s), including
the installed-package and generated-consumer suites. The IDE0004 diagnostic-specific formatter
verification passes without changes. The source/style scan covers 466 C# source/template files
with no extra opening-brace blank lines or warning suppressions; `AGENTS.md` contains no personal
paths. XML inspection covers 878 internal declarations with zero omissions. README, the public
memory guide, native-boundary documentation and generated API pages are updated.
Documentation build, `pnpm check` and API freshness pass with 114 API pages,
1210 members and 144 site pages; type checks report zero errors/warnings/hints. Existing site
duplicate-404 and missing-site-URL warnings remain visible. Reference repositories are unchanged.

Memory-only unmanaged wrappers do not establish SQL type identity or PostgreSQL C layouts.
Native box/datum conversion, node APIs and the full version/platform
matrix remain required. Subsequent sections record virtual context parameters and borrowed native
allocator/failure witnesses. Custom release policies and unsized/context-bound datum layouts are
not claimed by these three sized ownership wrappers.


### Virtual memory-context parameters

Ordinary function, operator and cast methods accept by-value `PgMemoryContext` dependencies
without consuming SQL arguments. The shared parameter model keeps managed order separate from
contiguous SQL ordinals and drives validation, overload identity, declaration defaults/names,
NULL policy, scalar/set marshaling, dependencies, and operator/cast signatures. Multiple and
interleaved contexts are supported. The PostgreSQL limit counts 100 SQL inputs independently
of virtual parameters; unsupported types are still rejected rather than silently skipped.

SQL always supplies a non-null borrowed context. Nullable annotations and optional null defaults
are direct C# call contracts only. SQL strictness uses SQL parameters only. `PgParameter` on a
context reports `ANKUS004`; numeric and composite metadata retain their existing diagnostics.
By-reference contexts, arrays and context results remain unsupported. Specialized trigger,
event-trigger and aggregate callbacks retain their distinct signature contracts.

Reference review follows `pgrx/src/memcx.rs`, `callconv.rs`, `iter.rs`, and `pg_extern` wrapper/SQL
translation: virtual arguments do not advance native SQL slots, and initial set invocation runs
in its multi-call owner. Two deliberate C# adaptations are explicit in public docs: checked
handles retain a fixed owner snapshot rather than aliasing the global CurrentMemoryContext
pointer slot, and nullable virtual annotations do not affect SQL strictness. pgrx's optionality
inference includes optional Rust virtual arguments before filtering them from SQL.

Native set creation now selects its multi-call owner for the factory and GetEnumerator while
preserving the original caller's memory protection. Native finally restores the caller before
an error is raised. Later row callbacks retain their temporary-context behavior. Owner registry
invalidation is registered before iterator abort cleanup so direct owner storage remains readable
during Dispose; PostgreSQL still deletes children before parent callbacks.

A persistent reservation protects live set owners between cursor fetches. Managed destructive
operations reject owners and affected ancestors; ancestor ResetOnly is also forbidden because
PostgreSQL stores executor descriptors outside the surviving multi-call child. ResetChildren on
a suspended owner itself preserves direct owner storage and follows ordinary child reset rules.
Native executor cleanup retires the reservation through the existing registry invalidator.
No reparent API or native ABI change is introduced.

| Contract | Exact evidence |
| --- | --- |
| Executed scalar/set injection and contiguous SQL ordinals | `VirtualContextsPreserveCompiledScalarArgumentOrder`, `VirtualContextsPreserveCompiledSetFactoryAndNullableArguments` |
| SQL argument limit, defaults, nullability and overload identity | `VirtualContextsDoNotConsumePostgresArgumentLimit`, `VirtualContextNullabilityDoesNotChangeSqlStrictness`, `VirtualContextErasureDeterminesSqlOverloadIdentity` |
| Implicit type dependencies remain real graph edges | `VirtualContextsPreserveAutomaticSqlDependencyEdges` uses cycles that disappear if an automatic edge is omitted |
| Installed SQL signatures, defaults and NULL policy | `CatalogSignaturesDefaultsAndStrictnessExcludeVirtualDependencies` |
| Operator/cast SQL operand and typmod ordinals | `OperatorsAndCastsRetainSqlOperandAndTypmodOrdinals` |
| Fixed snapshot, nested SPI and guarded error restoration | `NestedCallbacksPreserveSnapshotProviderAndCurrentRestoration` |
| Factory/GetEnumerator owner and retained values across real scratch resets | `SetFactoriesRetainOwnerStorageAcrossActualPerRowScratchResets`, `DeferredIteratorBodiesUseCapturedOwnerRatherThanCurrentScratch` |
| Suspended owners, ancestor protection and child-only reset | `SuspendedOwnerGuardsPreserveCursorRecoveryAndExactChildResetSemantics`, `InterleavedPortalsRetainDistinctOwnersAndExpireIndependently` |
| Live direct-owner bytes during disposal followed by stale handles | `EarlyShutdownDisposesWithLiveInjectedPayload`, `ExecutorAbortPreservesDirectOwnerPayloadUntilRestrictedCleanupFinishes`, `NativeOutputFailurePreservesOwnerPayloadThroughAbortDisposal` |
| Original error, cleanup error and exactly-once disposal | `LifecycleErrorsPreserveDiagnosticsAndCleanupBeforeOwnerExpiry`, `DisposalErrorsPreserveLivePayloadObservationAndExactOnceCleanup` |
| Savepoint and transaction lifetime boundaries | `SavepointAbortExpiresSetOwnerAndPreservesTopTransactionControl`, `TransactionCompletionReclaimsSuspendedOwnerAndItsCapturedViews` |

The existing 1113 generator cases pass (zero failures/skips, 9.935s) after the shared model change.
All 59 new generator cases pass with zero failures/skips (3.224s). All 48 new backend cases
pass with zero failures/skips (1m19.984s) on PostgreSQL 18.6/Linux x64. Final plain `dotnet test`
passes 4004 cases with zero failures/skips (3m16.906s), including package and consumer suites. Assertion review strengthened automatic-dependency witnesses
and requires the reservation-specific error to distinguish it from unrelated native guards.
CA1861, CA1859 and MSTEST0037 findings in new tests were fixed without suppressions.

The non-incremental Release build passes with zero warnings/errors (5.11s). The IDE0004-specific
formatter verification passes without changes. Internal XML inspection covers 902 declarations with zero omissions; the source/style scan covers 470 C#
source/template files with no opening-brace blank lines or warning suppressions. AGENTS contains
no personal paths. README, function/memory/set guides, native-boundary documentation and generated
API pages are updated. Documentation build, `pnpm check` and API freshness pass with 114 API
pages, 1210 members and 144 site pages; type checks report zero errors/warnings/hints. Existing
site duplicate-404 and missing-site-URL warnings remain visible.

The native Delete reservation shares the guarded ancestry predicate but remains source-reviewed:
borrowed managed handles intentionally expose no native-delete operation. This phase does not
claim custom callback failures in every executor phase, native allocator exhaustion, or additional
PostgreSQL/platform execution. All other memory requirements and the full port/version/platform
inventory remain active.

### Borrowed allocator kinds and native acquisition rollback

Public context construction remains AllocSet, matching pgrx's safe factories. Borrowed Current,
parent and injected handles can represent native Slab, Generation or Bump contexts. Native fixtures
construct them against the selected server headers and capture them through a real generated scalar
callback. Catalog lookup and function metadata initialization occur before switching Current into
Slab; the callback returns a by-value integer. Transaction-owned fixture parents permit real native
deletion independently of borrowed managed wrappers.

Slab preserves exact-size allocation/reallocation and native errors for invalid sizes, including
no-OOM calls. Generation preserves variable-size storage and native reclamation behavior. Bump
permits allocation, typed context values, owner lookup from established registry provenance,
checked/raw borrowing, detachment and context cleanup. Native owner checks reject individual free,
all resize paths and header-based adoption before touching its headerless chunks. Failed operations
retain the handle, pointer, length, bytes and owner. No fake successful disposal or leaking Bump
replacement resize is introduced. Registration reserves C bookkeeping before native storage; native
ERROR and NO_OOM NULL release the unpublished record.

Name conversion uses independent AllocSet storage. Diagnostic reconstruction uses a TopMemoryContext
child, preserves source-location pointers until error flush, and releases every transport buffer.
After a throwing report begins, an ErrorContext reset callback owns scratch deletion; registering it
after reporting avoids recursive error resets consuming the input fields. PostgreSQL 19 can reset
ErrorContext on successful notices too. The reporter selects a live recovery context, and the outer
SPI guard checks the original caller's registered identity before restoring it. This applies to SQL
that emits a notice as well as direct managed logging. Actual PG19 execution remains unverified.

| Observable contract | Backend evidence |
|---|---|
| Exact bytes, zeroing, native ownership and accounting | `BorrowedAllocatorsPreserveExactBuffersOwnersAndIndependentAccounting` |
| Native Slab restrictions and retained control storage | `SlabWrongSizeErrorsPreserveControlPointerLengthBytesAndOwner`, `SlabResizeFailuresPreserveOwnershipAndNativeDiagnostic`, `SlabOverAlignmentReportsNativePaddedSizeAndPreservesControl` |
| Free, resize and immediate replacement reclamation | `IndividualFreeKeepsControlContentsAndPermitsExactReplacement`, `ResizePreservesPrefixZeroGrowthCheckedViewsAndNativeOwner`, `GenerationNoOomResizeReclaimsReplacedExternalStorageBeforeReset` |
| Headerless Bump rejection without lost ownership | `BumpUnsupportedOperationsRetainPointerLengthContentsAndCheckedOwner`, `RawTransferAdoptsSupportedKindsAndRejectsBumpWithoutLosingBytes` |
| Typed values, one-byte no-OOM allocation, generations and native cleanup | `TypedContextValuesRetainValuesAndFailedBumpDisposalCanReleaseOwnership`, `RepeatedResetPreservesContextIdentityAndExpiresEachAllocationGeneration`, `NativeParentDeletionRunsCallbacksAndInvalidatesBorrowedContextAndAliases`, `TransactionCleanupRunsCallbacksBeforeExpiringBorrowedAllocations` |
| Encoded names/diagnostics, notices and caller recovery | `SpecialCurrentContextPreservesEncodedNamesAndOwnedDiagnostics`, `ErrorContextChildNoticePreservesDiagnosticsAndRestoresLiveCaller` |
| Registry failure, native allocator ERROR/NULL, adoption retry and record cleanup | `RegistryExhaustionPrecedesNativeStorageAndPreservesLivePayload`, `SlabAllocatorErrorReleasesUnpublishedReservationAndAllowsRetry`, `NoOomNullReleasesUnpublishedReservationAndAllowsRetry`, `FailedAdoptionRetainsRawOwnershipUntilSuccessfulRetry` |

The fault module compiles the exact emitted bridge with translation-unit-local allocation controls.
It counts real C record acquisition/release and payload-boundary calls, checks independent retained
Bump/raw payloads, retries successfully, then verifies all records were released. These are controlled
native failure witnesses, not actual machine exhaustion. Test-source extraction checks unique
boundaries and fails if emission changes; expected outcomes are independent native observations.
Independent review added an external 1 MiB -> 2 MiB -> 32 byte Generation resize witness that proves
at least 2 MiB reclaimed before reset using both native and catalog counters. No production mutation
was executed or claimed.

All 95 affected cases pass on PostgreSQL 18.6/Linux x64 with assertions (1m16.808s) and without
assertions or MEMORY_CONTEXT_CHECKING (51.405s). The separate release installation is
`artifacts/postgres-release/18.6-install`, built from a read-only-reference archive of REL_18_6.
Tests compare fixture header assertion flags to the actual server setting. Full plain `dotnet test`
passes 4072 cases without failures/skips (3m22.788s), including package consumers. Release build has
zero warnings/errors (4.76s); IDE0004/IDE0300 verification is clean. XML inspection covers 909
internal declarations with no omissions; style inspection covers 475 C# source/template files.
GCC longjmp diagnostics were resolved with smaller guard/cleanup functions, retaining the original
compiler and warning flags. No warning suppression or consumer-template style changes were added.
README, memory/native-boundary/development guides and generated API pages are updated. Documentation
build/check and API freshness pass (114 API pages, 1210 members, 144 site pages); type checks
report zero errors/warnings/hints. Existing duplicate-404 and missing-site-URL site warnings remain
visible and unsuppressed.

The next section records actual >MaxAllocSize allocation and resizing. Broader allocator/resource
boundaries, datum/node integration, custom release policies, unsized layouts and the complete
remaining port inventory still remain required.
Only PostgreSQL 18.6/Linux x64 has execution evidence in this milestone; source review of PG13–19
and version-aware fixtures do not establish the full PostgreSQL/Windows/Linux/macOS matrix.

### Resource-budgeted huge allocation lifecycle

The new allocation lifecycle probe observes six stages: baseline, allocation, growth, shrink,
individual free, and context deletion. It preserves exact endpoint values, checked views and native
ownership, and compares native byte counts with independent `pg_backend_memory_contexts` totals.
Free must restore the live owner's baseline before any reset or deletion, and a new control
allocation in that same owner must still read 42. Integration tests also require the original
backend PID to execute `SELECT 42` after cleanup.

`AllocationPoliciesPreserveBytesOwnershipAndReclamation` has five ordinary-size cases and five
additional resource cases when `ANKUS_TEST_HUGE_ALLOCATIONS=1`: ordinary, no-OOM, 64-byte aligned,
aligned no-OOM, and zeroed allocation/growth. The large cases request 1,073,741,841 bytes, grow to
1,074,790,417 bytes (still above `MaxAllocSize`), shrink to 128 bytes, then free. The no-OOM and aligned
paths exercise actual allocate-copy-free replacement. Only a 4096-byte managed stack buffer is used
to scan the 1 MiB growth tail; the initial zeroed payload is sampled at three offsets.

Execution is explicitly admitted only for a release PostgreSQL backend on 64-bit Linux with cgroup
v2, more than 5 GiB host/finite-cgroup headroom, and an additional 3 GiB address-space allowance
measured after warming the Native AOT library. The helper preserves the hard resource limit,
restores the original soft limit, retains memory high-water/cgroup observations, and preserves both
primary failures and cleanup failures. Small default cases do not establish huge-size execution.
The affected small scope passes 5/5 cases on assertion-enabled PostgreSQL 18.6/Linux x64
(1m18.162s). The dedicated release-server resource run passes 10/10 cases, including all five actual
huge rows, with zero failures/skips in 53.581s. The external six-minute deadline did not expire.
Plain `dotnet test` passes all 4077 default cases with zero failures/skips (3m34.235s), including
the assertion-enabled backend and package-consumer suites. The non-incremental Release build
passes with zero warnings/errors (4.66s). IDE0004/IDE0300 verification is clean; XML inspection covers
911 internal declarations with zero omissions. All 478 C# source/template files have no extra
opening-brace blank lines or warning suppressions, and AGENTS contains no personal paths.
Documentation build/type checks and API freshness pass: 114 API pages, 1210 members and 144 site
pages; type checking reports zero errors/warnings/hints. Existing duplicate-404 and missing-site-URL
site build warnings remain visible. Public memory/development guides now describe replacement
memory costs and the reproducible resource run; the stale virtual-context remaining-work entry was
removed. No production diagnostic severity or consumer template changed.

Every huge case returned a native/catalog baseline of 8192 bytes, restored that baseline after
individual free, and reported zero context storage after deletion. Exact accounting and process
high-water evidence from the release run:

| Huge mode | Native/catalog bytes after growth | Backend peak resident bytes |
|---|---:|---:|
| Ordinary | 1,074,798,664 | 27,492,352 |
| No-OOM | 1,074,798,664 | 1,100,185,600 |
| Aligned 64 | 1,074,798,728 | 1,099,993,088 |
| Aligned no-OOM | 1,074,798,728 | 1,100,189,696 |
| Zeroed | 1,074,798,664 | 1,101,213,696 |

Native AOT had already reserved 51,154,059,264 virtual bytes per warmed backend; the temporary
soft limit was therefore 54,375,284,736 bytes. Initial host
MemAvailable ranged from 11,283,439,616 to 11,412,434,944 bytes. The actual `/init.scope` cgroup had
unlimited max/high values; max/oom/oom_kill counters stayed zero. All original soft/hard limits were
restored. The selected release headers expose PG_VERSION_NUM=180006 and do not define assertion,
memory-checking, randomization, clobbering or Valgrind macros. The native fixture and server use the
same release installation.

The five resource artifacts are under `artifacts/test-logs/huge-allocations/`, timestamped
20260923T070613–070615 with backend PIDs 3775406, 3775413, 3775425, 3775440 and 3775447. Exact per-stage
observations and ten passing rows are in
`artifacts/test-results/huge-allocations/Ankus.IntegrationTests_net10.0_x64.trx`.

These observations prove actual above-limit allocation and resize on the recorded target. They do
not prove every initial zeroed byte, physical allocator exhaustion, all allocator variants at huge
sizes, or the complete version/platform matrix. Initial zeroing samples three bytes; fresh libc
mappings can already be zero, so existing assertion-build dirty/regrowth zeroing tests remain
complementary. Datum/node integration, custom release policies, unsized layouts, remaining resource
boundaries and the full port inventory remain active requirements.

## Key research findings (verified)

### .NET Native AOT (Microsoft docs, verified)

- Publishing a **class library** with `<PublishAot>true</PublishAot>` produces a
   **self-contained native shared library** consumable from non-.NET code
  (`learn.microsoft.com/dotnet/core/deploying/native-aot/libraries`).
- Methods annotated **`[UnmanagedCallersOnly(EntryPoint = "name")]`** are exported as
  **public C entry points** from the AOT image — exactly the symbols PostgreSQL
  `dlsym`s for `CREATE FUNCTION ... AS 'module', 'func'` and `pg_finfo_*`.
- Caveats: no `dlclose` support for AOT libraries (Postgres keeps modules loaded; fine).
  `NativeLibrary`/`LinkerArg` MSBuild items allow linking extra native objects/libs.

### PostgreSQL module contract (verified from `~/src/postgres`, tags REL_13_23 … REL_18_6)

Postgres `dlopen(filename, RTLD_NOW|RTLD_GLOBAL)` then:

1. `dlsym(handle, "Pg_magic_func")` — must return a `const Pg_magic_struct *`
   (function, not data). Missing ⇒ `ERROR: incompatible library: missing magic block`.
2. Magic validation:
   - **PG 13–17**: `len == server's len && memcmp(module_magic, &server_magic, len) == 0` (byte-exact).
   - **PG 18, 64-bit**: magic length is **72 bytes**; PostgreSQL compares the ABI fields
     (name/version pointers may be NULL).
3. `dlsym(handle, "_PG_init")` — called if present. No compatibility alias is needed
   for the supported PostgreSQL majors.
4. Function lookup (`fmgr.c`): `dlsym` for `pg_finfo_<name>` (a **function** returning
   `const Pg_finfo_record *` where `Pg_finfo_record = { int api_version /* =1 */ }`),
   followed by lookup of the function entry point.

### `Pg_magic_struct` version matrix (must be compiled per PG version, like pgrx)

| PG | sizeof | layout | values |
|----|--------|--------|--------|
| 13 | 24 | `{len, version, funcmaxargs, indexmaxkeys, namedatalen, float8byval}` | 24, 1300, 100, 32, 64, 1 |
| 14 | 24 | same | 24, 1400, 100, 32, 64, 1 |
| 15 | 56 | same + `char abi_extra[32]` | 56, 1500, 100, 32, 64, 1, "PostgreSQL" |
| 16 | 56 | same | 56, 1600, 100, 32, 64, 1, "PostgreSQL" |
| 17 | 56 | same | 56, 1700, 100, 32, 64, 1, "PostgreSQL" |
| 18 | 72 | `{len, Pg_abi_values, name*, version*}` | 72, 1800, 100, 32, 64, 1, "PostgreSQL" |

(`FUNC_MAX_ARGS=100` for all 13–18; `INDEX_MAX_KEYS=32`; `NAMEDATALEN=64`; `FLOAT8PASSBYVAL=1`.)

### `FunctionCallInfoBaseData` layout (identical PG 10–18, x86-64)

```
offset 0:  FmgrInfo  *flinfo
offset 8:  fmNodePtr  context
offset 16: fmNodePtr  resultinfo
offset 24: Oid        fncollation
offset 28: bool       isnull      (result NULL flag; function sets it)
offset 30: short      nargs
offset 32: NullableDatum args[n]   // { Datum value; bool isnull; } = 16 bytes each
```
`Datum` = 64-bit: pass-by-value types are inline; pass-by-reference types are pointers
(varlena uses tagged one-byte or four-byte headers, with compressed/external forms).
Generated native wrappers use PostgreSQL's own access and detoasting APIs rather than
reimplementing those layouts in managed code. Text/bytea conversions are implemented;
the full pgrx datum and memory-context API remains required work.

## Transaction callback evidence

`PgTransaction` now covers all eight PostgreSQL outer-transaction events and all four
subtransaction events from pgrx's callback API. Outer callbacks are one-shot;
subtransaction callbacks repeat for the current outer transaction. Both registrations
return cancellable receipts, remain rooted when the receipt is discarded, and release
unused callbacks at the terminal transaction event.

The generated native dispatcher is installed once per extension backend. Reversible
phases expose SPI and preserve PostgreSQL snapshots. Savepoint `Start` and `PreCommit`
run in PostgreSQL transition states that reject another internal subtransaction, so a
dedicated callback guard executes SPI in the current transaction. Native errors remain
owned by the active callback frame until all managed frames unwind, then PostgreSQL
receives the original diagnostic. Nested callback frames support SPI that starts another
subtransaction. Guard-owned implementation subtransactions stay hidden from consumer
callbacks.

Direct runtime cases cover registration, ordering, repeated subtransaction delivery,
cancellation, invalid events, root lifetime, failure cleanup and thread ownership.
Generator cases verify every stable event mapping, guarded registration, callback-frame
error transport and terminal ERROR/FATAL selection. PostgreSQL 18.6/Linux x64 cases
execute commit, rollback, savepoint release, pre-commit SQL, nested dispatch, caught SPI
failure, same-backend recovery, root cleanup and terminal backend failure through a
published Native AOT extension. Actual two-phase and parallel-worker event execution,
PostgreSQL 13–17/19 beta and the remaining platform matrix are still required.

## Architecture direction

The target architecture consists of:

1. Source generators translate ordinary attributed C# methods into dispatchers and SQL.
2. Native entry points use the selected PostgreSQL headers for magic, finfo, and argument access.
3. Managed exceptions are caught before returning across the ABI. PostgreSQL ERROR paths must
   reach a native handler without crossing managed frames, including during recursive SPI dispatch.
4. Calls from managed code into PostgreSQL need their own guarded native boundaries before
   SPI, memory allocation, or other error-producing server APIs are exposed.
5. Build one native extension per PostgreSQL major and host architecture, as pgrx does.
6. Schema metadata must support ELF, PE/COFF, and Mach-O; an ELF-only design is insufficient.
7. `dotnet test` discovers all test projects normally. Its fixtures own publishing,
   local cluster setup, backend execution, diagnostics, and shutdown.

## Feature map (pgrx → Ankus)

| pgrx | Ankus | status |
|---|---|---|
| `#[pg_extern]` | `[PgFunction]` + source generator (exports, DDL, metadata) | Partial: supported scalar/array/enum types and SETOF/TABLE; nullability, overloads, variadics, named/defaulted arguments and execution options |
| `#[pg_schema]` | `[PgSchema("name")]`, nested inheritance and per-function overrides | Owned/existing schemas and relocation metadata implemented; future type/dependency graph integration pending |
| `#[pg_guard]` | automatic at export boundary, guarded native API calls and `[PgNativeCallback]` | Partial: export/datum boundaries, SPI, selected-header fixed/indirect calls and static native callback boundaries; full version/platform validation remains required |
| SETOF / TABLE (`SetOfIterator`, `TableIterator`) | `IEnumerable<T>`, named tuples, column overrides, streaming and materialized results | Implemented for supported value families; PostgreSQL 18.6/Linux x64 evidence above |
| `#[pg_trigger]` | `[PgTrigger]` | ☑ — supported tuple types; see trigger evidence |
| Raw `EventTriggerData` and event-trigger helpers in `pgrx-pg-sys` | `[PgEventTrigger]` and owned context metadata | Implemented for descriptive DDL/drop/rewrite metadata and login; PostgreSQL 18.6/Linux x64 evidence above; raw bindings remain in the full inventory |
| `#[pg_aggregate]` + `Aggregate` trait | `[PgAggregate]`, typed static callbacks, `PgAggregateState<T>` and `PgAggregateContext` | Concrete, polymorphic, internal, raw and custom-codec signatures implemented; heterogeneous variadic ANY remains required; verification recorded below |
| `#[pg_operator]` | `[PgOperator]`, backing function, planner options and SQL dependencies | Implemented for supported types; PostgreSQL 18.6/Linux x64 evidence above |
| `#[pg_cast]` | `[PgCast]`, three contexts, typmod/explicitness arguments and SQL dependencies | Implemented for supported types; PostgreSQL 18.6/Linux x64 evidence above |
| `extension_sql!` | `[assembly: PgSql]`, `[assembly: PgSqlFile]`, named dependencies and `PgSqlTypeProvider` | Inline/file SQL, ordering, bootstrap/final, relocation, catalog-name providers and owned managed scalar identity providers implemented; broader mapping forms and standalone extraction remain required |
| `#[derive(PostgresType)]` (custom base types) | generated CBOR storage, JSON text I/O, custom storage/I/O, binary send/receive | Manual raw callbacks, explicit codecs, generated CBOR/JSON contracts including tagged variants, custom text with generated storage, and packed native borrowing/copy-on-write implemented; additional shapes and broader native layouts remain required |
| `composite_type!`, `PgHeapTuple` | `PgHeapTuple`, `PgTupleDescriptor`, and `[PgCompositeType]` | Owned dynamic tuples, arrays, sets and SPI implemented; validation below |
| `#[derive(PostgresEnum)]` | `[PgEnum]`/`[PgEnumLabel]`, generated DDL/mappings, scalar/array SPI and `PgEnums` catalog helpers | Implemented; PostgreSQL 18.6/Linux x64 evidence above |
| Type mapping (`FromDatum`/`IntoDatum`) | Typed converters and explicit raw PostgreSQL values | Built-ins, declared enum/custom-codec mappings, raw PgDatum bindings and reusable PgDatumType scalar/vector/shaped-array readers/writers are implemented for documented callback, parameter, raw-read and typed scalar-result paths, including unsafe native-address results with caller-supplied type/ABI obligations. Readable scalar mappings can also supply generated equality, ordering and hashing families. Finite fully constructed generic roots support default and explicit per-construction SQL metadata with supplied or statically inferred, constraint-checked closed converters. Nested SQL array containers are rejected, matching pgrx; multidimensional values use one shaped array. Broader metadata and mapped container contracts, ordinary row/composite conversions and complete matrix validation remain required. |
| `Spi` | typed commands/results, sessions, prepared statements, cursors, tuple access | Partial: atomic commands, scoped sessions/plans, typed results, cursors, row edits, quoting and JSON EXPLAIN |
| `PgError` | `PgException` + logging helpers | Owned diagnostics, context, objects, positions/location; `PgLog` severities and structured reporting |
| `pgrx::guc` | `[PgGucInt/Real/String/Bool/Enum]` (registered in `_PG_init`) | ☐ |
| `background_worker` | `[PgBackgroundWorker]`, `PgBackgroundWorkerOptions`, `PgBackgroundWorker` and callback-owned handles | Static/dynamic registration, generated `void(nuint)` entries, lifecycle, signals/latches, connections and transaction callbacks implemented; validation and remaining requirements recorded above |
| `palloc`/`MemoryContextManager`, `PgBox`, `PBox` | `PgMemoryContext`, `PgAllocation`, `PgMemoryCallback`, `PgNativeBox<T>`, `PgContextValue<T>`, `PgNativeReference<T>` | Checked contexts, virtual context parameters, typed/aligned allocation, sized native ownership and borrowed references, exact copies, raw transfer, transient sizing, borrowed Slab/Generation/Bump and controlled native failure witnesses, cancellable cleanup and actual huge-size allocation/resize implemented; datum/node APIs and full version/platform requirements listed above |
| `pgrx::rel` (`PgRelation`) | `PgRelation`, `PgLockMode` | Checked cache references, exact locks, live metadata, index/heap access, descriptors, ownership transfer, statistics and regclass transport implemented; raw RelationData bindings and complete platform/version evidence remain required |
| `iter`, `pg_sys` tuple-store APIs | generated native materialization with spill and bounded row storage | Set results implemented; standalone tuple-store API pending |
| `callbacks` (transaction/subtransaction callbacks) | `PgTransaction` outer/subtransaction registration with cancellable receipts | Partial: all event mappings, typed subtransaction IDs and callback lifetimes implemented; commit/abort/savepoint behavior verified on PostgreSQL 18.6/Linux x64; two-phase, parallel-worker and matrix execution pending |
| `pg_catalog`, `PgOid`, built-in OIDs | catalog and type/function lookup APIs | Versioned built-in constants, tagged OID conversion, type/operator lookup, owned PgProc metadata/default trees and checked relation access implemented; complete platform/version evidence remains required |
| `pg_sys::elog` and logging macros | PostgreSQL logging and full diagnostics | `PgLog` levels, filtering, diagnostics, managed unwind and native terminal reporting; PG18 Linux verified |
| `pgrx::pg_sys` (raw FFI) | versioned native bindings and guarded entry points | ☐ |
| `nodes`, `pg_sys` custom scan bindings | Custom scan providers, node types, callbacks, and supporting APIs | ☐ |
| `cargo pgrx` CLI | .NET tool and standard SDK commands; full command inventory below | ☐ |
| `cargo pgrx schema` (one-compile, `.pgrxsc`) | metadata-only schema generation and standalone extraction | Partial: build-time assembly metadata extraction |
| pgrx-examples | `samples/` mirroring the example set | ☐ |

## Repository-derived parity inventory

Reference: [pgrx](https://github.com/pgcentralfoundation/pgrx), commit `70383e884582d1bcc7cd681d10886b995a2830cb`,
workspace version `0.19.2`. Paths in this section are relative to that read-only repository.
This inventory covers feature families discovered in the workspace, including features absent from its
README. Each family's public APIs, options, error behavior, ownership rules, examples, and regression
cases require implementation and evidence. Family coverage is not API-by-API completion evidence.

**Partial** means specific implemented behavior is identified above or below; all other behavior in
that row is pending. **Pending** means no validated equivalent is recorded. Proposed API names elsewhere
in this tracker are design directions rather than completed contracts.

### Development environment, commands, and distribution

The dispatch enum in `cargo-pgrx/src/command/pgrx.rs` contains all 17 commands below. Standard .NET
commands can supply the equivalent operation, with the Ankus tool providing PostgreSQL-specific behavior.

| Source command | Required equivalent behavior | Evidence / status |
|---|---|---|
| `new` | Generate an ordinary extension project, control/configuration defaults, functions, and discoverable backend tests | Ordinary solution scaffold implemented with package-based SDK, CPM, managed/native MSTest cases, explicit names/output, and existing-file preservation. `--background-worker` adds a preloaded worker, shared results and a real backend test |
| `init` | Install/build supported PostgreSQL versions or register existing installs; persist configuration and toolchain options | Partial: installed CLI registration with locked/atomic configuration updates; provisioning pending |
| `info` | Installation path, `pg_config` path, and exact PostgreSQL version queries | Implemented for registered/explicit installations; `ToolCommandTests.InitPreservesSettingsAndInfoUsesRegistration` |
| `start`, `stop`, `status` | Manage version-specific persistent development clusters, ports, logs, and lifecycle | Partial: isolated test lifecycle in `src/Ankus.Testing`; development CLI pending |
| `run`, `connect` | Build/install/load an extension and connect through `psql` or configured client, including `pgcli` | Pending |
| `test` | Backend test discovery, filters, expected errors, configuration, rollback, and supported-major matrix | Partial: canonical `dotnet test`, scaffolded managed/backend MSTest tests, reusable framework-neutral publish/load fixture; multi-framework templates, attribute-generated backend tests, CLI forwarding and matrix pending |
| `bench` | Attribute-driven benchmarks running inside PostgreSQL and result reporting (`pgrx-bench`) | Pending |
| `regress` | PostgreSQL regression SQL/expected-output suites and diagnostics | Pending |
| `schema` | Schema generation from one compilation, standalone extraction, ordering/dependencies, custom SQL, output options | Partial: `src/Ankus.Build` reads managed metadata without loading extension code |
| `install` | Install libraries, control files, schema and upgrade scripts into selected PostgreSQL paths | Partial: installed CLI validates manifests and copies/stages native libraries, control and versioned SQL files; upgrade scripts pending |
| `package` | Produce a relocatable installation tree for a selected version/target with custom library naming | Partial: publish output; distribution command pending |
| `get` | Query extension control properties and derived extension metadata | Pending |
| `cross` / `pgrx-target` | Export target configuration/binding information and support target-aware build workflows | Pending |
| `upgrade` | Upgrade framework package references, including workspace/central versions and dry-run selection | Pending; distinct from PostgreSQL extension SQL upgrades |

Additional tooling sources: `cargo-pgrx/src/{manifest,metadata}.rs`, command options in each command file,
`pgrx-pg-config/src/`, `pgrx-bindgen/src/`, and installation/upgrade fixtures in `cargo-pgrx/tests/`.
The framework also requires versioned extension SQL upgrades, custom/versioned shared-library names,
control-file settings, dependency handling, and deterministic packaging.

NuGet packages now provide the extension-author project SDK, runtime, source generator, PostgreSQL configuration,
testing harness, and .NET tool. The SDK embeds a framework-dependent .NET 10 native-build helper and references
matching runtime/generator versions during the first restore. `Ankus.Generators` ships only its analyzer assembly;
compiler dependencies do not flow into extension projects. `Ankus.Testing` exposes its PgConfig and Npgsql dependencies.

`ToolCommandTests` packs unique versions and restores consumers outside the checkout with an empty package directory.
Installed-tool publishing/staging and direct `dotnet publish` both execute SQL in PostgreSQL 18. A separate MSTest
consumer uses the testing package and ordinary `dotnet test`, checks successful native calls, and recovers from a
managed exception on the same connection. Direct publishing also exercises Central Package Management and SDK
version selection from `global.json`. Paths contain spaces, including the NuGet cache; the SDK quotes native library
arguments that .NET 10's Unix Native AOT targets otherwise pass unquoted. Public publication still requires full feature
and platform/version validation.

### Source generation, schema, and extension declarations

Primary sources: `pgrx-macros/src/lib.rs`, `pgrx-sql-entity-graph/src/`, `pgrx/src/{fcinfo,iter,aggregate}.rs`.

| Feature family | Required behavior | Status |
|---|---|---|
| `pg_extern` / `pgrx` | Names, schemas, overloads, strictness, defaults, named arguments, variadics, polymorphic/raw inputs and results | Synchronous supported types, SETOF/TABLE, names, fixed schemas, overloads, strictness, named/defaulted arguments, variadics, polymorphic signatures, explicit raw/internal bindings and reusable mapped scalar/array callback slots implemented, including finite fully constructed generic mapped roots. Nested SQL containers, broader generic mapping forms and the full platform/version matrix remain required. |
| Function options (`extern_args.rs`) | Create-or-replace, immutable/stable/volatile, security invoker/definer, parallel modes, cost, support functions, dependencies, search path | Implemented declaration options, existing planner support references and explicit named SQL/schema/function dependencies; future entity families pending |
| `pg_schema`, `search_path` | Schema declarations, qualification, nested declarations, lookup/search-path semantics | Implemented for functions and standalone schemas, including owned/existing schemas, named graph dependencies, per-call search paths and non-relocatable metadata; future type-family integration pending |
| `extension_sql!`, `extension_sql_file!` | Inline/file SQL, entity requirements, bootstrap/finalize positioning, declared created entities | Inline/file SQL, named requirements/before constraints, bootstrap/final, file-change invalidation, SQL-only packages, declared catalog-type providers and reusable owned managed-identity providers implemented. Providers order raw/composite/mapped scalar/array signatures and support explicitly ordered shell/I/O/completion sequences. Standalone declared-entity extraction remains required. |
| `pgrx(sql = ...)` | Literal/disabled SQL generation with retained wrappers and entity dependencies | Implemented for PgFunction (including attached operator/cast SQL), PgType/PgEnum, PgAggregate and PgOrdering/PgHashing with their distinct ownership boundaries. Generator and real Native AOT installation/execution/relocation/rollback evidence includes Linux x64/PostgreSQL 18.6 in UTF8 and LATIN1; full hosted suites pass on Linux x64/PG18.6, macOS ARM64/PG18.6 and Windows x64/PG17.11. The pinned pgrx source rejects callback paths despite stale macro documentation advertising them; its equality derive does not consume parent SQL controls. |
| `default!`, `name!`, `composite_type!` | SQL default arguments, named table/aggregate fields, named composite type resolution | SQL argument names/defaults, TABLE fields and concrete aggregate inputs/direct arguments implemented; named composite resolution implemented |
| `SetOfIterator`, `TableIterator` | SETOF and TABLE results, nullability, tuple metadata, iteration cleanup on early exit/error | Implemented for supported scalar/array/enum columns, named tuples and explicit column overrides; streaming/materialized execution, interruption and owned resource cleanup validated on PG18/Linux |
| `pg_trigger` | Row/statement and before/after/instead-of triggers; event/argument metadata; OLD/NEW tuple access and modification | Implemented for supported tuple types, with guarded transition-table SPI; PostgreSQL 18.6/Linux x64 verified |
| `pg_aggregate`, `AggregateName` | Transition/final/combine/serialize/deserialize; moving/inverse states; ordered-set/hypothetical; initial states, sort and parallel options | Concrete, polymorphic, internal, raw and custom-codec bindings implemented, including native ownership, worker transport, ICU/custom ordering and lifecycle recovery; heterogeneous ANY and full matrix remain required |
| `pg_operator` and option attributes | Operator name, commutator, negator, selectivity/join support, hashes/merges, and schema dependencies | Implemented for supported types, including custom base-type operands, binary/prefix operators, separate graph IDs, exact references and declaration diagnostics; full matrix validation remains required |
| `PostgresEq`, `PostgresOrd`, `PostgresHash` | Equality, order and hash functions, operator classes/families and index use | Implemented for PgType/PgEnum with explicit value contracts, stable hashes, default B-tree/hash classes, real indexes/joins, fresh-backend reuse and independently controlled ordering/hash family SQL. Manual raw mappings and the complete platform/version matrix remain required. |
| `pg_cast` | Explicit/assignment/implicit casts and generated SQL | Implemented for supported source/target types, including custom codecs, nullable values, arrays and optional typmod/explicit arguments; full matrix validation remains required |
| `pg_test`, `pg_bench` | Generated in-backend tests/benchmarks, discovery and expected-error metadata | Pending |
| `pg_guard`, `initialize`, module magic | Guarded callbacks, bootstrap, panic/exception boundaries, module name/version and ABI checks | Partial: function exports, guarded static native callbacks, module magic, immediate `[PgModuleLoad]` registration and backend/shared-preload `[PgInitialize]` with retry/recursion handling; callback workers verified on Linux x64/PG18.6 and Windows x64/PG17.7, remaining platform/version matrix required |
| SQL entity graph and metadata | Type/function/schema dependencies, cycle diagnostics, SQL translation hooks, section encoding/decoding, ELF/PE/Mach-O extraction | Partial: deterministic SQL/schema/enum/function/operator/cast graph with aliases, dependency diagnostics, bootstrap/final edges and managed assembly metadata; future type-family graph edges, translation hooks and standalone extraction pending |

The operator option attributes are `opname`, `commutator`, `negator`, `restrict`, `join`, `hashes`, and
`merges`. GUC-specific derives/hooks are tracked with GUCs below. PostgreSQL event callbacks and owned
descriptive metadata are implemented as documented above. Full custom-scan support remains required
alongside the source-level macro inventory.

### Datum conversions and user-defined types

| Source | Required behavior | Status |
|---|---|---|
| `datum/{from,into,unbox,borrow}.rs`, `nullable.rs`, `callconv.rs` | Conversion contracts, typed OIDs, SQL NULL distinct from zero, owned/borrowed lifetimes and argument/return ABI | Partial: built-in scalar/xid/text/bytea/UUID/JSON transport |
| `datum/{bytea_type,varlena}.rs`, `varlena.rs`, `toast.rs` | Bytes/text, C strings, packed/compressed/external TOAST, encoding, alignment, custom varlena layouts | Partial: text/bytea including TOAST and server encoding; packed custom native payloads with checked PgVarlena borrowing, copy-on-write, cloning and explicit transfer; broader layouts and borrowed text/bytea views remain |
| `array.rs`, `array/`, `datum/array.rs` | Arrays, dimensions/lower bounds, null elements, owned and borrowed iteration, variadic arrays | Owned arrays and vectors implemented for supported scalar/enum/composite/custom-codec types, including xid, with shape/subscripts/NULL handling, explicit composite identity and C# params variadics. Raw borrowed views remain required |
| `datum/{anyarray,anyelement,internal}.rs` | Polymorphic datums, resolved element OIDs, internal/pointer-bearing values | `PgAnyElement` and `PgAnyArray` implemented for scalar/SETOF/TABLE/aggregate signatures and query/call results with checked native ownership. General internal values remain pending. |
| `datum/{numeric,numeric_support/}` | Arbitrary precision and constrained numeric types, arithmetic, rounding, conversion, exceptional values | Implemented value/constraint surface: full-range `PgNumeric`, exact decimal adapters, arithmetic, rescaling, exceptional values, owned SPI conversion, JSON, declarative boundary constraints, primitive casts, generic integer conversion, mixed operators and summation. Cross-version/platform evidence remains pending |
| `datetime.rs`, `datetime/` | Date, time, timestamp, timestamp with timezone, time with timezone, interval; infinities, ranges, arithmetic and time zones | Partial: full-range types, exact conversions, function/SPI transport, native parsing/formatting/arithmetic/parts/truncation/zones/clocks, exact numeric extraction, comparisons, operators, component/unit factories, precision modifiers, explicit-zone ISO and JSON; detached field/epoch/raw factories, native zone-offset lookup, interval-zone overloads and owned timeofday text. Full raw bindings and the PostgreSQL/platform matrix remain required |
| `datum/{json,uuid,inet,geo,range}.rs` | JSON/JSONB, UUID, network, geometric and range datums with their operations | Partial: UUID, owned JSON/JSONB, inet/cidr, checked .NET network mappings, seven geometric datums, owned vertex collections, six built-in range families and explicitly mapped value-type range bounds/operations implemented; dedicated geometric operation wrappers, broader range-bound forms and multiranges pending |
| `heap_tuple.rs`, `htup.rs`, `tupdesc.rs`, `datum/tuples.rs` | Named/anonymous composites, tuple descriptors, access/mutation, dropped/null attributes, tuple ownership | Owned dynamic tuples and descriptors implemented with strict edits, physical slots, nested arrays, domains/typmods, SQL bindings, SETOF/TABLE and SPI; raw heap interfaces and the platform/version matrix remain required |
| `PostgresEnum`, `enum_helper.rs` | Label/OID mappings, schema lookup, generated enum DDL, enums in containers | Implemented through attributes, closed generated mappings, guarded live catalog helpers and all supported array/SPI paths; composite fields and arrays validated; custom base-type containers and matrix validation remain required |
| `PostgresType`, `inoutfuncs.rs` | Custom base types with default CBOR in-memory/on-disk serialization and JSON human-readable input/output | Explicit codecs, custom text with generated storage, and generated CBOR/JSON contracts implemented for records/classes/structs/enums, tagged class variants, inherited members and nested collections; additional shapes remain required |
| `inoutfuncs`, `pgvarlena_inoutfuncs` type options | Custom textual representation, custom in-memory/on-disk layouts, alignment and manual datum conversion | Explicit storage/text codecs, custom text with generated CBOR, packed native layouts with checked borrowing/copy-on-write and optional NULL-input errors implemented; broader layouts remain required |
| `pg_binary_protocol` | Generated send/receive functions, binary protocol/COPY round-trips and invalid-input diagnostics | Implemented for explicit and generated codecs; independent binary COPY and recovery evidence recorded below; full PostgreSQL/platform matrix remains required |
| `postgres_type_variants` example/tests | All four custom-type paths, enum/struct variants, related derives and SQL override options | Explicit codecs, default records and tagged variants implemented; remaining storage paths and related derives remain required |

Custom base types and PostgreSQL composite types have distinct storage and I/O contracts; both require
complete implementations. AOT serialization must use statically generated metadata/converters.

### Runtime and PostgreSQL internals

| Source modules | Required behavior | Status |
|---|---|---|
| `spi.rs`, `spi/{client,query,tuple,cursor}.rs` | Sessions; read-only/read-write queries; typed parameters/results; tuple mutation; owned/borrowed prepared plans; keep/free; cursors, fetch, detach/find by name; scalar helpers and quoting | Guarded commands, scoped sessions/plans, typed results, cursors, local tuple edits, quoting, JSON EXPLAIN, first-row pairs/triples, owned raw query/cursor results, explicit converters, raw parameter binding and generated custom-codec types implemented. The complete PostgreSQL/platform matrix remains required |
| `memcx.rs`, `memcxt.rs`, `palloc.rs`, `palloc/`, `pgbox.rs`, `layout.rs` | Context selection/creation/switch/reset/delete; allocation/reallocation; context-bound cleanup; owned/borrowed server pointers | Partial: checked typed/aligned allocation, virtual context parameters, sized native boxes/context values/borrowed references, exact copies, raw transfer, transient sizing, reset/delete invalidation, cancellable cleanup, borrowed allocator kinds, controlled native failures, guarded recovery and actual huge-size AllocSet allocation/resize implemented; datum/node integration, custom release policies, remaining native resource boundaries and full matrix remain required |
| `fcinfo.rs`, `callconv.rs`, `fn_call.rs` | Function call context, collation, argument types/nulls, cached state, direct/named calls and result ownership | Injected contexts and cached state implemented; scalar name/OID calls, explicit native entry-point calls, defaults, collation, polymorphic argument resolution, and owned managed/raw results implemented. Complete raw bindings and version/platform validation remain required |
| `list.rs`, `list/`, `stringinfo.rs` | PostgreSQL lists and string/binary buffer operations with native ownership | StringInfo and typed lists, including checked mutation/iteration, exclusive borrowing and container ownership, verified on Linux x64/macOS ARM64 PostgreSQL 18.6 and Windows x64 PostgreSQL 17.11; full version/platform evidence remains pending |
| `rel.rs`, `itemptr.rs`, `pg_catalog/`, `namespace.rs`, `wrappers.rs` | Relation/index access and locks, tuple locations, function/type catalog lookups, namespaces and type resolution | Tuple locations, checked native storage, native type-syntax, qualified operator lookup, owned function-catalog metadata and native defaults verified on Linux x64/macOS ARM64 PostgreSQL 18.6 and Windows x64 PostgreSQL 17.11. Checked relation APIs verified on Linux x64/PostgreSQL 18.6. Raw RelationData bindings and complete version/platform evidence remain pending |
| `xid.rs` | Transaction identifier wrappers and conversions | Implemented: distinct `PgTransactionId`/xid scalar and array datum contracts, pgrx-compatible invalid-to-NULL output, wrap-aware full-ID expansion and typed callback-only `PgSubtransactionId`; PostgreSQL 18.6/Linux x64 executed, PG13–19 headers source-reviewed, remaining matrix pending |
| `callbacks.rs` | Transaction/subtransaction callbacks, unregister and error cleanup | Implemented event mappings, one-shot/repeating lifetimes, cancellation, nested dispatch and guarded errors. Actual prepared commit/rollback, preparation failure/durability/recovery, parallel worker terminal events/errors and callback capture lifetimes pass on Linux x64/PG18.6 and Windows x64/PG17.11; the complete PG13–19/platform matrix remains required. |
| `guc.rs`, `PostgresGucEnum`, `pg_guc_hook` | Bool/int/real/string/enum settings, contexts/flags/bounds, hidden/named enum entries, check/assign/show hooks and structured errors | Partial: native-backed typed declarations, hooks/extra, prefixes/logging, source/privilege/transaction/reload semantics, actual worker propagation, bounded lifetime measurements, cold package consumers and managed preload verified above. Raw-placeholder treatment, mixed-encoding preload and the full matrix remain required |
| `bgworkers.rs` | Static/dynamic workers, startup/restart/shutdown, handles, signals/latches and backend connections | Partial: generated entries, checked registration, callback-owned observation handles, native signal/latch operations, name/OID connections and recoverable transaction callbacks implemented. Linux x64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11 evidence includes restart, exhaustion, commit failures, SIGCHLD delivery/consumption, detached-worker lifetime, explicit role permissions, eleven connection failure/recovery cases, actual postmaster death during latch/shutdown waits, and owner/entry/PostgreSQL-handle allocation failures with identity exhaustion and same-session recovery. The full version/platform matrix remains required |
| `shmem.rs`, `atomics.rs`, `lwlock.rs`, `spinlock.rs` | Shared memory registration, synchronization, atomics, lock lifecycle and preload initialization | Partial: named unmanaged values, ordered preload initializers, shared/exclusive guards, primitive/enum scalar atomics, scoped immutable aggregate views, inline atomic fields, bounded list/deque/map views and local/inline spinlocks are implemented. Lightweight-lock and spinlock guards provide scoped original readonly access; exclusive guards also provide scoped mutations with alias and child-lock protection. Shared values, mutation/queue persistence, error cleanup, contention and segment recreation pass on Linux x64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11. Remaining platform/version evidence is required |
| `nodes.rs`, `pgrx-pg-sys/src/node.rs` | Node tags/type checks, allocation, conversion/string output, planner/executor node access | Partial: selected-header generated declarations, checked tag/cast views, zeroed tagged allocation and guarded native formatting with ABI, bounds and original-lifetime validation; planner/executor integration, broader ownership/callback witnesses and the full version/platform matrix remain required |
| `pg_sys` hooks and `pgrx-examples/hooks` | Planner/executor, utility, parse, authentication and other exposed hooks; chaining and version-specific callback signatures | Partial: typed static managed callbacks, explicit global installation, previous-hook chaining/fallback and restoration implemented. Actual executor chains, managed/native errors and recovery pass on Linux x64/PostgreSQL 18.6 and Windows x64/PostgreSQL 18.1; initialization/shared preload/parallel workers pass on Linux. Remaining hook protocols, examples and full version/platform validation are required |
| `pg_sys` custom scan structures/functions | Provider registration, paths/plans/states, executor lifecycle and supporting node/tuple APIs | Partial: selected-header method tables and field-named callbacks support a real trace provider; paths/plans/states, projection, rescan, EXPLAIN, cached plans, backward reads, provider-owned parallel DSM, index/index-only mark/restore, provider-owned parameter expressions and partition-child remapping, independent predecessor chaining, native registry identity/lifetime/error boundaries, concurrent-update rechecks and error cleanup pass on Linux x64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11. Older parameter-helper ABI adaptation and full platform/version evidence remain required |
| `ffi.rs`, `pg_sys.rs`, `pgrx-pg-sys/src/submodules/{ffi,panic,pg_try,thread_check}.rs` | Native call guards, nested recovery, thread affinity, interrupts, deterministic managed cleanup | Partial: function/SPI boundaries, guarded selected-header fixed and indirect calls, global access, static managed callbacks with nested capability/lease restoration, and explicit nested `PgTransaction.RunInSubtransaction` recovery implemented; remaining callback/lifetime conveniences, variadics and the complete matrix remain required |
| `pgrx-pg-sys/src/submodules/{elog,errcodes,panic,ffi,pg_try}.rs` | All log levels and SQLSTATE values; full diagnostics/context/object/location fields; catch/filter/rethrow behavior | Partial: all pgrx log levels, owned diagnostics, managed catch/filter/rethrow and unwind; PgSqlStates supplies the complete named PostgreSQL 13–19 beta catalog union with native aliases and exact custom string codes; remaining guard/raw APIs and full matrix validation pending |
| `pgrx-pg-sys/src/{include,include.rs,cshim.rs,libpq.rs,port.rs,cstr.rs}` | PG13–19 functions, globals, constants, structs, unions, callbacks, inline/macro shims and string utilities | Partial: pinned PG13–19 inventories and a shared selected-header node/function/global companion with guarded fixed and indirect calls, static managed native callbacks, qualified global value/address access, and selected alignment, memory, buffer/page, tuple and spinlock helpers are connected to the SDK and actual Native AOT/backend execution. Remaining hook protocols, variadics, callback/lifetime conveniences, atomic/locking APIs, remaining handwritten conveniences/string utilities and the complete version/platform matrix remain required |
| `pgrx-pg-sys/src/submodules/{datum,oids,transaction_id,htup,tupdesc,utils,cmp,sql_translatable}.rs` | Built-in OIDs, raw datum/tuple access, identifier helpers, comparison and SQL type metadata | PostgreSQL 13–19 versioned built-in OID catalogs, tagged PgOid classification and explicit invalid/custom datum conversion implemented alongside selected scalar mappings. Raw tuple, identifier and remaining type metadata APIs plus the full matrix remain required |
| `misc.rs`, `prelude.rs`, internal `ptr.rs`/`slice.rs` | Hash helpers, ergonomic API access, pointer/slice lifetime semantics underlying public APIs | Pending |

`pgrx-bindgen` and the per-major `pgrx-pg-sys/src/include/pg13.rs` through `pg19.rs` are required input
to the versioned raw API inventory. The raw API includes direct unsafe access as well as safe wrappers;
error-producing calls still need a native guard that prevents longjmp across managed frames.

### Examples and test corpus

All example directories in `pgrx-examples/` require a corresponding working .NET scenario and validation:

- Types/data: `arrays`, `bytea`, `composite_type`, `custom_types`, `datetime`, `json`, `numeric`,
  `postgres_type_variants`, `range`, `strings`.
- SQL/functions: `aggregate`, `generic_agg`, `custom_sql`, `operators`, `schemas`, `spi`, `spi_srf`, `srf`, `triggers`.
- Backend/runtime: `bgworker`, `errors`, `hooks`, `memory_contexts`, `notify`, `pglz_inspect`, `pgthread`,
  `pgtrybuilder`, `rewrite_manip`, `shmem`, `subtrans_infos`, `wal_decoder`.
- Build/tooling/constraints: `bad_ideas`, `benching`, `custom_libname`, `nostd`, `versioned_custom_libname_so`,
  `versioned_so`. Rust-specific mechanisms require an explicit idiomatic .NET capability mapping and tests.

The `samples/Ankus.Examples.Hello`, `samples/Ankus.Examples.Enums`, `samples/Ankus.Examples.Operators`,
`samples/Ankus.Examples.Sets`, `samples/Ankus.Examples.Composites` and
`samples/Ankus.Examples.Ranges` samples are validated. Full example parity is pending.

Required test-source inventory:

- `pgrx-unit-tests/src/tests/`: datum/array/borrow/NULL/zero-datum tests; numeric/date/network/JSON/UUID/geometric/range
  tests; custom type/enum/composite/tuple tests; function/default/variadic/cast/operator/aggregate/schema/attribute tests;
  SPI/SRF/call-context tests; memory/list/relation/shared-memory/GUC/worker/callback/XID tests; guard/log/error tests;
  property/round-trip tests; lifetime/name/type-identity/signature/version/inline-binding and issue regressions.
- `pgrx-unit-tests/tests/{compile-fail,nightly,todo}` and `ui.rs`: diagnostics and unsupported-signature/lifetime cases,
  with each Rust-specific constraint translated to the relevant .NET compile-time or runtime guarantee.
- `pgrx-tests/src/framework{.rs,/}`: local installation/cluster management, backend test setup, expected errors,
  per-test transactions, diagnostics, cleanup, configuration and concurrent execution; `proptest.rs`: property testing.
- `pgrx-bench/src/` and `cargo-pgrx/src/command/bench.rs`: benchmark discovery and in-backend execution.
- `cargo-pgrx/tests/`: install/test regression fixture, CLI dependency upgrades and workspace fixtures.
- Inline unit tests in runtime, macro, SQL graph, binding-generation, and configuration crates; SQL and expected-output
  fixtures in the examples and regression-command paths.

The passing Ankus tests verify the implemented milestones, not this entire corpus. Each family still needs
source-case-level mapping to named .NET tests and any additional boundary cases introduced by AOT/native interop.

### Release evidence requirements

| Deliverable | Required evidence | Current evidence |
|---|---|---|
| Full runtime/macro/CLI parity | Source API/option inventory mapped to implemented APIs, behavior tests and examples | Family inventory above; most implementation pending |
| Native AOT safety | Trim/AOT-clean consumers; deterministic cleanup on exceptions, native errors, cancellation and recursive callbacks | Implemented suites pass Linux x64/PG18, macOS ARM64/PG18 and Windows x64/PG17 at `cbd0fdd`; remaining APIs/targets pending |
| PostgreSQL 13, 14, 15, 16, 17, 18, 19 beta | Per-major builds against that server's headers, version-specific APIs/gating and complete backend tests | PG18 on Linux/macOS and PG17 on Windows verified at `cbd0fdd`; complete major/platform combinations pending |
| Windows, Linux, macOS | Native builds, exports/loading, lifecycle, encoding, toolchain and installer tests for each supported RID | Linux x64, macOS ARM64 and Windows x64 pass [CI run 36043147127](https://github.com/willibrandon/ankus/actions/runs/36043147127); macOS x64 and the full version matrix remain pending |
| Ordinary .NET usage | One NuGet reference, attributed methods, `dotnet publish`, discoverable plain `dotnet test` and working tool commands | NuGet project SDK, cold isolated consumers, CPM/global.json, installed-tool and direct publishing, plus an external MSTest consumer verified on Linux/PG18; remaining CLI commands pending |
| Installation and upgrades | Clean install, relocation, removal, versioned-library coexistence, upgrade scripts and data compatibility | Basic PG18 `CREATE/DROP EXTENSION` and schema relocation pass |
| Examples and documentation | Every inventoried scenario runnable with tested usage/configuration/API documentation | Minimal sample, native boundary and SPI usage documented |

## Phase plan

The phases track implementation of the complete pgrx feature surface.

- [x] **P0 — Feasibility spike**
   - [x] Minimal attributed `add(int,int)→int` extension
    - [x] `Greet(string)→string` / `greet(text)→text`
   - [x] Generated native magic, finfo, integer argument access, and managed-exception error reporting
   - [x] AOT publish and actual PostgreSQL 18 integer-function invocation
   - [x] Error path: C# exception ⇒ Postgres `ERROR`, transaction aborts cleanly, backend survives
- [ ] **P1 — Runtime core**
  - [ ] `Ankus.Runtime`: `FunctionCallInfo` reader, `Datum`/`Value`, varlena/detoast, type conversion table
  - [ ] `Ankus.PgSys`: symbol resolution (`dlopen(NULL)`+`dlsym`), P/Invoke surface (SPI, elog via shim, memory, catalog)
   - [x] Guarded `Spi.Execute`, recoverable command errors, and basic `PgException` diagnostics
    - [x] Typed built-in SPI parameters, materialized results/scalars, metadata, read-only mode and limits
    - [x] Owned prepared statements with guarded keep/execute/free and backend-thread disposal
    - [x] Owned cursors, batched fetch, detach/find, prepared-plan cursors, and portal lifetime invalidation
    - [x] Owned error diagnostics, context/object/query/source fields, native rethrow and diagnostic cleanup
    - [x] Scoped SPI sessions, session-bound plans, retention and stack/lifetime enforcement
     - [x] Local SPI tuple mutation, native quotation, JSON EXPLAIN and temporary-operation cleanup
     - [x] All pgrx logging severities, native filtering and terminal reporting after managed unwinding
     - [x] Full-range temporal datum transport and checked .NET conversions in generated functions and typed SPI
      - [x] Core temporal arithmetic, floating-point extraction, parsing/formatting, named-timezone operations and clocks
      - [x] Full-range numeric and checked decimal conversion, native arithmetic/rescaling and exact numeric temporal extraction
      - [x] Temporal operators, component/unit factories, precision clocks, explicit-zone ISO and temporal/numeric JSON
      - [x] Numeric function-boundary constraints, primitive casts, checked generic conversions and operator/sum conveniences
      - [x] Temporal field/epoch accessors, saturating/wrapping raw factories, named/interval timezone conveniences and timeofday
    - [x] Two-/three-column first-row helpers on static SPI, sessions and prepared statements
    - [x] Owned raw SPI queries on static calls, sessions, and prepared statements; exact OIDs/NULLs, explicit converters and raw parameter binding
    - [x] Raw cursor batches with independent native ownership, scrolling, portal invalidation, and guarded cleanup
    - [x] Injected function-call metadata and raw argument snapshots, with actual type/collation identity and scalar/set ownership
   - [x] Backend `_PG_init` bootstrap, guarded exceptions/retry, recursive-load rejection and session preload (PostgreSQL 18.6/Linux x64)
   - [ ] Remaining memory-context parity, shared-preload platform/version validation, and guarded PostgreSQL APIs
- [ ] **P2 — Source generator** (`Ankus.Generators`)
    - [x] `[PgFunction]` → per-function dispatcher + `pg_finfo` shim emission + DDL metadata
    - [x] Scalar/text/bytea conversions, inferred strictness, `T?` NULL handling, SQL overloads
     - [x] Scalar arrays, vectors, dimensions/lower bounds, NULL elements and SQL variadics
    - [x] `[PgSchema]`, explicit function options, SETOF and named TABLE results
    - [x] Polymorphic scalar, SETOF, TABLE and aggregate signatures
    - [x] General `internal` state in scalar, SETOF, TABLE and aggregate callbacks
    - [x] Explicit raw datum bindings in scalar, SETOF, TABLE and aggregate signatures
    - [x] Strongly typed custom codecs and generated base-type declarations
    - [x] Generated default custom-type serialization, tagged variants, custom text and packed native storage for documented shapes
    - [x] Reusable scalar datum readers/writers and owned managed-identity SQL providers for documented paths
    - [x] Typed mapped scalar results in SPI conveniences and named/OID catalog calls, with full local evidence
    - [x] Mapped vectors and shaped arrays with exact element/array identity, independent conversion directions and guarded ownership
    - [x] Finite fully constructed generic mapped roots selected by signatures or exact managed providers, with fixed SQL identity and exact closed converters
    - [x] Explicit closed mapping declarations with independent SQL metadata and finite local raw-only registration
    - [x] Open converter templates closed from exact interfaces, with finite constraint-checked factories
    - [x] Explicit mapped value-type range bounds with independent SQL identities, finite scalar conversion, native operations and range arrays
    - [x] Reject nested SQL array containers consistently with pgrx; preserve multidimensional values through one shaped array
    - [ ] Broader SQL metadata and mapped container contracts, ordinary row/composite conversions and additional serialization/native shapes
  - [ ] `.ankusc` metadata section (JSON) embedded in the `.so`; `ankus schema`
- [ ] **P3 — Extension features**
  - [x] custom installation SQL, binary/prefix operators and explicit/assignment/implicit casts
  - [x] row and statement triggers for supported tuple types
  - [x] event triggers with owned DDL/drop/rewrite metadata and login callbacks
  - [x] aggregates for supported concrete and polymorphic types, owned managed states, worker transport, moving windows and native ordering
  - [x] general raw aggregate signatures with checked type identity and state ownership
  - [x] strongly typed custom base-type aggregate signatures
  - [ ] heterogeneous ordered-set VARIADIC ANY
  - [x] generated equality/order/hash operator classes for declared types, enums and readable manual scalar mappings with independent family SQL controls
  - [x] enum declarations, label/catalog helpers, nullable/scalar/array conversions and SQL dependencies
  - [x] owned named/anonymous composites, descriptors, nested arrays, SETOF/TABLE and SPI bindings
  - [x] generated custom base types with explicit storage/text codecs and binary send/receive
  - [x] custom SQL text with generated CBOR storage and optional NULL-input errors
  - [x] packed native custom-type payloads with custom SQL text and copied managed transport
  - [x] checked native PgVarlena borrowing, copy-on-write, cloning and explicit datum transfer for packed layouts
  - [ ] Complete default CBOR/JSON custom-type serialization (concrete contracts and tagged variants implemented; additional shapes remain), broader native layouts and remaining borrowed storage APIs
  - [x] Typed GUCs/hooks/extras, prefixes/logging, source/privilege/worker/lifetime/package witnesses on PostgreSQL 18.6/Linux x64
  - [x] Static/dynamic background workers, generated entries, lifecycle, signals/latches, connections and transaction callbacks
  - [ ] Remaining GUC raw/preload parity, worker failure/signal boundaries and complete version/platform validation
- [ ] **P4 — Tooling** (`ankus` dotnet tool)
  - [x] Packable `Ankus.Tool`, top-level entry point, System.CommandLine 2.0.12
  - [x] `init`, `info`, `build`, `publish`, and `install` commands, registered installations and explicit overrides
  - [x] `new` creates version-matched extension/MSTest solutions with CPM and discoverable native tests, including the background-worker option
  - [ ] Provisioning/downloads, specialized templates, `schema`, `test`, `run`, server lifecycle and `package` commands
  - [x] Publish native library, `.control`, and versioned `.sql` artifacts
  - [x] Native/SQL installation and DESTDIR staging with target/artifact validation
  - [ ] Distribution packaging and extension upgrades
  - [x] NuGet entry package with automatic runtime, generator, and build integration dependencies
  - [x] Local tool package installation and invocation tests
  - [x] Reusable backend-testing packages
  - [x] Isolated consumer tests using packed NuGet artifacts
  - [ ] After feature parity, add `ankus new --test-framework` templates for MSTest,
    xUnit.net, NUnit and TUnit on Microsoft.Testing.Platform. Keep MSTest as the default
    and validate the same backend lifecycle and recovery contract in each framework.
  - [ ] Public NuGet release after full parity and platform/version validation
- [ ] **P5 — Multi-version matrix**
   - [ ] PostgreSQL 13–18 (+19 beta) and Windows/Linux/macOS validation matrix
- [ ] **P6 — Examples + docs**
    - [x] Astro/Starlight documentation site, using the `ilrepl` docs as a read-only design reference
    - [x] User-facing guides for extension authors; repository workflows and design notes live in `docs/contributing/`
    - [x] Concise guides, short explanations, and restrained formatting for the implemented APIs
    - [x] Verify site build, navigation, links, search, and desktop/mobile layouts
     - [x] Package-based setup guide, validated with isolated NuGet consumers
     - [x] Public hosting and canonical site URL/sitemap; deployed HTML and 244 canonical sitemap entries verified on 2026-09-28
  - [ ] `samples/` mirroring pgrx-examples (aggs, gucs, triggers, bgworker, customscan…)
    - [x] Published range example mirroring pgrx's nine constructors/comparison functions and stored-range sequence
    - [x] README and verified datum-boundary design notes (`docs/contributing/native-boundary.md`)
    - [ ] Complete getting-started, API, deployment, and ported-feature documentation
    - [x] Generated public API reference from XML comments, following the `Dotsider.DocGenerator` design
- [ ] **P7 — Custom scan + nodes**
   - [ ] Full custom scan provider API, native callbacks, and lifecycle integration
     - [x] Trace provider sample with real paths/plans, child execution, projection, rescan, EXPLAIN, cached methods and query-context cleanup
     - [x] Backend tests for backward reads, actual parallel child scans, concurrent-update rechecks and managed/native error recovery on Linux x64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11
     - [x] Provider-owned parallel DSM initialization, worker attachment, reinitialization, shutdown snapshots and error recovery on Linux x64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11
     - [x] Index/index-only mark and restore, backward cursors, parameterized rescans, parallel children and error recovery on Linux x64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11
     - [x] Provider-owned parameter expressions and partition-child remapping on Linux x64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11
     - [x] Independent predecessor chaining and native registry identity, name boundaries, lifetime, cleanup and recovery on Linux x64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11
     - [ ] Older parameter-helper ABI adaptation and complete PostgreSQL/platform validation
   - [ ] PostgreSQL node representations and pgrx node support APIs
   - [ ] Corresponding examples and backend-executed tests

## Risk register

| Risk | Mitigation |
|---|---|
| AOT `.so` inside a Postgres backend | Validate runtime initialization and signal behavior before expanding |
| Postgres `longjmp` crossing AOT frames | Design a guarded native boundary and avoid finalizer-dependent state |
| Variadic PostgreSQL C functions | Add minimal native helpers only where a non-variadic API is unavailable |
| Struct layout drift across PG versions | Generated shim + layout table generated from per-version headers; matrix tests |
| Native library size | Initial integer probe was approximately 933 KB on Linux x64 |
| `dlclose` unsupported by AOT libs | N/A — Postgres keeps extension modules loaded for the backend's lifetime |

## Milestones

- 2026-09-21 — Native AOT shared-library exports and the PostgreSQL module contract verified.
- 2026-09-22 — `c819160`: local PostgreSQL cluster harness under `tests/`.
  `dotnet test`: 30 passed, including 16 PostgreSQL integration cases.
- 2026-09-22 — `82ca5e6`: `[PgFunction]`, Roslyn incremental generation,
  metadata-only artifact extraction, and a native error boundary linked into the AOT image.
  `dotnet test`: 58 passed, including 16 PostgreSQL integration cases.
- 2026-09-22 — `e227b14`: extension control and versioned SQL files; installation with `CREATE EXTENSION`.
  `dotnet test`: 61 passed, 0 failed, 0 skipped, including 19 PostgreSQL integration cases.
- 2026-09-22 — `a5523ff`: scalar/text/bytea conversions, nullable signatures and results, SQL overloads,
  strict UTF-8 conversion, server-encoding conversion, and native buffer cleanup across PostgreSQL errors.
  The sample now includes `Greet`; backend-only datum probes live in `tests/Ankus.TestExtension`.
  `dotnet test`: 149 passed, 0 failed, 0 skipped (84 PostgreSQL integration cases).
- 2026-09-22 — Guarded `Spi.Execute`, recoverable native errors, structured `PgException` reporting,
  recursive backend bindings, and cancellation-safe managed unwinding.
   `dotnet test`: 166 passed, 0 failed, 0 skipped (93 PostgreSQL integration cases).
- 2026-09-22 — Typed SPI parameters, owned query results and metadata, scalar reads, read-only mode,
  limits, domain base conversion, and conversion-error rollback. IDE1006 naming rules enforced in builds.
  `dotnet test`: 207 passed, 0 failed, 0 skipped (134 PostgreSQL integration cases).
- 2026-09-22 — Owned prepared statements, reusable typed execution, plan invalidation, guarded disposal,
  recursive execution, and cleanup across query errors and cancellation.
  `dotnet test`: 238 passed, 0 failed, 0 skipped (165 PostgreSQL integration cases).
- 2026-09-22 — SPI cursor batching, plan-independent portals, detach/find, forward/backward fetch,
  native portal lifetime identities, rollback invalidation, and guarded disposal.
  `dotnet test`: 267 passed, 0 failed, 0 skipped (194 PostgreSQL integration cases).
- 2026-09-22 — Full-length owned PostgreSQL error diagnostics, context/object/query/source fields,
  original-location rethrow without duplicated context, server-only diagnostics, and fallback/encoding cleanup.
  `dotnet test`: 286 passed, 0 failed, 0 skipped (206 PostgreSQL integration cases).
- 2026-09-22 — Scoped SPI sessions, session-bound prepared plans with native invalidation registration,
  retained ownership transfer, nested-scope recovery, and connection/plan/tuple cleanup.
  `dotnet test`: 313 passed, 0 failed, 0 skipped (233 PostgreSQL integration cases).
- 2026-09-22 — UUID and owned JSON/JSONB datum conversion across generated functions and all SPI paths,
  source-generated JSON serialization in Native AOT, and native/managed conversion error recovery.
  `dotnet test`: 368 passed, 0 failed, 0 skipped (265 PostgreSQL integration cases).
- 2026-09-22 — Local SPI row edits and cell type metadata, native SQL quotation, JSON EXPLAIN,
  and per-operation memory contexts preventing temporary buffer retention until transaction end.
  `dotnet test`: 409 passed, 0 failed, 0 skipped (300 PostgreSQL integration cases).
- 2026-09-22 — PostgreSQL logging, structured reports, native routing and severity mapping,
  managed ERROR handling, FATAL connection termination and isolated PANIC crash-recovery tests.
  `dotnet test`: 434 passed, 0 failed, 0 skipped (325 PostgreSQL integration cases).
- 2026-09-22 — Astro/Starlight site with eight author-facing pages, search, theme selection, and responsive navigation.
  Contributor setup, backend tests, and native design notes moved to `docs/contributing/`.
  `pnpm install --frozen-lockfile`, `pnpm check` and `pnpm build` passed. Local Playwright checks passed at
  1440px and 390px, including navigation, mobile menu, search, theme switching and all 44 internal links/anchors.
  Screenshots are in `artifacts/docs-check/`. Astro emits a duplicate 404 route warning; the generated 404 page works.
  Sitemap generation awaits the public site URL. These checks cover the current pages, not complete feature documentation.
- 2026-09-22 — Packable `ankus` tool with registered PostgreSQL selection, atomic configuration updates,
  Native AOT build/publish, manifest-based install and DESTDIR staging. `ToolCommandTests` installs a freshly
  packed tool, publishes an author project, stages its files, and executes it through an isolated PG18 backend.
  Invalid registrations, artifact targets, incomplete output and failed builds fail without silent fallback.
  System.CommandLine 2.0.12 is centrally pinned; IDE0305 now fails builds. `dotnet test`: 457 passed,
  0 failed, 0 skipped, including 23 installed-tool cases. Full CLI provisioning/lifecycle parity remains pending.
- 2026-09-22 — `Ankus.DocGenerator` uses DocFX 2.80.1 to generate Starlight API reference pages from
  `Ankus.Runtime`, `Ankus.PgConfig`, and `Ankus.Testing` assemblies and XML comments. Hidden interop
  types are excluded. The current reference has 26 generated namespace/type pages and 207 members.
  `--check` passed and detected an intentionally changed page; regeneration restored it and removed
  a marked stale page. `pnpm build` regenerates the API pages. Build and `pnpm check` pass; browser checks
  cover all 36 content pages at 1440px and 390px, API-member search, and 366 internal links/anchors.
  Plain `dotnet test` remains 457 passed, 0 failed, 0 skipped after adding the generator to the solution.
- 2026-09-22 — Temporal datum transport for date, time, timetz, timestamp, timestamptz, and interval, with
  full-range value types and ordinary .NET adapters. Microseconds, BC values, infinities, mixed interval signs,
  second-resolution offsets, and nullable contracts survive every SPI path. Interval infinity has a separate
  discriminator and version-gated native support; finite sentinel collisions raise a native range error.
  Tests independently verify numeric fields and binary storage, DST calendar-day differences, domain ownership,
  output conversion failures, session recovery, and zero extra contexts after 100 parameterized native operations.
  `dotnet test`: 542 passed, 0 failed, 0 skipped (394 integration cases). `pnpm build` passed and regenerated
  32 public API pages with 282 members. `pnpm check` and API freshness verification passed.
  Temporal arithmetic/text/timezone APIs and the platform/version matrix remain pending.
- 2026-09-22 — PostgreSQL-backed temporal parsing, TryParse, formatting, calendar arithmetic, age, extraction,
  truncation, named timezones, interval normalization/scaling, and server clocks. Typed native calls use the
  existing guarded subtransaction and disposable context without connecting to SPI. Managed comparisons preserve
  infinity, 24:00 and timetz offset tie-breaking; interval comparison remains distinct from exact component equality.
  `TemporalOperationTests` adds 113 backend cases, including independent SQL comparisons, explicit expected formats,
  DST gap/overlap rules, error SQLSTATEs, write preservation, finally execution, clock identity and temporary cleanup.
  `dotnet test`: 658 passed, 0 failed, 0 skipped (507 integration cases). `pnpm build` passed and regenerated
  33 public API pages with 408 members. `pnpm check` and API freshness verification passed.
  Remaining temporal features and the platform/version matrix are tracked above.
- 2026-09-22 — Full-range `PgNumeric` and exact `decimal` adapters across generated functions and every typed SPI path.
  Numeric arithmetic, result scale, rescaling, transcendental routines and exceptional values use PostgreSQL's native
  functions. Managed value comparison/hash ignores display scale and follows PostgreSQL's NaN ordering. `BigInteger`
  conversions retain finite integers; decimal narrowing rejects silent rounding and underflow. Temporal `Extract`
  returns exact numeric on PG14+, with the PG13 floating-point fallback still awaiting matrix validation.
  Numeric tests verify binary payloads, full range limits, TOAST/packed storage, domain ownership, typmod validation,
  native/managed recovery, write preservation and zero retained temporary contexts after repeated operations.
  `dotnet test`: 754 passed, 0 failed, 0 skipped (580 integration cases, including 73 new numeric cases).
  `pnpm build`, `pnpm check` and API freshness verification pass; the reference has 34 pages and 461 members.
  Remaining numeric features and the platform/version matrix are tracked above.
- 2026-09-22 — Added temporal operators, component/unit factories, current/local clocks and precision rounding,
  interval comparison duration/sign and checked component absolute value, explicit-zone ISO and temporal/numeric
  JSON converters. The native scalar table supports seven validated arguments. Instant formatting resolves the
  target instant's offset without changing session settings. Rounding at the maximum finite timestamp revealed
  that the server's scale routine can return an out-of-range value; the bridge now raises SQLSTATE 22008 inside
  the native guard before materialization. Source-generated JSON contracts preserve scale/full ranges and wrap
  invalid input with JSON property paths and native causes while leaving backend-access errors intact.
  `dotnet test`: 894 passed, 0 failed, 0 skipped (716 integration cases, including 136 new temporal/JSON cases).
  The four new runtime cases verify exact interval limits, checked precision and detached converter access.
  `pnpm build`, `pnpm check` and API freshness verification pass; the reference has 34 pages and 524 members.
- 2026-09-22 — Packaged the MSBuild project SDK, runtime, generator, configuration, testing harness and tool.
  Cold consumers outside the checkout use only NuGet artifacts. Installed-tool and direct publishing load real
  extensions, including CPM/global.json and paths with spaces; an external MSTest consumer verifies ordinary
  discovery and native error recovery. `dotnet test`: 899 passed, 0 failed, 0 skipped (721 integration cases).
- 2026-09-22 — Added declarative numeric precision/scale with `ANKUS003` validation and native guarded rescaling;
  PostgreSQL primitive casts, exact generic integer conversion, exact implicit primitive operators, .NET generic
  operator interfaces and one-pass numeric summation. Added 67 backend cases, 10 generator cases and three runtime
  cases. `dotnet test`: 979 passed, 0 failed, 0 skipped (788 integration cases). The generated reference now contains
  35 pages and 560 members. Platform/version validation remains pending.
- 2026-09-22 — Added `ankus new`, creating ordinary extension/MSTest solutions from bundled templates with CPM,
  pinned matching package versions, source/test separation, and root-level `dotnet test` discovery. Added
  `PostgresExtensionTest` to publish, start and load the native extension in PostgreSQL 18+ without modifying
  the shared installation. Installed-tool tests cover path/name handling, keyword namespaces, one-pass token
  replacement, file preservation, solution selection, five generated tests, deliberate native behavior changes,
  and failed-build/failed-load cleanup. `dotnet test`: 995 passed, 0 failed, 0 skipped (804 integration cases).
  All six packages and tool `--no-build` packing pass. Docs build, type check and API freshness pass; 36 public
  API pages and 563 members. Specialized templates, automatic pre-18 extension staging and the platform/version
  matrix remain pending.
- 2026-09-22 — Moved the public testing package to `src/Ankus.Testing`, expanded single-line XML summaries,
  and enforced CA1000 in repository and generated projects. Release build and all 995 tests pass.
  Added C# type, attribute, generic-parameter and variable colors to both documentation themes. Playwright CLI
  verified the home, SPI API, function guide and numeric guide in dark/light mode, including the reported
  `Connect<TResult>` signature. Mobile layout has no horizontal overflow; docs build, type check and API
  freshness pass. Theme changes require a forced Astro rebuild to invalidate cached Markdown.
- 2026-09-22 — Added scalar arrays across generated functions and all SPI owners, with `T[]` vectors,
  `PgArray<T>` dimensions/lower bounds, exact element adapters and C# `params` SQL variadics. The single-buffer
  transport preserves native ownership boundaries and embedded binary zeroes. Added 30 backend cases,
  20 generator cases and 19 runtime cases; isolated package consumers also execute shaped and variadic arrays.
  Plain `dotnet test`: 1064 passed, 0 failed, 0 skipped. Release build passes with zero warnings/errors.
  Documented 101 previously undocumented internal declarations; a follow-up Roslyn scan reports zero omissions.
  Docs build, type check and API freshness pass; the reference contains 37 pages and 574 members.
- 2026-09-22 — Added function execution options, named/defaulted arguments, and fixed schema declarations.
  Defaults preserve signed minima, uint bounds, decimal scale, signed zero, Unicode and value-type epochs.
  Schema ownership is explicit; fixed placement generates non-relocatable control metadata, and schema-only
  packages publish and load as native libraries. Catalog, privilege, setting restoration, replacement-dependency,
  uninstall/reinstall and isolated package tests verify the resulting behavior. Added 28 generator and 27 backend
  cases. Plain `dotnet test`: 1119 passed, zero failures/skips; Release build: zero warnings/errors.
  Internal documentation scan, site build/type check and API freshness pass. The API reference has 42 pages and
  599 members. Complete entity dependencies, custom SQL, additional type families and the PG/platform matrix
  remain part of the active port.
- 2026-09-22 — Added custom SQL strings/files and a deterministic dependency graph shared with generated
  schemas/functions. Named dependencies, before constraints, bootstrap/final edges and cycle diagnostics run at
  compile time. AdditionalFiles content is tracked incrementally; an isolated package consumer verifies file-only
  edits, SQL-only Native AOT loading, relocation, uninstall and rollback after a SQL installation error.
  Added 30 generator cases and three backend/package cases. Plain `dotnet test`: 1152 passed, zero failures/skips;
  Release build: zero warnings/errors. Internal documentation scan and documentation build/type/freshness checks
  pass; the API reference has 45 pages and 620 members. Function SQL overrides, declared type providers and the
  wider runtime/tooling/platform inventory remain pending.
- 2026-09-22 — Added immutable inet/cidr values, checked IPAddress/IPNetwork mappings, PostgreSQL parsing,
  detached masks/subnets/comparison, and source-generated JSON support. Binary transport uses PostgreSQL's
  send/receive functions with portable family markers and existing allocator-matched ownership. Scalars and
  arrays work through every SPI lifetime path, including packed/domain/TOAST inputs. Added 21 runtime,
  12 generator and 40 backend cases. Plain `dotnet test`: 1225 passed, zero failures/skips. Release build:
  zero warnings/errors; XML documentation and site build/type/freshness checks pass. The API reference has
  47 pages and 665 members. Geometry, ranges, future type families and the broader port inventory remain pending.
- 2026-09-22 — Added all seven pgrx geometric datum families, owned path/polygon vertices, detached bounds
  and formatting, guarded parsing, and scalar/array SPI conversions. Native binary I/O preserves exact IEEE
  coordinates, and empty owned collections use PostgreSQL-header-derived storage. Tests verify all lifetime
  paths, signed zero/NaN payloads, polygon bounds, 10,000-vertex compressed/external TOAST values, domains and
  native input/output failure recovery. Added 15 runtime, seven generator (28 signature contracts), and 47
  backend cases. Plain `dotnet test`: 1294 passed, zero failures/skips; Release build: zero warnings/errors.
  XML documentation and site build/type/API freshness checks pass; 54 API pages document 744 members.
  Dedicated geometric operation wrappers, ranges and the remaining full-port inventory are still pending.
- 2026-09-22 — Added `PgRange<T>` for the six built-in range families and ten managed bound types, with
  explicit empty/unbounded/inclusion states, checked .NET aliases, scalar/array SPI transport and C# index-range
  conversion. Guarded native calls handle canonicalization, parsing/output, containment, adjacency, overlap and
  set operations. Added 18 runtime, 14 generator (40 supported signature contracts) and 96 backend cases,
  covering eight ownership paths, invalid/disjoint/overflow boundaries, full-range/special bounds, session
  DateStyle/TimeZone, toasted 32,001-digit numeric bounds and 10,000-element arrays. Plain `dotnet test`: 1422
  passed, zero failures/skips; Release build: zero warnings/errors; internal XML scan: zero omissions.
  The range guide, native-boundary notes and generated API pages pass site build/type/freshness checks;
  56 API pages document 772 members.
  Custom range subtypes, multiranges and the remaining full-port inventory are still pending.
- 2026-09-22 — Added generated PostgreSQL enums, exact labels, schema/type/function dependencies, all scalar/array
  SPI paths and guarded live catalog helpers. Validation covers enum identity, C# numeric boundaries, source label
  order, SQL defaults/variadics, domains/TOAST, DDL recreation, extension relocation, missing/wrong-kind catalog entries,
  label changes, transaction visibility and LATIN1 labels/identifiers. Installation scripts now declare UTF-8.
  Added 18 runtime, 72 generator and 40 backend/package cases. Plain `dotnet test`: 1552 passed, zero failures/skips;
  Release build: zero warnings/errors; internal XML scan: zero omissions. Site build/type/freshness checks pass;
  60 API pages document 796 members. Added path-independent `AGENTS.md` conventions and enforced runtime/MSBuild
  explicit-type rules in the repo and scaffold, including build-based consumer checks. The full-port inventory and
  supported PostgreSQL/platform matrix remain active requirements.
- 2026-09-22 — Added standalone operator/cast declarations with optional backing-function settings, PostgreSQL
  name/signature validation, planner options, conversion contexts and independent SQL graph dependencies.
  Added 170 generator and 37 backend cases, including actual hash/merge joins, nullable/typmod conversions,
  enum-array ownership, guarded rollback/recovery, relocation/reinstallation and cold package consumers.
  Plain `dotnet test`: 1759 passed, zero failures/skips on PostgreSQL 18.6/Linux x64; Release build: zero warnings/errors.
  Internal XML scan: 495 declarations, zero omissions. Site build/type/API freshness checks pass; 63 API pages
  document 813 members. IDE0290 now enforces eligible primary constructors alongside the existing explicit-type
  rules. Removed both warning pragmas and extra blank lines after opening braces, preserving copy-ownership tests.
  Corrected the previous scaffold policy: repository coding style is enforced only in the repository; `ankus new`
  emits neither a style `.editorconfig` nor code-style build enforcement. Removed duplicate analyzer release-file
  entries without disabling diagnostics. Automatic operator classes, further type families, and the full port/platform
  inventory remain active requirements.
- 2026-09-22 — Added SETOF/TABLE declarations through ordinary `IEnumerable<T>` and named tuples, column-name
  overrides, planner rows, streaming and PostgreSQL tuple-store materialization. Typed iterator ownership covers
  early LIMIT/portal shutdown, native errors, cancellation and restricted owned-resource cleanup during abort.
  Regressions identified and verified disposal snapshots and deferred cursor release after PostgreSQL portal scans,
  including adoption of parent-transaction cursors. Added 161 generator, 12 runtime and 52 backend/sample cases.
  Plain `dotnet test`: 1984 passed, zero failures/skips on PostgreSQL 18.6/Linux x64, including the packed SDK consumer.
  Non-incremental Release build: zero warnings/errors; internal XML scan: 525 declarations, zero omissions.
  Site build/type/API freshness checks pass; 65 API pages document 820 members. Existing site warnings for the
  duplicate 404 route and missing public site URL remain visible. IDE2003 enforces blank lines after closing blocks;
  repository sources and emitted dispatchers are corrected, without applying repository style to consumers.
  The wider runtime/tooling/type inventory and PostgreSQL/platform matrix remain active requirements.
- 2026-09-22 — Converted `Ankus.Build/Program.cs` to top-level statements with static local helpers.
  Argument handling, compiler flags, generated artifacts and exit codes are preserved. The build-tool Release
  build has zero warnings/errors; the invalid-argument probe verifies the error text and exit code 1.
  Plain `dotnet test`: 2414 passed, zero failures/skips on PostgreSQL 18.6/Linux x64, including Native AOT
  publishing and isolated package consumers. This structural change does not alter the public API or guides.
- 2026-09-22 — Added event-trigger callbacks, immutable DDL/drop/rewrite snapshots, login support, native
  invocation guards and nested event/row/function scope restoration. Added 71 runtime, 80 generator and
  44 backend/sample cases, including NULL catalog identities, address arrays, rewrite effects, ownership,
  rollback, cancellation, LATIN1, connection recovery and relocation/reinstallation. Independent review
  identified mutable automatic callback headers read after `longjmp`; event and row bridges now store
  those headers in callback memory contexts through stable pointers, preserving cursor cleanup ordering.
  Plain `dotnet test`: 2609 passed, zero failures/skips on PostgreSQL 18.6/Linux x64; non-incremental Release
  build: zero warnings/errors. Style verification passes; 318 C# files have no extra opening-brace blank
  lines and 573 internal declarations have no XML omissions. Added the public event-trigger guide/sample;
  81 API pages document 924 members and the site builds 107 pages. Site type/API freshness checks pass.
  Raw parse-tree/opaque-command bindings, the remaining full-port inventory and PostgreSQL/platform
  validation remain active requirements.
- 2026-09-22 — Completed temporal field/epoch conveniences, raw saturation/wrapping, named-zone timetz
  construction, server timezone-offset lookup, interval-zone overloads and owned live timeofday text.
  Added 115 runtime and 148 backend cases, with exact full-range/BC/microsecond/offset/binary oracles,
  finite-endpoint local-cast failure witnesses and 50-cycle error/finally/plan/write/context recovery.
  Plain `dotnet test`: 3172 passed, zero failures/skips on PostgreSQL 18.6/Linux x64; Release build:
  zero warnings/errors. The public guide, README and generated API now document the field/raw/timezone
  distinctions; 89 API pages contain 1033 members and the site builds 116 pages. XML review found no
  omissions in 678 internal declarations. The remaining raw API, extension features, tooling and complete
  PostgreSQL/platform validation remain required full-port work.
- 2026-09-22 — Added backend `[PgInitialize]` with init-only publication, ANKUS013 declaration diagnostics,
  owned exceptions, managed finally, retry/reentrancy handling, session preload and a native postmaster
  guard. Actual preload testing exposed an absent startup snapshot; the native wrapper now owns a
  snapshot only when needed and releases it on success/error while preserving caller snapshots.
  Added 7 runtime, 50 generator and 13 backend cases. Plain `dotnet test`: 3242 passed, zero failures/skips
  on PostgreSQL 18.6/Linux x64; non-incremental Release build: zero warnings/errors. Documentation build
  and type checks pass; 90 API pages contain 1034 members, with 118 site pages. XML scan: 683 internal
  declarations, zero omissions. Warning suppression and opening-brace whitespace scans are clear.
  Native-only preload, full GUCs/hooks, managed postmaster initialization, actual no-transaction native
  loading and the remaining version/platform matrix remain explicit full-port requirements.

- 2026-09-22 — Added native-backed GUC properties for five types, contexts/options/units, full-width enum
  mappings, typed check/assign/show hooks, copied extras, placeholder adoption, restoration, reload,
  permissions and native-only preload. Native review and real publishing exposed phase restrictions,
  nontransactional encoding needs and unused helper emission; generated bridges now select only the
  required helpers, including check-only, assign-only and show-only libraries, without suppressing warnings.
  Added 52 runtime, 114 generator and 39 backend cases. Plain `dotnet test`: 3447 passed, zero failures/skips,
  3m14.369s on PostgreSQL 18.6/Linux x64. Release build: zero warnings/errors. XML scan: 755 internal
  declarations, zero omissions; 415 C# source/template files have no opening-brace blanks or suppressions.
  Documentation build/type/API checks pass: 104 API pages, 1118 members, 133 site pages. Public guides and
  sample describe exact hook/preload limits. Prefix reservation, raw placeholder behavior, restricted logging,
  arbitrary managed postmaster callbacks, mixed-encoding preload, remaining lifecycle/ownership witnesses,
  packaged GUC consumers and the complete PostgreSQL/platform matrix remain active full-port requirements.

- 2026-09-22 — Added assembly configuration prefix declarations and guarded logging in all GUC hook
  phases, including reload, abort restoration and client reporting. Native prefix behavior preserves
  literal case and PostgreSQL's version-specific warning/removal/reservation semantics; prefix-only
  libraries preload without managed entry. Diagnostics preserve every field, filtering and terminal
  severity after managed finally, including LATIN1 conversion failures. Shared native error copies now
  own version-dependent source/translation metadata. Added 47 runtime, 21 generator and 19 backend
  cases. Plain `dotnet test`: 3534 passed, zero failures/skips, 3m06.420s on PostgreSQL 18.6/Linux x64.
  Non-incremental Release build: zero warnings/errors. XML scan: 775 internal declarations, zero
  omissions; style scan: 426 C# source/template files, zero opening-brace blanks or suppressions.
  Documentation build/type/API freshness checks pass: 105 API pages, 1120 members and 134 site pages.
  Raw placeholders, managed postmaster callbacks, mixed-encoding preload, remaining source/privilege,
  worker/lifetime/package witnesses and the complete full-port/platform inventory remain required.

- 2026-09-22 — Added 24 native configuration lifecycle cases covering five startup sources and reset values,
  replay-time original setter grants, real worker values/extras/NULL semantics and recovery, measured native
  allocation/managed-root lifetimes, and cold NuGet consumers through SQL drop/reinstall. Added a parallel-safe
  configuration sample probe. No runtime or native bridge defect was found in this bounded work; test isolation
  and transaction setup were corrected. Plain `dotnet test`: 3558 passed, zero failures/skips, 3m37.716s on
  PostgreSQL 18.6/Linux x64. Non-incremental Release build: zero warnings/errors. XML scan: 789 internal
  declarations, zero omissions; style scan: 433 C# source/template files, no opening-brace blanks or suppressions.
  Public guide/README updates and documentation build/type/API freshness checks pass: 105 API pages,
  1120 members and 134 site pages. Native memory assertions prove warmed GUC allocation equality and bounded
  libc growth on glibc 2.41, not arbitrary leak absence or cross-platform allocation parity. Raw placeholders,
  managed postmaster callbacks, mixed-encoding preload, the broader port inventory and the full supported
  PostgreSQL/platform matrix remain active requirements.

- 2026-09-22 — Added checked PostgreSQL memory contexts and palloc chunks, monotonic lifetime identities,
  reset/delete invalidation, nested current-context restoration, UTF8/server-encoding context names,
  direct guarded allocation errors, and callback-independent provider identity. Every current generated
  callback family receives the memory capability; assign/show-only hooks retain SQL restrictions.
  Added 44 runtime, 16 generator, and 20 backend cases, including transaction/subtransaction cleanup,
  repeated resets, iterator shutdown, encoding-failure cleanup, native inventory, and protected callback
  storage. Fixed a GUC reload test's transaction timing using pre-prepared Close/Sync messages.
  Plain `dotnet test`: 3638 passed, zero failures/skips, 3m13.702s on PostgreSQL 18.6/Linux x64.
  Non-incremental Release build: zero warnings/errors. XML scan: 848 internal declarations, no omissions;
  style scan: 445 C# source/template files, no opening-brace blanks or suppressions. Public guides,
  README, generated API reference, and site validation pass: 108 API pages, 1155 members, 138 site pages.
  Managed reset/drop callbacks, typed/aligned/huge/raw allocation and ownership transfer, native boxes,
  virtual context/datum integration, broader cleanup-phase witnesses, and the complete full-port and
  PostgreSQL/platform inventory remain active requirements.

- 2026-09-22 — Added cancellable one-shot memory reset/drop callbacks, managed root ownership,
  native LIFO and error/retry behavior, and protected callback/iterator/aggregate cleanup owners.
  Guarded recovery now preserves interrupt holdoffs and avoids reclaimed ErrorContext scratch;
  ErrorContext-owned callbacks reject native work before entering PostgreSQL's error stack.
  Retained SPI resource cleanup now drains adopted parent cursors during late savepoint cleanup.
  Added 19 runtime and 52 backend cases with exact diagnostics, GC roots, native resource counts,
  UTF8/LATIN1 conversion, and same-session recovery. Focused backend run: 52 passed in 1m22.548s.
  Plain `dotnet test`: 3709 passed, zero failures/skips, 3m20.988s on PostgreSQL 18.6/Linux x64.
  Non-incremental Release: zero warnings/errors; XML: 856 internal declarations, no omissions;
  style: 451 C# source/template files, no opening-brace blanks or suppressions. README/guides/API
  updated; documentation build/check/freshness pass with 109 API pages, 1158 members, 139 site pages.
  Typed/aligned/huge/raw/native-box allocation, virtual context/datum integration, remaining full
  port inventory and actual PostgreSQL 13–19 beta/Windows/Linux/macOS validation remain active.

- 2026-09-22 — Added typed/aligned allocation, huge-policy routing, checked multiplication, independent
  span/UTF-8 copies, exact zero-tail resize, raw detach/adopt, AllocSet presets/custom sizes, and
  transient context cleanup with preserved diagnostics. Native aligned no-OOM calls enforce the
  source-reviewed maintenance/beta fixes. Added 67 direct runtime and 53 PostgreSQL cases; review
  added an independent ordinary-block maximum-growth witness. Plain `dotnet test`: 3829 passed,
  zero failures/skips, 3m13.046s on PostgreSQL 18.6/Linux x64. Non-incremental Release: zero
  warnings/errors; XML: 863 internal declarations, no omissions; style: 457 source/template files,
  no opening-brace blanks or suppressions. Documentation/API checks pass: 111 API pages,
  1180 members, 141 site pages. Actual >1 GiB allocation, native exhaustion/registration-failure
  witnesses, Slab/Bump behavior, native boxes, virtual contexts/datum/node APIs, the remaining
  full-port inventory and the actual PostgreSQL/platform matrix remain active requirements.

- 2026-09-22 — Added individually owned native boxes, context-owned values and typed borrowed
  references with copied access, exact padding/shallow-pointer clones, nullable raw interop,
  ownership transfer and checked reset generations. Direct and backend tests cover failed transfer,
  immediate 64 KiB native reclamation, wrapper collection, moved offset views, two successive
  generations, failed-reset retry, commit/rollback and savepoint cleanup. All 34 direct runtime and
  34 backend cases pass; plain `dotnet test` passes 3897/3897 with no skips in 3m43.662s on
  PostgreSQL 18.6/Linux x64. IDE0004 is now an error repository-wide; Roslyn removed redundant
  casts from 19 files and its verification pass is clean. Release build: zero warnings/errors;
  XML: 878 internal declarations with no omissions; style: 466 C# source/template files with
  no opening-brace blanks or warning suppressions. README/guides/API are updated; documentation
  build, type checks and API freshness pass with 114 API pages, 1210 members and 144 site pages.
  Virtual context/datum/node APIs, actual huge allocations, native allocator fault witnesses,
  Slab/Bump, custom release policies, the remaining full inventory and actual PostgreSQL/platform
  matrix remain required. Consumer style choices and read-only references are preserved.


- 2026-09-22 — Added virtual `PgMemoryContext` parameters to scalar/set functions, operators and
  casts with SQL-only ordinals, declarations and overload identity. Set factories select their
  multi-call owner, abort disposal precedes direct-owner invalidation, and persistent reservations
  protect suspended owners and ancestors. Added 59 generator and 48 real PostgreSQL cases;
  focused runs pass with zero failures/skips (3.224s and 1m19.984s). Plain `dotnet test` passes
  all 4004 cases without skips (3m16.906s) on PostgreSQL 18.6/Linux x64. Non-incremental Release
  passes with zero warnings/errors; XML inspection covers 902 internal declarations and style
  inspection covers 470 source/template files, both clean. README/guides/API are updated;
  documentation build/type checks/API freshness pass (114 API pages, 1210 members, 144 site pages).
  Direct native Delete protection remains source-reviewed, since borrowed handles do not expose
  native deletion. Actual huge-size and native fault witnesses, allocator variants, datum/node
  integration, custom release policies, full remaining port scope and the platform/version
  matrix remain required.


- 2026-09-22 — Enforced IDE0300 as an error in the root `.editorconfig` and recorded the rule in
  AGENTS and the development guide. A non-incremental build rejected existing simplifiable array
  initializers, proving build enforcement. Roslyn's diagnostic-specific fix simplified 16 initializers
  across eight test files without changing expected values or weakening assertions. The subsequent
  non-incremental Release build passes with zero warnings/errors (6.79s). Plain `dotnet test` passes
  4004 cases with zero failures/skips (3m53.735s) on PostgreSQL 18.6/Linux x64. Combined IDE0300/IDE0004
  formatter verification is clean. Documentation build, type checks and API freshness pass; the
  470-file source/style scan is clean and AGENTS contains no personal paths. Consumer templates
  retain their own style.

- 2026-09-22 — Added borrowed allocator support and controlled native failure witnesses. Source review across PostgreSQL 13–19
  confirmed Slab's fixed-size errors, Generation's version-dependent keeper behavior, and Bump's
  missing chunk headers in ordinary builds. The bridge now guards unsupported Bump free/resize/
  adoption before pointer dispatch, uses established registry ownership, and reserves bookkeeping
  before requesting native bytes. Context-name conversion and error reconstruction use AllocSet
  scratch storage. Full generator baseline (1172 cases), focused runtime (80 cases), and focused
  backend (95 cases, 1m16.808s) checks pass on assertion-enabled PostgreSQL 18.6/Linux x64. Backend
  witnesses include six controlled native registry/allocation-boundary failures, same-session
  recovery, encoded diagnostics, reset generations, and actual external-block reclamation before
  reset. GCC's longjmp-lifetime diagnostics were resolved by extracting guard and cleanup helpers;
  no compiler flags or diagnostic severities were reduced. A separate PostgreSQL 18.6 release
  installation is built under `artifacts/postgres-release/18.6-install`, with assertions and
  MEMORY_CONTEXT_CHECKING disabled in its matching server and headers. All 95 cases pass on that
  actual headerless backend too (51.405s). Plain `dotnet test` passes 4072 cases without failures or
  skips (3m22.788s); Release build has zero warnings/errors and analyzer/XML/style checks pass.
  Documentation build/check/API freshness pass (114 API pages, 1210 members, 144 site pages). PostgreSQL 19's
  successful-notice ErrorContext reset behavior is source-reviewed and has version-aware tests,
  but has not yet been executed on PostgreSQL 19. Actual huge allocations, remaining full-port
  scope and the version/platform matrix remain active.

- 2026-09-22 — Reproduced VS Code's `sigjmp_buf` error in the new native allocator fixture
  using strict `-std=c17`: glibc hides the POSIX jump-buffer type in that mode. The same file
  passes `cc -std=gnu17 -fsyntax-only -Wall -Wextra -Werror` against the selected PostgreSQL
  headers. Added repository-only `.vscode/settings.json` selecting GNU C17 for C/C++ IntelliSense,
  matching the native compiler mode without disabling any diagnostics.

- 2026-09-23 — Added actual huge-allocation lifecycle evidence on release PostgreSQL 18.6/Linux x64.
  Five resource cases allocate 1 GiB + 17, grow by 1 MiB while still above the ordinary limit,
  shrink to 128 and free before deleting the owner. Ordinary, no-OOM, aligned, aligned no-OOM
  and zeroed paths preserve values, checked views, metadata and native/catalog accounting.
  The dedicated run passes 10/10 cases (five small plus five huge), zero failures/skips, 53.581s.
  The assertion-enabled small baseline passes 5/5 (1m18.162s); plain `dotnet test` passes all
  4077 default cases without failures/skips (3m34.235s). Resource admission checks the actual
  cgroup hierarchy and host headroom, limits each warmed backend's additional address space to
  3 GiB, records VmHWM, and restores original limits. Peak observed RSS is 1,101,213,696 bytes;
  no cgroup max/oom/oom_kill events increased. The helper preserves lifecycle and cleanup failures
  without warnings or suppression. Non-incremental Release has zero warnings/errors (4.66s);
  IDE0004/IDE0300, XML (911 internal declarations), source style (478 files), documentation build,
  type checks and API freshness all pass. Memory/development guides and remaining-scope entries
  are updated. Actual physical exhaustion, datum/node APIs, custom release/unsized layouts,
  broader allocator/resource boundaries, the remaining port inventory and the full matrix
  remain required; the five huge rows are not included in default-suite totals.

- 2026-09-23 — Enabled managed `shared_preload_libraries` without consumer setup on
  PostgreSQL 18.6/Linux x64 and PostgreSQL 18.1 on macOS ARM64 and Windows x64.
  Runtime commit `232bc9d1f` keeps Unix postmasters at one thread between managed
  callbacks and restores runtime services in each forked backend; Windows follows
  PostgreSQL's `EXEC_BACKEND` process model. Repeated reload hooks and three fresh
  backends pass on every platform. Retained-service probes cover timers, queued work,
  waits, descendants, finalization, workstation GC and four-heap server GC. The
  Linux suite passes 4089/4089 in 4m04.670s; Release and runtime builds have zero
  warnings or errors. Removed production friend access from `Ankus.PgConfig` to
  `Ankus.Build`, exposed immutable parsed compiler arguments as a public contract,
  and removed the empty build-test project. API generation and documentation checks
  pass with 114 pages and 1213 members. macOS x64 and PostgreSQL 13–17/19 beta remain.

- 2026-09-23 — Added managed transaction and subtransaction callbacks with all
  PostgreSQL event mappings, ordered one-shot outer callbacks, repeating savepoint
  callbacks, cancellation receipts and transaction-owned managed roots. A reentrant
  native callback frame provides SPI during reversible phases without opening an
  illegal guard subtransaction in PostgreSQL's savepoint transition states. Native
  errors remain pending until managed frames unwind, including when managed code
  catches the transported exception; terminal failures end only the current backend.
  Focused validation passes 13 runtime, 3 generator and 12 PostgreSQL cases. Plain
  `dotnet test` passes 4117/4117 with no skips in 3m57.083s on PostgreSQL 18.6/Linux
  x64. The nonincremental Release build passes with zero warnings/errors in 47.55s.
  Public documentation, generated API freshness, `pnpm build` and `pnpm check` pass;
  the API contains 119 pages and 1231 members, and the site builds 150 pages. Actual
  prepared-transaction and parallel-worker event execution, PostgreSQL 13–17/19 beta
  and the remaining platform matrix remain required.

- 2026-09-23 — Implemented pgrx transaction-ID parity with distinct
  `PgTransactionId` (`xid`) and `PgSubtransactionId` value types. Generated
  functions, SPI parameters/results, nullable values, vectors and shaped arrays
  preserve xid type identity instead of treating it as `oid`; PostgreSQL's invalid
  xid maps to SQL NULL. `ToFullTransactionId` reads the server's next full ID
  behind the native error boundary and applies pgrx's wrap-aware epoch selection.
  Callback IDs now use the typed subtransaction value while preserving their numeric
  text. Eight runtime, five generator and ten dedicated PostgreSQL cases were added;
  the existing eight-path array test now includes xid. Plain `dotnet test` passes
  4140/4140 with no skips in 3m25.198s on PostgreSQL 18.6/Linux x64. The focused
  PostgreSQL run passes 18/18 in 1m27.476s. PostgreSQL 13–19 source review confirms
  the full-ID API and scalar/array OIDs are stable. The nonincremental Release build
  passes with zero warnings/errors in 16.44s. API generation/freshness, `pnpm build`
  and `pnpm check` pass with 121 API pages, 1259 members and 153 site pages. Actual
  PostgreSQL 13–17/19 beta and remaining platform execution are still required.

- 2026-09-23 — Added GitHub CI and trusted NuGet release automation. Repository
  automation is a .NET 10 file-based app; no Bash, PowerShell or `.env` files are
  used. CI builds the pinned runtime commit on Linux x64, macOS ARM64, macOS x64
  and Windows x64, installs PostgreSQL 18, and runs the complete test suite on
  every platform. Each job has a 15-minute limit and superseded CI runs cancel.
  Release jobs build six managed packages and four platform runtime packages in
  parallel, validate the exact package set, and publish through NuGet trusted
  publishing with `Ankus.Sdk` last. The runtime branch is published at commit
  `232bc9d1f06c300554a0646fa742d60cca49bcdd`. Local validation compiled the file
  app with zero warnings/errors, validated both workflows and their metadata,
  packed all six managed packages, and completed the Release/API/site quality
  path. Hosted CI execution and the PostgreSQL 15–18 matrix remain; PostgreSQL 19
  will join after PostgreSQL supports it.

- 2026-09-24 — Fixed aggregate-state corruption during hash spill on macOS. SQL
  `internal` values now carry opaque managed IDs, while query-lifetime reset
  callbacks own cleanup independently of PostgreSQL's recyclable per-group
  contexts. Foreign and expired IDs still report SQLSTATE `55000`. All 99
  aggregate integration cases pass on PostgreSQL 18.1/macOS ARM64, including
  the 3,000-group spill and sorted-group cleanup cases. Release validation passes
  1,181 generator, 879 runtime, 22 configuration and 5 sample tests locally.
  The complete macOS run passed 2,046 of 2,054 cases; six aligned-allocation
  cases require PostgreSQL 18.6 and two Linux-only cases skipped. Integration
  setup now publishes 18 Native AOT extensions in isolated directories with
  three-way bounded concurrency, then merges their outputs deterministically;
  publishing plus the aggregate suite completed in 2m02s. CI now builds and
  tests Release outputs in parallel MSBuild mode and initializes MSVC before
  Windows Native AOT publishing. The hosted cross-platform rerun and remaining
  PostgreSQL version matrix are pending.

- 2026-09-24 — Fixed the remaining first hosted-matrix failures. Runtime commit
  `0af10391603f2dc334259157abeeadc5a82f7be9` keeps the Unix finalizer joinable,
  joins its native thread before declaring a postmaster dormant, and discards
  the inherited handle before child recovery. The Linux x64 Release runtime and
  CoreLib build with zero warnings/errors; repeated finalizer-wait and descendant
  fork probes pass. PostgreSQL 15–17 Windows workers restore extension libraries
  and GUCs before starting the parallel transaction. Managed GUC checks could
  therefore acquire the worker's first transaction snapshot too early, causing
  PostgreSQL's later `transaction_deferrable` restore to fail. Native registration
  now continues in that phase while every managed initializer and GUC hook waits.
  After PostgreSQL finishes restoring the worker, Ankus replays checks and
  assignments against the final native values, rebuilds hook extras, then runs
  the initializer before the first managed entry. PostgreSQL 18 keeps its eager
  path. Windows reload tests wait for the exact setting/value pair in
  `config_exec_params`; startup `FATAL` tests accept Windows' connection reset
  only after the server log proves the expected diagnostic and cleanup.
  Transaction-backed parallel tests are restored, and test clusters no longer
  log every statement. The generator suite passes 1,182/1,182. Plain `dotnet test`
  passes 4,142/4,142 without skips in 2m57.146s on PostgreSQL 18.6/Linux x64; the
  nine directly affected integration cases pass in 44.828s. Native initializer,
  hook-only, assign-only, show-only and native-only fixtures compile with warnings
  as errors; the nonincremental Release build passes with zero warnings/errors in
  46.76s. Hosted Windows proof and the complete PostgreSQL 15–18 platform matrix
  remain pending.

- 2026-09-24 — Diagnosed the next PostgreSQL 17/Windows CI failures. A deferred
  worker check normalized its value but assigned the original raw value with the
  normalized extra payload. Replay now goes back through PostgreSQL's setter so
  the value, extra payload, check and assignment stay together. Windows
  `EXEC_BACKEND` children also reapply reloaded Backend/SUBackend placeholders
  with PostgreSQL's reload semantics after custom registration. Pre-18 fixture
  staging is serialized and installs the shared sample once; generated consumer
  tests and publishes use the selected PostgreSQL major; cluster shutdown falls
  back from bounded fast shutdown to immediate shutdown. The first rerun completed
  Windows in 9m03s and exposed four bounded failures: worker errors lacked the GUC
  name, one fixture emitted a PostgreSQL 18-only setting, and nested consumer paths
  exceeded Windows process-path handling. Replay now adds the PostgreSQL error
  context, the fixture selects version-specific settings, and consumer staging uses
  a short owned temporary path. Release builds with zero warnings/errors. Generator
  contracts pass 1,182/1,182, the affected worker/package scope passes 60/60, and
  plain `dotnet test` passes 4,142/4,142 in 3m08.004s on PostgreSQL 18.6/Linux x64.
  The next hosted run reduced Windows to two generated-project failures: `initdb`
  could start from the short staged installation but could not create PGDATA beyond
  Windows' legacy path limit. Consumer fixtures now keep ephemeral PGDATA in a short,
  owned temporary directory and retain build/server logs in the project. The focused
  generated-project scope passes 3/3, plain `dotnet test` passes 4,142/4,142 in
  3m11.053s, and the Release/API/documentation quality gate passes with zero
  diagnostics on PostgreSQL 18.6/Linux x64.
  That run also exposed an intermittent PostgreSQL 18.6/macOS ARM64 postmaster exit.
  Apple libpthread wakes `pthread_join` before `__bsdthread_terminate` removes the
  kernel thread from XNU's task list. Runtime commit
  `bb56f167c97e2c8e79423de9e43f43cde9ae1a88` waits for that removal before resetting
  libpthread's single-thread state. Native AOT Release builds with zero warnings/errors
  on Linux x64 and macOS ARM64. The two-round host/fork probe passes on both; 50
  consecutive macOS ARM64 executions also pass. The final Windows failure was a test
  harness shutdown race: bounded fast shutdown completed after its caller timed out,
  so the immediate fallback correctly found no running postmaster but returned exit
  code 1. The harness now checks `pg_ctl status` and accepts its documented exit code 3
  when that race proves the server has stopped.
  [Hosted CI run 35992682774](https://github.com/willibrandon/ankus/actions/runs/35992682774)
  passes every job: Linux x64/PostgreSQL 18 passes 4,142/4,142 in 9m28s; macOS
  ARM64/PostgreSQL 18 passes 4,140 with two Linux-only skips in 7m38s; macOS
  x64/PostgreSQL 18 passes 2,088/2,088 unit cases in 4m59s and 2,052 integration
  cases with two Linux-only skips in 11m32s; Windows x64/PostgreSQL 17 passes 4,140
  with the same two skips in 12m51s. A repeat completed every macOS x64 test but
  exceeded the 15-minute job limit during cleanup. A diagnostic run split the
  integration suite into six complete, disjoint shards containing 646, 804, 554,
  16, 5 and 29 tests.
  [Hosted CI run 35999926137](https://github.com/willibrandon/ankus/actions/runs/35999926137)
  passes every job and all 2,054 macOS x64 integration cases; the slowest shard
  completes in 11m07s. Windows repeats the full suite successfully in 14m18s. The
  intermediate four-shard configuration combined the 50 tool tests and included
  the 2,088 unit cases in the first job. The tool tests passed 50/50 locally in
  2m19.830s. Sharding has now been removed at the user's request. Each platform
  runs the complete unit and integration suites in one job, with the existing
  15-minute timeout. The automation no longer accepts shard selections or filters.
  The updated file-based app compiles in Release without diagnostics.
  Execution across PostgreSQL 15–18 on every supported platform remains pending.

- 2026-09-24 — Removed macOS Intel from the CI workflow at the user's request;
  its release build and runtime package remain included. CI runs on Linux x64,
  macOS ARM64, and Windows x64. Runtime and full-suite jobs now allow 20 minutes;
  test sharding remains removed. The preceding full-suite run
  [36013556684](https://github.com/willibrandon/ankus/actions/runs/36013556684)
  passed Linux x64/PostgreSQL 18, macOS ARM64/PostgreSQL 18, Windows x64/PostgreSQL
  17, and quality checks; macOS Intel reached its former 15-minute timeout.
  Local Release compilation, runtime metadata validation, API freshness, and
  documentation build/check pass without diagnostics.

- 2026-09-24 — Added `ExecuteScalars<TFirst, TSecond>` and
  `ExecuteScalars<TFirst, TSecond, TThird>` to `Spi`, `SpiSession`, and
  `SpiPreparedStatement`. They return owned tuples from the final statement's
  first row and copy only the requested leading columns. Commands run to
  completion, including every `INSERT ... RETURNING` write. NULL and empty
  results retain the scalar API's nullable-type rules; missing columns in a
  returned row and incompatible managed types are rejected. Native conversion
  failures release partial results and roll back the command before managed
  code recovers. Six direct result-contract tests and the 189-case affected SPI
  scope pass on PostgreSQL 18.6/Linux x64. This includes 148 new backend cases
  across standalone calls, sessions, retained plans and session-owned plans.
  Plain `dotnet test` passes 4,296/4,296 with no skips in 3m01.582s on that platform.
  Release compilation, API freshness (122 pages/1,270 members), `pnpm build`
  (154 pages), and `pnpm check` pass without warnings or errors. The API renderer
  also fixes invalid external links for named tuple returns, including existing
  temporal helpers. Extensible/raw SPI datum conversion and PostgreSQL/platform
  matrix validation remain pending.

  | Requirement | Evidence |
  | --- | --- |
  | Exact values, NULLs, first-row selection and owned results | `SpiScalarTests.FirstRowValuesRemainExactAndOwned`; `SpiResultTests.FirstValuesPreserveOrderAndTypes`, `EmptyResultsRequireNullableTypes`, `NullCellsAndMissingColumnsHaveDifferentContracts`, `ZeroColumnRowsRejectFirstValue` |
  | Complete writes while ignoring unused columns | `SpiScalarTests.FirstRowReadsCompleteAllWrites` |
  | Strict conversion errors and same-backend recovery | `SpiScalarTests.ResultErrorsPreserveBackendRecovery` |
  | Partial native conversion failure and write rollback | `SpiScalarTests.NativeConversionFailureRollsBackWrites` |
  | Focused and complete validation | SPI unit filter: 6/6; `SpiScalarTests\|SpiQueryTests` integration filter: 189/189; plain `dotnet test`: 4,296/4,296 |

- 2026-09-24 — Added context-owned raw SPI queries on `Spi`, `SpiSession`, and
  `SpiPreparedStatement`. `PgDatum` retains exact catalog type identity and SQL
  NULL separately from zero bits. Values support strict managed reads, explicit
  converters, PostgreSQL output text, raw parameter binding, and copies into a
  selected memory context. Native copies detoast variable-length data and flatten
  composite fields before SPI releases their source. Access checks the backend
  provider, context identity, and reset generation. Results survive session and
  plan disposal; result disposal and context reset/deletion invalidate native access.
  The 60-case raw SPI backend scope and six direct lifetime/transport tests pass
  on PostgreSQL 18.6/Linux x64. Plain `dotnet test` passes 4,362/4,362 with no
  skips in 3m10.137s. Release compilation, API freshness (125 pages/1,295 members),
  `pnpm build` (157 pages), and `pnpm check` pass without warnings or errors.
  Raw cursor batches, raw/polymorphic function
  signatures, custom base types, and the PostgreSQL/platform matrix remain pending.
  F# and VB.NET work remains deferred until the C# port is complete.

  IDE0380 is now an error in repository builds. Removed unnecessary `unsafe`
  modifiers from runtime partial declarations and test helpers; consumer templates
  retain their existing style configuration. The getting-started guide describes
  the project already generated by `ankus new`, and the README introduction now
  reflects the broader extension API. GitHub has a concise description, five
  relevant topics, and the documentation homepage URL.

  | Requirement | Evidence |
  | --- | --- |
  | Exact raw values, zero/NULL distinction, metadata, and SPI owner independence | `SpiRawTests.ResultsSurviveSpiOwnersAndRetainExactValues`; `PgDatumTests.RawBitsAndNullIdentitySurviveParameterTransport` |
  | Domains and unregistered type binding | `SpiRawTests.DomainsAndUnregisteredEnumsRoundTrip` |
  | Native TOAST and composite ownership after source removal | `SpiRawTests.ToastedValuesSurviveSourceRemoval` |
  | Strict conversions, row limits, read-only mode, rollback and recovery | `SpiRawTests.ManagedConversionsAndErrorsRecover`; `SpiRawTests.LimitsReadOnlyAndWriteErrorsPreserveTransaction` |
  | Disposal/reset invalidation and explicit lifetime copies | `SpiRawTests.CopySurvivesDisposalAndExpiresWithDestination`; `PgDatumTests.ResetGenerationRejectsEveryNativeAccess` |
  | Provider guards and preserved operational diagnostics | `PgDatumTests.AccessRequiresOriginalBackendProvider`; `PgDatumTests.DeletedOwnerAndOperationalErrorsRemainDistinct` |

- 2026-09-24 — Added `SpiCursor.FetchRaw` with forward/backward and current-row
  fetching. Batches retain exact type OIDs and NULL flags, support types without
  managed mappings, and own their native storage independently of the portal,
  session, prepared plan, and subsequent fetches. Fetch errors release the result
  context before same-backend recovery. Reentrant disposal, worker-thread access,
  negative counts, expired portals, and reused portal names retain the existing
  cursor checks. The 52-case managed/raw cursor scope passes on PostgreSQL
  18.6/Linux x64.

  Review also found that raw parameter type lookup could put a context-bound
  handle into an ordinary managed row or tuple. Raw edits now convert to owned
  managed values before changing the target cell. SQL NULL and exact type
  identities are retained, and failed conversion leaves the old value unchanged.
  Eight live-backend regression cases pass, covering generic and explicit-parameter
  tuple edits, NULLs, source disposal, and stale-input rejection.
  Plain `dotnet test` passes 4,394/4,394 without skips in 3m05.155s on PostgreSQL
  18.6/Linux x64. Release compilation has zero warnings/errors. API freshness
  (125 pages/1,297 members), `pnpm build` (157 pages), and `pnpm check` pass.
  The preceding raw-query milestone also passed hosted Linux, macOS ARM64, and
  Windows CI in [run 36023629516](https://github.com/willibrandon/ankus/actions/runs/36023629516).

  | Requirement | Evidence |
  | --- | --- |
  | Independent raw batches, empty metadata, unmapped types, NULLs and large values | `SpiRawCursorTests.BatchesSurviveSubsequentFetchesAndCursorDisposal` |
  | Scrolling, current-row fetches, mixed managed/raw fetches and native cleanup | `SpiRawCursorTests.FetchSemanticsAndRecoveryRemainExact` |
  | Reentrant disposal and native portal removal | `SpiRawCursorTests.RecursiveDisposalCannotInvalidateActiveFetch` |
  | Raw portal identity after commit, rollback and name reuse | `SpiCursorTests.TransactionEndInvalidatesCursor`; `SpiCursorTests.ReusedPortalNameDoesNotReviveStaleCursor` |
  | Owned managed edits and failure atomicity | `SpiRawTests.ManagedEditsCopyRawValuesAndRejectStaleSources`; `PgDatumTests.FailedRawConversionPreservesManagedRow` |

  Function-call context, raw/polymorphic signatures, custom base types, remaining
  backend APIs, and the complete PostgreSQL/platform matrix remain required.

- 2026-09-24 — Added injected `PgFunctionContext` parameters. They expose the
  invoked function and result OIDs, call collation, and ordered `PgDatum`
  arguments without consuming SQL slots. Native capture uses PostgreSQL's
  `FunctionCallInfo` and expression helpers from the selected server headers;
  no managed struct assumes a PostgreSQL internal layout. Argument copies retain
  actual domain identities and independent NULL flags. Scalar storage belongs to
  the callback context; iterator storage belongs directly to the multi-call owner
  so reset cleanup can read it before invalidation. Metadata remains managed and
  immutable after native storage expires. Repeated injected call parameters share
  one snapshot, while injected memory contexts keep their existing behavior.

  The 19-case PostgreSQL scope passes on PostgreSQL 18.6/Linux x64 in 34.439s.
  The 83-case affected generator scope passes, including compiled existing
  memory-context callbacks with the updated native signatures. The direct runtime
  metadata test passes. Plain `dotnet test` passes 4,426/4,426 without skips in
  2m51.700s on PostgreSQL 18.6/Linux x64. Release compilation has zero warnings
  and errors. API freshness (126 pages/1,301 members), `pnpm build` (158 pages),
  and `pnpm check` pass without diagnostics.

  | Requirement | Evidence |
  | --- | --- |
  | Actual function/result/type/collation identity, NULLs, nested calls and native error recovery | `FunctionContextTests.ScalarSnapshotsRetainExactCallMetadata`, `DomainArgumentsRetainTheirActualCatalogTypes`, `OperatorExpressionsExposeTheirActualOperand` |
  | Immutable managed metadata and guarded raw lifetimes | `PgFunctionContextTests.MetadataRemainsOwnedAndArgumentsCannotBeReplaced`; `FunctionContextTests.ExpiredArgumentsFailAndExplicitCopiesRemainLive` |
  | Streaming/materialized/empty sets, early stop and native abort cleanup | `FunctionContextTests.SetSnapshotsSurviveYieldsAndCleanup`, `IteratorErrorsUnwindWithLiveArguments`, `ExecutorAbortPreservesArgumentsUntilIteratorDisposal` |
  | SQL signature erasure, defaults, strictness, diagnostics and overload identity | `PgFunctionGeneratorTests.FunctionContextsPreserveSqlSignatures`, `InvalidFunctionContextShapesAreDiagnosed`, `FunctionContextErasureRejectsDuplicateSqlSignature`, `SetFunctionContextIsCapturedDuringFactoryCreation` |

  Per-function cached state (`fn_extra`), direct/named function invocation,
  raw/polymorphic signatures, custom base types, the remaining backend APIs,
  and complete PostgreSQL/platform validation remain required. The preceding
  raw-cursor milestone passes all jobs in
  [CI run 36026412947](https://github.com/willibrandon/ankus/actions/runs/36026412947).

- 2026-09-24 — Added `PgFunctionContext.GetOrCreateState<T>` for pgrx's
  `pg_func_extra` behavior. Each PostgreSQL expression initializes its state once
  after a successful factory call. NULL and zero are cached; failed factories
  remain retryable. The API enforces the original managed type, rejects recursive
  initialization and off-thread lookup, and runs `IDisposable.Dispose` when
  PostgreSQL resets or deletes `fn_mcxt`. Cleanup releases roots even when a
  disposer throws. `StateMemoryContext` provides the checked native owner for
  state allocations and explicit raw-argument copies.

  A native registry identifies the `FmgrInfo` call site without occupying
  PostgreSQL's `fn_extra` slot. Set-returning functions therefore retain their
  iterator machinery and can share cached state across repeated iterator
  instances. Monotonic site IDs, provider checks, and owner generations prevent
  recycled addresses or retained snapshots from reviving released state.
  Existing iterator state remains readable during abort cleanup; cleanup cannot
  initialize replacement state.

  Direct runtime tests pass 12/12. The 23-case live PostgreSQL scope passes
  without skips in 45.181s on PostgreSQL 18.6/Linux x64, including separate
  expressions, repeated prepared executions, cursor close/rollback, large and
  NULL raw inputs, nested SPI, native errors, early iterator stop, executor abort,
  disposal failures, root collection, and same-backend recovery. The Release
  build passes with zero warnings/errors. Plain `dotnet test` passes 4,461/4,461
  without skips in 2m51.823s on PostgreSQL 18.6/Linux x64. API freshness
  (126 pages/1,303 members), `pnpm build` (158 pages), and `pnpm check` pass
  without diagnostics.

  | Requirement | Evidence |
  | --- | --- |
  | Once-only initialization, NULL/zero caching, exact types, retry and recursive initialization | `PgFunctionStateTests.SuccessfulFactoryRunsOnceAndRetainsExactType`, `NullAndZeroValuesAreCached`, `FailedAndRecursiveFactoriesPermitRetry`; `FunctionStateTests.InitializationContractsPreserveBackendRecovery` |
  | Independent call sites, copied native payloads, expired handles and reclaimed roots | `FunctionStateTests.RowCallsRetainIndependentStateAndOwnedNativeValues`, `PreparedExecutionsDoNotReviveExpiredState` |
  | Live portal state and close/rollback cleanup | `FunctionStateTests.PortalOwnsStateAcrossFetches` |
  | Iterator state separation, repeated instances, empty/early/error cleanup | `FunctionStateTests.RepeatedIteratorsShareOnlyTheirCallSiteState`, `IteratorTerminationReleasesStateAndRecovers` |
  | Throwing disposers, cleanup reentry, ownership ending during initialization | `FunctionStateTests.DisposalErrorsUnwindAndReleaseRootsBeforeRecovery`; `PgFunctionStateTests.ReleaseIsFinalAndDisposesExactlyOnce`, `OwnerEndingDuringFactoryDisposesUnpublishedValue` |
  | Failed registration, provider mismatch and exact native lifetime errors | `PgFunctionStateTests.RegistrationFailureDoesNotPublishOrStrandState`, `ProviderMismatchFailsBeforeNativeAccess`, `NativeOwnerFailuresNeverRunFactory` |

  Direct/named invocation, raw/polymorphic signatures, custom base types,
  remaining backend APIs, and complete PostgreSQL/platform validation remain
  required. The preceding function-context milestone passed all jobs on Linux,
  macOS ARM64, and Windows in
  [CI run 36029314830](https://github.com/willibrandon/ankus/actions/runs/36029314830).

- 2026-09-24 — Added `PgFunctions` with named and OID scalar calls, void calls,
  caller-owned raw results, and explicit native entry-point calls corresponding
  to pgrx's `fn_call` and `direct_function_call` helpers. `PgFunctionArgument`
  distinguishes values, typed NULLs, and typed defaults; options select explicit
  collation and SQL VARIADIC array binding. Name lookup uses PostgreSQL's parser;
  OID calls use the declared parameter list. Native expression evaluation retains
  ordinary overload/coercion rules, polymorphic types, default expressions,
  security-definer identity, function-local settings, and nested call metadata.

  EXECUTE permission is checked before expression planning, including strict
  NULL calls that PostgreSQL could otherwise fold away. Requested result types
  are checked before invoking the function. Results are copied before executor
  cleanup; domain OIDs and NULL flags remain intact in raw returns. PostgreSQL
  errors roll back the guarded call and return owned diagnostics after managed
  unwinding. Native-address calls preserve pgrx's null `flinfo`/`context`/
  `resultinfo` contract and allow the version-1 ABI's wider argument count;
  callers own pointer validity and exact argument/result representation.

  The 27-case integration scope passes without skips in 44.734s on PostgreSQL
  18.6/Linux x64. Four direct argument/validation tests pass. The selected
  PostgreSQL 15–18 declarations were reviewed for the parser, executor, and
  identifier/ACL APIs; PG15 uses its older identifier and ACL signatures.
  Plain `dotnet test` passes 4,492/4,492 without skips in 3m08.001s on
  PostgreSQL 18.6/Linux x64. The Release build has zero warnings and errors.
  API freshness (129 pages/1,326 members), `pnpm build` (162 pages), and
  `pnpm check` pass without diagnostics.

  | Requirement | Evidence |
  | --- | --- |
  | Built-in, SQL, PL/pgSQL, strict NULL and exact typed results | `FunctionCallTests.FunctionLanguagesAndNullInputsMatchSql`; `PgFunctionArgumentTests.TypedNullZeroAndDefaultRemainDistinct` |
  | Quoted identifiers, search path, overloads, omitted/explicit/volatile defaults | `FunctionCallTests.IdentifierAndOverloadResolutionUsePostgresRules`, `DefaultExpressionsRemainTypedAndExecuteOnce`, `TypedDefaultsResolveOverloadsWithoutGuessing` |
  | Variadic arrays, polymorphic results, explicit collation and nested managed state | `FunctionCallTests.VariadicAndPolymorphicCallsPreserveTypes`, `CollationAndTextOwnershipSurviveNativeReturn` |
  | Domain/enum/record identity, raw input binding, result ownership and invalidation | `FunctionCallTests.RawResultsPreserveExactIdentityAndLifetime`, `RawArgumentsPreserveDomainIdentityAndNull`; `PgFunctionArgumentTests.RawArgumentsRetainCatalogIdentityAndLifetime` |
  | Execute permissions, strict-NULL planning, security definer and setting restoration | `FunctionCallTests.PermissionsAndSecurityDefinerStateFollowPostgres` |
  | Native addresses, zero/NULL separation, zero/101 arguments, referenced results and native errors | `FunctionCallTests.NativeEntryPointsPreserveDatumAndErrorContracts` |
  | Pre-execution result validation, invalid calls, rollback, owned diagnostics and same-session recovery | `FunctionCallTests.ErrorsPreserveDiagnosticsRollbackAndSameBackendRecovery`, `InvalidCallsFailBeforeExecutionAndRecover`; `PgFunctionArgumentTests.InvalidIdentitiesFailWithoutNativeAccess` |

  Raw/polymorphic extension signatures, custom base types, remaining backend
  APIs and full PostgreSQL/platform validation remain required. The cached-state
  milestone passed all Linux, macOS ARM64, and Windows CI jobs in
  [run 36032194319](https://github.com/willibrandon/ankus/actions/runs/36032194319).

- 2026-09-24 — Added `PgAnyElement` and `PgAnyArray` for PostgreSQL `anyelement`
  and `anyarray` signatures. Generated scalar, SETOF, and TABLE functions resolve
  real argument/result types through the server's function-expression APIs.
  Managed inputs copy raw storage into the callback or iterator owner; return
  conversion checks exact type identity and owner generation before reading the
  datum. Nullable wrappers preserve SQL NULL independently of zero values.
  Domains, unregistered enums, named/anonymous records, and unmapped types retain
  native identity. Array metadata retains dimensions, lower bounds, and element
  OIDs; cells preserve NULLs and independent checked ownership after copying.

  Wrappers bind through `SpiParameter.Create` and `PgFunctionArgument.Create`.
  `CopyTo` gives values and array cells another owner. Runtime checks reject
  non-array construction, stale returned storage, and incompatible result types;
  the generator diagnoses polymorphic results with no type-resolving input.
  Polymorphic composite sets use PostgreSQL's row descriptors in streaming and
  materialized execution. Transport-marker checks also retain ordinary temporal
  values whose auxiliary fields happen to match a marker number.

  Direct runtime checks pass 2/2; generator contracts and diagnostics pass 7/7.
  The 35-case PostgreSQL scope passes without skips in 44.700s on PostgreSQL
  18.6/Linux x64, including PostgreSQL void values and NOT NULL domain results.
  The affected generator scope also passes all ten cases, including three
  existing compiled iterator-factory checks updated for the native signature.
  API freshness (131 pages/1,342 members), `pnpm build` (165 pages), and `pnpm check`
  pass without diagnostics. The Release build has zero warnings and errors.
  Plain `dotnet test` passes 4,536/4,536 without skips in 2m55.807s on PostgreSQL
  18.6/Linux x64.

  | Requirement | Evidence |
  | --- | --- |
  | Scalar type identity, SQL NULL, exact values, domains, enums, records and unmapped types | `PolymorphicTests.ScalarValuesPreserveResolvedTypes`; `PgAnyElementTests.ExactIdentityAndLifetimeSurviveTransport`, `NullInputsRequireNullableWrappers` |
  | Array shape, non-one bounds, empty arrays, NULL cells and element type identity | `PolymorphicTests.ArraysPreserveShapeAndUnmappedElements` |
  | Scalar/composite SETOF, materialization, TABLE and iterator lifetimes | `PolymorphicTests.SetResultsFollowResolvedElementTypes`, `IteratorOwnersRetainValuesAndTableColumns` |
  | Array copies, independently expired cells, built-in invocation and invalid runtime array conversion | `PolymorphicTests.ArrayCopiesAndBuiltinCallsPreserveValues` |
  | Exact output validation, stale storage, strict managed reads, domain NULL constraints and same-backend recovery | `PolymorphicTests.InvalidResultsFailWithoutCorruption`, `NullResultsRespectResolvedDomainConstraints` |
  | Compiled dispatch, SQL pseudotypes, NULL policies and invalid signature diagnostics | `PgFunctionGeneratorTests.PolymorphicSignaturesCompileWithExactSqlTypes`, `InvalidPolymorphicSignaturesAreDiagnosed` |

  Typed polymorphic query/call results, polymorphic aggregate signatures,
  general internal/raw signature bindings, custom base types, remaining backend
  APIs, and full PostgreSQL/platform validation remain required.
  The preceding function-invocation milestone passed Linux x64/PostgreSQL 18,
  macOS ARM64/PostgreSQL 18, and Windows x64/PostgreSQL 17 in
  [CI run 36036454638](https://github.com/willibrandon/ankus/actions/runs/36036454638).

- 2026-09-24 — Added typed `PgAnyElement` and `PgAnyArray` results to scalar and
  tuple SPI calls, sessions, prepared statements, and named/OID catalog calls.
  Mixed tuple results retain ordinary exact managed conversions. Native capture
  selects the requested first-row columns without limiting SQL execution or
  write effects. `SpiRawRow.Get<T>` and `PgDatum.Read<T>` can return wrappers with
  the raw result's checked lifetime; ordinary managed reads remain independent.
  `PgAnyArray.Read<T>` exposes the same typed conversion convenience.

  Integration testing found and fixed an iterator ownership bug: retained query
  results were anchored to a single row callback. Native memory envelopes now
  distinguish protected callback storage from the result owner; set results use
  the multi-call context across advances. Scalar/session results survive SPI
  disconnect and plan disposal. Explicit copies survive source disposal, while
  original values and cells reject access after disposal or reset. Catalog array
  calls reject scalar result types before side effects, including NULL results.
  PostgreSQL strips array domains when binding an `anyarray` argument; separate
  tests verify uncoerced query and catalog results retain those domain OIDs.

  The 80-case affected integration scope passes without skips in 36.948s on
  PostgreSQL 18.6/Linux x64, including streaming and materialized iterator
  ownership and every tuple position. Direct runtime checks pass 9/9, including
  one new raw-read case and existing scalar/identity cases. Plain `dotnet test`
  passes 4,582/4,582 without skips in 2m53.968s on PostgreSQL 18.6/Linux x64.
  The Release build has zero warnings and errors. API freshness (131 pages/1,345
  members), `pnpm build` (165 pages), and `pnpm check` pass without diagnostics.
  The preceding polymorphic-signature milestone passed
  Linux x64/PostgreSQL 18, macOS ARM64/PostgreSQL 18, and Windows x64/PostgreSQL 17
  in [CI run 36039713933](https://github.com/willibrandon/ankus/actions/runs/36039713933).

  | Requirement | Evidence |
  | --- | --- |
  | Typed scalar, tuple, session, prepared/retained plan, named and OID results with exact identity | `PolymorphicTests.TypedQueryResultsPreserveIdentityAfterExecutionOwnersEnd` |
  | Empty/shaped arrays, domain and enum identities, nullable cells and large composites | `PolymorphicTests.TypedArrayResultsPreserveShapeAndCellsAfterExecutionOwnersEnd` |
  | Raw query/cursor lifetime, explicit copies, reset/disposal rejection, name and ordinal lookup | `PolymorphicTests.TypedRawRowsRejectExpiredOwnersAndRetainCopies`; `PgAnyElementTests.TypedRawReadsPreserveIdentityNullsAndCheckedLifetime` |
  | Streaming/materialized results across advances, early termination and NULL rows | `PolymorphicTests.TypedQueryResultsSurviveIteratorAdvances` |
  | Every tuple position, managed array reads, NULL/empty/missing cells, final statement and exact conversions | `PolymorphicTests.PolymorphicFirstRowsPreserveExistingScalarContracts` |
  | Full write execution despite first-row capture or later managed conversion failure | `PolymorphicTests.PolymorphicFirstRowsDoNotLimitWriteEffects` |
  | Array result validation before side effects, native error rollback and same-backend recovery | `PolymorphicTests.PolymorphicCallsValidateArrayTypesBeforeExecutionAndRecoverFromErrors` |

  Polymorphic aggregate signatures, general internal/raw signature bindings,
  custom base types, remaining backend APIs, and full PostgreSQL/platform
  validation remain required.

- 2026-09-24 — Added polymorphic aggregate inputs, states, and results using
  `PgAnyElement` and `PgAnyArray`. Generated callbacks resolve actual argument
  and result OIDs through PostgreSQL, preserving domain constraints even for
  NULL results. Declaration diagnostics reject signatures without a type-resolving
  input. Internal managed states use `FinalExtra` or `MovingFinalExtra` when a
  final callback needs the aggregate input's type.

  `PgAggregateContext.MemoryContext` exposes checked aggregate storage for retained
  input copies. Inputs still expire after their support callback; typed query/call
  results use the aggregate owner. A guarded native lookup resolves the aggregate
  owner even during nested scalar callbacks, where ambient callback storage would
  expire too soon. Nested aggregates select their own owners, and context lookup
  rejects an expired callback or access from another thread.

  Direct aggregate checks pass 62/62, including owner selection and invalid scope
  checks. Compiled generator contracts and diagnostics pass 6/6. The 21-case real
  PostgreSQL scope passes without skips in 46.723s on PostgreSQL 18.6/Linux x64.
  Plain `dotnet test` passes 4,610/4,610 without skips in 3m15.086s on PostgreSQL
  18.6/Linux x64. The Release build has zero warnings and errors (11.89s).
  API freshness (131 pages/1,346 members), `pnpm build` (165 pages), and
  `pnpm check` pass without diagnostics.

  | Requirement | Evidence |
  | --- | --- |
  | pgrx first-anyelement/first-anyarray semantics, exact values, domain/enum/composite identity, shape, bounds and NULL cells | `AggregateTests.PolymorphicAggregateStatesPreserveValuesAndResolvedTypes`, `PolymorphicArrayAggregateStatesPreserveShapeAndCells` |
  | Textual initial state with resolved array types, including enum and empty input | `AggregateTests.PolymorphicAggregateInitialStateResolvesArrayTypes` |
  | Managed retention, nested scalar calls, invalid owner access and group cleanup | `AggregateTests.PolymorphicAggregateOwnershipSurvivesNestedScalarCalls`; `PgAggregateTests.AggregateMemoryContextRequiresActiveInnermostScope` |
  | Empty/all-NULL input, strict first-row seeding, filtering and independent groups | `AggregateTests.PolymorphicAggregatesPreserveNullEmptyFilterAndGroupSemantics` |
  | Moving inverse/restart paths, large retained values, NULL frames and ordered PostgreSQL comparisons | `AggregateTests.PolymorphicMovingAggregatesRetainValuesAcrossFrames`, `PolymorphicOrderedAggregatesUsePostgresComparison` |
  | Actual worker launch, partial aggregation and leader-side combination | `AggregateTests.PolymorphicParallelAggregatesCombineActualWorkerStates` |
  | Wrong result type, NOT NULL domain result, expired borrowed storage, rollback and same-backend recovery | `AggregateTests.PolymorphicAggregateErrorsEnforceResultTypesAndRecover` |
  | Compilable dispatch, exact SQL pseudotypes, extra type witnesses and invalid-signature diagnostics | `PgFunctionGeneratorTests.PolymorphicAggregateStatesCompileWithExactSqlTypes`, `PolymorphicAggregateFinalExtraCompilesWithOwnedStorage`, `UnresolvedPolymorphicAggregateSignaturesAreDiagnosed` |

  General internal/raw signature bindings, heterogeneous variadic `any`, custom
  base types, remaining backend APIs, and full PostgreSQL/platform validation
  remain required. The preceding typed-query milestone passed Linux x64/PostgreSQL
  18, macOS ARM64/PostgreSQL 18, and Windows x64/PostgreSQL 17 in
  [CI run 36043147127](https://github.com/willibrandon/ankus/actions/runs/36043147127).

- 2026-09-24 — Fixed two Windows test races found in
  [CI run 36046783192](https://github.com/willibrandon/ankus/actions/runs/36046783192).
  The cleanup-error test filtered a shared server log by operating-system process
  ID, which Windows had reused for another session. It now assigns a unique
  application name and checks only that session's diagnostics. An intentional
  earlier error under the same process ID proves unrelated errors are excluded;
  exact error ordering, detail, hint, cleanup, and backend recovery remain checked.

  The PANIC test could accept a connection before PostgreSQL had noticed the
  dying backend, then incorrectly treat that connection as recovery. It now waits
  for the postmaster's recovery-start message before checking a new connection
  and transaction rollback. The existing 30-second deadline is unchanged, and
  retry handling is limited to connection and shutdown/startup failures.

  Both data-driven tests pass locally: 4/4 cases without skips in 30.686s on
  PostgreSQL 18.6/Linux x64. Plain `dotnet test` passes 4,629/4,629 without skips
  in 2m58.766s; that working-tree run also includes pending internal-state work.
  The Release build passes with zero warnings and errors in 8.00s. The CI repair
  commit contains only these test fixes and this evidence. The failed CI run
  passed Linux x64/PostgreSQL 18,
  macOS ARM64/PostgreSQL 18, runtime builds, and quality checks.

  The repair then passed all seven jobs in
  [CI run 36050481428](https://github.com/willibrandon/ankus/actions/runs/36050481428)
  at `bfd931c`. Linux x64/PostgreSQL 18 passed all 2,463 integration cases;
  macOS ARM64/PostgreSQL 18 and Windows x64/PostgreSQL 17 each passed 2,461 with
  only the two existing Linux allocator checks skipped. All other test modules
  passed without skips. Windows completed in 14m34s and macOS in 5m32s.
  Workflow structure and time limits are unchanged.

- 2026-09-24 — Added `PgInternal` for PostgreSQL's general `internal` callback
  type, following pgrx's `datum/internal.rs`. It can retain managed state under a
  PostgreSQL context or borrow a native word without interpreting its pointee.
  Nullable wrappers represent SQL NULL separately from a present zero pointer.
  Managed aliases recover the original owner and exact payload type; reset or
  deletion releases the root and invokes `IDisposable` once. A failed cleanup
  cannot revive state through reads, raw access, or a previously bound parameter.
  Registration failure releases the native identity without taking ownership of
  the caller's payload.

  Generated scalar, SETOF, TABLE, and aggregate callbacks transport internal
  words directly. Aggregate state selects the proper native owner, including
  moving windows and temporary deserialization. Combination rejects returning
  temporary managed state as destination-owned state. Native caller tests exposed
  two set-result issues: PostgreSQL classifies `internal` as a pseudotype, and a
  materialized store outlives its iterator. The set bridge now accepts the exact
  internal type and retains materialized managed payloads with the caller's
  result context instead of deleting them with the iterator.

  Direct runtime checks pass 4/4. Compiled signatures and affected diagnostics
  pass 26/26. The complete 20-case internal backend scope passes without skips in
  43.759s on PostgreSQL 18.6/Linux x64, including actual workers and native callers
  for streaming/materialized SETOF and TABLE results. Public documentation now
  describes managed state, native borrowing, cleanup, and result ownership.
  A separate SPI round-trip check passes and retains the original managed owner
  after execution storage is released. Final plain `dotnet test` passes
  4,641/4,641 without skips in 2m50.526s on PostgreSQL 18.6/Linux x64. The Release
  build passes with zero warnings and errors in 5.64s. API freshness (132 pages,
  1,354 members), `pnpm build` (167 pages), and `pnpm check` pass without
  diagnostics. This milestone then passed all seven jobs in
  [CI run 36055390473](https://github.com/willibrandon/ankus/actions/runs/36055390473)
  at `574f106`. Linux x64/PostgreSQL 18 passed all 2,483 integration cases;
  macOS ARM64/PostgreSQL 18 and Windows x64/PostgreSQL 17 each passed 2,481 with
  only the two existing Linux allocator cases skipped. All other modules and
  the documentation workflow passed.

  | Requirement | Evidence |
  | --- | --- |
  | Native words, zero versus NULL, exact type, owner generations and parameter transport | `PgInternalTests.NativeWordsRetainNullTypeAndLifetimeContracts`; `AggregateTests.InternalNativePointersPreserveNullZeroAndWritableValues` |
  | Original managed identity, exact type, invalidation before disposal and failed registration | `PgInternalTests.ManagedAliasesRetainExactPayloadAndInvalidateBeforeDisposal`, `FailedRegistrationReleasesIdentityAndPermitsRetry` |
  | Managed rooting and release even when disposal throws | `PgInternalTests.NativeOwnerRootsPayloadAndReleasesItOnThrowingCleanup`; `AggregateTests.InternalThrowingCleanupRejectsReleasedStateAndRecovers` |
  | Ordinary support functions, explicit owners, groups, NULL inputs and moving frames | `AggregateTests.InternalFunctionStateSurvivesCallbacksAndReleasesAtQueryEnd`, `InternalAggregateStatesPreserveGroupsAndMovingWindows` |
  | Native SETOF/TABLE callers, both execution modes, repeated state, NULL rows and early termination | `AggregateTests.InternalSetStatesOutliveRowsAndReleaseWithTheirOwner` |
  | Worker launch, serialization, deserialization, combination and independent native owners | `AggregateTests.InternalParallelStatesSerializeDeserializeAndCombineAcrossWorkers` |
  | Transition/serialization/deserialization errors, invalid borrowed state and same-backend recovery | `AggregateTests.InternalAggregateErrorsReleaseOwnedStateAndRecover`, `InternalNativeTypeErrorsPreserveBackendRecovery` |
  | Compilable scalar/set/table/aggregate declarations and PostgreSQL internal type rules | `PgFunctionGeneratorTests.InternalFunctionSignaturesCompileWithCheckedTransport`, `GeneralInternalAggregateStateCompilesWithSerialization`, `InternalResultsRequireInternalInputs`, `UnsupportedSignaturesAreRejected` |

  PostgreSQL's SQL parser and pgrx's `fn_call` both reject explicit catalog calls
  with internal signatures; the existing Ankus catalog-call rule follows those
  references. Native entry-point calls retain their explicit pointer-lifetime
  contract. General raw signatures, heterogeneous variadic `any`, custom base
  types, remaining backend APIs, and the full PostgreSQL/platform matrix remain
  required. This milestone is not evidence of full port completion.

- 2026-09-24 — Added explicit `[PgSqlType]` bindings for `PgDatum` parameters
  and results in scalar, SETOF, TABLE, aggregate, operator, and cast declarations.
  Bindings retain exact catalog identifiers, schema dependencies, array identity,
  and per-column TABLE types. Missing, ambiguous, malformed, and incompatible
  bindings produce ANKUS016. Existing composite binding behavior remains covered
  after sharing its internal binding model with raw types.

  Raw transport preserves complete datum words, SQL NULL, domain/composite/array
  identity and native owners. Results validate type and lifetime even when they
  carry a typed NULL. Aggregate states can copy into the aggregate context and
  survive group transitions and real worker combination. Explicit polymorphic
  bindings resolve the caller's concrete type; raw internal inputs retain their
  original pointer identity.

  Ported pgrx's hand-written unsigned 24-bit base-type example with shell SQL,
  input/output callbacks, and a cast. Real PostgreSQL calls exposed two ABI
  details: type input functions receive three native argument slots even with
  one declared SQL argument, and an output callback used by `CoerceViaIO` sees
  the enclosing expression's return type. Generated callbacks now accept extra
  native slots, expose only declared SQL arguments in `PgFunctionContext`, and
  resolve fixed result types from the catalog. Polymorphic results still use the
  call site. Missing arguments are rejected before managed dispatch with 22023;
  the former default XX000 caused Npgsql to close the otherwise healthy session.
  The native-caller regression proves rejection followed by successful execution
  on the same backend.

  | Requirement | Evidence |
  | --- | --- |
  | Compilable scalar/set/table/aggregate/operator/cast bindings and exact SQL types | `RawSignaturesCompileWithExactSqlBindings`, `RawAggregateOperatorAndCastBindingsCompile` |
  | Invalid declarations, pseudotype safety, schema dependencies and relocation | `InvalidRawBindingsAreDiagnosed`, `RawPseudotypeResultsRequireInputs`, `RawBindingsPreserveSchemaDependenciesAndRelocation` |
  | Complete words, array shape, NULL cells, polymorphic resolution and native internal pointers | `RawScalarsPreserveBitsNullAndExactType`, `RawArraysPreserveShapeAndUnmappedCells`, `RawPseudotypesResolveActualInputs`, `RawInternalInputsPreserveNativeIdentity` |
  | Native base-type input/output ABI, exact catalog type and cast | `HandWrittenBaseTypeUsesNativeInputOutputAndCast`, `HandWrittenBaseTypeErrorsPreserveBackendRecovery` |
  | External TOAST, temporary-owner deletion, domain identity and NOT NULL constraints | `RawByReferenceValuesRetainOwnershipAndDomainIdentity` |
  | Streaming/materialized SETOF, TABLE columns, composite expansion, NULL rows and early exit | `RawSetsRetainRowsNullsAndAllowEarlyExit`, `RawCompositeSetsPreserveRowsAndNulls` |
  | Typed NULL and zero, wrong types, expired owners, row errors and same-session recovery | `RawResultsPreserveNullAndZero`, `RawResultErrorsValidateIdentityAndRecover`, `RawNativeCallsRejectMissingArgumentsAndRecover` |
  | Independent group state, NULL/empty input and actual partial aggregation in workers | `RawAggregateStatesRetainStorageAcrossGroupsAndWorkers` |

  Final plain `dotnet test` passes 4,696/4,696 without skips in 3m14.335s on
  PostgreSQL 18.6/Linux x64, including all 22 new generator cases and 33 new
  backend cases. The Release build passes with zero warnings and errors in
  5.87s. API freshness (133 pages, 1,359 members), `pnpm build` (169 pages),
  and `pnpm check` pass without diagnostics. The public guide contains a
  complete manual base-type example. Hosted CI run 36061937604 passes on Linux
  x64/PostgreSQL 18 (4,696 passed), macOS ARM64/PostgreSQL 18 and Windows
  x64/PostgreSQL 17 (4,694 passed each, two Linux-only allocator tests skipped).
  All jobs completed successfully; Windows took 13m53s, macOS 7m55s and Linux
  7m16s. The documentation deployment also passed.
  Strongly typed custom codecs, declarative base-type generation, default
  CBOR/JSON storage, binary send/receive, heterogeneous variadic `any`, remaining
  backend APIs and the full PostgreSQL/platform matrix remain required.

- 2026-09-24 — Added `[PgType(typeof(Codec))]` and `PgTypeCodec<T>` for classes,
  structs and enums. Generated SQL installs a shell type, native text I/O
  callbacks, optional binary send/receive, and a completed variable-length base
  type before dependent functions and aggregates. Explicit codecs define the
  stored payload and text format. PostgreSQL handles packed, compressed and
  external TOAST. Invalid type/codec declarations produce ANKUS017; generated
  helper signatures participate in duplicate function detection.

  Closed generated registrations support nullable values, vectors, shaped arrays,
  scalar/set/table functions, aggregate state and inputs, operators, casts and
  all SPI ownership paths without runtime code generation. Native envelopes
  retain exact type OIDs; live catalog lookup follows extension relocation and
  reinstallation. Codec errors unwind managed frames before PostgreSQL reports
  them. Direct tests exposed and fixed an unnecessary backend lookup when
  constructing an owned custom array outside a callback.
  Codec construction is lazy and runs inside that same boundary, so user
  constructor exceptions become PostgreSQL errors rather than escaping module
  initialization. The backend regression verifies recovery after that failure.

  | Requirement | Evidence |
  | --- | --- |
  | Compilable value/reference/enum contracts, SQL ordering, type-only modules and diagnostics | `CustomTypesCompileAcrossTypedContracts`, `CustomTypeOnlyExtensionEmitsCompleteModule`, `InvalidCustomTypeContractsAreDiagnosed`, `CustomTypesPreserveLongNamesAndDependencyOrder`, `CustomTypeIoSignaturesCannotBeReplaced` |
  | Exact payload identity, copied storage, empty versus NULL, closed array conversions and registration failure | `CustomPayloadCopiesStorageAndRequiresExactIdentity`, `CustomPayloadEmptyAndNullRemainDistinct`, `CustomArraysPreserveShapeAndRejectLossyConversions`, `CustomRegistrationRetainsOriginalContractOnFailure`, `NullCodecResultsAreRejected` |
  | Scalar/reference values, integer extremes, empty strings, NULLs, array shape and every SPI ownership path | `CustomScalarsArraysAndNullsCrossEveryOwnershipPath` |
  | SETOF/TABLE, early exit, operator/cast, independent aggregate groups and empty input | `CustomSetsOperatorsCastsAndAggregatesUseTypedStorage` |
  | Independent binary COPY bytes, minimum/zero/maximum values and malformed receive recovery | `CustomBinaryCopyPreservesExactBytes`, `CustomBinaryErrorsPreserveConnectionAndRows` |
  | Deferred single codec construction, constructor failure, all four conversion errors and same-session recovery | `CustomCodecConstructionIsDeferredAndCached`, `CustomCodecErrorsUnwindAndPreserveBackend` |
  | Domain/array identity, TOAST, type-only extension relocation and changed OIDs after reinstall | `CustomDomainsPreserveIdentityAndArrayShape`, `CustomStorageSurvivesToastAndCatalogLookup`, `CustomTypeOnlyExtensionTracksRelocationAndReinstallation` |
  | Real launched workers and partial aggregation of custom states | `CustomAggregateStatesWorkInParallelWorkers` |

  Final plain `dotnet test` passes 4,740/4,740 without skips in 3m17.206s on
  PostgreSQL 18.6/Linux x64, including 14 new generator cases, six direct runtime
  cases and 24 backend cases. The final Release build passes with zero warnings
  and errors in 4.82s. API freshness (135 pages, 1,371 members), `pnpm build`
  (172 pages), and `pnpm check` pass without diagnostics. Hosted run 36067509967
  passes Linux x64/PostgreSQL 18 (4,740 passed) and macOS ARM64/PostgreSQL 18
  (4,738 passed, two Linux-only allocator cases skipped). Windows x64/PostgreSQL
  17 passes the new custom-type cases but exposes one existing transaction
  callback failure, investigated below. The public custom-type guide and Distance sample
  explain the complete codec contract. Default CBOR storage/JSON text generation, zero-copy
  `PgVarlena` equivalents, generated operator classes, remaining backend APIs,
  heterogeneous variadic `any`, and the full PostgreSQL/platform matrix remain
  required; this is not full `PostgresType` parity.

- 2026-09-24 — Investigated Windows' `CommitFailureEndsTheBackendWithoutStoppingPostgres`
  failure using the actual server artifact. PostgreSQL reported the expected
  managed error, while Npgsql received Windows socket reset 10054. Strengthening
  the test with a committed write exposed a deeper cross-platform bug: `FATAL`
  starts normal exit cleanup, which tries to abort a transaction already marked
  committed. The Linux reproduction reported `cannot abort transaction ... it
  was already committed` and restarted the cluster. The previous read-only
  test never assigned an XID and incorrectly promised backend-only termination.

  PostgreSQL 15–18 source places commit callbacks after the durable commit and
  before resource cleanup. pgrx's `register_xact_callback` explicitly documents
  whole-cluster recovery after an unhandled commit/abort callback failure.
  Irreversible Ankus callback failures now report the original diagnostic at
  `PANIC` after managed frames unwind, avoiding a second attempt to abort a
  committed transaction. Reversible phases continue to report `ERROR`.

  The regression runs read-only and writing transactions in isolated clusters.
  It verifies the original SQLSTATE/message, managed finally logging before
  PANIC, peer disconnection, actual postmaster recovery, committed-row survival,
  and subsequent managed callback execution. Windows connection resets are
  accepted only for the exact socket error and with the same session's exact
  server diagnostic. The public callback guide and XML comments now describe
  this PostgreSQL behavior. The 13 focused transaction callback cases pass on
  PostgreSQL 18.6/Linux x64. Final plain `dotnet test` passes 4,741/4,741 without
  skips in 3m17.706s; Release builds with zero warnings and errors in 12.91s.
  API freshness (135 pages, 1,371 members), `pnpm build` (172 pages), and
  `pnpm check` pass without diagnostics. Hosted run
  [36070618730](https://github.com/willibrandon/ankus/actions/runs/36070618730)
  is green: Linux x64/PostgreSQL 18 passes 4,741; macOS ARM64/PostgreSQL 18 and
  Windows x64/PostgreSQL 17.11 each pass 4,739 with the two existing Linux-only
  allocator cases skipped. Quality, runtime packaging, and documentation also
  pass. The full PostgreSQL/platform matrix and remaining port requirements
  stay open.

- 2026-09-24 — Rejected Dahomey.Cbor 1.27.0 for default serialization: its
  released generator fails the immutable-record and nested-nullability probe.
  The attempted local patch and its package-dependent probe were removed at
  the user's direction. The plan is to write our own serializer.
  Only use NuGet packages owned by Microsoft and/or .NET.
  No serializer dependency was added to Ankus. Default CBOR/JSON type
  generation and its PostgreSQL/platform validation remain open.

- 2026-09-24 — Added Ankus-owned default custom-type serialization.
  `[PgType]` without an explicit codec now builds a closed source-generated
  contract with direct constructor/member access. Ankus owns contract selection,
  nullable shape, construction, unknown-member handling and diagnostics;
  Microsoft's unmodified `System.Formats.Cbor` 10.0.12 and framework JSON token
  APIs provide the format primitives. No serializer fork, runtime code generation,
  or reflection-based contract discovery is involved.

  The implementation supports immutable records, mutable fields/properties,
  nested nullable arrays/lists/string-keyed dictionaries, primitive numeric/string/
  Boolean values and named enum values. Unsupported contracts receive `ANKUS017`.
  The custom-type guide and sample now explain CBOR storage, JSON text, selected
  standard naming/constructor attributes, schema evolution and invalid-input
  behavior. Required-member presence is independent of constructor assignment,
  preserving constructor validation and normalization. Unsupported serialization
  attributes, including derived converters and nonpublic inclusion requests,
  produce diagnostics rather than silently dropping the requested contract.

  Input checks reject integer overflow, floating underflow, decimal rounding,
  invalid Unicode (including skipped fields), missing/duplicate required members,
  excessive nesting, and trailing data. Owned decimal reconstruction preserves
  zero's scale; negative decimal zero is explicitly rejected because CBOR decimal
  fractions cannot retain its sign bit. Cyclic graphs and runtime subtypes are
  rejected before they can lose state. Invalid JSON reports `22P02` rather than
  pgrx's current parse-failure-to-NULL behavior; invalid binary payloads report
  `22P03`. The native error boundary remains responsible for unwinding before ERROR.

  | Required behavior | Direct evidence |
  |---|---|
  | Independent CBOR bytes, exact numeric values and decimal scale | `PgSerializationTests.SignedIntegerFixturesPreserveExactValues`, `DecimalZeroPreservesExactScale`, `DecimalInputRejectsLossyConversions`, `FloatingPointUnderflowCannotSilentlyBecomeZero` |
  | Compiled and executed immutable/mutable construction, naming and nullable graphs | `DefaultSerializedRecordsExecuteImmutableConstructors`, `DefaultSerializationAttributesPreserveContract`, `DefaultSerializedNestedNullabilityExecutes`, `DefaultSerializedRequiredConstructorMembersPreserveValidation` |
  | Invalid declarations, nested NULLs, subtype loss, cycles and depth | `InvalidDefaultSerializationContractsAreDiagnosed`, `DefaultSerializedNestedRequiredReferencesRejectNull`, `DefaultSerializedCollectionSubtypesAreRejected`, `DefaultSerializedRecursiveGraphsRespectDepthAndRejectCycles` |
  | Native AOT scalar/SPI/array/set values, exact SQL type and shape | `SerializedValuesAndNullsCrossOwnershipPaths`, `SerializedArraysPreserveShape`, `SerializedEnumsAndMutableMembersUseGeneratedContracts` |
  | Binary COPY, row preservation, malformed input and same-backend recovery | `SerializedBinaryCopyUsesIndependentCborFixture`, `SerializedBinaryErrorsPreserveBackendAndRows`, `SerializedInputErrorsPreserveBackend`, `SerializedWriteErrorsPreserveBackend` |
  | Real compressed/external storage and type-only relocation/reinstallation | `SerializedStorageSurvivesToast`, `CustomTypeOnlyExtensionTracksRelocationAndReinstallation` |

  Focused validation passes 82 runtime cases, 65 generator cases (including 14
  existing explicit-codec cases), and 39 PostgreSQL cases on PostgreSQL 18.6/Linux
  x64, without skips. Final plain `dotnet test` passes 4,913/4,913 with zero skips
  in 3m11.777s on that platform/version. Release builds with zero warnings and
  errors in 2.59s. API freshness (135 pages, 1,371 members), `pnpm build`
  (172 pages), and `pnpm check` pass without diagnostics. Hosted
  [CI run 36077748480](https://github.com/willibrandon/ankus/actions/runs/36077748480)
  at `0b4b57a` subsequently passed the complete suite on Linux x64/PostgreSQL
  18.6 (4,913 passed, zero skips), macOS ARM64/PostgreSQL 18.6 and Windows
  x64/PostgreSQL 17.11 (4,911 passed and two explicitly Linux-only allocation
  checks skipped on each). Quality, all runtime-package jobs and the documentation
  workflow also passed. This is the recorded matrix, not PG13–19 parity.
  At this milestone, polymorphic unions,
  additional framework/collection shapes, custom text with generated storage,
  zero-copy storage and the other full-port requirements remain visible.

- 2026-09-24 — Added generated tagged variants and inherited custom-type state.
  Standard `JsonDerivedType` registrations and optional `JsonPolymorphic`
  discriminator naming now describe closed class/record unions, including abstract
  roots, concrete base values and explicit self registrations. Discriminators
  preserve string versus Int32 identity in JSON and CBOR; reads accept metadata
  anywhere in the object and reject missing abstract-root tags, unknown or duplicate
  tags, invalid token kinds, and numeric overflow. Writes reject unregistered exact
  runtime types. Lossy fallback, ambiguous registrations, discriminator/member
  collisions and hidden inherited state receive `ANKUS017`.

  Constructors bind inherited members, virtual overrides appear once, and naming,
  required-presence and ignore metadata follow the selected property. Ignored
  overrides do not activate unsupported ancestor metadata. Nullable variants work
  in arrays, lists, dictionaries and recursive graphs. JSON lookahead copies its
  cursor; CBOR lookahead shares owned input with an independent cursor and retains
  the enclosing depth. Decimal-fraction token arrays count as scalar implementation
  details consistently in lookahead, reads and writes. Skipped decimal fractions
  validate structure without narrowing unknown values to .NET decimal.

  Backend testing exposed runtime-subtype selection in erased transports. SPI
  parameters now retain their declared scalar and array codecs, shaped arrays use
  their declared element codec, and composite cells select the codec matching the
  descriptor's base OID. This preserves a tagged base even when its concrete
  variant has a separate PostgreSQL type, including covariant CLR vectors and
  vectors stored in erased tuple cells. Aggregate comparator parameters forward
  the same declared mappings. Edited SPI rows materialize writable base-type
  vectors independently of a narrower source container. Value-type vectors still
  require exact runtime element identity: CLR enum/integer array compatibility
  cannot reinterpret integers as a PostgreSQL custom enum. No runtime contract
  reflection is introduced.

  | Required behavior | Direct evidence |
  |---|---|
  | Exact typed tags, independent CBOR and concrete construction | `PolymorphicStringAndIntegerTagsPreserveExactVariants`, `PolymorphicConcreteBaseAndSelfRegistrationExecute` |
  | Inherited state, constructor normalization and metadata | `PolymorphicInheritedConstructorsAndOverridesPreserveValues`, `OrdinaryInheritedMembersExecute` |
  | Cursor ownership, nested offsets, depth and decimal scalar semantics | `DiscriminatorPositionPreservesOriginalFieldTraversal`, `NestedDiscriminatorUsesCurrentOffsetAndPreservesParentSiblings`, `DecimalScalarLookaheadSharesTheWriterDepthLimit`, `UnknownDecimalFractionsRequireExactlyTwoIntegralComponents` |
  | Invalid declarations, discriminator errors, cycles and unknown subtypes | `InvalidPolymorphicContractsAreDiagnosed`, `PolymorphicDiscriminatorsRejectInvalidInput`, `PolymorphicUnknownRuntimeTypesAreRejected`, `PolymorphicRecursiveGraphsRespectDepthAndCycles` |
  | Native AOT values, SQL NULL, SPI, shaped arrays and sets | `PolymorphicValuesAndNullsCrossOwnershipPaths`, `PolymorphicArraysAndSetsPreserveValues` |
  | Distinct base/variant SQL identities through erased tuples and covariant vectors | `PolymorphicTupleCellsRetainDeclaredMappings`, `PolymorphicCovariantVectorsRetainDeclaredMappings` |
  | Independently writable covariant vectors and strict custom-enum array identity | `PgTypeArrayConversionTests.CovariantCustomVectorsReturnIndependentWritableRootArrays`, `CustomEnumVectorsRejectUnderlyingIntegerArrays` |
  | Independent binary COPY, row preservation and same-backend recovery | `PolymorphicBinaryCopyUsesIndependentFixture`, `PolymorphicBinaryErrorsPreserveBackendAndRows`, `PolymorphicInputErrorsPreserveBackend`, `PolymorphicWriteErrorsPreserveBackend` |
  | Compressed/external storage and sample installation/relocation | `PolymorphicStorageSurvivesToast`, `CustomTypeOnlyExtensionTracksRelocationAndReinstallation` |

  The README, custom-type guide, generated attribute API page and compiled
  `Measurement` sample describe the supported contracts and persistence changes.
  Focused validation passed 112 affected generator cases, 143 serializer/runtime
  cases, two direct array-conversion cases and 51 PostgreSQL cases, with zero
  skips. Final plain `dotnet test` passed 5,060/5,060 with zero skips in
  3m10.400s on PostgreSQL 18.6/Linux x64. The Release build passed with zero
  warnings and errors in 6.83s. API freshness (135 pages, 1,371 members),
  `pnpm build` (172 pages) and `pnpm check` passed without diagnostics.
  An earlier targeted attempt aborted in the Native AOT compiler while a separate
  check rebuilt a shared assembly; exclusive targeted and full-suite reruns passed.
  Hosted [CI run 36080645199](https://github.com/willibrandon/ankus/actions/runs/36080645199)
  at `89a74d2` passed the complete suite: Linux x64/PostgreSQL 18.6 passed
  5,060 with zero skips; macOS ARM64/PostgreSQL 18.6 and Windows x64/PostgreSQL
  17.11 each passed 5,058 with the two explicitly Linux-only allocation cases
  skipped. Quality, all runtime-package jobs and the documentation workflow
  passed. At that milestone, additional
  framework/collection shapes, custom text with generated storage, zero-copy
  storage and the complete PostgreSQL/platform matrix remain required; the full
  port is not complete.

- 2026-09-24 — Added custom SQL text with generated CBOR storage.
  `PgTypeTextCodec<T>` supplies `Parse` and `Format`; selecting it through
  `[PgType(TextCodec = typeof(...))]` retains Ankus's generated structural CBOR
  contract. Existing full `PgTypeCodec<T>` implementations inherit the same
  text API and continue to supply their own stored bytes. Records, structs,
  enums and tagged abstract roots support the text option. Nested attributed
  members retain the enclosing structural contract rather than invoking their
  own SQL text or full storage codecs.

  The generated serializer constructs a text codec lazily on first text use
  inside the managed error boundary. CBOR reads/writes never invoke or construct
  it, including after a cached constructor failure. User `PgException` diagnostics
  remain intact, ordinary exceptions report `38000`, and null text results or
  null parsed values are rejected. Codec validation requires an accessible,
  closed, concrete type with a parameterless constructor and an exact non-nullable
  managed contract. Closed generic codecs are supported; constructors with
  required members must carry `SetsRequiredMembers`.

  Optional `NullInputErrorMessage` makes only the generated text input function
  `CALLED ON NULL INPUT`. A direct `_in(NULL)` raises `22004` before constructing
  a codec. PostgreSQL also calls non-strict input when coercing untyped NULL
  literals (including inferred nullable arguments), NULL text-array elements and
  text COPY fields, so those inputs follow the same configured policy. Already-typed
  SQL NULL values bypass the codec and retain NULL through storage, binary COPY,
  nullable managed arguments/results and arrays. Backend tests
  exposed PostgreSQL's null C-string pointer with an unset SQL null flag; the
  native adapter now recognizes that pointer before encoding or measuring it.
  Default JSON types, custom text types and full codecs share this policy.
  Other I/O functions remain strict, and configuration rejects embedded zero
  characters and invalid Unicode. The existing native wrapper transports owned
  errors and raises PostgreSQL ERROR after managed frames unwind.

  The README, public custom-type guide and compiled `RgbColor` sample describe
  the separate text/storage contracts, binary protocol and NULL policy.

  | Required behavior | Direct evidence |
  |---|---|
  | Independent text/CBOR contracts and lazy retained construction | `CustomTextCodecIsDeferredCachedAndSeparateFromBinary`, `CustomTextCodecConstructionIsDeferredAndCached`, `CustomTextAndGeneratedBinaryUseIndependentFormats` |
  | Cached factory errors with continued binary operations and exact diagnostics | `CustomTextFactoryFailureDoesNotDisableBinaryStorage`, `CustomTextFactoryErrorsLeaveBinaryStorageUsable`, `CustomPgExceptionsRetainTheirIdentityAndDiagnostics` |
  | Executed structs/enums/tagged variants and nested structural contracts | `CustomTextContractsExecute`, `CustomTextTaggedVariantsExecute`, `CustomTextNestedTypesRemainStructural`, `CustomTextNestedContractsRemainStructural` |
  | Exact codec type, inherited required members, closed generics and friend accessibility | `InvalidCustomTextContractsAreDiagnosed`, `CustomCodecValidationRejectsNullableContracts`, `CustomCodecValidationExecutesInitializedRequiredMembers`, `CustomCodecValidationExecutesClosedGenericFullCodec`, `CustomCodecValidationHonorsFriendAssemblies` |
  | Catalog strictness, direct/literal/text-array/COPY NULL input, typed NULL preservation and empty/Unicode messages | `CustomTextNullInputOptionsPreserveSqlNull`, `CustomTextNullInputPolicyAppliesToTextArraysAndCopy` |
  | Native AOT SPI, arrays, sets, tuples, independent binary COPY, TOAST and sample relocation | `CustomTextValuesAndNullsCrossOwnershipPaths`, `CustomTextArraysSetsAndTuplePreserveValues`, `CustomTextBinaryCopyUsesIndependentFixture`, `CustomTextStorageSurvivesToast`, `CustomTypeOnlyExtensionTracksRelocationAndReinstallation` |
  | Malformed input, failed COPY row preservation and same-backend recovery | `CustomTextCodecErrorsPreserveBackend`, `CustomTextBinaryErrorsPreserveBackendAndRows` |

  Focused validation passed 100 runtime cases, 40 generator cases and 45
  Native AOT PostgreSQL cases on PostgreSQL 18.6/Linux x64, without skips.
  The final 12-case runtime refinement also passed. Final plain `dotnet test`
  passed 5,156/5,156 with zero skips in 3m06.572s on that platform/version.
  Release builds passed without warnings or errors. API freshness (136 pages,
  1,374 members), `pnpm build` (173 pages) and `pnpm check` passed without
  diagnostics. Hosted CI for commit `757382f` passed the quality job and full
  PostgreSQL suites: Linux x64/PostgreSQL 18.6 passed 5,156 with zero skips;
  macOS ARM64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11 each passed 5,154
  with the two explicit Linux-only allocation checks skipped. CI run
  [36082900587](https://github.com/willibrandon/ankus/actions/runs/36082900587)
  and documentation run
  [36082900624](https://github.com/willibrandon/ankus/actions/runs/36082900624)
  both completed successfully.
  Additional framework/collection shapes, zero-copy storage and all unresolved
  full-port/platform requirements remain required.

- 2026-09-24 — Implemented the packed native-storage foundation for custom types.

  `[PgType(NativeLayout = true, TextCodec = typeof(...))]` stores dense native
  payload bytes and uses a separately lazy text adapter. Every aggregate must
  explicitly declare sequential `Pack = 1` layout. The generator validates
  fixed-width numeric fields, enums (including unnamed values), same-assembly
  nested structs and fixed numeric buffers, including private and generated
  backing fields. It rejects padding/layout overrides, empty or generic structs,
  references, pointers, native integers, booleans, characters, inline arrays and
  opaque framework/external structs. Cached aggregate sizes avoid repeated
  expansion of shared nested layouts; checked arithmetic rejects size overflow.
  The closed runtime codec checks the generated size against the managed size,
  reads unaligned data into independent values, rejects nonexact payload lengths
  with `22P03`, and preserves numeric bits without structural serialization.

  Optional binary send/receive exposes the same native payload. Field order and
  host byte order form the persisted contract; this is not the default CBOR wire
  format, and layout changes require migration. Text-domain validation is
  separate from binary size validation. This milestone deliberately retains
  copied function/SPI/array transport. It does not complete pgrx's borrowed
  `PgVarlena<T>` views, copy-on-write, native ownership transfer, or broader native
  layouts. Those requirements and the remaining full-port checklist stay open.
  The README, custom-type guide, source XML/API and compiled `PackedColor` sample
  describe the contract and those limits.

  | Required behavior | Direct evidence |
  |---|---|
  | Independent native bytes, unaligned reads, numeric limits, NaN payloads and signed zero | `PackedNestedValuesUseIndependentNativeBytes`, `SinglePrecisionPreservesExactBits`, `DoublePrecisionPreservesExactBits`, `NativeLayoutMixedFieldsPreserveIndependentBytes` |
  | Nested/fixed-buffer/private/backing fields, excluded static state and rejected layouts/size overflow | `NativeLayoutNestedEnumsAndFixedBuffersExecute`, `NativeLayoutBackingFieldsRemainNativeStorage`, `InvalidNativeLayoutContractsAreDiagnosed` |
  | Lazy shared text, cached errors and binary independence | `NativeLayoutTextFactoryIsDeferredAndBinaryIndependent`, `NativeLayoutFactoryFailureLeavesBinaryUsable`, `FactoryFailuresAreCachedWithoutDisablingStorage` |
  | Exact writes, malformed lengths, rejected COPY rows and same-backend recovery | `WritesRespectDestinationPositionAndPayloadBoundaries`, `NativeLayoutBinaryCopyUsesIndependentFixture`, `NativeLayoutBinaryErrorsPreserveRowsAndBackend`, `NativeLayoutTextErrorsPreserveBackend` |
  | SQL type/NULL identity, raw copies, eight scalar SPI paths, arrays, sets and tuples | `NativeLayoutValuesAndNullsCrossOwnershipPaths`, `NativeLayoutArraysSetsAndTuplesPreserveValues`, `NativeLayoutRawValuesPreserveTypeAndLifetime`, `NativeLayoutNullInputPolicyPreservesTypedNull` |
  | Short-header storage, compressed/external TOAST and sample relocation/reinstallation | `NativeLayoutBinaryCopyUsesIndependentFixture`, `NativeLayoutFixedBuffersSurviveToast`, `CustomTypeOnlyExtensionTracksRelocationAndReinstallation` |

  Focused runtime validation passed 39 cases, generator validation passed 39 and
  published Native AOT PostgreSQL validation passed 29 (including the existing
  sample lifecycle test), all without failures or skips. The backend run used
  Linux x64/PostgreSQL 18.6 and took 41.306s. Initial fixture attempts exposed
  analyzer violations and a missing explicit type dependency on a raw SQL
  signature; these were corrected before the passing run. No failed setup is
  counted as backend evidence. The final plain `dotnet test` passed 5,262/5,262
  with zero skips in 3m10.582s on Linux x64/PostgreSQL 18.6. An earlier full run
  stopped in fixture publication when an MSBuild child node exited (`MSB4166`);
  its 2,702 unexecuted integration cases are not counted as backend evidence.
  The subsequent complete run passed without that setup failure recurring.
  The final Release build passed with zero warnings/errors in 14.24s. API
  generation and freshness passed (136 pages, 1,375 members), as did `pnpm build`
  (173 pages) and `pnpm check` (zero errors, warnings or hints). Independent
  source/assertion review found no unresolved defect. Commit `ef2e28b` passed
  [hosted CI](https://github.com/willibrandon/ankus/actions/runs/36085657783):
  Linux x64/PostgreSQL 18.6 passed 5,262 tests with no skips; macOS
  ARM64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11 each passed 5,260
  with the two existing Linux-only allocation-injection cases skipped. Quality,
  all three runtime jobs and documentation publication also passed. Windows
  validation took about 14 minutes, above the preferred ten-minute feedback
  target but within the enforced twenty-minute limit. The full PostgreSQL-major
  matrix and macOS x64 remain unverified for this milestone.

- 2026-09-24 — Implemented checked native-layout `PgVarlena<T>` ownership.

  Direct scalar callbacks now borrow an unchanged four-byte-header PostgreSQL
  datum, while short/compressed/external inputs use writable detoast temporaries.
  The first borrowed write allocates a private native varlena in the captured
  source context. Backend header macros select short or full headers, and value
  access copies the exact packed payload without exposing a managed reference
  into native storage. Unique callback leases expire without backend calls and
  cannot revive when nesting depth is reused. Access validates thread, provider,
  context generation and allocation identity as applicable.

  Constructors and `Clone(destination)` validate the destination before native
  allocation; `Clone()` uses the source context. Explicit `IntoDatum()` copies
  callback inputs before transfer and consumes aliases only after successful
  detach. Failed allocation, initialization, transfer and disposal preserve
  retry rights and owned diagnostics. Ordinary generated output and SPI binding
  copy payloads without consuming aliases. Canonical untyped SPI remains bare
  `T`; typed wrapper conversions allocate independent storage and share the
  original type's lazy text codec. Failed array conversions release provisional
  wrappers without disposing caller aliases, and vector shape rejection occurs
  before allocating converted elements.

  Deferred set and aggregate inputs use independent copies in the callback's
  result owner, including every wrapper element in vector/shaped array inputs.
  Review found and corrected an unchecked destination-context lookup and an
  aggregate array path that initially used the per-call temporary context.
  README, custom-type and memory-context guides, and source XML describe these
  lifetimes. This remains limited to the existing packed native layouts. Broader
  layouts, borrowed array/text/bytea views, other unresolved pgrx requirements and
  the complete PostgreSQL-major/platform matrix remain open.

  | Required behavior | Direct evidence |
  |---|---|
  | Exact header offsets, zero initialization and destination validation | `CreationUsesExactHeaderOffsetAndZeroedPayload`, `DefaultValueBypassesUserConstructor`, `InvalidDestinationsFailBeforeAllocation` |
  | Borrowing, first-write allocation, source aliases and writable temporary cleanup | `BorrowedReadsPreserveInputWithoutAllocation`, `FirstMutationCopiesOnceAndPreservesSourceAliases`, `WritableInputMutatesInPlaceWithoutOwningCleanup`, `VarlenaBorrowingAndMutationPreserveOriginalStorage` |
  | Callback nesting, depth reuse, thread/provider/context denial and clone lifetime | `NestedScopesPreserveAncestorsAndExpireChildren`, `ReusedDepthNeverRevivesExpiredScope`, `LeaseRequiresOriginatingThreadWithMatchingProvider`, `ClonesUseIndependentStorageAndSelectedContext`, `VarlenaClonesSurviveSourceAndExpireWithDestination` |
  | Explicit transfer, ordinary output alias preservation and failed-operation retry | `OwnedTransferConsumesAliasesWithoutFreeingStorage`, `FailedOwnedTransferRetainsRetryRights`, `FailedCopyOnWritePreservesBorrowAndBothErrors`, `VarlenaTransferConsumesOnlyExplicitOwners`, `VarlenaOrdinaryReturnsPreserveOwnedAliases` |
  | Type/NULL/shape identity, malformed envelopes and provisional array cleanup | `NativeInputEnvelopeRejectsNonexactLength`, `NativeInputEnvelopeRejectsInvalidProvenance`, `PartialWrapperArrayFailureReleasesEveryNewOwner`, `MalformedNativeArrayReleasesDecodedWrappers`, `VarlenaValuesCrossOwnershipPaths`, `VarlenaArraysAndTuplesRetainTypeShapeAndNull` |
  | Short-header boundaries, TOAST, deferred sets and retained aggregate elements | `VarlenaShortHeaderBoundaryPreservesZeroPayload`, `VarlenaDetoastedValuesMutateWithoutReusingSource`, `VarlenaDeferredSetsRetainInputsAndReleaseOwners`, `VarlenaDeferredArraySetsRetainEveryElement`, `VarlenaAggregatesRetainWrapperInputs`, `VarlenaAggregateArraysRetainEveryElement` |

  Focused validation passed 61 direct runtime cases, nine generator cases and
  33 published Native AOT backend cases, all without failures or skips. The
  backend run used Linux x64/PostgreSQL 18.6 and took 40.599s.
  Native publication initially rejected an unused borrow helper; the generator
  now emits it only for scalar functions that use it. Backend fixture review
  corrected two PostgreSQL assumptions: column `STORAGE PLAIN` can receive values
  packed by an intermediate tuple, and managed raw `PgDatum` inputs intentionally
  copy their storage. The borrowing witness uses two wrappers over one stored
  127-byte value: both must share the original header, and mutating one must leave
  the other's address and bytes intact. Cleanup commands use nonquery execution
  rather than asserting a scalar from a void SQL function.

  The final complete `dotnet test` run passed 5,365/5,365 with zero skips in
  3m11.356s on Linux x64/PostgreSQL 18.6. An earlier attempt passed all 2,630
  non-backend tests but stopped during fixture publication when an MSBuild child
  node exited (`MSB4166`); its 2,735 unexecuted integration cases are not backend
  evidence. The reported temporary diagnostics directory was already gone.
  The unchanged full rerun retained an explicit MSBuild diagnostics directory
  and passed without recurrence; the intermittent publication failure's cause
  remains unestablished. The Release build passed with zero warnings/errors in
  14.02s. API generation and freshness passed (137 pages, 1,383 members), as did
  `pnpm build` (174 pages) and `pnpm check` (zero errors, warnings or hints).
  Independent source/assertion review found no unresolved ownership defect.
  Hosted [CI run 36089023397](https://github.com/willibrandon/ankus/actions/runs/36089023397)
  and the documentation workflow passed for commit `bf34b7e`. The complete suite
  passed on Linux x64/PostgreSQL 18.6 (5,365 passed, zero skipped), macOS
  ARM64/PostgreSQL 18.6 (5,363 passed, two existing Linux-only skips), and Windows
  x64/PostgreSQL 17.11 (5,363 passed, the same two skips). Platform jobs took
  8m27s, 8m49s and 14m53s respectively; Windows exceeds the preferred ten-minute
  feedback target but remains below the hard twenty-minute limit.

- 2026-09-24 — Implemented generated custom-type operators. Added explicit
  `PgEquality`, `PgOrdering` and `PgHashing` contracts, closed managed interface
  dispatch, dependency-ordered operator families/classes, and `IPgHashable` for
  stable database hashes. The callbacks reuse the existing scalar conversion and
  error boundary. Enum opt-ins use underlying numeric order; unmarked enums keep
  PostgreSQL label order. Manual same-schema equality can supply the equality
  dependency, and non-boolean manual operators are rejected before SQL emission.

  `PgHash` implements the SeaHash v4 byte-buffer algorithm with pgrx's frozen
  seeds, explicit little-endian integer input and strict UTF-8 text. Independent
  vectors from both official SeaHash 4.1.0 implementations agree; the focused
  runtime scope passes 63 cases with zero failures/skips. The generator and
  updated custom-types sample build with zero diagnostics. README, operator and
  custom-type guides describe the API and stable normalization requirements.
  The generator scope passes 30 cases with zero failures/skips in 2.544s, including
  compiled execution of exact and inherited interfaces, signed/unsigned enum
  boundaries, independent opt-ins, invalid contracts, graph edges/collisions,
  source permutations and byte-limited Unicode names. Generated functions are
  strict, immutable and parallel safe. Only comparison signs matter; a comparator
  can return `int.MinValue` or `int.MaxValue`. Native-layout callbacks use copied
  payloads and preserve live varlena aliases. Abstract tagged roots compare through
  their declared interfaces while retaining concrete variant data.

  | Required behavior | Direct evidence |
  |---|---|
  | Logical equality independent of identity, non-key metadata and stored bytes | `CustomOperatorHelpersExecuteExactValueContracts`, `CustomOperatorsPreserveLogicalEqualityAndExtremeSigns` |
  | Exact SeaHash bytes, tails, UTF-8, invalid UTF-16 and fixed-width integer inputs | `ByteSequencesMatchSeaHashFourReferenceVectors`, `ZeroBytesRetainTheirOriginalLength`, `TextUsesExactUtf8ReferenceVectors`, `UnpairedUtf16SurrogatesAreRejected`, `UnsignedValuesUseEightLittleEndianBytes` |
  | Nonstandard order, extreme comparator signs and every B-tree predicate strategy | `CustomOperatorHelpersExecuteExactValueContracts`, `CustomOperatorsUseNonstandardIndexedOrdering`, `CustomOperatorBtreeUsesEveryStrategy` |
  | Independent opt-ins, exact interface contracts, graph IDs and collision diagnostics | `CustomOperatorOptionsAcceptManualEquality`, `InvalidCustomOperatorContractsAreDiagnosed`, `CustomOperatorManualEqualityMustReturnBoolean`, `CustomOperatorGroupIdsOrderCompleteFamilies`, `CustomOperatorGraphsDiagnoseInvalidDependencies`, `CustomOperatorLongNamesRemainDistinctAndDeterministic` |
  | Actual default classes, catalog strategies/support functions, logical uniqueness and collisions | `CustomOperatorCatalogRetainsFamiliesAndPlannerContracts`, `CustomOperatorUniqueIndexUsesLogicalEquality`, `CustomOperatorHashIndexRechecksCollisions` |
  | Sorted/hashed grouping, DISTINCT, Hash Join and Merge Join with exact results | `CustomOperatorGroupingPreservesEqualityAndNull`, `CustomOperatorFamiliesExecuteRealJoins` |
  | Native/custom-base enums, codec/native/tagged storage, NULL bypass and alias preservation | `CustomOperatorEnumsKeepTheirDistinctStorageContracts`, `CustomOperatorStorageModesRetainValuesAndAliases`, `CustomOperatorTaggedRootsPreserveConcreteState`, `CustomOperatorNullsBypassManagedContracts` |
  | Stable committed index reuse from distinct backends | `CustomOperatorHashesPersistAcrossFreshBackends` checks independent hash literals, distinct unpooled PIDs, collisions, mutations, reindex and constrained index results |
  | Extension ownership, schema relocation, removal and reinstallation | `CustomOperatorSampleRelocatesAndReinstalls` checks 19 exact members, OID continuity through relocation, indexed queries/joins, cleanup, unrelated shadow objects and fresh installed identities |
  | Exact errors, failed index builds/inserts and same-session recovery | `CustomOperatorErrorsRecoverInSameBackend`, `CustomOperatorIndexErrorsPreserveRowsAndRecovery` |

  All 26 focused published Native AOT backend cases pass on Linux x64/PostgreSQL
  18.6 with zero failures/skips in 31.440s. The first attempt stopped on four test
  style diagnostics before any backend execution. The first executed run passed
  23/26: two index-recovery fixtures incorrectly assumed deleting a row made it
  physically absent from the next index build. PostgreSQL can visit recently dead
  tuples. The fixture now checks the exact visible row after deletion, resets
  physical storage with TRUNCATE, and retains failed-build/insert atomicity and
  subsequent constrained-index/same-PID assertions. The lifecycle fixture needed
  an explicit `oid[]` binding for its client-side catalog query. No production
  changes were needed for these fixture corrections. Independent source and
  assertion review found no unresolved defect in this bounded implementation.

  The complete unfiltered `dotnet test` run passes 5,484/5,484 with zero skips in
  3m11.785s on Linux x64/PostgreSQL 18.6. The Release build passes with zero
  warnings/errors in 15.41s. API generation/freshness passes (142 pages, 1,396
  members), as do `pnpm build` (179 pages) and `pnpm check` (zero errors, warnings
  or hints). [Hosted CI](https://github.com/willibrandon/ankus/actions/runs/36092985059)
  subsequently passed: Linux x64/PostgreSQL 18.6 ran all 5,484 tests with no skips
  in a 7m37s job; macOS ARM64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11
  each passed 5,482 with the two existing Linux-only allocation cases skipped,
  in 8m9s and 14m38s jobs. Quality, runtime preparation, and documentation
  build/deployment also passed. No custom-operator case was skipped.
  Fresh backend reuse
  does not establish postmaster restart or upgrade persistence. The public test
  harness has no data-preserving restart operation.
  The hash helper specifies exact byte encodings, not arbitrary Rust `Hash` value
  feeds. Custom SQL generation override/disable hooks, manual raw base-type
  mappings, and the full PostgreSQL-major/platform matrix remain full-port work.

- 2026-09-24 — Added function SQL generation controls. `PgFunction.GenerateSql`
  disables installation statements while retaining the managed method, native
  export/finfo, type conversions and dependency identifiers. `PgFunction.Sql`
  replaces the complete function and attached operator/cast SQL with a literal;
  null preserves defaults and empty/comment-only strings remain replacements.
  `@FUNCTION_NAME@` resolves to the actual native export and `@MODULE_PATHNAME@`
  to PostgreSQL's control-file substitution marker. Replacements conservatively
  prevent relocation unless `SqlRelocatable` opts in; fixed schemas and other
  custom blocks still govern the extension-wide result.

  Scalar, SETOF/TABLE, trigger, event-trigger and aggregate-helper callbacks use
  the same policy without changing their native boundary. Shared aggregate
  helpers emit one replacement while the parent aggregate remains generated.
  A function replacement owns its attached operator/cast declarations as one
  fragment. Their aliases and original internal edges remain in the graph;
  external incoming dependencies are lifted to the function after all
  `Requires`, `Before`, bootstrap and final edges resolve. This preserves valid
  default/disabled interleaving and diagnoses impossible replacement interleaving.
  `ANKUS005` rejects contradictory controls, malformed Unicode/NUL and invalid
  graph contracts before emitting a partial manifest. Existing ABI, nullability,
  operator/cast and specialized callback validation remains active.

  | Requirement | Concrete evidence |
  |---|---|
  | Defaults, retained native contracts and literal boundaries | `SqlGenerationDefaultPreservesAllWrapperKinds`, `SqlGenerationControlsPreserveEveryWrapperKind`, `SqlGenerationEmptyAndLiteralReplacementsRemainExact`, `SqlGenerationLiteralUsesExactWrapperAndModulePlaceholders` |
  | Overloads, incremental edits, dependencies and diagnostics | `SqlGenerationOverloadsRemainDistinctAndDeterministic`, `SqlGenerationAttributeChangesInvalidateIncrementalOutput`, `SqlGenerationBundlePreservesRelatedAliasesAndPrerequisites`, `SqlGenerationDisabledPreservesAnchorsWithoutLiftingRequirements`, `SqlGenerationRetainsInvalidRelatedGraphs`, `InvalidSqlGenerationOptionsAreDiagnosed`, `SqlGenerationDoesNotBypassExistingContractValidation` |
  | Type/schema prerequisites, shared helpers and relocation metadata | `SqlGenerationReplacementRetainsAutomaticTypeAndSchemaEdges`, `SqlGenerationSharedAggregateHelperIsReplacedOnce`, `SqlGenerationRelocationRequiresEveryCustomDeclaration` |
  | Real scalar/NULL/options and iterator ownership | `CustomSqlScalarAliasesPreserveNativeIdentityAndOptions`, `CustomSqlSetFunctionsPreserveRowsAndNulls`, `CustomSqlSetEarlyTerminationDisposesIterator`, `CustomSqlTableFunctionsPreserveColumnsAndCleanup` |
  | Specialized callbacks, exact catalogs and ownership | `CustomSqlTriggerFunctionsExecuteAndRecover`, `CustomSqlEventTriggerFunctionsExecuteAndRecover`, `CustomSqlAggregateHelpersExecuteIndependentlyOfParents`, `CustomSqlOperatorAndCastBundlesExecuteOnce`, `CustomSqlFunctionObjectsAreExtensionMembers` |
  | Errors, cleanup and retained disabled exports | `CustomSqlFunctionErrorsRecoverInSameBackend`, `DisabledOnlySqlPackageRetainsCallableNativeExports`: exact values/IEEE bytes, SQL NULL, diagnostics and successful native calls in the same backend |
  | Packaged incremental SQL, failure atomicity and lifecycle | `ReplacementSqlPackageRebuildsRelocatesAndRollsBackInstallation`: native callback failure after object creation rolls back all five explicit objects; corrected publication preserves exports, relocation preserves OIDs, drop preserves unrelated shadow objects and reinstall creates working fresh identities |

  The 59 generator cases pass with zero failures/skips in 2.647s. The first
  published backend/package scope passes 17/18 on Linux x64/PostgreSQL 18.6,
  including all 16 shared backend cases and the disabled-only package. The
  remaining fixture attempted a PL/pgSQL DO block while its isolated harness's
  library search path contained only the package output, causing `58P01` for
  `plpgsql`. The fixture now invokes its newly registered native callback to
  raise the intended `P8211` after creating the replacement objects. Its focused
  rerun passes 1/1 with no skips in 1m15.282s, preserving real installation
  rollback and same-session recovery. No production correction was needed.
  Independent production/generator/backend review found no unresolved defect.
  The complete unfiltered `dotnet test` run passes 5,561/5,561 with zero skips
  in 3m43.970s on Linux x64/PostgreSQL 18.6. The Release build passes with zero
  warnings/errors in 14.95s. API generation/freshness passes (142 pages, 1,399
  members), as do `pnpm build` (179 pages) and `pnpm check` (zero errors,
  warnings or hints). [Hosted CI](https://github.com/willibrandon/ankus/actions/runs/36095871876)
  passed for commit `d6984ae`: Linux x64/PostgreSQL 18.6 passed all 5,561 tests
  with zero skips in a 9m41s job; macOS ARM64/PostgreSQL 18.6 and Windows
  x64/PostgreSQL 17.11 each passed 5,559 with the two existing Linux-only
  allocation cases skipped, in 8m16s and 15m9s jobs. All function-control cases
  ran on each platform. Quality, runtime preparation and documentation
  build/deployment passed. Windows remains above the preferred ten-minute
  feedback target and below the hard twenty-minute limit.

  The pinned pgrx `ToSqlConfig` stores only enabled/literal content, and its
  function/trigger/general parsers reject callback paths. The advertised callback
  bullet in its macro documentation is stale; it is not an unimplemented parity
  requirement. C# literal attributes provide the outcome of Rust's `pgrxsql`
  documentation fences without interpreting source comments. Type shell/I/O and
  enum overrides, aggregate-declaration overrides, ordering/hash family overrides,
  declared custom-type providers, raw/manual mappings, and the complete
  PostgreSQL/platform matrix remain required. Current pgrx ignores the parent SQL
  option for its equality derive and applies ordering/hash options only to the
  family/class, retaining helper SQL; subsequent work must preserve those distinct
  ownership boundaries without treating this function milestone as full parity.

- 2026-09-24 — Extended SQL controls to declaration owners. `PgType` owns the
  shell/I/O/completed-type bundle, `PgEnum` owns its
  enum declaration, `PgAggregate` owns its parent statement, and `PgOrdering`
  and `PgHashing` own only their family/class statements. Codecs, native exports,
  type registration, aggregate helpers, comparison/hash functions and relational
  operators retain their existing contracts and independent policies.

  Type literals receive role-specific native I/O tokens; unavailable binary
  tokens produce `ANKUS005` instead of fabricating exports or enabling binary
  support implicitly. Family literals receive exact quoted SQL helper-name
  tokens so long type identifiers remain usable. Module substitution happens
  before inserting helper identifiers, preserving marker-like text in legitimate
  quoted names. The focused generator
  run passes 119 cases (60 new declaration cases and 59 function-control
  regressions), with zero failures/skips in 3.415s. Its first attempt stopped
  before test execution on two culture-formatting diagnostics in fixture text
  construction; explicit invariant formatting fixed those test-only errors.
  Generator and runtime builds pass with zero warnings/errors.

  | Requirement | Concrete evidence |
  |---|---|
  | All five declaration boundaries, independent siblings and retained native contracts | `DeclarationSqlDefaultsAndControlsPreserveOwnedBoundaries`, `DeclarationSqlAggregateAndHelperPoliciesRemainIndependent`, `DeclarationSqlFamilyPoliciesRemainIndependent` |
  | Exact I/O exports, binary availability and quoted/long helper names | `DeclarationSqlTypeTokensMatchActualExports`, `DeclarationSqlUnavailableBinaryTokensAreDiagnosed`, `DeclarationSqlFamilyTokensPreserveQuotedHelperNames`, `DeclarationSqlOutOfContextTokensRemainLiteral` |
  | Dependencies, collisions, incremental edits, relocation and unchanged validation | `DeclarationSqlControlsRetainDependencies`, `DeclarationSqlControlsRetainGraphDiagnostics`, `DeclarationSqlControlsRetainNameCollisions`, `DeclarationSqlAttributeEditsInvalidateOutput`, `DeclarationSqlRelocationRequiresEveryReplacement`, `InvalidDeclarationSqlOptionsAreDiagnosed`, `DeclarationSqlDoesNotBypassContracts` |
  | Native text/binary values, SQL NULL, shaped arrays and same-backend error recovery | `ReplacementTypeSqlPreservesTextBinaryAndArrays`, `ReplacementTextOnlyTypePreservesJsonAndIdentity`, `ReplacementTypeBinaryReceiveUsesIndependentBytes`, `ReplacementTypeBinaryErrorsRollbackAndRecover`, `ReplacementTypeTextErrorsRecover`: independent CBOR/native payloads and complete COPY bytes; a malformed later row rolls back earlier received rows |
  | Enum identity, labels and independent aggregate/helper policies | `ReplacementEnumSqlPreservesLabelsAndIdentity`, `ReplacementAggregateSqlRetainsIndependentHelpers`: exact labels/numbers, array shape/NULL, empty/all-NULL/nonempty aggregate results, helper identities, error cleanup and same-PID recovery |
  | Retained disabled-family helpers and real replacement index classes | `DisabledFamilySqlRetainsExecutableHelpers`, `ReplacementFamilySqlRetainsHelpersAndExecutesIndexes`: absent default classes, successful retained calls, named B-tree/hash Index Scan with Index Cond, exact duplicate/collision rows, long Unicode names, failed inserts and successful REINDEX |
  | Suppressed type exports remain usable | `DisabledTypeSqlPackageRetainsCallableIoExports`: LOAD before the type exists, empty extension membership, manual registration of actual I/O/typed exports, exact values/NULL/errors and objects still callable after extension drop |
  | Installation atomicity, incremental package rebuild and ownership lifecycle | `DeclarationSqlPackageRelocatesRollsBackAndReinstalls`: failure after all declarations rolls back their catalogs; corrected attribute-only publication retains exports; 30 exact owned identities survive relocation, 12 unrelated shadow identities survive drop, and reinstall gives fresh working identities |

  All 27 focused published Native AOT cases pass with zero failures/skips in
  1m25.803s on Linux x64/PostgreSQL 18.6. The initial attempt stopped during
  fixture publication on `CA1036`: two comparable test records needed relational
  operators. Its 26 reported failures were initialization failures, with no test
  bodies executed. Review also corrected two fixture assumptions: a text-array
  NULL must invoke the configured rejecting input policy, while a typed array
  preserves ordinary SQL NULL; PostgreSQL debug builds may invoke successful
  constant input twice, so an exact parser counter uses an explicit I/O call.
  The corrected fixtures test those distinct paths, including an added `22004`
  text-array rejection case. No production correction was required.

  Independent production/generator/backend review found no unresolved defect.
  The first full suite passed 5,627/5,648 and reported 21 failures, all while
  installing the shared fixture into existing LATIN1 test databases: its new
  CJK type identifier cannot be represented in LATIN1 (`22P05`). The fixture's
  Greek enum label has the same limitation. Fixture names now use accented
  LATIN1-compatible text, preserving a 60-byte UTF-8 type identifier,
  helper-name fallback and exact label mapping. The corrected focused scope
  passes 27/27 with zero skips in 55.547s, including an existing failed enum
  case and `DeclarationSqlLatin1InstallationPreservesTypesAndFamilies`. The new
  regression installs the complete fixture into LATIN1, checks exact enum/native
  I/O values and bytes, executes both long-name index classes, and recovers from
  a failed indexed hash call in the same backend. The final complete unfiltered
  `dotnet test` run passes 5,649/5,649 with zero skips in 3m43.579s on Linux
  x64/PostgreSQL 18.6. The Release build passes with zero warnings/errors in
  15.94s. API generation/freshness passes (142 pages, 1,414 members), as do
  `pnpm build` (179 pages) and `pnpm check` (zero errors, warnings or hints).
  Commit `47ee3c7` passed [CI run 36098297661](https://github.com/willibrandon/ankus/actions/runs/36098297661):
  Linux x64/PostgreSQL 18.6 passed 5,649 cases with zero skips in a 9m55s job;
  macOS ARM64/PostgreSQL 18.6 passed 5,647 with two existing Linux-only allocation
  skips in 8m57s; Windows x64/PostgreSQL 17.11 passed 5,647 with those same two
  skips in 17m47s. Every new declaration-control case executed. Runtime preparation,
  quality checks and [documentation deployment](https://github.com/willibrandon/ankus/actions/runs/36098297659)
  also passed. These runtime-cache-hit jobs do not establish a cold-runtime-build
  baseline. Windows remains above the preferred ten-minute budget and below the
  hard twenty-minute timeout. Reusable managed-to-SQL mappings, declared type
  providers and the complete version/platform matrix remain full-port requirements;
  explicit raw datum and internal callback bindings are already implemented.

- 2026-09-24 — Added declared catalog-type providers for existing raw and named
  composite bindings. `[assembly: PgSqlTypeProvider("block-id", "type-name")]`
  identifies one inline/file SQL block and an exact catalog name with an optional
  fixed schema. It adds automatic dependencies for parameters, scalar/SETOF/TABLE
  results, arrays and aggregate helpers; operators and casts retain their backing
  function dependencies. Known provider schemas are prerequisites, and fixed
  schemas prevent relocation. Metadata does not parse SQL or register a converter.

  Generated type/enum identities remain reserved under default, disabled and
  replacement SQL policies. Duplicate providers, invalid identifiers and missing
  or wrong-kind SQL block references produce `ANKUS005` without partial artifacts.
  Catalog identity uses exact case-sensitive names and nullable schemas, with
  array bindings referring to their element provider. Unqualified bindings do
  not inherit a function schema or match every fixed schema; external types
  continue to work without a provider.

  A separate inferred-edge set supports manual shell → I/O → completion ordering.
  Only an explicit `Requires`/`Before` path that already places the consumer before
  its completed provider can defer that inferred type edge. Generated, schema and
  bootstrap/final edges cannot authorize deferral; hard and unresolved cycles
  remain errors. A shell block can instead be the provider, with completion still
  an explicit prerequisite of ordinary consumers.

  The focused generator scope passes 246 cases with zero failures/skips in
  3.585s. Its first run passed 238/246: eight fixtures expected spaces where the
  SQL emitter places a newline before `RETURNS`. Exact expected strings were
  corrected, including a later final-helper assertion; no production change was
  required. The first native attempt stopped at fixture publication on `ANKUS004`
  because a TABLE fixture reused output names for inputs. Renaming only those
  inputs preserves its contract; the 15 reported initialization failures executed
  no test bodies. The next run passed all 14 shared-backend cases but failed the
  package fixture's missing-type message expectation: PostgreSQL's function
  declaration path reports `type package_code does not exist` without identifier
  quotes. The exact expectation now matches the verified backend diagnostic.
  The corrected focused run passes all 15 cases with zero failures/skips in
  1m28.276s on Linux x64/PostgreSQL 18.6.

  | Requirement | Concrete evidence |
  |---|---|
  | Exact catalog identity, external bindings and tracked files | `DeclaredTypeProvidersMatchExactLeafIdentity`, `DeclaredTypeProvidersKeepSchemaPlacementAndMultipleClaimsIndependent`, `DeclaredTypeProvidersLeaveExistingExternalBindingsAndNativeContractsUnchanged`, `DeclaredTypeProviderFilesAndMetadataInvalidateIncrementalOutput`, `DeclaredTypeProviderOrderingIsDeterministicAcrossInlineAndFileInputs` |
  | Every signature slot contributes its own prerequisite | `DeclaredTypeProvidersOrderEveryBoundSignature`, `DeclaredTypeProvidersOrderEveryIndependentTableColumn`, `DeclaredTypeProvidersOrderIndependentAggregateInputAndFinalResult`: separate output-only providers prevent another argument or column from masking a missing edge |
  | Reserved generated identities and precise validation | `DeclaredTypeProvidersRecognizeReservedGeneratedIdentities`, `DeclaredTypeProvidersRejectGeneratedIdentityCollisions`, `InvalidDeclaredTypeProvidersSuppressAllArtifacts`, `DeclaredTypeProviderIdentifiersUseUtf8Boundaries`: disabled/replaced anchors, exact diagnostics, 63/64 UTF-8-byte boundaries and no partial artifacts |
  | Shell/completed ordering, hard cycles, schemas and replacement bundles | `DeclaredTypeProvidersDeferOnlyExplicitReversePaths`, `DeclaredTypeProvidersAllowShellProvidersWithoutInferringCompletion`, `DeclaredTypeProvidersPreserveHardAndUnjustifiedCycles`, `DeclaredTypeProvidersComposeWithBundlesAndBoundaries`, `DeclaredTypeProviderSchemasOrderCreationAndConstrainRelocation`: the schema itself waits for a late prerequisite so lexical sorting cannot mask its edge |
  | Actual catalog signatures, manual storage, values/NULL and array/domain identity | `DeclaredProvidersInstallEveryBoundSignature`, `CompleteProviderPreservesManualByValueStorage`, `DeclaredProvidersPreserveCompositeAndArrayIdentity`, `DeclaredDomainConstraintsRecover`: exact OIDs, four-byte by-value U24, zero/max values, real operators/casts, shape/lower bounds, distinct equal-shaped types, exact errors and same-PID recovery |
  | Deferred values and ownership | `DeclaredSetAndTableProvidersRetainValues`, `DeclaredProviderCopiesOutliveSourceStorage`, `DeclaredProviderRejectsInvalidOutputs`, `DeclaredProviderAggregateRetainsFirstValue`: complete large values, normal/early/error cleanup, deleted source storage, wrong/stale present and typed-NULL outputs, retained aggregate state and empty/all-NULL groups |
  | File-only package rebuild, atomic failure and ownership lifecycle | `DeclaredProviderPackageRelocatesReinstallsAndRollsBack`: three publications with unchanged C#/exports; exact enum/revision changes; false claim leaves empty catalogs, retained native callback recovers in the failed-installation backend; eight identities survive relocation and are fresh after reinstall, while four unrelated shadow identities survive drop |

  Independent static production/generator/backend review found no unresolved
  defect or concrete assertion gap; package assertions received separate root
  review. No empirical mutation or coverage claim is made. The completed
  unfiltered `dotnet test` run passes 5,750/5,750 with zero skips in 4m19.622s on
  Linux x64/PostgreSQL 18.6, including 86 new generator cases and 15 new native/
  package cases. The Release build passes with zero warnings/errors in 15.80s.
  API generation/freshness passes (143 pages, 1,418 members), as do `pnpm build`
  (180 pages) and `pnpm check` (zero errors, warnings or hints). Commit `f185f15`
  passes hosted CI run 36101031328: Linux x64/PostgreSQL 18.6 passes all 5,750
  tests with zero skips (10m11s platform job); macOS ARM64/PostgreSQL 18.6 passes
  5,748 with the two existing Linux-only allocation tests skipped (12m31s);
  Windows x64/PostgreSQL 17.11 passes 5,748 with the same two skips (16m53s).
  Quality and all runtime preparation jobs pass; runtime cache hits do not
  establish a cold-runtime build baseline. Documentation run 36101031362 passes,
  including deployment. All platform jobs remain within the hard twenty-minute
  timeout, though macOS and Windows exceed the preferred ten-minute target.

  This catalog-name feature is a bounded step toward pgrx's declared providers.
  The pinned reference matches reusable `SqlTranslatable.TYPE_IDENT` identities
  with explicit ownership, independently of SQL spelling and datum conversion.
  Reusable managed mappings, ownership-aware identity providers, manual mapped
  derived operators, standalone schema extraction and the complete
  PostgreSQL/platform matrix remain full-port requirements.

- 2026-09-24 — Implemented reusable scalar datum mapping contracts with
  `PgDatumType`, independent `IPgDatumReader<T>`/`IPgDatumWriter<T>` directions,
  and explicit `PgTypeOrigin`. Requested CLR identity selects conversion even
  when several wrappers share a catalog OID. The generator registers closed
  converters lazily, including local raw/SPI-only roots and referenced roots
  used by signatures or managed providers. Matching registrations from a
  generated library and consumer share one instance; conflicting converter
  identity, name, schema, origin or directions fail without constructing user
  code. No runtime code generation or unbounded reflection is introduced.

  `PgSqlTypeProvider(sqlId, typeof(T))` supplies extension-owned managed identity
  independently of SQL spelling. Owned mappings require that provider even for
  built-in names; external mappings require an explicit schema and acquire no
  provider dependency. Multiple wrappers and a raw name alias can share one
  block. Conflicting providers, generated identity collisions and unsupported
  mappings fail without partial artifacts. Schema, shell/completion, hard-cycle,
  relocation and tracked-file rules remain active. `ANKUS019` diagnoses invalid
  mapped declarations, directions, containers and signature overrides.

  Supported paths are scalar/nullable generated arguments and results,
  SETOF/TABLE outputs, aggregate helpers, manual operators/casts,
  `PgDatum.Read<T>`, typed SPI parameters and function arguments/defaults.
  Readers must detach managed values; writers receive the current exact OID and
  destination context. SQL NULL skips user conversion, while present managed
  values may deliberately produce a live typed SQL NULL. Exact OIDs, owner
  generations and PostgreSQL domain checks still apply. Saved typed parameters
  reject replacement OIDs after DDL before invoking their writer.

  Independent review identified and closed three production gaps: duplicate
  registration across generated assemblies, canonical fallback for excluded
  raw mapped arrays (including NULL and CLR enum/underlying-array equivalence),
  and named/OID function calls bypassing domain validation for NULL arguments.
  Actual NULL arguments now pass through guarded conversion; default-argument
  placeholders remain unevaluated until PostgreSQL expands their expressions.
  Ordinary typed result APIs reject mapped scalar and array targets before SQL
  execution; typed arrays are not enabled by these rejection guards.

  The focused runtime scope passes 26 tests with zero failures/skips in 929ms;
  the focused generator scope passes 185 in 3.147s, including 99 new cases and
  86 provider regressions. Earlier attempts stopped on enforced analyzer rules
  (concrete helper return type, collection assertions, cancellation propagation
  and diagnostic release tracking), or the existing null-name fixture's newly
  ambiguous overload. The fixture now explicitly selects the name overload,
  and a separate null-managed-type case checks the new overload.

  Its initial native fixture publication stopped on
  an unnecessary using directive before any test body executed. After removing
  it, 67 of 69 focused backend/package/function-call cases passed. Two catalog
  assertion queries needed correction: explicitly cast `pg_type.typstorage` to
  text, and inspect shell metadata by namespace/name instead of a `regtype`
  conversion that rejects shell types. The corrected complete focused scope
  passes all 69 cases with zero failures/skips in 1m19.265s on Linux x64/
  PostgreSQL 18.6, including existing function-call regressions. Independent
  static production/assertion review is Strong; no empirical mutation or
  coverage claim is made.

  | Requirement | Concrete evidence |
  |---|---|
  | Closed lazy registration and generated assembly composition | `RegistrationDefersUserCodeAndCatalogAccess`, `ReadWriteAdaptersShareFactoryAndResolveEveryOperation`, `DatumMappingRegistrationRemainsLazyForSpiOnlyRoots`, `DatumMappingsInitializeGeneratedDependenciesAndConsumersTogether`: zero construction/backend access at module initialization, shared instance and conflicting metadata rejection |
  | Exact CLR identity, directions and every selected generated position | `DeclaredManagedIdentitySelectsConverterInsteadOfRuntimeTypeOrOid`, `DatumMappingsSelectExactInterfacesFromSharedConverters`, `DatumMappingsPreserveEveryScalarSetAndTableContract`, `DatumMappingsCompileAggregateRolesOperatorsAndCasts`, `MappedDirectionsAndDeclaredParametersUseTheirOwnConverters`: independent converters for one SQL type, declared base-class parameters, CLR enums and type-only defaults |
  | Owned provider identity, exact names and graph constraints | `DatumMappingProvidersRejectInvalidOwnership`, `DatumMappingProvidersShareOneCatalogDeclaration`, `DatumMappingProvidersPreserveGeneratedReservations`, `DatumMappingProvidersPreserveExplicitShellOrdering`, `DatumMappingProvidersVisitIndependentTableAndAggregateResults`, `DatumMappingIdentifiersUseExactUtf8Boundaries`, `DatumMappingFilesAndMetadataInvalidateIncrementalOutput` |
  | Datum bits, SQL NULL, domain identity and native owners | `MappedRegistrationAndNullsDoNotInvokeConverters`, `MappedByValueTypesKeepBitsAndManagedIdentity`, `MappedFixedStoragePreservesEveryComponentAndOwner`, `MappedDomainsRejectSiblingAndBaseIdentity`, `MappedWritersValidatePresentAndNullResults`: zero/extrema, independent alias conversion, exact double words, sibling/base domain rejection and stale present/NULL handles |
  | Detached values, deferred sets and aggregate state | `MappedReferenceValuesOutliveToastedSources`, `MappedSetsAndTablesKeepValuesAndCleanup`, `MappedAggregateStateRetainsDetachedValues`, `MappedConverterErrorsRecoverInTheSameSession`: deleted storage sources, complete large values, normal/early/error cleanup, empty/all-NULL groups and exact diagnostics with same-backend recovery |
  | Typed NULL still receives all native checks | `MappedSetWritersPreserveAndValidateTypedNull`, `MappedAggregateWriterValidatesTypedNullResults`, `MappedWriterNullsCannotBypassDomainConstraints`, `MappedFunctionArgumentsValidateNullDomainsBeforeStrictTargets`: scalar/stream/materialized/TABLE/final paths, wrong/stale NULL envelopes, exact 23502 and valid named/OID default requests |
  | Unsupported conversion cannot execute SQL or reinterpret arrays | `OrdinaryMappedResultsFailBeforeBackendExecution`, `UnsupportedMappedResultsFailBeforeSqlSideEffects`, `UnsupportedMappedArrayReadsNeverBypassConverters`: nontransactional sequence state, direct NULL/present enum-array rejection, preserved ordinary integer arrays; generator diagnostics cover unavailable directions and containers |
  | Catalog changes and extension lifecycle | `MappedConcreteResolverRejectsPseudotypes`, `MappedExternalIdentityTracksDdlWithoutRebindingOldParameters`, `DatumMappingPackageRelocatesAndReinstallsWithCurrentTypeIdentity`: missing/shell/pseudo denial, retained-parameter rejection, one package publication, eight owned identities stable on relocation/fresh on reinstall and five unrelated shadow identities preserved |

  Final local verification on Linux x64/PostgreSQL 18.6: plain `dotnet test`
  passes all 5,906 tests, with zero failures/skips, in 4m25.007s. The Release
  build passes in 12.92s and the full non-incremental Release build in 16.21s,
  both with zero warnings/errors. API generation and freshness pass with
  147 pages/1,429 members; `pnpm check` reports zero errors, warnings or hints,
  and `pnpm build` produces 184 pages. Native fixture publication and these
  Release/documentation builds ran sequentially. Commit `ab971bc` is pushed;
  [full CI](https://github.com/willibrandon/ankus/actions/runs/36105331492) and
  [documentation deployment](https://github.com/willibrandon/ankus/actions/runs/36105331394)
  passed. Each platform executed the complete suite against PostgreSQL:

  | Platform | PostgreSQL | Passed | Skipped | Platform job |
  |---|---|---:|---:|---|
  | Linux x64 | 18.6 | 5,906 | 0 | 6m58s |
  | macOS ARM64 | 18.6 | 5,904 | 2 | 10m32s |
  | Windows x64 | 17.11 | 5,904 | 2 | 15m05s |

  The two non-Linux skips are the existing
  `WarmedNativeAllocationsStabilizeAcrossStateAndErrorPaths` cases, whose native
  allocation measurements require Linux. All jobs had zero failures. Runtime
  cache hits are not cold-runtime build measurements; the full supported-major
  and macOS x64 matrix remains unverified here.

  Typed mapped arrays, ordinary SPI-row/composite-field conversions, typed
  generic result APIs, automatic derived operator families, generic wrapper
  declarations, asymmetric SQL spellings, standalone extraction and the full
  PostgreSQL-major/platform matrix remain required. This scalar foundation is
  not complete `FromDatum`/`IntoDatum` parity.

- 2026-09-25 — Implemented mapped scalar readers in SPI scalar/pair/triple
  helpers, their session and retained/session-owned prepared-plan forms, and
  named/OID `PgFunctions.Call<T>`. Each requested CLR type selects its reader;
  no writer is required for a result. Every requested position is checked before
  execution, including later mixed columns. Matching typed NULL checks nominal
  identity without invoking user code; empty results preserve ordinary absence
  semantics without inventing a datum. Mixed polymorphic values retain their
  callback copies while mapped readers detach from temporary storage.

  Catalog calls carry explicit exact-result intent in an existing request field,
  without changing ABI layout. They compare the declared result OID before
  `ExecPrepareExpr`, preventing callee and default-expression evaluation on
  mismatch. Existing built-in domain/base, record, raw, void and polymorphic
  compatibility remains separate. SPI result conversion runs after SQL completes;
  catching a mapped reader/factory error retains completed transactional writes.
  The same timing applies to mapped catalog result conversion.

  Independent design review also identified cleanup paths that could replace a
  primary conversion/native error when temporary-context deletion failed. SPI
  raw acquisition, scalar conversion and mapped catalog results now preserve
  the primary exception and stack, propagate cleanup-only failures, and retain
  both errors in order when both fail. A failed deletion leaves parent-owned
  storage potentially live; it does not imply successful cleanup. Uncaught
  aggregate failures retain the existing generic native error transport policy.

  Focused local validation on Linux x64/PostgreSQL 18.6 passes 47 runtime cases
  in 922ms and 264 backend/package/SPI/polymorphic/function-call cases in
  1m20.437s, all with zero failures/skips. The first runtime attempt stopped
  before execution on four redundant native-integer casts in the new tests;
  removing them satisfied IDE0004 without changing behavior. A proposed TOAST
  assertion was corrected before execution to inspect physical storage in the
  fresh table's TOAST relation instead of assuming uncompressed external text
  occupies fewer bytes than its payload.

  Independent review requested one additional native evidence partition:
  caught catalog reader/factory errors after a function writes rows. The added
  name/OID cases pass 2/2 with zero failures/skips in 54.929s and independently
  preserve row sets `2,5,9` and `11,17`, exact diagnostics and same-backend
  recovery. Final independent static production/assertion review is Strong;
  no empirical mutation or coverage score is claimed. Defensive rejection of
  malformed private request modes was source-reviewed only; executed tests cover
  the modes emitted by the public APIs.

  | Requirement | Concrete evidence |
  |---|---|
  | Reader-only results and declared CLR identity across SPI owners | `ReaderOnlyResultsWorkAcrossSpiOwners`, `TypedMappedSpiResultsUseEverySurfaceAndDeclaredAlias`: independent alias values, all four owner forms, scalar/pair/triple selection and integer extrema |
  | Capability checks before execution in every requested position | `ReadCapabilityPreflightsEverySelectedPosition`, `TypedMappedLaterSlotCapabilitiesFailBeforeAnySqlEffect`, `TypedMappedCatalogWriterOnlyTargetsFailBeforeInvocation`: zero backend/owner/factory activity and untouched nontransactional sequences |
  | Exact present/NULL identity and ordinary absence behavior | `NullAndEmptyResultsPreserveIdentityAndAbsenceRules`, `TypedMappedAbsenceDoesNotInventAValueOrInvokeAConverter`, `TypedMappedSpiDomainsValidatePresentAndNullIdentities`, `TypedMappedShortRowsFailAfterReleasingTheirTemporaryOwner`: base/sibling/unrelated types, empty/utility results, missing columns and nonnullable failures |
  | Exact catalog result checks before expression preparation | `CatalogMappedResultsUseExactModeAndTemporaryOwners`, `ExistingCatalogResultsRetainCompatibilityMode`, `TypedMappedCatalogMismatchPreventsCalleeAndDefaultEffects`: emitted mode/current OID, immutable callee/default sequence witnesses and ordinary domain compatibility |
  | Detached values and temporary cleanup | `MixedResultsCopyPolymorphicStorageBeforeTemporaryCleanup`, `TypedMappedDetachedAndMixedResultsOutliveTheirOwners`, `TypedMappedCatalogStorageAndErrorsPreserveOwnership`: immediate temporary expiry in direct tests, exact fixed-storage words, complete toasted text, mixed polymorphic shape/NULLs and backend recovery |
  | Primary, cleanup-only and combined failure semantics | `ConversionAndCleanupFailuresPreserveTheirOrdering`, `RawAcquisitionErrorsPreserveNativeDiagnosticsAndCleanup`, `CleanupHelperRetainsOriginalExceptionInstances`, `CatalogFactoryFailureIsCachedWhileEveryOwnerIsReleased`: original exception identity/stack, ordered errors, transport release and failed-deletion lifetime |
  | Complete command effects and conversion timing | `TypedMappedFirstRowsDoNotLimitWritesOrUseEarlierStatements`, `TypedMappedManagedFailuresRetainCompletedWritesAndRecover`, `TypedMappedCaughtCatalogConversionErrorsRetainCompletedWrites`: full independent row sets, final-statement selection, reader/factory errors and successful later calls |
  | Live identity and bounded API scope | `TypedMappedResultsResolveExternalIdentityAfterCatalogChanges`, `DatumMappingPackageRelocatesAndReinstallsWithCurrentTypeIdentity`, `TypedMappedResultsDoNotExpandOrdinaryCellScope`: replaced OIDs, nine owned identities across relocation/reinstall, preserved shadows, stale parameters and explicit row/tuple rejection |

  Final local verification on Linux x64/PostgreSQL 18.6: plain `dotnet test`
  passes all 5,976 tests with zero failures/skips in 4m06.195s. The full
  non-incremental Release build passes in 16.10s with zero warnings/errors.
  API generation and freshness pass (147 pages, 1,429 members); `pnpm check`
  reports zero errors, warnings or hints, and `pnpm build` produces 184 pages.
  Native fixture publication and Release/documentation builds ran sequentially.
  Commit `1b5d6cb` passes [full CI](https://github.com/willibrandon/ankus/actions/runs/36108254178)
  and [documentation deployment](https://github.com/willibrandon/ankus/actions/runs/36108254145).
  Each platform executed the complete suite against PostgreSQL:

  | Platform | PostgreSQL | Passed | Skipped | Platform job |
  |---|---|---:|---:|---|
  | Linux x64 | 18.6 | 5,976 | 0 | 10m38s |
  | macOS ARM64 | 18.6 | 5,974 | 2 | 7m15s |
  | Windows x64 | 17.11 | 5,974 | 2 | 16m56s |

  All jobs had zero failures. The two non-Linux skips are the existing
  Linux-only native allocation measurement cases. Runtime cache hits do not
  establish cold-runtime build performance. Linux and Windows exceeded the
  preferred ten-minute target; all jobs remained within twenty minutes. Mapped
  arrays, ordinary row/composite conversion, unsafe native-address typed results,
  broader declaration forms and full platform/version parity remain open.

- 2026-09-25 — Implemented mapped arrays after reviewing the pgrx array
  conversion contracts, PostgreSQL array construction and the existing scalar
  mapping paths. The selected scope composes one `T[]` or `PgArray<T>`
  layer around a registered scalar converter, preserving exact current element
  and array identity, NULL and shape. Generated scalar/variadic/set/TABLE/
  aggregate slots, typed parameters, raw reads and typed SPI/catalog results are
  included; ordinary erased row/tuple conversions remain a separate requirement.

  The design uses temporary extraction storage for detached reads and eager
  guarded construction for writes. Every written element, including framework
  and writer-produced SQL NULL, receives PostgreSQL domain validation before the
  completed array is copied into its destination. Independent review identified
  an additional physical element-header check before native deconstruction and
  destination revalidation after callback-capable domain checks. These guards
  are part of the selected implementation. The focused generator scope passes
  235 cases with zero failures/skips in 3.545s, including 50 new array cases.
  Its initial attempt stopped before execution on an unused using directive in
  the new test file; removing it satisfied IDE0005.

  Review also identified canonical conversion paths that could reinterpret CLR
  mapped enum arrays or accept empty/all-NULL mapped shapes without selecting
  their converter. Explicit source/target guards close those erased paths.
  Supported scalar helpers preserve no-row absence, and declared custom-array
  codecs retain precedence over separately mapped runtime subtypes. The focused
  runtime scope passes 95 cases with zero failures/skips in 977ms, including 34
  new array cases. Those verify exact transport bytes, source survival and
  temporary-owner expiry, primary/cleanup error preservation, erased-path
  rejection, and stopping before the next converter when a callback changes
  catalog identity. Its earlier attempts found test-style diagnostics and one
  obsolete unsupported-array expectation; both were corrected. Independent
  production, generator and direct assertion reviews are complete.

  Exact array-of-domain identity remains distinct from base/sibling arrays and
  domains over the whole array, including NULL, empty and all-NULL values.
  Per-element converters share the scalar's lazy instance. Value/enum CLR
  arrays require their actual declared type; reference covariance uses the
  declared converter and reads produce a writable base array. Retained typed
  parameters reject changed array OIDs before writers; detached managed arrays
  resolve the current element identity when rebound. Type-only defaults and
  prepared metadata require neither a writer nor converter construction.

  Focused Native AOT/PostgreSQL 18.6 validation on Linux x64 passes all 49 new
  backend cases in 56.732s. The preceding broader run passed all 567 selected
  regression/package cases and 32 new cases; its remaining 17 failures came
  from the new test client's untyped nullable-array decoding. Selecting the
  requested type through `GetFieldValueAsync<T>` corrected the helper, without
  production changes. Initial attempts stopped on enforced analyzer diagnostics
  before backend bodies executed. Independent assertion review is complete.

  | Requirement | Named evidence |
  | --- | --- |
  | Closed registration, directions and independent converter selection | `RegistrationSharesLazyConverterAndAllowsOfflineShapes`, `DatumArraysAcceptIndependentDirections`, `DatumArraysRejectUnavailableAggregateDirections`, `MappedArraysSelectDeclaredAliasesAcrossOwners`, `MappedArrayDirectionsAreCheckedBeforeSqlAndFactory`: lazy/shared converter, distinct alias values, byte enum identity, all four SPI owners and untouched sequence state |
  | Exact identity, NULL and shape | `NullEmptyAndRequiredCellsKeepTheirDistinctContracts`, `MappedArrayNominalIdentityIncludesNullEmptyAndAllNull`, `MappedArrayHeaderIdentityIsValidatedBeforeDeconstruction`, `MappedArrayShapesUsePostgresBoundsAndRejectLossyVectorsEarly`: real physical-header mismatch, exact domain arrays, row-major values, rank six and lower bounds, vector rejection before readers |
  | Storage, constraints and ownership | `WriterBuildsExactTransportBeforeDeletingElementStorage`, `ArrayOwnersPreservePrimaryAndCleanupErrors`, `NativeArrayFailuresPreserveDiagnosticsAndCleanup`, `MappedArrayReadOwnershipReleasesTemporaryElementsOnly`, `MappedArrayStoragePreservesIndependentFixedAndToastedValues`, `MappedArrayNullWritersCannotBypassDomainConstraints`: literal transport/bit values, full TOAST text, both NULL sources checked by domains, temporary expiry and caller-source survival |
  | Reentrancy and eager failure boundaries | `ReentrantCatalogChangesStopBeforeTheNextElement`, `MappedArrayLaterWriterErrorsPreserveCleanupAndPreExecutionTiming`, `MappedArrayCatalogMismatchPreventsCalleeAndDefaultEffects`: current per-element identity, later owner reset, exact diagnostics, no target/default/callee effects on pre-execution failure |
  | Generated and deferred behavior | `DatumArraysPreserveEveryScalarSetAndTableContract`, `MappedArrayManualOperatorAndCastUseExactLeafIdentity`, `MappedArrayDeferredSetsAndTablesRetainValuesAndDispose`, `MappedArrayAggregatesRetainInputsAndMovingStates`: full generated SQL contracts, actual operator/cast catalog identities, streaming/materialized arrays/TABLE, early/error cleanup, retained and moving states |
  | Typed SPI/catalog behavior and completed SQL effects | `MappedArraysCrossEveryTypedSpiOwnerAndSelectedPosition`, `MappedArrayManagedErrorsRetainCompletedSqlEffects`, `MappedArrayCaughtCatalogErrorsRetainCompletedWrites`: every selected width/owner, mixed results, no-row/utility absence, complete independent row sets retained when managed conversion fails after SQL |
  | Catalog lifecycle and preserved boundaries | `MappedArraysRefreshExternalIdentityWithoutRebindingSavedParameters`, `DatumMappingPackageRelocatesAndReinstallsWithCurrentTypeIdentity`, `DeclaredCustomArrayConverterPrecedesRuntimeDatumMapping`, `ErasedArrayPathsRejectPresentEmptyAndNullWithoutChangingOwners`: live E/A replacement, stale parameters, twelve owned package identities across relocation/reinstall, unchanged shadows, declared codec precedence and rejected erased writes |

  Combine transport is executed by a test-local aggregate using the generated
  Combine helper as its transition function under a valid aggregate context;
  this does not claim parallel-worker execution. Private malformed constructor
  frames have defensive source review only; no malformed-frame execution,
  empirical mutation or coverage percentage is claimed. README and the public
  arrays, raw-values, SPI, function-call and function-signature guides describe
  these contracts. Plain `dotnet test` passes all 6,109 tests with zero failures
  or skips in 245.509s on Linux x64/PostgreSQL 18.6. The non-incremental Release
  build passes in 17.19s with zero warnings/errors. API generation and freshness
  checks pass for 147 pages/1,429 members; `pnpm check` reports zero
  errors/warnings/hints and `pnpm build` produces 184 pages. Commit `ad49b1f`
  passes [full CI](https://github.com/willibrandon/ankus/actions/runs/36112546321)
  and [documentation deployment](https://github.com/willibrandon/ankus/actions/runs/36112546302).
  Each platform ran the complete suite against a real PostgreSQL server:

  | Platform | PostgreSQL | Passed | Skipped | Platform job |
  |---|---|---:|---:|---|
  | Linux x64 | 18.6 | 6,109 | 0 | 10m28s |
  | macOS ARM64 | 18.6 | 6,107 | 2 | 8m24s |
  | Windows x64 | 17.11 | 6,107 | 2 | 18m41s |

  All jobs had zero failures. The two non-Linux skips are the existing
  Linux-only native allocation measurement cases. Linux and Windows exceeded
  the preferred ten-minute target; every job stayed within twenty minutes.
  Runtime cache hits do not establish cold-runtime build performance.
  Ordinary row/composite conversion, nested/generic mapping declarations,
  unsafe native-address typed results, automatic derived families and full
  platform/version parity remain open.

- 2026-09-25 — Corrected repeated domain validation during raw datum reads.
  PostgreSQL documents conversion-time domain checks, historical values retained
  by `ADD CHECK ... NOT VALID`, and domain-typed NULL from an outer join even
  when the domain is NOT NULL. The pgrx datum readers decode existing storage
  without assigning it back to the domain. Ankus's raw read, format, copy and
  array-extraction dispatcher previously passed stored values through its
  assignment helper and therefore repeated CHECK/NOT NULL validation.

  The native dispatcher now validates the operation, raw envelope, live catalog
  type and source/destination owners before accessing storage directly. Explicit
  parameter/output and mapped-array assignment paths retain their domain checks.
  Formatting still invokes the selected output function; user converters retain
  their own behavior. No public API, transport layout or serializer changed.

  The new tests first ran against unchanged production on Linux x64/PostgreSQL
  18.6: 17 failures and five passing controls in 59.048s. Seven cases observed
  unwanted nontransactional CHECK effects, seven rejected historical values with
  `23514`, and three rejected outer-join NULL with `23502`. After the correction,
  all 381 selected backend cases pass with zero failures/skips in 64.048s,
  including the 22 new cases and 359 existing regressions. The affected direct
  lifetime/mapping scope passes 56 tests with zero failures/skips in 960ms.

  | Requirement | Named evidence |
  | --- | --- |
  | Existing reads avoid CHECK execution | `StoredDomainAccessDoesNotRepeatCheckEffects`: all four native access operations, nested mapped readers, exact values/OIDs/shape, whole NULL versus present all-NULL arrays and unchanged nontransactional sequence state |
  | Historical values and independent copies | `StoredDomainValuesRemainReadableAfterNotValidConstraint`: values captured before rejecting CHECKs remain readable; all 4,096 copied array cells survive source-result disposal and table deletion, with old handles rejected after owner disposal/reset |
  | Domain-typed NULL | `OuterJoinDomainNullIsReadableWithoutReassignment`: real outer-join NULL retains NOT NULL domain identity through nullable reads, formatting and copies without invoking the mapped reader |
  | Assignment remains constrained | `RawAndMappedAssignmentsStillValidateCurrentDomains`: raw/mapped parameters, generated outputs and mapped arrays retain exact CHECK/NOT NULL diagnostics, including framework and writer-produced NULL, untouched target sequences, valid retries and same-backend recovery |
  | Deleted catalog identity still fails | `DroppedTypedNullStillRequiresALiveCatalogType`: actual captured typed NULL rejects native read/format/copy after its temporary domain is dropped, with exact OID diagnostics and same-session recovery |

  The raw-values and arrays guides and source XML comments document the timing.
  Independent source and assertion reviews found no unresolved selected-scope
  issue. Plain `dotnet test` passes all 6,131 tests with zero failures/skips in
  265.051s on Linux x64/PostgreSQL 18.6. The non-incremental Release build passes
  in 17.42s with zero warnings/errors. API generation and freshness pass for
  147 pages/1,429 members; `pnpm check` reports zero errors/warnings/hints and
  `pnpm build` produces 184 pages. Native publication and Release/documentation
  builds ran sequentially. Commit `caf6887` passes
  [full CI](https://github.com/willibrandon/ankus/actions/runs/36115969341) and
  [documentation deployment](https://github.com/willibrandon/ankus/actions/runs/36115969361).
  Every platform ran the complete suite against a real PostgreSQL server:

  | Platform | PostgreSQL | Passed | Skipped | Platform job |
  |---|---|---:|---:|---|
  | Linux x64 | 18.6 | 6,131 | 0 | 10m30s |
  | macOS ARM64 | 18.6 | 6,129 | 2 | 10m03s |
  | Windows x64 | 17.11 | 6,129 | 2 | 15m52s |

  All jobs had zero failures. The two non-Linux skips remain the existing
  Linux-only native allocation measurements. Each job exceeded the preferred
  ten-minute target and stayed within twenty minutes. These runs do not establish
  cold-runtime build performance.
  Private malformed request guards receive source review only; no forwarding
  shim, empirical mutation or coverage percentage is claimed. Raw composite
  layout provenance and the remaining full-port requirements remain open.

- 2026-09-25 — Enabled registered scalar and one-layer array readers for
  `PgFunctions.DangerousCall<T>` results. As with pgrx's direct-function-call
  helper, the requested managed type selects the reader; no independent catalog
  result declaration is discovered. The caller retains responsibility for the
  address, ABI, actual SQL result type and native layout/pointee validity.

  The managed dispatcher requires read capability before lookup, allocation or
  invocation, captures the current nominal type, and reuses the guarded raw call
  under a temporary child of the callback context. Reading completes before
  deterministic cleanup. Captured identities are never relabeled after a call;
  current mapping identity is checked before conversion, including NULL. Existing
  raw argument ownership, assignment checks, native error guards and ordinary
  result paths remain unchanged. No native ABI, serializer, registration rule or
  public API was added.

  The final direct scope passes 92 tests with zero failures/skips in 944ms,
  including 18 new cases and 74 regressions. The selected real backend scope
  passes 164 tests with zero failures/skips in 57.598s on Linux x64/PostgreSQL
  18.6, including 17 new cases and 147 regressions. The initial backend attempt
  stopped during publication on CA2219 in two fixture assertions inside `finally`;
  no PostgreSQL test body executed. Moving those checks into ordinary flow while
  preserving primary errors fixed the fixture without suppressing warnings.

  | Requirement | Named evidence |
  | --- | --- |
  | Requested scalar/array readers and exact values | `NativeResultsSelectDeclaredReadersAndPreserveArguments`, `MappedNativeScalarsSelectRequestedReadersAndPreserveNull`, `MappedNativeVectorsSelectExactElementReaders`, `MappedNativeArrayShapesRemainExact`: distinct aliases, zero/extrema, true array identity, NULL cells, rank and lower bounds |
  | Lazy factories and capability preflight | `MappedNativeNullAndEmptyResultsBypassLazyFactories`, `MappedNativeArraysRequireReadersBeforeConversion`: whole NULL, empty and all-NULL arrays bypass lazy factories; writer-only targets reject; direct tests pin rejection before lookup/owner/invocation |
  | Native ownership and error recovery | `MappedNativeTextResultsOutliveSourceAndOperationOwners`, `MappedNativeReferenceArraysOutliveTheirOriginalStorage`, `MappedNativeTextReaderErrorsReleaseOwnersAndRecover`, `MappedNativeArrayReaderErrorsReleaseEveryCapturedElement`: complete Unicode/TOAST values, input preservation, expired captured handles, exact diagnostics and same-backend recovery |
  | Completed native effects survive caught conversion failures | `MappedNativeManagedFailuresRetainCompletedCatalogWrites`: exact transactional large-object identities, unchanged preexisting metadata and sentinel bytes, no repeated invocation, valid retries and cleanup restricted to owned objects |
  | Current identities and native call contract | `MappedNativeArrayResultsRefreshCurrentExternalIdentities`, `MappedNativeCallsForwardCollationAndRecoverFromNativeErrors`: actual domain replacement, native collation and division-error recovery; direct tests separately pin during-call identity changes, original captured type, callback-parent selection and ordered primary/cleanup failures |

  Independent source and assertion reviews found no unresolved selected-scope
  issue. Direct tests establish immediate owner-deletion wiring; later backend
  capture checks establish post-callback expiry. Existing catalog write-then-error
  and native-pointer error tests provide compositional guard evidence, not a new
  pointer target that writes and then raises. No empirical mutation or coverage
  percentage is claimed. README, source XML and the function-call, raw-value and
  array guides document the capability and its unsafe caller obligations.
  Plain `dotnet test` passes all 6,166 tests with zero failures/skips in 244.676s
  on Linux x64/PostgreSQL 18.6. The non-incremental Release build passes in 18.03s
  with zero warnings/errors. API generation and freshness pass for 147 pages/
  1,429 members; `pnpm check` reports zero errors/warnings/hints and `pnpm build`
  produces 184 pages. Native publication and Release/documentation builds ran
  sequentially. Commit `06398c0` passes
  [documentation deployment](https://github.com/willibrandon/ankus/actions/runs/36118564664).
  Its [full CI](https://github.com/willibrandon/ankus/actions/runs/36118564543)
  also passes. Every platform ran the complete suite against a real PostgreSQL
  server:

  | Platform | PostgreSQL | Passed | Skipped | Platform job |
  |---|---|---:|---:|---|
  | Linux x64 | 18.6 | 6,166 | 0 | 10m50s |
  | macOS ARM64 | 18.6 | 6,164 | 2 | 9m33s |
  | Windows x64 | 17.11 | 6,164 | 2 | 17m52s |

  All jobs had zero failures. The two non-Linux skips are the existing
  Linux-only native allocation measurements. Linux and Windows exceeded the
  preferred ten-minute target; every job stayed within twenty minutes. Runtime
  cache hits do not establish cold-runtime build performance. Ordinary
  row/composite mappings, generic mapped roots, nested mapped arrays, derived
  families, raw composite layout provenance and full-port validation remain open.

- 2026-09-25 — Implemented generated equality, ordering and hashing for readable
  manual `PgDatumType` scalar mappings. pgrx's `HexInt` example combines manual
  datum readers/writers and a declared SQL type provider with all three derives.
  Ankus now accepts the exact readable CLR mapping as the derived input, permits
  reader-only views, and places owned helpers, operators and classes after the
  completed declared provider. Derive-only modules emit the native mapped-input
  support. External SQL types remain external; generated fixed-schema helpers
  and operators make their extension nonrelocatable. The generator preserves
  existing interface algorithms, SQL controls, collision diagnostics and
  ordinary shell-capable function ordering. Documented converter obligations
  include detached reads and stable equality-compatible logical keys.

  The affected generator scope passes 306 tests with zero failures/skips in
  3.826s, including 31 new cases and 275 regressions. The first run's single
  failure was an alias-test source rename that changed `int.MinValue` and
  `int.MaxValue`; correcting the fixture required no production change.
  Independent generator source/assertion review found no unresolved issue.
  The affected PostgreSQL 18.6/Linux x64 Native AOT backend/package scope passes
  all 95 cases with zero failures/skips in 94.492s, including 25 new cases and
  70 existing mapping/operator/SQL-control regressions. The first backend run
  exposed a test fixture that read `PgDatum.IsNull` to check lifetime; the
  corrected fixture checks `DangerousGetBits()` without dereferencing native
  storage. Production code was unchanged. The tests execute real default B-tree
  and hash indexes, all five B-tree strategies, both scan directions, duplicate
  and collision handling, grouping, joins, failed index operations and
  same-backend recovery, plus a fresh backend using a committed hash index.
  Helper-only Native AOT packages prove owned-object relocation/reinstallation,
  preserved shadow objects, external type ownership, SQL controls and normal
  PostgreSQL collision rollback. Plain `dotnet test` passes all 6,222 tests with
  zero failures/skips in 284.423s on Linux x64/PostgreSQL 18.6. The
  non-incremental Release build passes in 16.25s with zero warnings/errors.
  API generation and freshness pass for 147 pages and 1,429 members; `pnpm check`
  reports zero errors/warnings/hints and `pnpm build` produces 184 pages.
  Commit `86c6de3` passes [documentation build/deployment](https://github.com/willibrandon/ankus/actions/runs/36127621131)
  and [full CI](https://github.com/willibrandon/ankus/actions/runs/36127621091).
  Every platform ran the complete suite against a real PostgreSQL server:

  | Platform | PostgreSQL | Passed | Skipped | Platform job |
  |---|---|---:|---:|---|
  | Linux x64 | 18.6 | 6,222 | 0 | 9m25s |
  | macOS ARM64 | 18.6 | 6,220 | 2 | 10m26s |
  | Windows x64 | 17.11 | 6,220 | 2 | 18m38s |

  All jobs had zero failures. The two non-Linux skips are the existing
  Linux-only native allocation measurements. macOS and Windows exceeded the
  preferred ten-minute target; every job stayed within the then-current
  twenty-minute timeout. Runtime cache hits do not establish a cold-runtime
  build baseline. Accessible non-generic nested CLR mappings already work;
  generic mapped roots, nested mapped arrays, ordinary row/composite
  conversions and full-port validation remain open.

- 2026-09-25 — Added finite closed constructions for type-level `PgDatumType`
  declarations. Local generic definitions are templates; only exact fully
  constructed roots selected by supported function/aggregate signatures or
  managed `PgSqlTypeProvider` type arguments register. Constructed containing
  types preserve their full CLR identity. Each selected construction retains
  the declaration's fixed SQL name/schema/origin and requires an exact reader
  and/or writer interface on its already closed converter. No open registration,
  inferred converter construction, runtime reflection or native generic export
  is introduced. Managed generic arguments used only as tags do not acquire
  unrelated SQL provider dependencies. Owned constructions each require an
  exact provider, though they may share a completed SQL type block. Readable
  selected constructions can emit finite derived operator families; duplicate
  fixed-SQL families are rejected before generating artifacts.

  Generator tests cover two independent closed views of one SQL type,
  constructed containing identities, unused templates, wrong interfaces and
  conversion directions, exact owned providers, completed ordering and derived
  SQL collisions. Final review found and corrected a filter that also skipped
  invalid, unmapped generic derives; a regression now preserves `ANKUS018`.
  Another test proves exact managed-provider-only roots register without a
  callback. The affected generator scope passes 165 cases with zero
  failures/skips in 3.352s. Published Native AOT/PostgreSQL 18.6 tests on Linux x64 pass
  four focused cases with zero failures/skips: exact int/long readers and
  writers, per-construction lazy factories, present zero versus SQL NULL,
  array bounds/NULL cells/type, nested raw and typed SPI reads, expired native
  owners, owned diagnostics and same-backend recovery. The owned manual domain
  package test passes one focused relocation/drop/reinstall case with its exact
  closed managed provider and unchanged shadow objects.

  The first complete local run passed 6,234 tests. After the diagnostic correction
  and two additional cases, plain `dotnet test` passes all 6,236 tests with zero
  failures/skips in 281.680s on Linux x64/PostgreSQL 18.6. Static assertion and
  pseudo-mutation review is Strong for the finite fixed-SQL contract; no
  empirical mutation or coverage result is claimed. The non-incremental Release
  build passes in 18.23s with zero warnings/errors. API generation and freshness
  pass for 147 pages/1,429 members; `pnpm check` reports zero errors, warnings or
  hints and `pnpm build` produces 184 pages. Commit `e512065` passes
  [full CI](https://github.com/willibrandon/ankus/actions/runs/36131126510) and
  [documentation build/deployment](https://github.com/willibrandon/ankus/actions/runs/36131126618).
  Each platform executed the complete suite against a real PostgreSQL server:

  | Platform | PostgreSQL | Passed | Skipped | Platform job |
  |---|---|---:|---:|---|
  | Linux x64 | 18.6 | 6,236 | 0 | 10m20s |
  | macOS ARM64 | 18.6 | 6,234 | 2 | 11m07s |
  | Windows x64 | 17.11 | 6,234 | 2 | 18m08s |

  All jobs had zero failures. The two non-Linux skips are the existing Linux-only
  native allocation measurements. All platform jobs exceeded the preferred
  ten-minute target but stayed within their limits; Windows used the new
  25-minute allowance. Runtime cache hits do not establish cold-runtime build
  performance, and the full supported-major/macOS x64 matrix remains unverified.

  This remains a fixed-metadata subset of generic mapping parity. A root used
  only inside an arbitrary raw method body needs another supported registration
  route; different SQL identities per construction, open converter templates,
  nested mapped SQL containers, ordinary row/composite conversion, broader
  metadata and full PostgreSQL/platform validation remain open. The previous
  complete Windows job took 18m38s under a twenty-minute limit, so this
  milestone gives Windows 25 minutes while Linux and macOS retain 20 minutes.

- 2026-09-25 — Added explicit type-owned closed mapping declarations through
  `PgDatumType(typeof(ClosedType), name, converter)` and the `ManagedType`
  property. Multiple exact declarations on one generic type can assign distinct
  SQL names, schemas, origins and already closed converters, matching pgrx's
  concrete generic SQL-metadata model. Exact declarations win over one optional
  default regardless of attribute order. Null/open/unrelated targets, duplicate
  defaults or exact targets, unlisted constructions without a default and
  invalid selected converters fail with `ANKUS019` before any artifacts are
  generated. Targets must be closed constructions of the annotated definition,
  including constructed containing types.

  Explicit local targets become finite registration roots even when used only
  inside raw/SPI method bodies. Referenced declarations retain signature or
  exact-provider discovery. Owned roots retain independent exact provider
  dependencies; distinct SQL identities can generate distinct derived helpers.
  Registration remains lazy and does not resolve backend OIDs or reflect over
  converters. The initial affected generator scope passes 152 cases with zero
  failures/skips in 3.155s, including raw-only roots, independent scalar/array
  SQL signatures, fallback order, nested identity, rejected declarations,
  referenced metadata, exact providers, derived helpers and reused-driver
  invalidation. Initial backend fixture publication stopped on the enforced
  static-field naming rule; the fixture counters now use documented properties.
  The focused Native AOT/PostgreSQL 18.6 scope on Linux x64 passes all 13 cases
  with zero failures/skips in 82.235s, including eight new explicit cases, four
  existing generic cases and the enhanced owned package lifecycle. Direct
  converter results preserve int4/int8 extrema and every int8 bit; independent
  SQL overloads, rank-two arrays with non-one bounds, NULL cells and empty/all-NULL
  arrays retain their exact types. A metadata-only int2 root executes without a
  signature/provider. Wrong sibling OIDs are rejected before factory invocation
  for present and NULL values; owned errors, expired handles and same-backend
  recovery remain intact. Both owned generic SQL identities preserve exact
  extension ownership through relocation/drop/reinstall, with fresh OIDs after
  reinstall and untouched shadow objects.

  Final generator review adds independent schema/ownership assertions and all
  equality/B-tree/hash branches for distinct closed SQL identities. The broader
  affected generator scope passes 183 cases in 3.015s, and the strengthened
  explicit scope passes 18 in 1.769s, all with zero failures/skips. Static
  assertion and pseudo-mutation review is Strong for this declaration contract;
  no empirical mutation or measured coverage claim is made. Plain `dotnet test`
  passes all 6,262 tests with zero failures/skips in 287.056s on Linux x64/
  PostgreSQL 18.6. The non-incremental Release build passes in 17.88s with zero
  warnings/errors. API generation and freshness pass for 147 pages/1,431 members;
  `pnpm check` reports zero errors, warnings or hints and `pnpm build` produces
  184 pages. Commit `3db9d68` passes
  [full CI](https://github.com/willibrandon/ankus/actions/runs/36133461545) and
  [documentation build/deployment](https://github.com/willibrandon/ankus/actions/runs/36133461338).
  Each platform executed the complete suite against a real PostgreSQL server:

  | Platform | PostgreSQL | Passed | Skipped | Platform job |
  |---|---|---:|---:|---|
  | Linux x64 | 18.6 | 6,262 | 0 | 10m41s |
  | macOS ARM64 | 18.6 | 6,260 | 2 | 10m38s |
  | Windows x64 | 17.11 | 6,260 | 2 | 17m58s |

  All jobs had zero failures. The two non-Linux skips remain the existing
  Linux-only native allocation measurements. Windows stayed within its new
  25-minute allowance; all platforms still exceed the preferred ten-minute
  feedback target. Runtime cache hits do not establish cold-build performance.

  Open converter templates, asymmetric argument/result SQL metadata,
  const-generic typmod forms, nested mapped SQL containers, ordinary
  row/composite conversion and complete PostgreSQL/platform validation remain
  full-port requirements.

- 2026-09-25 — Added compile-time inference for open datum converter definitions
  such as `typeof(Converter<>)`. Exact reader/writer interface patterns determine
  one finite closed factory for each selected mapped root, including inherited
  patterns, reversed parameter positions, constructed containing types and
  compatible partial reader/writer assignments. Missing or ambiguous parameters
  fail with `ANKUS019`; constraints do not choose among ambiguous candidates.
  C# declaration binding validates the unique construction, including containing
  constraints, dependent parameters, nullable constraints and required-member
  `new()` rules, before any generated artifacts. Its private validation tree is
  never emitted. The runtime registry and native error boundary remain unchanged.

  Generator review found a nullable annotation loss in generated mapped names.
  Generated C# now retains inner annotations, while callback NULL handling keeps
  the outer root non-nullable. Every selected annotation variant is constraint
  checked before equivalent CLR registrations are combined. Explicit nullable
  interface variables contribute their annotation without forcing an invalid
  nullable converter argument. Portable cases also verify invalid constructors,
  required members, referenced metadata, provider-only roots, derived families
  and reused-driver invalidation.

  Focused published Native AOT tests pass six cases with zero failures/skips in
  60.309s on Linux x64/PostgreSQL 18.6. A constrained generic math converter
  preserves every int4/int8 bit through independent reader and writer values,
  extrema, lazy closed identities, NULL and rank-two arrays with non-one bounds.
  Raw-only int2 and reversed nested converter roots execute without callback
  discovery. Present/NULL sibling OIDs fail before any converter construction;
  borrowed handles expire and owned reader/writer diagnostics preserve
  same-backend recovery. The initial two OID tests expected the wrong SQLSTATE;
  corrected assertions pin the established `38000` contract and exact OID
  message. The final affected generator scope passes 207 cases with zero
  failures/skips in 3.250s. Static assertion and public-outcome pseudo-mutation
  review is Strong for this finite contract; no empirical mutation or coverage
  claim is made. Plain `dotnet test` passes all 6,317 tests with zero
  failures/skips in 286.276s on Linux x64/PostgreSQL 18.6. The non-incremental
  Release build passes in 10.04s with zero warnings/errors. API generation and
  freshness pass for 147 pages/1,431 members; `pnpm check` reports zero errors,
  warnings or hints and `pnpm build` produces 184 pages. Commit `bffacbb` passes
  [full CI](https://github.com/willibrandon/ankus/actions/runs/36137204278) and
  [documentation build/deployment](https://github.com/willibrandon/ankus/actions/runs/36137204255).
  Each platform executed the complete suite against a real PostgreSQL server:

  | Platform | PostgreSQL | Passed | Skipped | Platform job |
  |---|---|---:|---:|---|
  | Linux x64 | 18.6 | 6,317 | 0 | 10m29s |
  | macOS ARM64 | 18.6 | 6,315 | 2 | 11m03s |
  | Windows x64 | 17.11 | 6,315 | 2 | 19m23s |

  All jobs had zero failures. The two non-Linux skips remain the existing
  Linux-only native allocation measurements. Windows finished only 37 seconds
  below its former twenty-minute limit and used the new 25-minute allowance.
  The preferred ten-minute feedback target remains unmet. All runtime jobs
  restored cached artifacts and skipped rebuilding the runtime; these timings
  do not establish cold-runtime build performance or the full version matrix.

  Asymmetric argument/result SQL metadata, const-generic typmod forms, nested
  mapped SQL containers, ordinary row/composite conversion and the complete
  PostgreSQL/platform matrix remain full-port requirements.

- 2026-09-25 — Corrected the mapping parity tracker after reviewing pgrx's
  explicit nested-array contract. In the read-only reference at `70383e8`,
  `array_argument_sql` and `array_return_sql` reject an existing array mapping
  with `NestedArray`; `nested_vec_arrays_fail_fast`,
  `nested_numeric_arrays_fail_fast`, and `nested_composite_arrays_fail_fast`
  pin this behavior. See the
  [reference metadata implementation and tests](https://github.com/pgcentralfoundation/pgrx/blob/70383e884582d1bcc7cd681d10886b995a2830cb/pgrx-sql-entity-graph/src/metadata/sql_translatable.rs).
  Earlier entries incorrectly listed nested mapped arrays as unfinished port
  implementation. Ankus already rejects those generated signatures through
  `DatumMappingsRejectUnsupportedContainers`, with `ANKUS019` and no partial
  artifacts. One `PgArray<T>` retains native multidimensional shape, bounds and
  NULL cells; it is distinct from a nested managed array container.

  This documentation correction does not remove ordinary detached row/composite
  conversion, custom mapped container contracts, broader directional/typmod
  metadata or full platform/version validation from the remaining scope.
  Existing generator and backend coverage ran in `bffacbb`'s complete local suite
  (6,317 passed, zero failures/skips on Linux x64/PostgreSQL 18.6). No runtime or
  generator behavior changed in this audit. `pnpm check` reports zero errors,
  warnings or hints; `pnpm build` regenerates the 147-page/1,431-member API and
  builds 184 pages. The unchanged code retains the preceding 10.04s Release
  build with zero warnings/errors and the complete hosted evidence recorded
  for `bffacbb` above.

  The documentation-only successor `ae228be` also passes
  [full CI](https://github.com/willibrandon/ankus/actions/runs/36139710757) and
  [documentation build/deployment](https://github.com/willibrandon/ankus/actions/runs/36139710686).
  Linux x64/PostgreSQL 18.6 passes 6,317 tests with zero failures/skips in a
  10m59s job; macOS ARM64/PostgreSQL 18.6 passes 6,315 with two existing
  Linux-only allocation skips in 11m08s; Windows x64/PostgreSQL 17.11 passes
  6,315 with the same two skips in 20m03s. The Windows job now exceeds its
  former twenty-minute limit and succeeds within the authorized 25 minutes.
  These cached-runtime jobs do not establish cold-build performance.

- 2026-09-25 — Implemented explicit mapped range bounds through `PgRangeType`
  on value types carrying `PgDatumType`. Default and exact closed generic
  declarations select finite roots; scalar readers/writers and inferred
  converter templates are shared by finite bounds. A range keeps independent
  SQL identity, schema and ownership. Its owned provider completes after the
  owned scalar provider, including relocation and reinstall. Invalid range
  declarations report `ANKUS020` before generated artifacts.

  Native deserialization copies finite raw bounds into checked temporary owners.
  Empty and infinite bounds never call a scalar converter. Whole SQL NULL still
  checks current range/subtype identity. Construction validates exact raw bound
  envelopes, applies domain checks on assignment, invokes version-correct
  `make_range`, and copies the completed value before temporary cleanup. A
  finite writer returning SQL NULL is rejected. Native operations accept only
  the established range signatures, using catalog subtype identities and raw
  owned results for mapped range-valued operations. PostgreSQL ERROR remains
  inside the existing native guard; no managed reflection or runtime code
  generation is added.

  | Requirement | Concrete evidence |
  |---|---|
  | Finite metadata, SQL signatures and dependencies | `DatumRangesPreserveScalarSetTableAndArrayContracts`, `DatumRangesSelectExactGenericRootsAndConverterTemplates`, `DatumRangesOrderIndependentOwnedProviders`, `DatumRangesCompileAggregateRolesAndOperators`, `DatumRangesSelectReferencedMetadata`, `DatumRangesInvalidateMetadataInReusedDriver` compile generated C# and compare complete SQL contracts; negative cases reject missing, duplicate and invalid declarations, providers, directions and overrides |
  | Detached values and native boundary validation | `RangeRegistrationAndConstructionRemainBackendFree`, `RangeReadsPreserveFlagsValuesAndTemporaryLifetimes`, `RangeReadsRejectMalformedNativeFlags`, `RangeNullChecksCurrentSubtypeAndCapturedRangeIdentity`, `RangeWritesPreserveBoundEnvelopesAndFinalOwner` verify exact values/flags, generation expiry, allocator release and current identities |
  | NULL, empty, infinite and independently converted finite values | `MappedRangesConvertOnlyFiniteBoundsAndShareScalarFactory` checks independent native/logical values, canonical bounds and exact lazy reader/writer counts |
  | Native range operations and custom domain semantics | `MappedRangesExecuteEveryNativeOperation` checks all predicates and range results, including containment boundaries and disjoint errors; `MappedDomainRangesKeepIdentityAndCheckOnlyAssignments` verifies custom input/output, predicates, union, malformed input and read-side CHECK nonexecution versus write-side enforcement |
  | Native ownership and variable-size bounds | `MappedRangesPreserveSpiOwnershipAndArrayShape` verifies detached raw/typed SPI results and shaped NULL/empty arrays; `MappedRangesPreserveVariableLengthBoundsAndBorrowedWriters` checks large Unicode text bounds and continued caller ownership of returned writer aliases |
  | Owned diagnostics and recovery | `MappedRangesRejectWrongRangeAndSubtypeBeforeConstruction` checks present and NULL mismatches before construction; `MappedRangeErrorsCleanUpAndPreserveDiagnostics` checks SQLSTATE/message/detail/hint, finite NULL writer rejection, expired handles and same-backend recovery |
  | SQL lifecycle and stale parameters | `DatumMappingPackageRelocatesAndReinstallsWithCurrentTypeIdentity` now includes an owned domain range and its array identity, relocation, drop/reinstall, changed OIDs, stale parameter rejection and untouched shadow types |

  Focused checks pass 33 generator cases in 1.945s, 31 runtime/range cases in
  1.025s, and nine published Native AOT backend/lifecycle cases in 76.418s, all
  with zero failures/skips on Linux x64/PostgreSQL 18.6. The existing 96-case
  built-in range backend scope also passes after the native dispatch change.
  Initial runtime fixture failures exposed missing callback/generation responses;
  the fixture now models deletion independently. The lifecycle inventory was
  corrected to distinguish generated C callbacks from PostgreSQL's overloaded
  range constructors. Static assertion and public-outcome pseudo-mutation
  review is Strong for this bounded contract; no empirical mutation or measured
  coverage claim is made. Plain `dotnet test` passes all 6,371 tests with zero
  failures/skips in 285.539s on Linux x64/PostgreSQL 18.6. The non-incremental
  Release build passes in 12.76s with zero warnings/errors. API generation and
  freshness pass for 148 pages/1,437 members; `pnpm check` reports zero errors,
  warnings or hints and `pnpm build` produces 185 pages. Commit `4aef0ee` passes
  [full CI](https://github.com/willibrandon/ankus/actions/runs/36143177033) and
  [documentation build/deployment](https://github.com/willibrandon/ankus/actions/runs/36143177185).
  Each platform executed the complete suite against a real PostgreSQL server:

  | Platform | PostgreSQL | Passed | Skipped | Platform job |
  |---|---|---:|---:|---|
  | Linux x64 | 18.6 | 6,371 | 0 | 9m11s |
  | macOS ARM64 | 18.6 | 6,369 | 2 | 11m24s |
  | Windows x64 | 17.11 | 6,369 | 2 | 18m57s |

  All jobs had zero failures. The two non-Linux skips remain the existing
  Linux-only native allocation measurements. Windows completed within its
  authorized 25-minute allowance. All runtime preparation jobs restored cached
  artifacts, so these timings do not establish cold-runtime build performance.

  Reference-type bounds, direct PgType/PgEnum range derivation, multiranges,
  range JSON, ordinary detached row/composite mapping, broader directional/typmod
  metadata and the complete PostgreSQL/platform matrix remain full-port work.

- 2026-09-25 — Ported the public pgrx range example to
  `samples/Ankus.Examples.Ranges`. Its nine ordinary C# functions cover bounded,
  lower/upper-unbounded, inclusive, fully unbounded and empty ranges plus detached
  representation comparison. The sample declares immutable, parallel-safe SQL
  functions and ships a runnable project with constructor and storage examples.
  Solution discovery, integration project references and Native AOT sample
  publishing include it.

  `RangeSampleRelocatesAndReinstalls` executes all nine functions with independent
  expected SQL values, strict NULL behavior, equal ends, negative bounds and
  integer extremes. It checks exact argument/result OIDs, function options,
  extension ownership and stable identities on relocation versus fresh identities
  on reinstall. All 101 values from pgrx's stored-range sequence are checked after
  relocation and after extension removal; their caller-owned table survives.
  `RangeSampleRejectsInvalidBoundsAndRecovers` verifies reversed bounds and both
  inclusive-upper overflow paths with SQLSTATE and same-session recovery.
  These four published backend cases pass with zero failures/skips in 62.660s
  on Linux x64/PostgreSQL 18.6. Plain `dotnet test` passes all 6,375 tests with
  zero failures/skips in 258.642s on that platform/server. The non-incremental
  Release build passes in 11.90s with zero warnings/errors. API generation and
  freshness pass for 148 pages/1,437 members; `pnpm check` reports zero errors,
  warnings or hints and `pnpm build` produces 185 pages. Static assertion and
  public-outcome pseudo-mutation review is Strong for this example contract;
  no empirical mutation or measured coverage claim is made. Commit `2914219`
  passes [full CI](https://github.com/willibrandon/ankus/actions/runs/36145489482)
  and [documentation build/deployment](https://github.com/willibrandon/ankus/actions/runs/36145489500).
  Each platform executed the complete suite against a real PostgreSQL server:

  | Platform | PostgreSQL | Passed | Skipped | Platform job |
  |---|---|---:|---:|---|
  | Linux x64 | 18.6 | 6,375 | 0 | 11m27s |
  | macOS ARM64 | 18.6 | 6,373 | 2 | 11m24s |
  | Windows x64 | 17.11 | 6,373 | 2 | 18m23s |

  All jobs had zero failures. The non-Linux skips remain the existing two
  Linux-only native allocation measurements. Windows completed within its
  25-minute allowance. The preferred ten-minute feedback target remains unmet;
  cached runtime preparation does not establish cold-runtime build performance.

- 2026-09-25 — Added `PgSqlStates`, a named string catalog compatible with the
  existing `PgException`, `PgDiagnostic` and exception-filter APIs. It includes
  every native macro in the union of PostgreSQL 13–18 and 19 beta source
  catalogs: 269 names, 263 distinct values, including all six native aliases.
  All 257 named entries in the pinned pgrx `PgSqlErrorCode` match this union.
  The older `SnapshotTooOld` name is retained; XML comments identify codes
  present only in particular version catalogs. Conditions shared by warning,
  data and routine classes have distinct C# names. Custom strings retain their
  exact values through the existing transport; no enum fallback, runtime
  reflection or code generation is introduced into consumers.

  `eng/Ankus.SqlStates.cs` reads the pinned source tags from a read-only local
  PostgreSQL checkout and regenerates the documented constants. It rejects
  malformed entries, conflicting definitions and generated-name collisions.
  Generation and freshness checks pass; an intentionally stale private output
  is rejected without being replaced. An independent audit compares every
  compiled public constant and its documented macro with separately parsed
  upstream records, including all aliases and exact version differences.
  These catalog checks are source evidence, not execution of every PostgreSQL
  version or every condition that can produce an error.

  Existing Native AOT diagnostic and logging probes now use named codes while
  retaining independent client SQLSTATE assertions. The 37-case published
  backend scope passes with zero failures/skips in 69.938s on Linux x64/
  PostgreSQL 18.6, covering named errors, warnings, native catch filters,
  full diagnostics, encoding, managed unwind, cleanup and same-session recovery.
  The affected runtime scope passes 62 cases with zero failures/skips in 3.464s.
  Plain `dotnet test` passes all 6,375 tests with zero failures/skips in 260.431s
  on Linux x64/PostgreSQL 18.6. The non-incremental Release build passes in
  12.12s with zero warnings/errors. API generation and freshness pass for 149
  pages/1,706 members; `pnpm check` reports zero errors, warnings or hints and
  `pnpm build` produces 186 pages. Static assertion and public-outcome
  pseudo-mutation review is Strong for the catalog and existing diagnostic
  boundaries; no empirical mutation or measured coverage claim is made.
  Commit `b2cb27d` passed Linux x64/PostgreSQL 18.6 (6,375 passed, no skips;
  11m04s) and macOS ARM64/PostgreSQL 18.6 (6,373 passed, two existing Linux-only
  allocation-accounting skips; 10m08s), plus quality, cached runtime preparation
  and [documentation build/deployment](https://github.com/willibrandon/ankus/actions/runs/36147748823).
  The first Windows x64/PostgreSQL 17.11 attempt finished in 18m02s with 6,372
  passed, one failed, and two existing skips. `ShowFatalPreservesTerminalSeverity`
  received a connection-reset exception instead of a client PostgreSQL error;
  the retained server log shows its managed finally notice followed by the
  expected FATAL message and detail, without a related backend crash. The
  [full Windows job rerun](https://github.com/willibrandon/ankus/actions/runs/36147748841)
  passed all 6,373 eligible cases with the same two skips in 15m55s, with
  diagnostic assertions unchanged. The first failure was not a timeout;
  transient wire delivery remains an observation, not a proven root cause.
  Remaining guard/raw APIs and complete PostgreSQL/platform validation remain
  required.

- 2026-09-25 — Implemented the bounded pgrx StringInfo surface as
  `PgStringInfoStream`, an ordinary write-only .NET stream over native PostgreSQL
  storage. Factories accept capacity, raw bytes or strict UTF-8 text; writes
  support bytes, strings, characters and Unicode scalars. Checked copies,
  replacement, growth, reset and explicit lossy display preserve binary/NUL
  contracts. StreamWriter formatting and synchronous completion of async
  write/flush/dispose APIs retain backend-thread requirements.

  The guarded C bridge uses the selected PostgreSQL headers, independently
  tracks the native struct and data owner, and removes registry records on
  context reset/deletion. Partial acquisition frees every acquired native
  chunk and unpublished record. Borrowed pointers retain an explicit context
  generation without assuming palloc headers on stack structs or read-only
  storage. Mutable self-appends resolve their source again after native growth.
  Whole-struct, data and validated C-string transfers consume handles only on
  success. Read-only mutation and unsupported allocator operations are rejected
  before native assertions or chunk-header access.

  Focused runtime and published-backend tests cover exact bytes, UTF-8 failure,
  NUL preservation, native cursor/terminator behavior, context switches,
  stack/unterminated/NULL read-only borrowing, ownership transfers, reset and
  transaction expiry, owned diagnostics and same-session recovery. Test-only
  native interposition exercises struct/data acquisition failures and checks
  record/chunk cleanup; 128 repeated large-buffer cycles separately exercise
  disposal, context reset and data transfer with bounded native retention.
  Ten focused runtime cases pass with zero failures/skips in 0.876s. The focused
  published backend scope passes all 33 cases (27 StringInfo cases and six
  existing allocator-registry cases), with zero failures/skips in 59.464s on
  Linux x64/PostgreSQL 18.6. Initial validation exposed a missing standard C
  limits header in GUC-only emitted bridges and incorrect probe ordering/byte
  length expectations; these are corrected. Native fault probes verify actual
  Bump/Slab rejection as well as controlled acquisition errors. Static assertion
  and public-outcome pseudo-mutation review is Strong for this bounded contract;
  no empirical mutation or measured coverage claim is made.

  Plain `dotnet test` passes all 6,412 tests with zero failures/skips in 290.620s
  on Linux x64/PostgreSQL 18.6, including the final exact native error-detail
  assertion. The non-incremental Release build passes in 12.29s with zero
  warnings/errors. API generation and freshness pass for 150 pages/1,746 members;
  `pnpm check` reports zero errors, warnings or hints and `pnpm build` produces
  188 pages. Hosted CI run 36152696821 subsequently passes at `445a5ec`:
  Linux x64/PostgreSQL 18.6 passes 6,412 tests with no failures/skips in an
  11m54s job; macOS ARM64/PostgreSQL 18.6 passes 6,410 with two existing
  Linux-only allocation-measurement skips in 7m19s; Windows x64/PostgreSQL
  17.11 passes 6,410 with those same two skips in 17m57s. Windows stays within
  its authorized 25-minute allowance. These jobs reuse cached runtime artifacts;
  they do not establish a cold runtime build. Quality and documentation
  deployment run 36152696719 also pass. PostgreSQL lists and the broader
  runtime/tooling/version/platform inventory remain required.

- 2026-09-25 — Implemented the bounded modern and legacy pgrx List surface as
  `PgList<T>` with non-generic factories and ordinary `IList<T>`/`IReadOnlyList<T>`
  operations. Exact integer, OID, transaction-ID and opaque-pointer cells keep
  distinct native tags; transaction-ID lists require PostgreSQL 16 or later.
  Default NIL is backend-independent, while explicitly bound empty lists retain
  context lifetime checks. Native capacity, no-allocation TryAdd, additional-cell
  reserve, insertion/replacement/removal, eager draining, independent copies and
  clones, borrowed and consuming iteration, exclusive native borrowing, raw views
  and transfer preserve the container/pointee ownership distinction.

  The guarded C bridge uses selected-version headers, retains the actual owner
  across ambient context changes and removes registry records during context
  cleanup. NIL is the only empty native representation. Failed initial acquisition
  or growth preserves existing storage and rolls back partial acquisitions. Clear,
  final removal and full drain release header/cells without freeing pointees.
  Safe APIs copy cells instead of exposing references into resizable native storage.

  Ten focused runtime tests pass with zero failures/skips in 0.935s. The expanded
  backend scope passes 70 cases (43 list cases plus 27 StringInfo regressions),
  with zero failures/skips in 60.063s on Linux x64/PostgreSQL 18.6. Independent
  C reads check actual tags and every cell in the 10/1,000-value growth cases.
  Tests cover full retained/removed sequences, NIL and singleton transitions,
  exact bit boundaries, borrowed disposal, wrong tags/owners, pointer-element
  survival, reset/delete and transaction/subtransaction expiry, and native ERROR
  transport with managed finally and same-session recovery. The exact emitted
  bridge is also compiled with controlled header/cell/registry failures and
  actual Bump/Slab rejection. Separate 128-cycle large-buffer cases verify native
  and registry cleanup for disposal, reset, transfer, clear and full drain.

  Static public-outcome and assertion review found and fixed oversized reserve
  on bound NIL and enumeration checks after reaching the end. No empirical
  mutation or measured coverage claim is made. An overlapping local Release
  build invalidated one test-publish attempt; the serialized rerun above passes.
  Plain `dotnet test` passes all 6,465 tests with zero failures/skips in 373.189s
  on Linux x64/PostgreSQL 18.6, including the six existing native allocation
  registry cases. The non-incremental Release build passes in 20.96s with zero
  warnings/errors. API generation and freshness pass for 152 pages/1,780 members;
  `pnpm check` reports zero errors, warnings or hints and `pnpm build` produces
  191 pages. The public list guide, memory-context guide and README describe the
  supported contracts and unsafe caller obligations. Hosted list CI, complete
  PostgreSQL 13–19/platform evidence, and the broader runtime/tooling inventory
  remain required.

- 2026-09-25 — Hosted list milestone `ec6ce3a` passes complete CI run
  [36157519749](https://github.com/willibrandon/ankus/actions/runs/36157519749).
  Linux x64/PostgreSQL 18.6 passes 6,465 tests with zero failures/skips in an
  11m23s test job. macOS ARM64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11
  each pass 6,463 tests with zero failures and two existing Linux-only native
  allocation measurements skipped; their test jobs finish in 12m13s and
  18m57s respectively. All three jobs run the entire suite against a real
  server. Runtime preparation uses cached fork builds, so this is not cold
  runtime-build evidence. Quality and documentation deployment run
  [36157519829](https://github.com/willibrandon/ankus/actions/runs/36157519829)
  also pass. Windows retains its authorized 25-minute timeout. Complete
  PostgreSQL 13–19/platform proof and the remaining port inventory stay open.

- 2026-09-25 — Implemented pgrx `itemptr.rs` as immutable `PgItemPointer` values
  and checked `PgNativeItemPointer` storage. Raw unsigned fields, invalid and
  moved-partition markers, checked getters, exact equality/ordering and invariant
  text preserve PostgreSQL tuple-location semantics. The pgrx sparse UInt64
  codec remains distinct from PostgreSQL's dense index key. Checked decoders
  reject lost bits; explicitly named truncating decoders retain the raw reference
  behavior. Increment/decrement preserve full-field carries and saturated bounds.

  Generated C reads/writes fields through selected-version ItemPointerData
  headers instead of assuming a managed struct layout. `tid` and `tid[]` retain
  their built-in identity; offset-zero values remain present and distinct from
  SQL NULL. Scalar functions, nullable/shaped arrays, SPI, records, sets, raw and
  domain reads, and typed/raw native function-address calls share the conversion.
  Native ownership reuses the allocation registry. Managed ownership bookkeeping
  is created before acquiring native storage. Checked borrows share owner
  invalidation, external stack/interior pointers use an explicit reset generation,
  and clone, disposal and transfer retain exact release obligations.

  Reference review covered pgrx's complete item-pointer helpers and inline index
  encoding witness; PostgreSQL headers for each major version 13–19, native tid
  I/O, comparisons and index encoding; and existing memory/raw/array contracts.
  This source review is not execution evidence for those versions.

  Focused validation passes 15 runtime cases (0.927s), six generated-contract
  cases (2.214s), and 37 published Native AOT backend cases (50.491s), with zero
  failures/skips, on Linux x64/PostgreSQL 18.6. Independent native `tidsend` bytes
  and C reads prove size, block halves, offsets and the actual owner. SQL operators
  verify 81 ordering combinations. Actual heap `ctid`, independently queried
  domain OIDs, stack guards, reset/delete/transaction/subtransaction expiry,
  native ERROR/finally and same-session recovery provide observable boundaries.
  Controlled registry/allocation failures compile the exact emitted bridge;
  4,096 cycles each of free, reset and transfer verify every location, registry
  cleanup and bounded native allocation bytes.

  The first backend run exposed a missing native tid array allowlist entry;
  it was fixed and the complete focused scope above passes. Static assertion
  and public-outcome review strengthened domain identity, failed-transfer retry,
  accepted maximum decoding and endpoint carry checks. No empirical mutation
  or measured coverage claim is made. Plain `dotnet test` passes all 6,523 tests
  with zero failures/skips in 294.973s on Linux x64/PostgreSQL 18.6.
  The tuple-location guide, function type tables and memory guide describe
  validity, encodings and unsafe caller obligations. The getter-only memory
  context `Id` auto-property cleanup preserves the previous readonly-field
  behavior and passes the same full suite. The non-incremental Release build
  passes in 17.81s with zero warnings/errors. API generation and freshness pass
  for 154 pages/1,815 members; `pnpm check` reports zero errors, warnings or hints,
  and `pnpm build` produces 194 pages. Hosted validation, complete PostgreSQL
  13–19/platform proof and the broader port inventory remain required.

- 2026-09-25 — Hosted tuple-location milestone `ec5624d`, including the preceding
  `151bc1d` auto-property cleanup, passes complete CI run
  [36163435123](https://github.com/willibrandon/ankus/actions/runs/36163435123).
  Linux x64/PostgreSQL 18.6 passes 6,523 tests with zero failures/skips in a
  10m58s test job. macOS ARM64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11
  each pass 6,521 tests with zero failures and two existing Linux-only allocation
  measurements skipped, in 12m25s and 19m43s test jobs respectively. Every
  platform runs the entire suite against a real server. Quality and runtime
  preparation pass; documentation deployment
  [36163435184](https://github.com/willibrandon/ankus/actions/runs/36163435184)
  also passes. Windows retains its authorized 25-minute limit. These runs do not
  establish cold runtime-build or complete PostgreSQL 13–19/platform evidence.

- 2026-09-25 — Implemented the bounded pgrx `namespace.rs` and `wrappers.rs`
  surface. `PgQualifiedNameBuilder` preserves exact, ordered components in a
  reusable managed collection and resolves operators through native
  `OpernameGetOprid`. `PgTypes.GetOid` uses `regtypein` for full native type syntax;
  `GetOidByManagedName<T>` supplies the short CLR metadata name explicitly,
  independently of generated SQL type mappings. Lookup preserves exact OIDs,
  search-path order, implicit catalog priority, temporary-namespace behavior,
  schema permissions, numeric OID and dash handling, and native parser errors.
  No identity is cached across DDL or search-path changes.

  Managed text validation rejects embedded zero characters and malformed UTF-16
  before dispatch or builder mutation. Native lists, nodes and server-encoded
  names live in the existing guarded temporary operation context. Native errors
  unwind below managed frames, and input/result/diagnostic ownership is released
  on failure and retry. Selected-version source review covered PostgreSQL 13–19
  lookup implementations, including parser-signature changes; that review is
  not execution evidence for those versions.

  Six focused runtime tests pass with zero failures/skips in 1.055s. The expanded
  published Native AOT scope passes 35 backend cases with zero failures/skips
  in 62.264s on Linux x64/PostgreSQL 18.6. Independent catalog OIDs, exact native
  parser results, qualified-name counts, schema USAGE denial and grant/retry,
  domain argument identity, reused builders across DROP/CREATE and path changes,
  and native catch/finally plus same-session recovery establish observable
  contracts. LATIN1 tests verify a 63-server-byte accented namespace, exact type
  and operator identity, and rejected unrepresentable names. After warmup,
  128 repeated success/missing/error cycles check every OID, complete 32-KiB
  owned diagnostics, zero retained operation contexts and bounded transaction
  allocation growth.

  Validation exposed and fixed conditional emission of the shared native C-string
  writer and explicit OID typing in the test client. Static assertion and
  public-outcome review added permission retry, domain signature, encoding and
  generic-name cases; no empirical mutation or measured coverage claim is made.
  The catalog lookup guide, operator guide and README describe exact component
  versus SQL type syntax and name-based managed lookup. Plain `dotnet test`
  passes all 6,564 tests with zero failures/skips in 284.857s on Linux
  x64/PostgreSQL 18.6. The non-incremental Release build passes in 11.25s with
  zero warnings/errors. API generation and freshness pass for 156 pages/1,823
  members; `pnpm check` reports zero errors, warnings or hints, and `pnpm build`
  produces 197 pages. Hosted lookup validation, relation/function catalog and
  node APIs, the complete PostgreSQL/platform matrix, and the broader port
  inventory remain required.

- 2026-09-25 — Hosted lookup milestone `5900d77` passes complete CI run
  [36166435027](https://github.com/willibrandon/ankus/actions/runs/36166435027).
  Linux x64/PostgreSQL 18.6 passes 6,564 tests with zero failures/skips in an
  11m17s test job. macOS ARM64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11
  each pass 6,562 tests with zero failures and the two existing Linux-only
  allocation measurements skipped, in 12m55s and 24m17s jobs respectively.
  Each platform executes the entire suite against a real server. Quality and
  runtime preparation pass; documentation deployment
  [36166435119](https://github.com/willibrandon/ankus/actions/runs/36166435119)
  also passes. Windows completed only 43 seconds below its 25-minute cap, so
  its next CI run uses the user's authorized 30-minute timeout. Linux and macOS
  retain 20 minutes. This is not cold runtime-build or full version-matrix proof.

- 2026-09-25 — Added typed built-in OID constants and tagged OID helpers from
  pgrx's complete PostgreSQL 13–18 and 19 beta catalogs. The reproducible
  `eng/Ankus.Oids.cs` app reads pinned reference files without modifying them.
  `PgBuiltInOid` retains 305 distinct unsigned values; `PgBuiltInOids` preserves
  all 315 native names and each version's membership, including historical
  renames, added multirange/OID8/regdatabase constants and removed names.
  Explicit-version operations work detached; active operations read the selected
  native headers' major version through the existing backend guard. The pgrx
  constant-name heuristic is preserved, including non-object constants, without
  treating membership as proof of catalog existence or object category.

  `PgOid` preserves invalid, custom and built-in tags independently of its raw
  unsigned number. Equality retains the tag, and explicit custom zero remains
  distinct from invalid zero. Built-in conversion distinguishes invalid zero,
  unlisted 32-bit values and oversized unsigned datum words without truncation.
  `ToDatum` preserves pgrx's Invalid-to-NULL versus Custom(0)-to-present-zero
  behavior with exact `oid` type identity and a checked explicit context lifetime.
  Ordinary `uint` SQL transport continues to preserve zero. This port introduces
  no runtime-generated code, unbounded reflection or new NuGet dependency.

  Focused runtime validation passes 26 cases with zero failures/skips in 0.978s.
  Independently captured complete reference-manifest hashes verify every native
  name/value pair for all seven majors; tests also check conversion outcomes,
  all unlisted values through 65,536, native renames, version boundaries,
  immutable snapshots, tagged equality, owned diagnostics and abort restrictions.
  Five published Native AOT backend cases pass with zero failures/skips in
  38.840s on Linux x64/PostgreSQL 18.6. They compare the entire active catalog
  with the actual server major and verify independently queried type, array,
  relation, procedure, access-method, collation, operator-class and operator-family
  OIDs. Native datum results and SPI parameters preserve NULL/zero/maximum values;
  actual context reset rejects expired access, and managed catch/finally plus
  same-session SQL demonstrate recovery. The first backend run exposed a test
  expectation using `true` instead of native Boolean output `t`; the corrected
  complete focused scope passes. Static assertion/public-outcome review makes
  no empirical mutation or measured coverage claim.

  The catalog guide and README document classification, version selection,
  numeric representation and explicit sentinel-to-NULL conversion. Plain
  `dotnet test` passes all 6,595 tests with zero failures/skips in 285.675s on
  Linux x64/PostgreSQL 18.6. The non-incremental Release build passes in 20.16s
  with zero warnings/errors. Catalog regeneration/freshness passes for all 315
  native names; API generation/freshness passes for 161 pages/2,157 members.
  `pnpm check` reports zero errors, warnings or hints, and `pnpm build` produces
  202 pages. Hosted OID evidence, function/relation and node APIs, complete
  PostgreSQL/platform execution, and the broader full-port inventory remain
  required.

- 2026-09-25 — OID milestone `13b3443` passes complete hosted CI
  [36169851187](https://github.com/willibrandon/ankus/actions/runs/36169851187)
  and documentation deployment
  [36169851249](https://github.com/willibrandon/ankus/actions/runs/36169851249).
  Linux x64/PostgreSQL 18.6 passes 6,595 tests with zero failures/skips in an
  11m31s job. macOS ARM64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11
  each pass 6,593 tests with zero failures and the two existing Linux-only
  allocation measurements skipped, in 10m29s and 20m52s jobs respectively.
  Each platform executes the complete suite against a real server; quality and
  runtime preparation also pass. The authorized 30-minute Windows cap allows
  its healthy full suite to finish. This does not establish cold runtime-build
  performance or the remaining complete PostgreSQL/platform matrix.

- 2026-09-25 — Ported every metadata getter and default-expression operation in
  pgrx's `pg_catalog::PgProc`. `PgFunctions.GetInfo` returns an immutable
  `PgFunctionInfo` snapshot or null for a missing OID. Native code briefly pins
  the selected-header `PROCOID` tuple, copies every field into owned transport,
  and releases the pin in a native finally block before returning to managed
  code. Metadata survives callback return, ALTER and DROP without retaining a
  native cache reference. Routine and argument kinds use explicit discriminators;
  existing volatility and parallel-safety enums are reused. Input versus all
  argument types, absent versus empty names, synthesized all-IN modes, nullable
  variadic/binary/configuration fields, exact float4 costs and row estimates,
  source, security/strictness flags and planner support identities are preserved.

  `GetDefaultArguments(context)` parses the captured catalog representation
  into actual native expression trees in an explicit memory owner, without
  evaluating defaults. Repeated calls create independent trees. The returned
  `PgList<nint>` owns its container; nodes remain context-owned after disposal.
  Reset/deletion invalidates checked list access. Native guards contain parser
  errors, release temporary input contexts and unpublished registry records,
  and permit retries. No SQL deparsing substitute or managed node-layout parity
  is claimed; the complete raw node and inheritance surface remains required.

  Focused runtime validation passes 15 cases with zero failures/skips in 1.101s:
  exact copied fields, zero/singleton/multiple argument fallbacks, immutable
  collections, every discriminator and unknown-value rejection, missing OIDs,
  abort restrictions, malformed frames, diagnostic/result cleanup and exact
  default-list requests. Twelve published Native AOT backend cases pass with
  zero failures/skips in 39.568s on Linux x64/PostgreSQL 18.6. Independent
  `pg_proc` SQL compares every metadata field across all routine and argument
  kinds, including domains, Unicode names, variadic/table outputs, planner support
  and C binary metadata. A role denied EXECUTE can inspect a routine without
  invoking it. Snapshots retain original metadata and defaults across ALTER/DROP.
  Selected-header native evaluation checks default order, exact values, Unicode,
  SQL NULL and sequence effects; native errors exercise managed catch/finally
  and same-session recovery. Six controlled native parser/registry failure cases
  prove unpublished-record cleanup, context restoration, owned container versus
  pointee lifetime, exact-value retry and final registry reclamation.

  Initial verification corrected test helper naming and PostgreSQL's string
  JSON representation of OIDs/regproc in the independent SQL expectation.
  Static assertion/public-outcome review added restricted-role inspection and
  native temporary-context cleanup checks; no empirical mutation or measured
  coverage claim is made. README and the catalog/calling-functions guides now
  describe detached snapshots and explicit native default ownership.
  Plain `dotnet test` passes all 6,622 tests with zero failures/skips in 279.992s
  on Linux x64/PostgreSQL 18.6, including the final native input-context cleanup
  assertions. The non-incremental Release build passes in 20.43s with zero
  warnings/errors. API generation and freshness pass for 164 pages/2,192 members;
  `pnpm check` reports zero errors, warnings or hints, and `pnpm build` produces
  205 pages. Hosted evidence for this function-catalog milestone is pending.
  Relation access, complete node APIs, the full PostgreSQL/platform matrix and
  the broader full-port inventory remain required.

- 2026-09-25 — Function-catalog milestone `393ae99` passes complete hosted CI
  [36173100001](https://github.com/willibrandon/ankus/actions/runs/36173100001)
  and documentation deployment
  [36173100000](https://github.com/willibrandon/ankus/actions/runs/36173100000).
  Linux x64/PostgreSQL 18.6 passes 6,622 tests with zero failures/skips in an
  11m51s job. macOS ARM64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11
  each pass 6,620 tests with zero failures and the two existing Linux-only
  allocation measurements skipped, in 10m59s and 19m42s jobs respectively.
  Each platform executes the entire suite against a real server; quality and
  runtime preparation also pass. Windows retains the authorized 30-minute cap.
  These runs do not establish cold runtime-build performance or the remaining
  complete PostgreSQL/platform matrix.

- 2026-09-25 — Ported pgrx's `rel.rs` helpers as `PgRelation` and `PgLockMode`.
  Exact OID/name opens, missing-name lookup, eight lock modes, unsafe no-lock
  access, independent cloning, raw borrowing/adoption and consuming ownership
  transfer retain distinct close obligations. Metadata reads observe the live
  cache entry: identity, names, namespace, all nine kind predicates and exact
  float4 tuple estimates, including pgrx's zero-to-null behavior and minus-one
  sentinel. Tuple descriptors are owned copies with physical dropped slots;
  index enumeration returns independently owned references and unwinds partial
  acquisition. Index-to-heap access and all seven statistics helpers use the
  selected PostgreSQL headers and native APIs.

  Owned references have private native resource owners, retained past the
  guard's internal subtransaction and bounded by the caller's native lifetime.
  Explicit disposal releases the acquired pin and lock. Native resource cleanup
  invalidates checked tokens after PostgreSQL releases pins, without double
  closing; automatic cleanup retains PostgreSQL's transaction lock-transfer
  rules. A failed guard commit releases a published acquisition. Raw adoption
  and transfer become owned only after successful guard completion, so failure
  preserves the external caller's pin. No finalizer calls PostgreSQL.

  `PgRelation` maps to `regclass` across scalar, vector, shaped-array, aggregate,
  SETOF/TABLE, SPI, function-call and explicit datum paths. Generated callbacks
  close provisional arguments/results, including failures; iterator inputs stay
  live through repeated advances and cleanup. Yielding the same input repeatedly
  preserves its iterator owner. Detached rows and composite cells retain only
  exact OIDs, array shape and SQL NULLs, opening references on explicit typed
  reads. Editing cells copies identities before supplied handles close. Failed
  multi-column SPI conversion releases earlier scalar and array acquisitions.
  Ordinary `oid` values remain a distinct managed conversion contract.

  Focused runtime validation passes 48 cases with zero failures/skips in 0.910s.
  The 38 published Native AOT backend cases pass with zero failures/skips in
  60.868s on PostgreSQL 18.6/Linux x64. Native tests
  exercise independent catalog metadata, all lock modes, second-session
  lock timeouts/retries, exact reference-count deltas, owner commit/abort,
  controlled guard-commit failure, failed adoption/transfer, signed statistics,
  restricted-role resolution, Unicode/search paths, partial conversions,
  iterator/aggregate ownership, early LIMIT, explicit portal close, forced
  materialization and same-session recovery. Initial verification corrected an
  anonymous-record projection in the test query and preserved unrelated arrays'
  wrong-type diagnostic before shape validation. Static assertion/public-outcome
  review makes no measured coverage or empirical mutation claim. The final
  statistics probe compares native counters after each individual helper, so
  swapped operations cannot hide behind matching aggregate totals.

  Plain `dotnet test` passes all 6,703 tests with zero failures/skips in 294.622s
  on Linux x64/PostgreSQL 18.6, including the final per-operation counter checks.
  The non-incremental Release build passes in 17.07s with zero warnings/errors.
  API generation and freshness pass for 166 pages/2,239 members; `pnpm check`
  reports zero errors, warnings or hints, and `pnpm build` produces 208 pages.
  The README, new relation guide and related SPI/catalog/set guides document
  the ownership and locking contracts. Hosted evidence for this milestone is
  pending. Raw RelationData/node bindings, complete PostgreSQL/platform evidence
  and the wider full-port inventory remain required.

- 2026-09-25 — Relation milestone `2677e2f` passes complete hosted CI
  [36181839807](https://github.com/willibrandon/ankus/actions/runs/36181839807)
  and documentation deployment
  [36181839751](https://github.com/willibrandon/ankus/actions/runs/36181839751).
  Linux x64/PostgreSQL 18.6 passes 6,703 tests with zero failures/skips in a
  12m22s job. macOS ARM64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11
  each pass 6,701 tests with zero failures and the two existing Linux-only
  allocation measurements skipped, in 10m46s and 20m07s jobs respectively.
  Every platform executes the complete suite against a real server. Quality and
  runtime preparation also pass; the Windows job stays within its authorized
  30-minute cap. Cached runtime preparation does not establish cold runtime-build
  performance or the remaining complete PostgreSQL/platform matrix.

- 2026-09-25 — Centralized `MSTestAnalysisMode=All` beside
  `TreatWarningsAsErrors=true` in `Directory.Build.props` and removed every
  project-local `Recommended` override. All 35 repository projects evaluate to
  `All` with warnings treated as errors. Fixed all eight newly reported
  `MSTEST0026` findings using explicit null/presence assertions followed by exact
  range-bound, parent-context and backend-process identity checks. No suppression
  or severity reduction was introduced. `AGENTS.md` now explicitly preserves
  these standards and restricts `InternalsVisibleTo` to test assemblies.

  Native declaration catalog generation now runs through the
  `Ankus.Build binding-catalogs` command. The file-based automation app invokes
  that command without an assembly reference or access to internal parser/model
  types. The PostgreSQL 13–19 catalogs record native tags, struct/union fields,
  typedefs, enum representations and pgrx's node cast sets from pinned pgrx commit
  `70383e884582d1bcc7cd681d10886b995a2830cb`. Sixteen parser cases cover prefix
  inheritance, typedef aliases, union directionality, legacy Value tags,
  nested callbacks/arrays, native identifier escaping, trivia and invalid input.
  Fresh command output matches all seven catalogs byte for byte, freshness
  checks pass, and missing catalogs or invalid arguments fail explicitly.
  The build-tool test project participates in solution discovery and every
  platform's complete CI suite.

  Focused assertion validation passes 35 tests with zero failures/skips in
  0.891s. Plain `dotnet test` passes all 6,719 tests with zero failures/skips in
  302.013s on Linux x64/PostgreSQL 18.6. The non-incremental Release build passes
  with zero warnings/errors in 23.92s. API generation and freshness pass for
  166 pages/2,239 members; `pnpm check` reports zero errors, warnings or hints,
  and `pnpm build` produces 208 pages. Hosted validation of this change remains
  pending. These catalogs establish declarations only: selected-header physical
  layouts, typed managed node APIs and their backend ownership/casting tests,
  the complete raw FFI and the wider full-port/platform inventory remain required.

  CI timeouts may increase when needed, with an absolute 40-minute maximum per
  job. This authorization is recorded in `AGENTS.md`; current platform test caps
  remain 20 minutes for Linux/macOS and 30 minutes for Windows.

- 2026-09-25 — Investigated the Ubuntu failure at `6006b80` in
  [CI 36185939545](https://github.com/willibrandon/ankus/actions/runs/36185939545).
  Linux x64/PostgreSQL 18.6 passed 6,718 tests and failed one, with no skips:
  `GucPreloadTests.ClientDefaultsAndBackendSettingsRetainSources` failed during
  postmaster startup because another process claimed its reserved loopback port
  after the harness released it. This was a native address-in-use failure, not
  an analyzer finding or a timeout. macOS ARM64/PostgreSQL 18.6 and Windows
  x64/PostgreSQL 17.11 each passed 6,717 tests with zero failures and the two
  existing Linux-only allocation measurements skipped, in 13m09s and 17m40s
  jobs respectively. Quality, runtime preparation and
  [documentation deployment](https://github.com/willibrandon/ankus/actions/runs/36185939499)
  also passed.

  Cluster startup now retries only a confirmed PostgreSQL address-in-use
  diagnostic for its selected loopback port. It allows at most three attempts
  within the original startup deadline, creates fresh data/socket/log paths,
  cleans each failed attempt and retains its native log. Configuration and
  other startup errors still fail immediately; cancellation prevents further
  attempts. Cleanup retains its independent shutdown deadline and cannot stop
  the process that claimed the port. No analyzer setting, suppression or CI
  timeout changed.

  Four regression cases use real competing TCP listeners and PostgreSQL:
  collision recovery with successful SQL and preserved competing ownership;
  three persistent collisions with complete cleanup and retained diagnostics;
  an unrelated configuration failure after a collision without stale-log
  retries; and cancellation at handoff without starting a postmaster. The
  per-invocation callback is internal and uses the existing test-only friend
  assembly; it adds no global hook or production friend assembly. The focused
  run, including the original GUC case and existing cluster tests, passes all
  13 cases with zero failures/skips in 58.765s on Linux x64/PostgreSQL 18.6.

  Plain `dotnet test` passes all 6,723 tests with zero failures/skips in
  263.834s on Linux x64/PostgreSQL 18.6, including the final portable path
  assertion. The non-incremental Release build passes with zero warnings/errors
  in 19.18s. API generation and freshness pass for 166 pages/2,239 members;
  `pnpm check` reports zero errors, warnings or hints, and `pnpm build` produces
  208 pages. The public testing guide and generated API reference document
  retry, deadline and cleanup behavior. Hosted validation of this repair is
  pending. This repairs harness startup only; selected-header node layouts,
  typed managed node APIs, complete raw FFI and the wider full-port/platform
  inventory remain required.

- 2026-09-25 — Port-handoff repair `dce1ede` passes complete hosted
  [CI 36189350482](https://github.com/willibrandon/ankus/actions/runs/36189350482)
  and [documentation deployment](https://github.com/willibrandon/ankus/actions/runs/36189350500).
  Linux x64/PostgreSQL 18.6 passes all 6,723 tests with zero failures/skips in
  a 7m43s job. macOS ARM64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11 each
  pass 6,721 tests with zero failures and the two existing Linux-only allocation
  measurements skipped, in 11m08s and 19m27s jobs. Every platform runs the full
  suite against a real server, including all four port-handoff regressions.
  Quality and runtime preparation also pass. No CI timeout increase was needed;
  the complete PostgreSQL-major/platform matrix remains open.

- 2026-09-25 — Added selected-header native layout observation through the
  internal `Ankus.Build binding-layouts` command. The packaged tool embeds the
  pinned PostgreSQL 13–19 declaration catalogs and matching pgrx include
  manifests; the regeneration command also preserves upstream attribution.
  An installed SDK can compile the probe without a pgrx checkout. The probe
  runs on the build host and rejects a different selected PostgreSQL major.

  It measures every node plus its complete embedded value dependencies, using
  actual C member paths for anonymous structs/unions and array elements. It
  records pointer/C long widths, char signedness, byte order, type sizes and
  alignments, field offsets/sizes/alignments and array element sizes. Flexible
  arrays retain zero inline length and independent element size, including
  enclosing tail padding. Observations must include every expected field and
  native tag exactly once; invalid extents, primitive models, tags, duplicate
  records and incomplete output fail explicitly. Indirect fields do not expand
  the embedded graph. Cyclic typedef arrays fail without recursive overflow.

  The command compiles against PostgreSQL 18.6/Linux x64 headers and measures
  499 values and 3,694 fields: all 483 node types/3,632 node fields plus their
  embedded dependencies. Seventy-five build-tool cases pass with zero
  failures/skips in 0.990s, including an independently compiled C fixture for
  anonymous unions, arrays and flexible tail padding, all seven packaged
  declaration graphs and malformed observation boundaries. The two
  user-reported CA1720 parameter names are corrected without suppression.
  Two installed-SDK integration cases pass in 80.260s against the selected real
  server headers, verifying server version, target primitives and concrete
  native field contracts, plus rejection of a mismatched major before output
  creation. Plain `dotnet test` passes all 6,784 tests with zero failures/skips
  in 285.362s on Linux x64/PostgreSQL 18.6, including the final package checks
  for the upstream license notice beside the installed build tool.

  The final probe measures member addresses within allocated root storage,
  avoiding MSVC's unsupported flexible-array element paths in `offsetof` and
  null-pointer arithmetic. The independent C11 fixture uses Clang on Windows
  with `/W4 /WX`, preserving warning enforcement for standard flexible arrays.
  The selected PostgreSQL header command uses the existing MSVC toolchain.
  Windows/macOS execution of this layout milestone remains pending hosted CI.

  The final non-incremental Release build passes with zero warnings/errors in
  18.61s. Freshness checks pass for all seven catalogs/header manifests and the
  license notice. API generation/freshness remain clean for 166 pages/2,239
  members; `pnpm check` reports zero errors, warnings or hints, and `pnpm build`
  produces 208 pages. Development prerequisites document the standalone C
  compilers used by these tests.

  These observations establish physical layout infrastructure. Managed node
  declarations, SDK compilation integration, checked casting/ownership/backend
  node tests, complete raw FFI and the broader full-port/platform inventory
  remain required. `eng/README.md` documents the command and host-only scope.

- 2026-09-25 — Repaired two failures from selected-header layout
  [CI 36192015621](https://github.com/willibrandon/ankus/actions/runs/36192015621).
  Windows x64/PostgreSQL 17.11 rejected the probe's expression operand to
  MSVC `__alignof`; the emitter now supplies its native type through
  `__typeof__`. The development guide records the Visual Studio 17.9 minimum
  for that compiler feature. The packaged-tool integration test continues to
  compile against real selected headers with MSVC and warnings as errors.

  The macOS ARM64/PostgreSQL 18.6 configuration-after-collision test stopped
  after one handoff, without a corresponding PostgreSQL bind failure in the
  retained logs. Its assertion hid the original exception. The test had a
  second port-allocation race while binding its competing listener after the
  reservation was released. Contention tests now take ownership of the
  already-bound reservation listener before handoff. They still exercise real
  PostgreSQL bind failures, retry limits, isolated cleanup and preservation of
  the competing listener. Attempt-count failures now include the original
  exception, and the configuration case explicitly checks the first native
  collision log. Production startup retains its existing retry policy.

  All four focused handoff cases pass on Linux x64/PostgreSQL 18.6, and all
  75 build-tool tests pass. Plain `dotnet test` passes all 6,784 tests with zero
  failures/skips in 259.547s. The non-incremental Release build passes with
  zero warnings/errors in 17.46s. API generation/freshness pass for 166 pages
  and 2,239 members; `pnpm check` reports zero errors/warnings/hints and
  `pnpm build` produces 208 pages. Hosted verification is pending. No analyzer
  modes, warning severities, suppressions, test skips or CI timeouts changed.
  Typed bindings, complete raw FFI and the remaining full-port/platform inventory
  remain open.

- 2026-09-25 — Repair `947efd5` passes complete hosted
  [CI 36194691946](https://github.com/willibrandon/ankus/actions/runs/36194691946)
  and [documentation deployment](https://github.com/willibrandon/ankus/actions/runs/36194691928).
  Linux x64/PostgreSQL 18.6 passes all 6,784 tests with zero failures/skips in
  an 11m57s job. macOS ARM64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.11
  each pass 6,782 tests with zero failures and the two existing Linux-only
  allocation measurements skipped, in 11m46s and 20m43s jobs. This includes
  the complete port-contention cases and the installed SDK's selected-header
  probe under MSVC. Quality and runtime preparation pass. Windows remains
  within its existing 30-minute timeout; no limit or analyzer changes were needed.

- 2026-09-25 — Generated selected-header native declarations through the SDK.
  The native probe now
  measures named enum widths/signedness and validates every constant's stored
  bits. This accounts for MSVC enum storage that differs from Linux bindgen,
  including high-bit constants represented by signed native storage, without
  accepting truncated values. The internal `binding-sources` command emits
  explicit C# layouts with mutable embedded fields and inline arrays, flexible
  tail accessors, cast-tag contracts and a deterministic companion identity.
  Against PostgreSQL 18.6/Linux x64 headers it measures 499 native values,
  3,694 fields and 84 enums, then emits the managed declarations.

  The narrow build-tool suite passes 113 cases with zero failures/skips in
  2.211s. `CompiledProbeObservesAnonymousValuesAndFlexiblePadding` compiles and
  executes both an independent C fixture and emitted C#, checking native sizes,
  offsets, union overlap, direct array mutation, flexible tails and generated
  tag predicates. `CompiledEnumAndPointerFieldsPreserveNativeValues` verifies
  negative enum values, unsigned high bits, pointer bits and ABI identity.
  `NativeBindingEnumTests` checks selection, completeness, exact constants and
  signed/unsigned 64-bit boundaries. The native compiler now reports its target
  identity, with explicit rejection of a requested runtime that differs from
  the measured target. Generated assemblies also validate their host target,
  pointer width and byte order before use.

  SDK integration builds the declarations as an ordinary Microsoft.NET.Sdk
  companion before extension compilation, using the content-based assembly
  identity and resolved runtime reference. Managed and Native AOT sample builds
  compile all 499 selected declarations with zero warnings/errors. CI quality
  now installs the required PostgreSQL headers; platform builds pass the same
  selected installation to both managed generation and native publication.
  A cold installed-package test exposed reuse of MSBuild's pre-restore project
  evaluation. Restore now uses a distinct evaluation so the first compilation
  sees the restored imports. Companion outputs also participate in MSBuild's
  project-reference protocol, allowing ordinary .NET projects to consume the
  native declarations transitively. Three installed-package integration cases
  pass with zero failures/skips in 140.657s on Linux x64/PostgreSQL 18.6. They
  verify exact shared assembly identity/bytes, mutable native values across
  project boundaries, repeat-build stability, clean/rebuild, plain managed
  execution and Native AOT execution inside PostgreSQL, plus rejection of a
  mismatched target. Generated packing now retains measured native alignment;
  the independent C/C# fixture checks actual managed embedding after a byte.

  Companion outputs remain inside each parent project's intermediate directory,
  including with `--artifacts-path`; the development SDK uses the build tool's
  resolved project output. The final `SdkSharesNativeTypesAcrossProjectsAndPublishesThem`
  case also recompiles and executes a changed plain consumer using existing
  project outputs, then verifies independent custom-artifact companions with
  byte-identical assemblies. `PackagedBuildToolRejectsMismatchedBindingRuntime`
  requires the exact target mismatch diagnostic and absence of managed outputs.

  Final plain `dotnet test` passes all 6,824 tests with zero failures/skips in
  421.277s on Linux x64/PostgreSQL 18.6. The non-incremental Release build passes
  with zero warnings/errors in 20.04s. The CI automation app compiles and its
  metadata check passes. API generation/freshness pass for 168 pages/2,245
  members; `pnpm check` reports zero errors/warnings/hints and `pnpm build`
  produces 210 pages. The README, development guide, public raw-value guide and
  engineering command reference describe the generated declarations and their
  limits. No analyzer modes, severities, suppressions, skips or CI timeouts changed.

  Hosted verification of this milestone remains pending. Complete typed
  pointer/callback contracts, emitted alias/inheritance/legacy-value cast
  validation, checked node allocation/casts/formatting, backend node ownership
  tests, complete raw FFI and the full PostgreSQL/platform inventory remain open.

- 2026-09-25 — Repair selected-header integer typedef widths and native field
  names. The Windows job for `a43c448` in
  [CI 36200868194](https://github.com/willibrandon/ankus/actions/runs/36200868194)
  exposed a Linux bindgen typedef assumption: `uint64` resolves to `c_ulong`
  in the pinned declarations, but Windows uses eight-byte storage while C
  `unsigned long` is four bytes. Managed emission now uses each selected-header
  field's measured width for these signed/unsigned integer representations.
  Nested array elements retain their measured extents instead of inheriting
  the build target's `long` width. Unsupported representations still fail.

  `CompiledTargetSizedAliasesPreserveNativeWidths` compiles independent C
  declarations and emitted C# with both 32-bit and 64-bit signed/unsigned
  typedefs, then checks negative boundaries, high bits, fixed arrays and nested
  strides. `CompiledNodesPreserveCastInheritanceAndAliases` executes root,
  parent and descendant views, alias tags, unrelated/invalid tag rejection,
  shared addresses and payload mutation. `CompiledValueTagsPreserveVersionedUnionRules`
  checks all five legacy Value tags and later tagged union members for the
  PostgreSQL 13–19 declaration rules, retaining integer and pointer payloads
  while rejecting downcasts to an untagged union. These independent declaration
  fixtures do not establish live-server coverage for all those versions.

  The legacy fixture caught bindgen's escaped `str_` field being used as a C
  member name. The parser now restores `str`, as declared in PostgreSQL 13/14
  `Value.val`; all seven catalogs were regenerated from the pinned read-only
  pgrx commit. The narrow build-tool suite passes 122 tests with zero failures
  or skips in 2.681s. The initial full suite passes all 6,833 tests with zero
  failures/skips in 446.144s on Linux x64/PostgreSQL 18.6.

  The same hosted run passes all 6,824 Linux tests but exposes a separate macOS
  companion-reproducibility failure. A local reproduction through symbolic-link
  directories produces different DLL hashes from identical declarations:
  the compiler's physical source paths escaped the logical project path map.
  Generated projects now map both physical and logical directories to the same
  stable compiler path, with compiler/MSBuild/XML escaping. The independent
  reproduction now produces byte-identical DLLs with zero warnings/errors.
  The installed SDK's shared-type test also links its companion intermediate
  directories on Unix, retaining its exact DLL, clean/rebuild, custom-output
  and Native AOT assertions on every platform. Parent project roots are
  canonical: a separate reproduction with ordinary Microsoft.NET.Sdk projects
  exposed an upstream dependency-manifest omission when project references mix
  physical and symbolic paths. The complete installed-SDK regression passes in
  131.349s. The final narrow suite passes 122 tests with zero failures/skips in
  2.742s. Final plain `dotnet test` passes all 6,833 tests with zero failures/skips
  in 438.205s on Linux x64/PostgreSQL 18.6. The non-incremental Release build
  passes with zero warnings/errors in 20.48s. All seven native catalogs are
  current against the pinned pgrx declarations; API freshness passes for 168
  pages/2,245 members. The documentation check reports zero errors/warnings/hints,
  and the site build produces 210 pages. Hosted quality and macOS ARM64 checks
  pass in CI 36203633498. macOS/PostgreSQL 18.6 passes 6,831 tests with two
  existing Linux-only allocation-measurement skips; the Windows follow-up is
  recorded below.
  No diagnostic modes, severities, suppressions, skips or timeouts changed.
  Checked node ownership/casts/formatting, typed
  pointer/callback contracts, complete raw FFI and the remaining platform and
  PostgreSQL version inventory are still open.

- 2026-09-25 — Stop physical-directory traversal before resolving a filesystem
  root. Windows in [CI 36203633498](https://github.com/willibrandon/ankus/actions/runs/36203633498)
  gets past the integer-width failure but exposes `ResolveLinkTarget` querying
  the drive root through Win32 file enumeration, which rejects that root path.
  Both managed-binding generation and the installed-SDK fixture now return a
  root directly before querying links. Ordinary directory and ancestor-link
  resolution is retained. Plain `dotnet test` passes all 6,833 cases with zero
  failures/skips in 459.533s on Linux x64/PostgreSQL 18.6. The non-incremental
  Release build passes with zero warnings/errors in 22.73s. API freshness
  passes for 168 pages/2,245 members; the site build produces 210 pages and
  its check reports zero errors/warnings/hints. Hosted validation of the root
  correction remains pending. Analyzer settings, test assertions and CI
  timeouts are unchanged.

- 2026-09-25 — Separate binding compiler discovery from Native AOT linker setup.
  Windows in [CI 36204730752](https://github.com/willibrandon/ankus/actions/runs/36204730752)
  now completes the managed build. Its full test execution exposes nine enum
  fixtures retaining CRLF endings and a Native AOT SourceLink input captured
  before `ManagedBinary` exists. Enum fixtures now normalize their line endings
  before supplying records to the strict reader; production probe parsing and
  every value/error assertion are retained. The build-tool suite passes all 122
  cases with zero failures/skips in 2.769s.

  Compiler discovery runs the SDK's native setup in a separate MSBuild project
  instance and returns only the compiler, library directories and target triple.
  The parent retains Native AOT's normal compile/input/setup order, including
  SourceLink and debug symbols. Before/after MSBuild observations reproduce the
  premature linker inputs and prove they remain deferred after the fix.
  `SdkBindingDiscoveryLeavesNativeLinkInputsDeferred` requires an actual built
  companion with no premature managed/native linker inputs. It and the full
  installed-SDK shared-type/publication scenario pass with zero failures/skips
  in 138.182s on Linux x64/PostgreSQL 18.6. The final unfiltered `dotnet test`
  run passes all 6,834 cases with zero failures/skips in 442.602s on that
  platform/server. The non-incremental Release build passes with zero
  warnings/errors in 40.77s. API freshness passes for 168 pages/2,245 members;
  the site builds 210 pages and its check reports zero errors/warnings/hints.
  Repaired hosted checks remain pending.

  The preceding hosted Linux job passes all 6,833 tests with zero failures/skips.
  Its macOS job passes in 17m 24s, so the macOS test timeout increases from
  20 to 25 minutes under the authorized 40-minute ceiling. Linux remains at 20
  and Windows at 30 minutes; every platform still runs the complete suite.
  Analyzer modes, severities and suppressions are unchanged.

- 2026-09-25 — Hosted verification of the binding/Windows repairs is complete.
  Commit `94c0e61` passes all seven jobs in
  [CI 36206527276](https://github.com/willibrandon/ankus/actions/runs/36206527276).
  Linux x64/Ubuntu 24.04/PostgreSQL 18.6 passes all 6,834 tests with no failures
  or skips in a 17m 08s job. macOS ARM64/macOS 15/PostgreSQL 18.6 passes 6,832
  tests with no failures and the two existing Linux-only memory-measurement
  skips in 16m 38s. Windows x64/Windows Server 2025/PostgreSQL 17.11 passes
  6,832 tests with no failures and those same two skips in 26m 53s; its
  integration suite takes 22m 29.158s. Hosted quality reports zero Release
  warnings/errors, a current API reference and zero documentation errors,
  warnings or hints. Timeouts remain Linux 20, macOS 25 and Windows 30 minutes,
  below the authorized 40-minute ceiling. This is the tested matrix; complete
  PostgreSQL-major/platform validation remains open.

- 2026-09-25 — Implemented checked native node views. `PgNodes.Borrow`
  and `PgNodeReference<T>` retain the original checked allocation/offset or
  explicit raw address/extent/provider/context/reset generation. Upcasts do
  not discard the complete original extent, and downcasts cannot invent extra
  raw storage or recapture a newer context generation. Allocation views follow
  resizing and reject incomplete source and target ranges. A raw-borrow
  overload lets the caller explicitly guarantee a larger accessible extent.

  Every node access checks its declared size, native alignment, target runtime
  and measured binding identity against an independent native memory capability.
  The generator embeds the compilation's measured identity; no SPI query or
  production friend assembly is needed. Tag inspection preserves all bits;
  target predicates decide cast acceptance. These checks cannot validate native
  pointer members supplied by the caller. The reference tests pass 41 cases
  with zero failures/skips, and five binding-generator cases pass. Nine real
  PostgreSQL 18.6/Linux x64 cases pass in 63.583s, covering pgrx's RangeTblRef
  roundtrip and AlternativeSubPlan/Expr/Node inheritance, shared addresses and
  mutations, unrelated Var rejection, OpExpr aliases, allocation
  shrink/regrowth/release, original raw generations after reset/deletion and
  native ABI-mismatch diagnostics with same-session recovery. A direct runtime
  union fixture also preserves source-only upcasts and rejects union downcasts.
  Final plain `dotnet test` passes all 6,876 cases with zero failures/skips in
  409.832s on Linux x64/PostgreSQL 18.6. The non-incremental Release build passes
  with zero warnings/errors in 37.69s. The regenerated API reference is current
  at 170 pages/2,252 members; the site builds 212 pages and its check reports
  zero errors/warnings/hints.

  | Checked-view requirement | Concrete witnesses |
  |---|---|
  | Shared allocation offsets, mutations, resizing and release | `AllocationReinterpretPreservesOffsetAndResize`, `ReinterpretRejectsAnInvalidSourceRange`, `NodeViewsRevalidateAllocationBoundsAndRelease` |
  | Original raw extent, reset generation and provider; explicit extent boundaries | `RawReinterpretRetainsExtentAndCapturedGeneration`, `RawReinterpretCannotWidenThePromisedExtent`, `ReinterpretRejectsExpiredAndForeignAnchors`, `ExplicitRawExtentValidatesBeforeCapturingGeneration`, `RawNodeCastsRetainTheirOriginalAnchorGeneration` |
  | Tag identity, inheritance, aliases and source-only union directionality | `RangeTableNodeRoundtripPreservesNativeValueAndAddress`, `NativeInheritanceRetainsTagAndRejectsUnrelatedNodes`, `NativeAliasesRetainExactTagsAndSharedPayload`, `SourceOnlyUnionCanUpcastWithoutAcceptingDowncasts`, `TagRejectionAndIncompleteTargetStorageRemainDistinct` |
  | ABI/layout/alignment validation before native access, including retained views | `InvalidNodeMetadataFailsBeforeNativeAccess`, `ActiveBindingMismatchPreservesDiagnosticAndPrecedesStorageRead`, `NodeBorrowRejectsMisalignmentBeforeDereferencing`, `RetainedNodeViewsRevalidateAbiBeforeEveryAccess`, `NodeBorrowRequiresReferenceAndActiveCapability` |
  | Generated native capability contract and malformed-identity rejection | `NativeNodeCapabilityUsesTheCompilationsMeasuredBindingIdentity`, `NativeNodeCapabilityRejectsMalformedBindingConstants` |
  | Owned native diagnostics and same-session recovery | `IncompatibleBindingRaisesOwnedNativeErrorAndRecovers` |

  Hosted validation of this checked-view milestone is pending. Node-specific
  allocation, native formatting, further ownership/error witnesses, planner and
  executor integration, complete raw FFI and the full PostgreSQL/platform matrix
  remain required. Analyzer modes and severities are unchanged, with no added
  suppressions or production friend assemblies.

- 2026-09-25 — Checked node views in `b510a08` pass all seven jobs in
  [CI 36210282542](https://github.com/willibrandon/ankus/actions/runs/36210282542)
  and the separate documentation deployment. Linux x64/Ubuntu 24.04/PostgreSQL
  18.6 passes 6,876 tests with zero failures/skips in a 17m 29s job. macOS
  ARM64/macOS 15/PostgreSQL 18.6 passes 6,874 tests with no failures and the two
  existing Linux-only memory-measurement skips in 16m 30s. Windows x64/Windows
  Server 2025/PostgreSQL 17.11 passes 6,874 tests with no failures and those
  same two skips in 26m 58s. Quality and all runtime preparation jobs pass.
  The existing Linux 20/macOS 25/Windows 30-minute limits remain sufficient.

- 2026-09-25 — Added zeroed tagged node allocation and guarded native formatting.
  `PgNodes.DangerousAllocate<T>` uses the measured representation's alignment,
  zeroes its complete storage and writes the exact caller tag without invoking a
  constructor. Existing boxes retain individual ownership or transfer release
  rights to their context. As in pgrx's unsafe `alloc_node`, callers choose the
  correct complete representation and initialize required fields; inheritance
  acceptance alone does not prove that a base node can store a descendant.

  `PgNodeReference<T>.DangerousToNativeString()` calls PostgreSQL's `nodeToString`
  below the native memory error guard. Generated companions retain concrete
  tag sizes/alignments, including typedefs, list-family aliases and PostgreSQL
  13/14's shared Value representation, in their content identity. The formatter
  revalidates allocation identity/offset/current extent or the original raw
  address/extent/reset generation. It checks the actual concrete root layout
  before traversal, protects its context during formatting and deletes the
  temporary formatting context on success and error. Its Unicode result owns
  an allocator-matched copy independently of the node's lifetime. The caller
  still guarantees valid, unchanged pointer members and variable-length tails
  throughout traversal, including reentrant callbacks. Unknown tags retain
  PostgreSQL's WARNING and fallback output.

  Focused verification passes six concrete-layout cases, 15 generator cases,
  32 runtime node cases and 24 real PostgreSQL node cases, with no failures or
  skips. Backend execution uses Linux x64/PostgreSQL 18.6 and takes 66.101s.
  UTF8 and LATIN1 databases preserve native escaping and distinguish null from
  empty names. Repeated valid-address cycles raise native stack-depth errors;
  each leaves no retained formatting context and permits a successful nested
  traversal in the same backend. Final plain `dotnet test` passes all 6,918
  cases with zero failures/skips in 413.287s on Linux x64/PostgreSQL 18.6.
  The non-incremental Release build reports zero warnings/errors in 40.60s.
  The regenerated API reference is current at 170 pages/2,254 members; the site
  builds 212 pages and its check reports zero errors/warnings/hints. Hosted
  validation of this allocation/formatting milestone remains pending.

  | Requirement | Concrete witnesses |
  |---|---|
  | Measured concrete layouts, typedef/list aliases and legacy Value | `ConcreteNodeLayoutsPreserveAliasesAndListFamilies`, `ConcreteValueLayoutsRespectSelectedMajor`, `NativeNodeCapabilityRejectsMalformedLayoutMetadata` |
  | Zeroed allocation, exact caller tag, ABI ordering and ownership | `NodeAllocationPreservesZeroPolicyTagAndOwnership`, `NodeAllocationValidatesBeforeAllocating`, `NodeAllocationFailurePreservesCleanupErrors`, `NodeAllocationZeroesPayloadAndFormattedTextOutlivesOwner` |
  | Original offset/extent/generation and concrete root bounds/alignment | `NodeFormattingRetainsAllocationOffsetAndCurrentExtent`, `NativeNodeFormattingPreservesInteriorStorage`, `NativeNodeFormattingRejectsIncompleteRootsAndRecovers`, `NativeNodeFormattingRejectsMisalignedConcreteRoot`, `NativeNodeFormattingRetainsRawAnchorGeneration` |
  | Exact native output, list families, nested pointers, warnings and encoding | `NativeNodeFormattingUsesIntegerListLayout`, `NativeNodeFormattingTraversesNestedValues`, `NativeNodeFormattingPreservesUnknownTagWarning`, `NativeNodeFormattingConvertsServerEncoding` |
  | Output release, native ERROR containment and same-session cleanup/recovery | `NodeFormattingRetainsRawGenerationAndReleasesOutput`, `NativeNodeFormattingRecoversFromRecursiveErrorWithoutRetainingContexts` |

  This is not full node or pgrx parity. Planner/executor integration, complete
  raw FFI and callback surfaces, further native ownership/error boundaries and
  the full PostgreSQL-major/platform matrix remain required. README and the
  public raw-value guide now describe allocation, explicit formatting and the
  caller's graph lifetime obligations. Analyzer modes, warnings, suppressions,
  production assembly boundaries and CI timeouts are unchanged.

- 2026-09-25 — Node allocation and formatting in `d000135` pass all seven jobs
  in [CI 36212131890](https://github.com/willibrandon/ankus/actions/runs/36212131890)
  and documentation deployment. Linux x64/Ubuntu 24.04/PostgreSQL 18.6 passes
  6,918 tests with no failures or skips in a 16m 51s job. macOS ARM64/macOS
  15/PostgreSQL 18.6 and Windows x64/Windows Server 2025/PostgreSQL 17.11 each
  pass 6,916 tests with no failures and the two existing Linux-only memory
  measurement skips, in 12m 11s and 20m 52s respectively. The Windows integration
  suite takes 17m 08.720s. Existing timeout limits remain sufficient; the full
  PostgreSQL-major/platform inventory remains open.

- 2026-09-25 — Corrected documentation syntax highlighting shared across pages.
  A scoped C# grammar extension recognizes typed top-level `using` declarations
  before the upstream namespace-import rule, preserving generic type, local,
  initializer and string tokens. The SQL grammar now recognizes PostgreSQL's
  `SHOW` command. The configuration guide's hook signatures terminate with
  semicolons so each method starts a fresh declaration. Both themes pass 14 C#
  token checks and seven SQL keyword/string/comment cases. The rebuilt HTML
  confirms the expected type, method and keyword colors in both themes. A forced
  site build produces 212 pages, and `pnpm check` reports zero errors, warnings
  or hints. The non-incremental Release build passes with zero warnings/errors
  in 73.50s. No analyzer settings, suppressions, runtime behavior or CI limits change.

- 2026-09-25 — Added the PostgreSQL 13–19 foreign-declaration inventory from
  pinned pgrx commit `70383e884582d1bcc7cd681d10886b995a2830cb`. Separate raw
  catalogs retain 7,349–8,799 functions, 475–591 globals and 4,968–6,159 top-level
  reference constants per major. Functions preserve ordered parameters, nested
  callbacks and arrays, return representations, variadics, declared ABI and
  original linkage attributes. Globals retain mutability and callback aliases.
  pgrx-specific C shim symbols remain distinct from PostgreSQL exports.

  Reference constants remain unevaluated expressions from the reference build;
  their platform-dependent values cannot substitute for selected-header probes.
  Node layout generation loads its existing type graph independently of the raw
  inventory. The existing type catalogs remain byte-for-byte unchanged. Direct
  tests pin `ExecutorRun`'s PostgreSQL 18 argument removal, the PostgreSQL 13/14
  parse-hook parameter change and PostgreSQL 19's const-qualified hook parameter.
  Inventory counts come from an independent scan of all seven pinned sources.

  Focused tests pass 53 cases with no failures/skips in 3.602s. Parser tests
  assert exact declarations, exclusion of Rust helpers/nested items/trivia,
  malformed and duplicate input rejection, ordinal ordering and serialization
  independent of declaration order. All seven catalogs and header manifests pass
  exact freshness checks. A separate command-level check verifies identical
  regeneration, successful fresh checks, rejection of stale and missing raw
  files without writes, and recovery by explicit regeneration.

  Final plain `dotnet test` passes all 6,971 cases with zero failures/skips in
  439.030s on Linux x64/PostgreSQL 18.6; the integration suite takes 438.453s.
  The non-incremental Release build reports zero warnings/errors in 63.87s.
  API generation retains 170 pages/2,254 members, the documentation site builds
  212 pages, and its check reports zero errors/warnings/hints. README, the public
  raw-value guide and contributor/catalog documentation state the current scope.

  | Requirement | Concrete witnesses |
  |---|---|
  | Exact function signatures, linkage, variadics and callback returns | `ForeignFunctionsPreserveSignaturesAndLinkage`, `PackagedRawDeclarationsRetainVersionedContracts` |
  | Global mutability, declared ABI and versioned hook aliases | `ForeignGlobalsRetainMutabilityAndCallbacks`, `RawHooksResolveTheSelectedMajorsCallbackContract` |
  | Unevaluated reference expressions and exclusion of non-foreign helpers | `ReferenceConstantsPreserveExpressions`, `RawInventoryExcludesHelpersAndTrivia` |
  | Deterministic ordering and explicit invalid-input/version rejection | `RawCatalogOrderingIsStable`, `MalformedRawDeclarationsFailExplicitly`, `EmptyInventoriesAndArgumentBoundariesAreExplicit`, `UnsupportedRawCatalogMajorsAreRejected` |
  | Packaged per-major inventory and reproducible command output | `PackagedRawDeclarationsRetainVersionedContracts`, `Ankus.Bindings.cs --check`, the isolated stale/missing-file command checks described above |

  This is declaration inventory, not callable raw API parity. General native
  function generation, selected-header validation of those signatures, global
  access, guarded callback/hook registration and chaining, remaining native shims
  and the full PostgreSQL-major/platform matrix remain required. No runtime API,
  analyzer mode/severity, suppression, production friend assembly or CI timeout
  changes. Hosted CI `36214506967` and docs deployment `36214506968` pass for
  commit `49d06c3`. Linux x64/Ubuntu 24.04/PostgreSQL 18.6 passes all 6,971
  tests with no failures or skips in a 16m 52s job. macOS ARM64/macOS 15/
  PostgreSQL 18.6 and Windows x64/Windows Server 2025/PostgreSQL 17.11 each
  pass 6,969 tests with no failures and the two existing Linux-only memory
  measurement skips, in 12m 56s and 24m 33s jobs. Windows integration takes
  20m 28.995s; the existing 30-minute job limit remains sufficient.

- 2026-09-25 — Implemented selected-header native signature verification as the
  next dependency of general guarded raw-call generation. The build tool's
  `binding-signatures` command consumes an explicit function list and the same
  installation/compiler arguments as the layout probe. It checks full native
  prototypes without calling PostgreSQL, measures fixed parameter/result storage,
  and validates complete observations before writing its JSON contract.

  C declaration generation retains native typedef names instead of substituting
  the reference platform's primitive aliases. Nested callback return values,
  pointer const levels, arrays, void/non-returning results and variadics retain
  their native distinctions. Foreign arrays with unknown outer extents remain
  incomplete extern arrays. pgrx shim symbols resolve to their header functions.
  Unknown/duplicate selections, unsupported syntax or ABI, incompatible native
  prototypes, unexpected/missing/duplicate measurements and inconsistent target
  identities fail explicitly. The existing layout probe shares its unchanged
  compiler options and process lifecycle with the signature command.

  Development validation passes 267 binding cases, including every retained
  PostgreSQL 13–19 foreign signature and global type expression. Independent C
  declarations execute full-width values, nested callbacks and array-pointer
  calls; deliberately changed native argument and result types fail compilation.
  Selected PostgreSQL 18.6/Linux x64 headers verify 14 real signatures with GCC
  and Clang, spanning rewrite manipulation, executor/utility calls, scalar and
  by-value results, and inline/shim functions. A broader PostgreSQL 18 probe
  also exposes retained
  reference limitations: tag-only C records and anonymous typedefs need their
  actual native spelling, `CreateStatistics` has a different installed-header
  parameter list, and Rust signatures omit volatile qualifiers used by native
  atomic/spinlock APIs.
  The probe rejects those mismatches; full target-derived signature generation
  must resolve them before these APIs can become managed calls.
  Prototype checks use C11 generic-selection static assertions, so no unused
  verification helper or optimizer-dependent symbol removal is required.

  Final plain `dotnet test` passes all 7,057 cases with zero failures/skips in
  418.820s on Linux x64/PostgreSQL 18.6; the integration suite takes 417.728s.
  The non-incremental Release build passes with zero warnings/errors in 63.01s.
  API freshness checks retain 170 pages/2,254 members. The site builds 212 pages,
  and its check reports zero errors, warnings or hints. Hosted CI for `739a7af`
  completed successfully: Ubuntu 24.04 x64/PostgreSQL 18.6 passes 7,057 cases
  with zero failures/skips in a 13m51s job; macOS 15 ARM64/PostgreSQL 18.6 passes
  7,055 with zero failures and the two existing Linux-only memory-measurement
  skips in 14m47s; Windows Server 2025 x64/PostgreSQL 17.11 passes 7,055 with
  the same two skips in 26m43s. Windows integration takes 22m06.969s. All seven
  CI jobs and documentation deployment pass. Existing 20/25/30-minute platform
  limits remain sufficient.

  | Requirement | Concrete witnesses |
  |---|---|
  | Typedef identity, qualifiers, arrays and nested callback results | `ValuesPreserveNativeTypeIdentity`, `CallbacksPreserveNestedResultsAndQualifiers`, `CompiledDeclarationsPreserveNativeCallSemantics` |
  | Versioned prototypes, void/non-returning and variadic distinctions | `FunctionPrototypesRetainCompleteSignatures`, `AllVersionedForeignDeclarationsProject`, `CompleteMeasurementsRetainSignatureContracts` |
  | Invalid syntax, selection, incomplete arrays and nesting boundaries | `InvalidTypesAreRejected`, `InputBoundariesFailExplicitly`, `GlobalArraysRetainUnknownOuterExtents`, `FunctionSelectionIsExact` |
  | Compiler-enforced header compatibility and measured storage | `CompiledSignatureProbeChecksHeadersAndMeasuresValues`, the PostgreSQL 18.6 signature command described above |
  | Complete target observations and command failure without output mutation | `InvalidObservationsFailExplicitly`, `InvalidTargetEvidenceIsRejected`, `InvalidArgumentCountsFailExplicitly`, `InvalidFunctionListsPreserveExistingOutput` |

  These are prototype/storage checks, not managed imports, export availability
  or backend-call evidence. General guarded calls, managed argument/result
  generation, call-site variadic promotion, raw global access, hook registration
  and chaining, and the complete version/platform matrix remain required. No
  analyzer settings, suppressions, production friend assemblies or timeouts change.

- 2026-09-25 — Added selected-header semantic type collection through the build
  tool's `binding-header-types` developer command. Clang resolves requested
  catalog functions and globals against the selected PostgreSQL installation.
  Structured types retain native typedef identities, anonymous records/enums,
  const/volatile/restrict levels, arrays and their parameter adjustment, nested
  callbacks, variadic/prototype distinctions and no-return metadata. Declaration
  metadata retains ordered parameter names, linkage, storage and thread-local
  status. Target identity comes from the same translation unit, including the
  exact PostgreSQL version, runtime identifier, pointer width, byte order and
  Clang major.

  The command reconstructs C declarations and recompiles compatibility assertions
  against the same headers before publishing its normalized JSON contract. It
  rejects unsupported types/ABIs, malformed observations, mismatched identities,
  excessive nesting and oversized compiler output. Diagnostic AST files retain
  local header paths; normalized contracts omit those paths and compiler IDs.
  Invalid selections, pre-start cancellation and runtime mismatch preserve an
  existing final contract. This tooling does not change consumer SDK generation.

  Linux x64/PostgreSQL 18.6 with Clang 21 collects and reconstructs all 9,224
  inventoried functions/globals (8,645 functions and 579 globals) successfully.
  This includes the installed `CreateStatistics` prototype, volatile atomic
  parameters, anonymous typedefs and `va_list` forms that the reference-prototype
  projection could not represent. A second run produces an identical normalized
  contract, and Clang 19 also passes eight focused real-header signatures.
  Native fixtures cover `const restrict` array-parameter adjustment: a regression
  first fails the compiler's pointer-qualifier assertion, then passes after the
  emitter restores qualifiers removed by conditional conversion.

  All 333 binding tests pass with zero failures/skips in 3.445s. The three new
  packaged-command integration cases pass on Linux x64/PostgreSQL 18.6 in
  121.067s, including fixture publication. The final non-incremental Release
  build passes with zero warnings/errors in 22.46s. API freshness retains
  170 pages/2,254 members; the site builds 212 pages and its check reports zero
  errors, warnings or hints. Final plain `dotnet test` passes all 7,126 cases
  with zero failures/skips on Linux x64/PostgreSQL 18.6 in 440.662s; the
  integration suite takes 439.645s. Hosted CI `36221061418` for `cfa6e6b`
  passes on Ubuntu 24.04 x64/PostgreSQL 18.6 (7,126 passed, no skips,
  16m35s job) and macOS 15 ARM64/PostgreSQL 18.6 (7,124 passed and the two
  existing Linux-only memory-measurement skips, 21m36s job). Windows Server
  2025 x64/PostgreSQL 17.11 passes 7,122 cases with two failures and the same
  two skips in 26m31s. Both failures are in the new header command: Clang
  diagnoses PostgreSQL's inline MSVC atomic-pointer conversions and deprecated
  CRT calls before inspecting the requested declarations. This is not a timeout.
  Quality, runtime checks and documentation deployment pass; the Windows repair
  is recorded in the next entry.

  | Requirement | Concrete witnesses |
  |---|---|
  | Typedef identity, qualifier levels, array adjustment, callbacks and no-return metadata | `HeaderTypesRetainNativeSemantics`, `DeclaratorsPreserveQualifierLevelsAndArrayPrecedence`, `CallbackReturnsAndPrototypeStatesRemainDistinct` |
  | Exact declaration/selection identity, metadata and bounded type trees | `DeclarationMetadataAndQualifiedParametersRemainExact`, `SelectionAndGeneratedAliasesAreExact`, `InvalidCompilerObservationsFailExplicitly`, `NestingBoundaryIsExplicit` |
  | Exact target facts and rejection of contradictory observations | `TargetFactsArePreserved`, `InvalidTargetFactsFailExplicitly` |
  | Bounded output and failure without replacing an existing contract | `InvalidHeaderSelectionsPreserveOutput`, `BoundedCompilerOutputPreservesExactBytes`, `CompilerOutputOverrunsFailExplicitly`, `InvalidBoundsAndPreCancelledCompilationPreserveState` |
  | Installed-package boundary and real selected-header identity | `PackagedBuildToolCollectsSelectedHeaderTypes`, `PackagedBuildToolPreservesHeaderContractOnRuntimeMismatch`, `PackagedBuildToolRejectsMismatchedHeaderMajor` |

  These are compiler type checks, not managed imports or backend-call evidence.
  Native scalar/record measurement, export availability, guarded managed calls,
  argument/result ownership, variadic call-site promotion, global access, hook
  registration/chaining and the complete version/platform matrix remain required.
  Parser fixtures for other targets do not establish real platform validation.
  No analyzer settings, suppressions, production friend assemblies or CI limits
  change.

- 2026-09-25 — Added selected-header storage measurement through the build
  tool's `binding-storage` command. It collects authoritative Clang types, then
  asks the same frontend to evaluate size, alignment, array stride and integer
  signedness for every fixed parameter, non-void result and requested global.
  Function/global linkage, parameter adjustment, qualifiers, callbacks,
  variadics and no-return metadata remain part of the contract. No target
  executable, PostgreSQL function call or backend export is needed.

  Header inspection now uses Clang's declaration-only frontend mode. It retains
  inline function signatures and compiler-evaluated constants without compiling
  implementation bodies that depend on PostgreSQL's original compiler dialect.
  This addresses the Windows failures above without suppressions or reduced
  warning enforcement. A native regression retains an inline declaration with
  a compiler-specific body, rejects a changed parameter through the reconstructed
  prototype assertion, and separately proves a declaration warning remains fatal.
  This command verifies declaration contracts; it does not validate PostgreSQL's
  implementation bodies.

  Record/enum completeness distinguishes complete definitions, opaque forward
  declarations and unavailable implicit compiler declarations. Incomplete arrays
  retain unknown total size with measured alignment/stride. Opaque records/enums
  retain unknown size/alignment instead of invented zero-sized storage; pointers
  to them still have measured pointer storage. An opaque result remains distinct
  from void. Over-aligned typedefs retain alignment greater than their byte size.
  Invalid, missing, extra, duplicate and contradictory constants or target facts
  fail before the final storage contract is written. Large selections use
  256-symbol batches, each subject to the existing 512 MiB compiler-output bound
  and complete target validation before the combined contract is published.

  Linux x64/PostgreSQL 18.6/Clang 21 measures all 9,224 inventoried symbols and
  22,821 parameter/result/global entries. Thirty incomplete arrays retain unknown
  total extent; three AIO globals retain unknown object size/alignment because
  the selected pgrx header manifest exposes only their forward declarations.
  The normalized result is exactly equal to an independently executed native
  measurement prototype for all 9,224 symbols and all 22,821 entries. Local
  Windows 11 x64 (build 26200.9457)/Clang 21 also collects and measures eight
  real-header signatures/globals against PostgreSQL 17.7 and 18.1. Those are
  compiler checks, not new Windows backend-suite evidence.

  Focused binding tests pass 417 cases with no failures/skips in 3.560s. The
  declaration/constant regressions also pass 19 cases on Windows x64 in 352ms;
  the independent native fixture preserves the Microsoft ABI's four-byte enum
  representation and the Unix fixture's packed one-byte representation. The
  non-incremental Release build passes with zero warnings/errors in 19.02s.
  API freshness retains 170 pages/2,254 members; the site builds 212 pages and
  its check reports zero errors, warnings or hints. The final full root suite
  passes all 7,212 cases with zero failures/skips in 447.435s on Linux x64/
  PostgreSQL 18.6; integration takes 446.881s. Hosted CI for this milestone
  failed as detailed in the following repair entry.

  The first full local run hit temporary-filesystem exhaustion during isolated
  package restore, causing 40 failures. The package fixture now respects the
  platform temporary-directory setting instead of selecting a hardcoded local
  directory. The complete suite passed with sufficient temporary storage, as
  recorded above; the failed run is not counted as passing evidence. The Release
  build after this fixture correction also passes with zero warnings/errors
  in 16.28s.

  | Requirement | Concrete witnesses |
  |---|---|
  | Sizes, alignment, array adjustment/stride and signedness | `CompleteHeaderStorageRetainsNativeShapes`, `CollectedHeaderStorageMatchesIndependentNativeTypes`, the complete PostgreSQL 18 comparison described above |
  | Opaque versus complete/unavailable tags, void versus opaque results | `OpaqueTagsRetainUnknownStorage`, `UnavailableCompilerTagsRetainUnknownCompleteness`, `CollectedHeaderStorageMatchesIndependentNativeTypes` |
  | Exact constants, target identity and malformed/partial observation rejection | `CompilerConstantsRetainExactStorage`, `EmptyCompilerStorageRetainsTarget`, `InvalidCompilerStorageFailsExplicitly`, `InvalidStorageObservationsFailExplicitly`, `StorageTargetMustMatchCollectedHeaders` |
  | Integer boundaries, exact array extents and representation-specific signedness | `ArrayStorageMustRetainExactExtent`, `SignednessMatchesNativeValueKind`, `StorageBoundsAndPointerWidthAreExact` |
  | Declaration inspection with fatal warnings and incompatible-prototype rejection | `HeaderFrontendChecksDeclarationsWithoutCompilingBodies`, the Windows PostgreSQL 17.7/18.1 commands described above |
  | Installed command and failure preserving prior final contracts | `PackagedBuildToolMeasuresCollectedHeaderStorage`, `PackagedBuildToolPreservesStorageOnRuntimeMismatch`, `InvalidStorageSelectionsPreserveContract` |

  These measurements do not classify a managed aggregate ABI or establish
  transitive native record fields, export availability, pointer ownership,
  guarded calls, callback transport, variadic promotion, global access or hook
  registration/chaining. Consumer SDK node generation is unchanged. Those
  requirements and the complete PostgreSQL-major/platform matrix remain open.
  No analyzer settings, suppressions, production friend assemblies or CI limits
  change.

- 2026-09-26 — Repair the header collector's CI toolchain prerequisite.
  Hosted run `36224258434` for `cf8c5bc` failed on Ubuntu 24.04 x64 and
  macOS 15 ARM64 because their default Clang frontends do not recognize
  `-skip-function-bodies`. Both jobs report three build-tool failures and four
  integration failures from that missing option; the remaining unit modules did
  not run after the first module failed. This is not passing platform evidence.
  LLVM introduced the command-line option in Clang 20; the preceding local
  declaration-only checks used Clang 21.

  The same run's Windows Server 2025 x64/PostgreSQL 17.11 job completed in
  28m20s with 7,208 passes, two failures and two Linux-only skips. Compilation
  and all 417 build-tool tests pass there. Its two failing integration assertions
  assumed `proc_exit` carried a no-return annotation on every target. PostgreSQL
  17's MSVC headers actually expand `pg_attribute_noreturn()` to nothing;
  PostgreSQL 18 uses C11 `_Noreturn`. The collector correctly preserves the
  absence/presence. Both packaged-command tests now assert the exact major and
  compiler-family contract, with no fabricated native metadata. A native frontend
  regression distinguishes unannotated exit bodies from declared `_Noreturn`.

  The .NET CI app now installs/selects LLVM 20 from LLVM's signed package
  repository on Linux and Homebrew on macOS, and explicitly selects the Windows
  runner's LLVM installation. A capability check prints the compiler identity
  and requires the declaration-only option before building/running the suites.
  The same `header-frontend-check` command can validate a local compiler without
  installing anything. It succeeds for Linux Clang 21.0 and Windows Clang 21.1.7,
  and rejects Linux Clang 19.1.7 with a clear prerequisite diagnostic.
  Developer and public raw-binding documentation now states the Clang 20 minimum.

  `HeaderFrontendChecksDeclarationsWithoutCompilingBodies` passes locally,
  retaining exact inline signatures, rejecting an incompatible prototype and
  keeping declaration warnings fatal. Its final annotation regression also
  passes on Windows x64/Clang 21.1.7 in 371ms. The non-incremental Release build passes
  with zero warnings/errors in 74.66s using one MSBuild worker; the first parallel
  attempt lost two MSBuild workers and is not counted as passing evidence.
  API freshness retains 170 pages/2,254 members; docs build 212 pages and check
  with zero errors, warnings or hints. After the final test corrections, the
  affected Release test projects build successfully, including the integration
  project with zero warnings/errors in 16.71s. The final plain root suite passes
  all 7,212 tests with zero failures/skips in 443.417s on Linux x64/PostgreSQL
  18.6; integration takes 442.804s. Replacement run `36226493533` passes quality,
  all runtime jobs and docs. Ubuntu 24.04 x64/PostgreSQL 18.6/Clang 20.1.8 passes
  all 7,212 tests without skips in a 17m54s job; macOS 15 ARM64/PostgreSQL 18.6/
  Clang 20.1.8 passes 7,210 with the two documented Linux-only skips in 12m54s.
  Windows Server 2025 x64/PostgreSQL 17.11 passes all integration cases (3,289
  passes and two Linux-only skips), including both corrected header assertions,
  but one build-tool test fails during temporary executable deletion. The four
  remaining unit modules do not run after that failure. The following entry
  records its repair. No analyzer enforcement, warning suppression,
  production friend assembly changes are part of this repair. The Windows job's
  timeout increases from 30 to 35 minutes after the observed 28m20s run; every
  job remains below the user's 40-minute maximum, with full suites unsharded.

- 2026-09-26 — Make native probe cleanup tolerate Windows file-release races.
  Run `36226493533` fails `CompiledValueTagsPreserveVersionedUnionRules (15)`
  because recursive cleanup cannot delete `probe.exe` after its process exits.
  The failure is `UnauthorizedAccessException` from `Directory.Delete`, rather
  than a native layout or generated-value assertion. The Windows job completes
  in 25m07s; no timeout or analyzer enforcement changes are needed.

  Native binding fixtures now share bounded deletion retries for Windows access,
  sharing, lock and directory-not-empty errors. Other failures propagate
  immediately, and persistent deletion failures still fail the test. Cancellation
  also waits for the killed probe process to exit before cleanup starts.
  Cleanup continues independently of the canceled test token and removes only
  the fixture's own directory. No failed assertion is retried or suppressed.

  `NativeProbeCleanupWaitsForReleasedFile` holds a real file handle that denies
  deletion on Windows, releases it, and requires the entire directory to be
  removed. `NativeProbeCleanupDoesNotHideAccessDenial` retains a Windows read-only
  file through retry exhaustion and verifies that the error and file contents
  survive; it then explicitly restores and removes the fixture.
  `NativeProbeCleanupRejectsMissingDirectory` requires a missing-path error.
  The affected native class passes all 19 cases on Windows x64/.NET 10.0.12/
  Clang 21.1.7 in 3.358s; Linux passes 18 with the Windows-only access-denial
  case skipped in 3.475s. The non-incremental Release build passes with zero
  warnings/errors in 69.11s. API freshness retains 170 pages/2,254 members;
  docs build 212 pages and check with zero errors, warnings or hints.
  The final plain root suite passes 7,214 cases with zero failures and the one
  Windows-only cleanup case skipped in 451.232s on Linux x64/PostgreSQL 18.6;
  integration takes 450.400s. Replacement hosted run `36228628463` for
  `a38b6f0` passes every job, and its docs run `36228628442` passes. Ubuntu
  24.04 x64/PostgreSQL 18.6 passes 7,214 tests with the Windows-only cleanup
  case skipped in a 17m40s job. macOS 15 ARM64/PostgreSQL 18.6 passes 7,212
  with that cleanup case and the two Linux-only cases skipped in 18m59s.
  Windows Server 2025 x64/PostgreSQL 17.11 passes 7,213 with the two Linux-only
  cases skipped in 27m13s, including all 420 build-tool tests and all 3,289
  applicable integration cases. Every platform uses Clang 20.1.8 and runs all
  six test modules against its real PostgreSQL installation. The Windows job
  stays within its existing 35-minute limit; analyzer enforcement and the
  user's 40-minute maximum remain unchanged.

- 2026-09-26 — Added transitive selected-header record collection through the
  build tool's `binding-records` command. It retains the validated semantic
  function/global catalog and adds an indexed graph of declared and canonical
  types, struct/union/enum identities, ordered physical fields, bit offsets and
  widths, array/vector extents, native size/alignment and exact enum constants.
  Canonical cursor identity preserves recursive pointers and distinct anonymous
  records. Anonymous typedefs retain their aliases without inventing C tag names;
  unnamed bitfields and anonymous containers remain distinct. Opaque declarations
  have unknown storage, while pointers to them retain measured pointer storage.

  The selected compiler serializes a declaration-only AST using the same driver,
  includes, target and fatal-warning settings as signature validation. An isolated
  worker loads it with the matching libclang C API; this avoids changing clang-cl
  into a different driver mode or reinterpreting its predefines. Compiler shims
  resolve their library through the selected compiler's resource directory.
  Native resources use matching disposal and explicit C calling conventions;
  managed callback exceptions unwind only after native traversal returns.
  Windows keeps the LLVM module loaded for the worker's lifetime because LLVM
  20/21 release builds retain a thread-exit callback after unloading; translation
  units, indexes, strings, diagnostics and printing policies still release normally.
  Worker streams and graphs have finite bounds, cancellation terminates and joins
  the worker, and failed collection preserves the previous final record artifact.

  Declared function parameters and canonical function parameters retain their
  distinct array/pointer shapes. Unprototyped functions retain that state, with
  zero fixed parameters. Callback annotations and non-default calling conventions
  remain visible alongside the richer existing signature contract. Padded vectors
  and over-aligned typedefs retain their actual storage. Enum constants wider than
  the native C API's 64-bit accessors fail explicitly before narrowing. Invalid
  graph edges, contradictory storage, missing roots, unreachable observations and
  malformed enum/protocol values fail before publication.

  Linux x64/PostgreSQL 18.6/Clang 21 collects all 9,224 selected symbols into
  14,828 types and 1,038 declarations: 851 structs/unions and 187 enums, with
  5,863 physical fields and 1,914 enum constants. Seventy-one declarations remain
  opaque and 23 fields retain bitfield widths. GCC 14.2 independently accepts
  8,710 generated size/alignment/ordinary-field-offset and enum-value assertions
  with warnings as errors. Those assertions cover nameable declarations and enum
  constants; native fixtures separately execute bitfield storage and anonymous
  container checks. Local Windows x64/Clang 21.1.7 also collects eight real-header
  symbols against PostgreSQL 17.7 and 18.1. These compiler checks are not full
  Windows backend-suite evidence.

  The focused Linux binding suite passes 467 cases with the existing Windows-only
  cleanup case skipped in 3.474s. All 48 new focused cases pass on Windows x64/
  .NET 10.0.12/Clang 21.1.7 in 1.019s. Both packaged-command integration tests pass
  against PostgreSQL 18.6 in 109.886s, including a library-load failure that leaves
  the prior final contract intact. The non-incremental Release build passes with
  zero warnings/errors in 75.68s; the final focused Release build also passes.
  API freshness retains 170 pages/2,254 members; docs build 212 pages and check
  with zero errors, warnings or hints. Plain root `dotnet test` passes all six
  modules against PostgreSQL 18.6 on Linux x64: 7,264 passed, zero failed and the
  existing Windows-only cleanup case skipped in 7m 36.924s. Hosted CI
  [36232029901](https://github.com/willibrandon/ankus/actions/runs/36232029901)
  and docs deployment `36232029943` pass for `52673cc`. All six test modules
  execute against real PostgreSQL installations with Clang 20.1.8: Ubuntu 24.04
  x64/PostgreSQL 18.6 passes 7,264 cases with one existing Windows-only skip in a
  12m 10s job; macOS 15 ARM64/PostgreSQL 18.6 passes 7,262 with three existing
  platform skips in 20m 41s; Windows Server 2025 x64/PostgreSQL 17.11 passes
  7,263 with two existing Linux-only skips in 24m 17s. No timeout changes were
  needed.

  | Requirement | Concrete witnesses |
  |---|---|
  | Exact fields, recursion, anonymous identity, flexible tails, bitfield bytes and parameter adjustment | `CollectedRecordsPreservePhysicalFieldsAndIdentity`, the independent GCC assertions described above |
  | Enum limits, typedef annotations, padded vectors, atomic/complex fields and alternate callback ABI | `CollectedRecordsRetainEnumsAndAnnotatedTypes`, `CollectedRecordsRejectEnumWiderThan64Bits` |
  | Complete graph/target/protocol validation and empty selection | `InvalidRecordGraphsFailExplicitly`, `InvalidRecordEnumConstantsFailExplicitly`, `NativeRecordProtocolRejectsIncompleteJson`, `EmptyRecordSelectionRetainsOnlyTarget` |
  | Worker failure/recovery, cancellation and preserved final output | `NativeRecordWorkerRejectsInvalidArtifactsAndRecovers`, `InvalidRecordCommandAritiesFailExplicitly`, `InvalidRecordSelectionsPreserveOutput`, `PackagedBuildToolPreservesRecordsOnLibraryFailure` |
  | Installed command and real selected PostgreSQL identity/layout | `PackagedBuildToolCollectsTransitiveHeaderRecords`, the Linux/Windows header commands described above |

  Engineering and contributor docs describe the command and matching-library
  prerequisite. The public raw-value guide and README retain consumer-facing
  capabilities and limitations without internal command or probe instructions.
  Linux CI explicitly installs the LLVM 20 development package;
  macOS and Windows LLVM installations already provide the library. No analyzer
  settings, warning suppressions, production friend assemblies or CI timeouts
  change. These records do not yet classify managed aggregate calls or establish
  export availability, pointer ownership, callback error transport, variadic
  promotion, raw globals or hook registration/chaining. Consumer SDK node
  generation and the unresolved complete PostgreSQL-major/platform matrix remain
  separate port work.

- 2026-09-26 — Added typed native call-body generation from the selected-header
  semantic signatures and record graph. The internal `binding-call-sources`
  command emits a uniform argument-address/length and result-address/length
  boundary. Native C performs each actual scalar or aggregate call, retaining
  typedefs, array adjustment, pointer values and callback signatures. Declared
  typedef alignment remains distinct from canonical function ABI shape. Empty
  records use the selected compiler's actual size rather than an assumed
  cross-platform representation.

  The generated bodies check every envelope before any native side effect and
  publish result bytes only after the function returns. Complete native prototype,
  storage, PostgreSQL version, processor, operating-system and byte-order checks
  reject stale contracts. Variadic/unprototyped calls require future explicit
  call-site promotion support; globals and incomplete by-value objects fail
  explicitly. A cyclic function-alias graph fails instead of hanging generation.
  The command compiles complete function bodies rather than accepting a
  declaration-only frontend pass. Clang collects the metadata; the Windows native
  bodies use MSVC, matching the SDK's native bridge compiler. Both stages keep
  warnings as errors. Compiler failure or cancellation leaves the
  previous final source intact. Frontend cancellation now joins the process and
  observes its stream tasks before releasing owned output files.

  Linux x64/PostgreSQL 18.6/Clang 21 compiles all 8,629 fixed-prototype
  functions in the selected catalog with warnings as errors. The selection
  excludes 579 globals and 16 variadic/unprototyped functions explicitly; it is
  compiler evidence, not execution of arbitrary backend functions. Local Windows
  x64/Clang 21.1.7/MSVC also validates six selected real-header functions against
  PostgreSQL 17.7 and 18.1. The 17 focused new unit cases pass on Windows x64/
  .NET 10.0.12 in 821ms. The complete Linux build-tool module passes 484 cases
  with its existing Windows-only cleanup case skipped in 3.753s.

  The Release build passes with zero warnings/errors in 25.59s. API freshness
  retains 170 pages/2,254 members; docs build 212 pages and check with zero
  errors, warnings or hints. Plain root `dotnet test` passes all six modules
  against PostgreSQL 18.6 on Linux x64: 7,283 passed, zero failed and one existing
  Windows-only cleanup skip in 8m 26.985s. This includes both installed-command
  cases, native compiler failure with preserved output, and subsequent successful
  source publication.

  | Requirement | Concrete witnesses |
  |---|---|
  | Native scalar/aggregate ABI, full-width values, long double, 128-bit integers, mixed/large records, unions, over-aligned typedefs, empty records, borrowed pointers, native callbacks and array parameters | `NativeCallBodiesPreserveCompilerAbi` executes optimized generated C and checks independent native values and side effects |
  | Count, pointer, byte-length, alignment and result-envelope validation before native effects | `NativeCallBodiesRejectInvalidStorageBeforeInvocation` checks exact status codes, unchanged output/counter state and subsequent successful calls |
  | Native non-local error leaves the result untouched and allows recovery | `NativeCallBodiesRetainResultUntilNativeReturn` uses an entirely native setjmp/longjmp fixture, including a no-return function; it does not claim managed/PostgreSQL guard integration |
  | Explicit unsupported shapes, empty selections, deterministic source and compiler rejection of changed types/storage/targets/bodies | `NativeCallBodiesRejectUnsupportedContracts`, `NativeCallBodiesAcceptEmptySelection`, `NativeCallBodiesRetainDeterministicCheckedContracts` |
  | Command validation, cancellation, compiler failure and atomic source replacement | `InvalidCallCommandAritiesFailExplicitly`, `InvalidCallSelectionsPreserveOutput`, `CallSourcePublicationRequiresSuccessfulUncancelledCompilation`, `NativeFrontendCancellationJoinsProcessAndReleasesFiles` |
  | Installed command executes real selected-header aggregate results and recovers after rejected selection | `PackagedBuildToolGeneratesExecutableNativeCalls`, `PackagedBuildToolPreservesCallSourcesAndRecovers` |

  Native backend guards, owned diagnostic transport, managed imports, callback
  lifetime/error transport, export availability, globals, hooks and the complete
  PostgreSQL-major/platform matrix remain required. These generated C bodies
  must run beneath the native error guard on the backend thread; they are not a
  directly callable managed API. Engineering usage stays in `eng/README.md`.
  Consumer guides retain their current supported capabilities and limitations.
  `AGENTS.md` now records that internal maintenance commands belong in engineering
  or contributor documentation. Hosted CI
  [36235088328](https://github.com/willibrandon/ankus/actions/runs/36235088328)
  passes for `d21a8c1`. All six modules run against real servers with Clang
  20.1.8: Ubuntu 24.04 x64/PostgreSQL 18.6 passes 7,283 cases with one existing
  Windows-only skip in a 14m 04s job; macOS 15 ARM64/PostgreSQL 18.6 passes
  7,281 with three existing platform skips in 19m 31s; Windows Server 2025
  x64/PostgreSQL 17.11 passes 7,282 with two existing Linux-only skips in
  17m 57s. CI quality also passes. No timeout changes were needed.

- 2026-09-26 — Connected generated native call bodies to the existing
  callback-scoped PostgreSQL error guard. A hidden generated-code transport pins
  argument descriptors and carries exact native result storage through the
  established native memory capability. The C body executes entirely beneath
  `PG_TRY`; owned diagnostics return to managed code after the native frames
  unwind. Calls require an active backend thread and retain native release access
  during iterator cleanup, matching pgrx's raw `pfree` use in `PgBox` destruction.
  The transport does not infer raw pointer validity, ownership,
  callback lifetime or ABI identity from an address.

  Successful raw calls preserve their deliberate native state changes, including
  interrupt holdoffs. Failed calls restore the entry context and holdoff values.

  Ten focused runtime cases pass for exact frame values, empty/void storage,
  full-width lengths, every native status category, absent/ended/masked/nested and
  off-thread scopes, cleanup access, diagnostic buffer release and subsequent
  recovery: Linux x64 completes in 929ms and Windows x64/.NET 10.0.12 in 148ms.
  Seventeen focused backend cases pass against PostgreSQL 18.6 on Linux x64 in
  82.690s with zero failures/skips. They compile actual `binding-call-sources`
  output into a test-only native module and invoke it through the published
  Native AOT extension. Abort disposal releases detached storage through the
  generated `pfree` body once, preserves the original query error, emits no
  cleanup warning, and permits another query on the same backend. The final
  fixture also compiles and links against local Windows x64/PostgreSQL 17.7
  using Clang 21.1.7 metadata and MSVC. This compiler
  evidence does not stand in for Windows backend execution.

  | Requirement | Concrete witnesses |
  |---|---|
  | Exact storage-frame ABI, native-width fields, empty/void values and native status handling | `RawCallsPreserveFrameAndResult`, `RawCallStatusesRejectInvalidContractsAndRecover` |
  | Active/nested/ended/masked callback scopes, backend-thread affinity and cleanup access | `RawCallsRequireActiveBackendScope`, `GeneratedRawCallsRejectWorkerThreads`, `GeneratedRawCallsReleaseStorageDuringQueryAbort` |
  | Owned native diagnostics are copied, released once and retained after recovery | `RawCallErrorsReleaseDiagnosticsAndRecover` |
  | Real selected-header aggregate and void calls, malformed storage with unchanged output and later success | `GeneratedRawCallsPreserveNativeValues`, `GeneratedRawCallsRejectStorageAndRecover` |
  | PostgreSQL ERROR preserves the failed result, restores entry native state and allows same-session recovery | `GeneratedRawCallsRecoverFromPostgresErrors` |
  | Nested managed callbacks reenter the raw guard, unwind before PostgreSQL ERROR and recover | `GeneratedRawCallsSupportNestedCallbackRecovery` |
  | Successful native state changes remain observable until explicitly reversed | `GeneratedRawCallsPreserveSuccessfulNativeStateChanges`, `GeneratedRawCallsPreserveSuccessfulContextSwitches` |

  The Release build passes with zero warnings/errors in 10.45s. API freshness
  retains 170 pages/2,254 members; docs build 212 pages and check with zero
  errors, warnings or hints. Plain root `dotnet test` passes all six modules
  against PostgreSQL 18.6 on Linux x64: 7,310 passed, zero failed and one existing
  Windows-only cleanup skip in 7m 12.253s. Contributor documentation describes
  the protocol and the successful-call state contract. Hosted CI
  [36237785959](https://github.com/willibrandon/ankus/actions/runs/36237785959)
  and docs deployment `36237785929` pass for `8964473`. All six modules run with
  real servers and Clang 20.1.8: Ubuntu 24.04 x64/PostgreSQL 18.6 passes 7,310
  cases with one existing Windows-only skip in a 17m 50s job; macOS 15 ARM64/
  PostgreSQL 18.6 passes 7,308 with three existing platform skips in 14m 37s;
  Windows Server 2025 x64/PostgreSQL 17.11 passes 7,309 with two existing
  Linux-only skips in 27m 27s. Quality passes and no timeout changes were needed.

  This transport remains a generated-code prerequisite. Typed companion methods,
  complete native type generation, signature identity, export/link selection,
  managed callback guards/lifetimes, variadic calls, globals, hooks and the full
  supported PostgreSQL-major/platform matrix remain required. The existing
  memory guard rejects calls during ErrorContext reset; supporting raw native
  release there without recursively resetting PostgreSQL error state remains
  unresolved. Public guides retain the currently supported APIs and limitations.
  The README also links to contributor instructions instead of repeating the
  repository-root test command; extension-author test commands remain in place.

- 2026-09-26 — Captured the native numeric ABI needed by typed managed
  bindings. Header contracts now retain compiler-observed plain-char signedness,
  `wchar_t` size/signedness, floating radix, and significand precision plus normal
  exponent limits for `float`, `double` and `long double`. These facts cannot be
  inferred from the type name and byte size: local Linux x64/Clang 21 reports
  64-bit and 113-bit significands in the same 16-byte long-double storage under
  its default and `-mlong-double-128` modes. That alternate-mode observation is
  compiler evidence, not backend execution in that mode.

  Both AST interfaces independently observe the numeric model; the native worker
  rejects a different requested interpretation. Storage observation protocol 2
  carries every numeric identity field and rejects older observations. Plain
  char storage must agree with the selected target's signedness. Generated C
  bodies assert every numeric fact against their actual body compiler, including
  MSVC on Windows. Missing, duplicate, malformed and contradictory facts fail
  instead of acquiring defaults from the managed build host.

  | Requirement | Concrete witnesses |
  |---|---|
  | Exact host-independent scalar interpretation and complete required metadata | `TargetNumericFactsArePreserved`, `MissingNumericFactsFailExplicitly`, `NumericModelsRejectIncompleteContracts` |
  | Invalid flags, widths, radix, precision/range ordering, zero/negative boundaries and malformed numeric text | `InvalidNumericFactsFailExplicitly` |
  | Real signed/unsigned char compiler modes, native wchar facts and floating limits | `CollectedNumericModelsFollowCompilerOptions` executes an independent native compiler witness |
  | Worker target mismatch preserves rejection and permits exact recovery | `RecordWorkerValidatesNumericIdentity`, `NativeRecordWorkerRejectsInvalidArtifactsAndRecovers` |
  | Every storage identity field participates in compatibility | `HeaderStorageRejectsNumericMismatch` rejects all 13 changed facts and accepts the original observation afterward |
  | Actual C compilation rejects every changed numeric fact and matching calls retain native values | `NativeCallBodiesRejectNumericAbiChanges` checks all 13 diagnostics, then compiles and executes exact character bits and retained long-double epsilon |

  All 206 focused cases pass on Linux x64 and Windows x64/.NET 10.0.12.
  The actual five-function PostgreSQL fixture also compiles and links against
  Windows x64/PostgreSQL 17.7 with Clang 21.1.7 metadata and MSVC body checks.
  This is compiler evidence, not local Windows PostgreSQL execution. The Release
  build has zero warnings/errors in 38.72s. API freshness retains 170 pages/
  2,254 members; the site builds 212 pages and checks with zero errors, warnings
  or hints. Plain root `dotnet test` passes all six modules against PostgreSQL
  18.6 on Linux x64: 7,384 passed, zero failed and one existing Windows-only
  cleanup skip in 7m 32.510s. Hosted CI
  [36239866856](https://github.com/willibrandon/ankus/actions/runs/36239866856)
  passes the quality job and Linux Build module (558 passed, one existing skip),
  but its Ubuntu integration fixture fails before backend execution: Native AOT
  10.0.11 throws `IndexOutOfRangeException` in
  `ILCompiler.LazyGenericsSupport.GraphBuilder.WalkMethod` while compiling
  `RuntimeExports.RhUnbox` during the Operators sample publish. The same sample
  publishes locally, and eight repeated direct compiler invocations with four
  workers pass. A further publish using the exact Linux runtime artifact
  downloaded from this hosted run also passes. These observations do not establish
  the hosted failure's cause or resolution. The macOS 15 ARM64/PostgreSQL 18.6
  full suite passes 7,382 cases with three existing platform skips in a 16m 40s
  job. Windows Server 2025 x64/PostgreSQL 17.11 passes 7,383 cases with two
  existing platform skips in 29m 11s. Both platforms use Clang 20.1.8.

  Engineering documentation describes the numeric contract and required
  observation regeneration. Public guides retain the currently supported APIs.
  Complete managed native declarations, typed companion calls, active-extension
  signature/export selection, native alignment, managed callback lifetimes,
  variadics, globals, hooks and the full version/platform matrix remain required.

- 2026-09-26 — Retaining a separate, uniquely named MSBuild binary log for
  every integration-fixture extension publish. Previously, an assembly-startup
  compiler failure could leave no files for the existing CI failure-artifact
  upload. Logs now go into that artifact's publish subdirectory before backend
  startup. Contributor documentation describes the evidence location; user guides
  are unchanged. The Release build passes with zero warnings/errors in 38.01s;
  root `dotnet test` passes all six modules against PostgreSQL 18.6 on Linux x64:
  7,384 passed, zero failed, one existing Windows-only skip in 7m 30.016s.
  API freshness retains 170 pages/2,254 members; the site builds 212 pages and
  checks with zero errors, warnings or hints. Follow-up hosted CI
  [36241459031](https://github.com/willibrandon/ankus/actions/runs/36241459031)
  passes the Ubuntu 24.04 x64/PostgreSQL 18.6 full suite (7,384 passed, one
  existing Windows-only skip) in an 18m 22s job. macOS 15 ARM64/PostgreSQL 18.6
  passes 7,382 cases with three existing platform skips in 22m 49s. Windows
  Server 2025 x64/PostgreSQL 17.11 passes 7,383 cases with two existing platform
  skips in 29m 34s. All three use Clang 20.1.8. The entire CI run and docs
  deployment pass. The earlier compiler failure did not recur in the
  successful Ubuntu run; its cause remains under investigation.

- 2026-09-26 — Added an internal managed declaration emitter for the complete
  selected-header record graph. It retains exact struct/union offsets and sizes,
  nested array strides, native enum representations, anonymous declaration
  identity and promoted members. Generated bitfield properties preserve signed
  and unsigned values through 128 bits, reject out-of-range writes before changing
  storage, and leave neighboring fields and padding intact. Valid native names
  that collide with enclosing managed types or CLR enum names receive distinct,
  deterministic managed names.

  Exact CLR numeric mappings require the compiler-observed numeric model.
  Extended native representations retain bytes instead of narrowing to a CLR
  number. Opaque and complete zero-sized declarations remain nonallocatable
  metadata; flexible tails require a live address and caller-owned extent.
  Their checked address arithmetic preserves the pointer's high bit and rejects
  overflow. Native alignment is retained as metadata, without promising native
  over-alignment for managed stack copies. Malformed by-value cycles, enum
  representations and missing target identities fail explicitly. Assembly
  identity includes the complete graph and generated source, independently of
  root dictionary enumeration order; generated initialization rejects an
  incompatible host.

  | Requirement | Concrete witnesses |
  |---|---|
  | Actual native record/union values, nested arrays, Boolean storage and recursive addresses | `ManagedRecordsPreserveNativeValues` compares compiled C# with an independent optimized C program |
  | Exact bitfield extrema, signed extension, unchanged neighboring bytes and rejection recovery | `ManagedBitfieldsPreserveNativeStorage`, `ManagedWideBitfieldsPreserveNativeExtrema`, `ManagedHugeIntegersPreserveNativeBits` |
  | Anonymous type identity, promoted fields, native enum extrema, name collisions and promoted flexible-tail offsets | `ManagedAnonymousRecordsRetainIdentity`, `ManagedRecordNamesAndPromotedTailsPreserveValues` |
  | Long-double bytes, measured over-alignment and untouched neighboring storage | `ManagedExtendedValuesRetainRepresentation` |
  | Opaque versus complete-zero storage, null/negative/overflow/zero-length tails and high-bit addresses | `ManagedRecordsRetainIncompleteStorage`, `ManagedRecordContractsDistinguishEmptyStorage` |
  | Invalid graphs reject and recover; complete identity is deterministic; empty selections and host rejection execute | `ManagedRecordContractsRejectInvalidGraphs`, `ManagedRecordContractsRetainCompleteIdentity`, `ManagedRecordContractsValidateHostWithEmptySelection` |

  The 32 focused cases pass on Linux x64 in 2.033s and Windows x64/.NET 10.0.12
  in 2.532s, without failures or skips. A separate real PostgreSQL
  18.6 selected-header experiment collects seven declarations and 77 types for
  `CreateStatistics`, `FullTransactionIdFromU64` and `pg_atomic_read_u32`. The
  emitted C# compiles with AOT compatibility diagnostics enabled. Thirteen size
  and value observations agree exactly with independently compiled C, including
  full-width transaction/object IDs, node tags, Boolean fields and union storage.
  A larger selection spanning executor startup, tuple formation, memory
  allocation, error copying and utility dispatch collects 318 declarations and
  2,553 types; its entire emitted source also compiles. Its `ErrorData` size and
  representative values agree with independently compiled C.
  This is compiler/value evidence, not backend execution through the new emitter.

  The Release build passes with zero warnings/errors in 20.94s. API freshness
  retains 170 pages/2,254 members; the site builds 212 pages and checks with zero
  errors, warnings or hints. Plain root `dotnet test` passes all six modules
  against PostgreSQL 18.6 on Linux x64: 7,416 passed, zero failed and one existing
  Windows-only cleanup skip in 7m 31.199s.

  Hosted CI [36243164812](https://github.com/willibrandon/ankus/actions/runs/36243164812)
  passes for `016b926`, including quality and all three complete platform suites
  with Clang 20.1.8. Ubuntu 24.04 x64/PostgreSQL 18.6 passes 7,416 cases with
  one existing platform skip in a 14m 28s job; macOS 15 ARM64/PostgreSQL 18.6
  passes 7,414 with three existing skips in 15m 51s; Windows Server 2025
  x64/PostgreSQL 17.11 passes 7,415 with two existing skips in 27m 50s.
  The documentation deployment also passes.

  Contributor documentation describes the boundary. Public user guides keep the
  currently supported APIs. The emitter is not yet wired to the SDK or an
  installed command: replacing the pinned node emitter must preserve its node
  contracts in one companion assembly. Typed calls, active signature/export
  selection, aligned native allocation, managed callback lifetimes, atomic
  operations, variadics, globals/hooks and the full PostgreSQL/platform matrix
  remain required port work.

- 2026-09-26 — Replaced the SDK's node-only emitter with the complete
  selected-header record emitter. One companion now contains node declarations
  and every native type reached through their fields, pointers and callback
  signatures. Unevaluated native type roots retain existing names for anonymous
  embedded values. The compiler graph must agree with independently executed C
  probes on node layouts and named enum representations/constants. Node cast
  rules, concrete allocation metadata, ordinary managed alignment and typed
  flexible-tail accessors retain their existing contracts.

  Full-header validation exposed an old probe error: an embedded field's packed
  alignment was being used as its type's alignment. The probe now measures the
  type independently of placement; incomplete arrays use their element type.
  PostgreSQL `BlockIdData` consequently retains its required two-byte alignment
  even when reached through packed `ItemPointerData`. Added a validated graph
  representation for Clang's resolved `typeof` expressions. Unknown or
  contradictory representations still fail explicitly.

  The SDK accepts explicit `AnkusClangPath` and `AnkusLibClangPath` settings.
  Clang declaration collection is separate from the Native AOT C toolchain.
  Compiler/worker processes finish before their large temporary AST artifacts
  are removed, including on failure. The obsolete emitter has been removed.
  Public guides describe the supported declarations, compiler prerequisites
  and configuration; engineering commands remain in contributor documentation.

  | Requirement | Concrete witnesses |
  |---|---|
  | Actual native identities, anonymous values, added fields and enum values, unchanged cast metadata | `CompiledNodeRecordRootsRetainNativeIdentity` compares independent C and emitted C# values |
  | Packed placement preserves required native type alignment and ordinary managed embedding | `CompiledNodeRecordsPreservePackedEmbedding` compares independent native/managed sizes, offsets and values |
  | Reject mismatched targets, roots, storage, enums and tags, with valid recovery | `NodeRecordContractsRejectContradictions`, `TypeofRecordContractsRejectContradictions` |
  | Empty selections, command boundaries and cancellation preserve state | `CompiledNodeRecordsAllowEmptyValueSelection`, `InvalidSourceCommandAritiesFailExplicitly`, `CancelledSourceCommandPreservesExistingCompanion` |
  | Existing inheritance, aliases, PG13–19 Value rules, target-sized scalars, enum bits and typed tails | Existing compiled node, value, alias, enum and flexible-padding tests now execute the shared emitter |
  | Cross-project dependency identity, Native AOT/backend use, reference-only rebuild and cleanup | `SdkSharesNativeTypesAcrossProjectsAndPublishesThem` now exchanges `ErrorData` as well as node values |
  | Failed worker load preserves companion bytes, removes temporary ASTs and permits deterministic recovery | `PackagedNodeBindingFailurePreservesCompanionAndRecovers` |

  Local Linux x64/PostgreSQL 18.6 validation measures 499 values and 3,694
  fields, then collects 826 declarations and 4,279 types. All emitted source
  compiles with AOT compatibility diagnostics enabled. Fourteen size, alignment,
  packed-field, node-value and bitmap-tail observations match an independent
  optimized C executable. Windows x64/Clang 21.1.7/MSVC independently measures
  491 values/3,600 fields for PostgreSQL 17.7 and 499 values/3,694 fields for
  PostgreSQL 18.1. Their graphs contain 808 declarations/4,180 types and
  826 declarations/4,268 types respectively; both emitted companions compile
  with zero warnings/errors. These local Windows checks are compiler evidence,
  not backend execution.

  The complete Linux build-tool module passes 632 cases with zero failures and
  one existing Windows-only cleanup skip in 4.583s. All 53 focused contracts and
  compatibility cases pass on Windows x64/.NET 10.0.12 in 4.460s, without failures
  or skips. The Release solution build passes with zero warnings/errors in
  37.68s. API freshness retains 170 pages/2,254 members; the site builds 212 pages
  and checks with zero errors, warnings or hints. Plain root `dotnet test`
  passes all six modules against PostgreSQL 18.6 on Linux x64: 7,459 passed,
  zero failed and one existing Windows-only cleanup skip in 10m 10.149s.
  This includes packaged Native AOT/backend execution, cross-project dependency
  identity, failed worker cleanup and deterministic recovery. The CI quality job
  now configures the same Clang frontend as the platform jobs; its automation
  app builds successfully. Hosted CI
  [36245904717](https://github.com/willibrandon/ankus/actions/runs/36245904717)
  passes quality, all runtime jobs and the macOS 15 ARM64/PostgreSQL 18.6 full
  suite: 7,457 passed and three existing platform skips in a 21m 43s job, with
  Clang 20.1.8. Documentation deployment passes. Ubuntu's five other modules
  pass, but its integration run is cancelled at the 20-minute job timeout while
  a packaged extension build is still running; its log reports no test failure
  before cancellation. Windows Server 2025 x64/PostgreSQL 17.11 completes its
  31m 46s job with 7,457 passed, one failed and two existing platform skips.
  `NewSolutionRunsManagedAndBackendTests` fails during binding generation for
  its nested project path; all other tests pass. The failure is repaired in the
  subsequent compiler-staging milestone below.

  General typed native calls, active signature/export selection, aligned native
  call storage, callback lifetimes, atomic operations, variadics, globals/hooks,
  remaining runtime/tooling inventories and the complete PostgreSQL/platform
  matrix remain required full-port work.

- 2026-09-26 — Native call-body emission can now select fixed functions from
  one complete declaration graph containing globals, variadic functions and
  unprototyped declarations. The full graph remains validated, including roots
  outside the body selection. Unknown and duplicate names reject; selecting an
  unsupported body still fails explicitly. Empty selections preserve validation
  and emit no invented call. This permits the eventual shared companion to keep
  one type identity across different native access mechanisms.

  Complete Windows header inspection exposed a generated C qualifier bug:
  argument typedefs already retain native `const`, so adding another `const` to
  the read cast triggers MSVC C4114. The cast now uses the exact declared typedef.
  Original const/volatile qualifiers, pointer identity and writable pointees
  retain their semantics. A compiler-executed regression first reproduced the
  error with MSVC warnings treated as errors, then passed after the correction.

  | Requirement | Concrete witnesses |
  |---|---|
  | Fixed scalar/aggregate calls from a mixed graph, exact values, unchanged arguments and no unused links | `NativeCallBodiesSelectWithinCompleteGraph` executes optimized C for two bodies and an empty selection |
  | Deterministic selection, unknown/duplicate/unsupported rejection, invalid unselected roots and valid recovery | `NativeCallBodySelectionsRetainCompleteValidation` also verifies unchanged companion identity and complete roots |
  | Direct and typedef qualifiers, volatile reads, const pointer objects and read-only/writable pointees | `NativeCallBodiesPreserveQualifiedArguments` executes compiled values with Clang on Linux and MSVC on Windows |

  A complete Linux x64/PostgreSQL 18.6 header experiment finds all 8,645 pinned
  functions and 579 globals. All reconstructed signature checks compile. Its
  transitive graph has 1,038 declarations and 14,828 types; the entire managed
  declaration source compiles with AOT compatibility diagnostics. All 8,629 fixed
  C call bodies compile; the other 16 functions are variadic. An optimized object
  has 8,092 imports, of which 61 are absent from the backend executable and its
  linked dependencies, including PL/pgSQL functions and module entry points.
  Header presence therefore cannot establish the correct export/module scope.
  These are compiler and symbol observations, not backend execution through a
  complete managed raw API.

  Windows x64/PostgreSQL 17.7 exposes 8,304 of the 8,339 pinned function names
  and 544 of 546 globals. The 37 absent names remain explicitly inventoried;
  build configuration and minor-version differences need target-aware selection.
  All available signatures collect and their reconstructed checks compile. The
  graph has 993 declarations and 14,152 types; its complete managed source builds
  with zero warnings/errors. Emission produces 8,289 fixed bodies, but the full
  MSVC body check rejects `pg_spin_delay_impl`: its MSVC header branch provides a
  macro instead of an addressable function. This compiler-dependent shim case
  remains required integration work. No compiler diagnostics were suppressed.

  All 26 focused call-body cases pass on Linux x64 in 2.250s and Windows
  x64/.NET 10.0.12 in 4.252s. The final Release solution build passes with zero
  warnings/errors in 18.39s. API freshness retains 170 pages/2,254 members;
  documentation builds 212 pages and checks with zero errors, warnings or hints.
  Plain root `dotnet test` passes all six modules against PostgreSQL 18.6 on
  Linux x64: 7,462 passed, zero failed and one existing Windows-only cleanup skip
  in 10m 00.941s, including the packaged Native AOT/backend paths.
  Contributor documentation records the internal boundary. Public guides keep
  the currently supported APIs.

  Raised the Ubuntu full-suite CI timeout from 20 to 30 minutes after the
  preceding milestone hit its limit during package validation. The full suite
  remains enabled in each platform job; every job stays within the authorized
  40-minute maximum.

  General typed consumer methods, active-extension signature/export selection,
  aligned call storage, compiler-specific inline/macro shims, callbacks,
  variadics, global/hook access, remaining feature inventories and the full
  PostgreSQL/platform matrix remain required for the complete port.

- 2026-09-26 — Repaired the Windows generated-solution CI failure from
  run 36245904717. Nesting the node compiler's temporary directory beneath a
  long project output path exceeded Windows' process working-directory limit.
  A local Windows x64/PostgreSQL 17.7 reproduction failed to start Clang with
  Win32 error 267, then passed after moving staging into a uniquely owned system
  temporary directory. The final companion still goes to the requested output
  path. Compiler and worker completion precede cleanup on success and failure.

  `PackagedNodeBindingFailurePreservesCompanionAndRecovers` now uses a 220-character
  output path and a separate temporary root selected only for its child process.
  It verifies staging cleanup after success, a failed libclang load and recovery;
  all four companion artifacts remain byte-identical, and an unrelated temporary
  sentinel survives. The same success/failure/recovery sequence passes directly
  on Windows x64/PostgreSQL 17.7 with Clang 21.1.7 and MSVC. These local Windows
  checks exercise the compiler command, not the complete backend suite.

  Generated-solution build logs now survive test-project cleanup under the CI
  artifact directory, so a future outer MSBuild failure retains its underlying
  diagnostic. Contributor documentation records this behavior. The public-guide
  audit finds only extension-author commands; maintainer commands remain in
  contributor documentation and `eng/README.md`.

  Four focused build-command tests pass, and both affected integration tests pass
  against PostgreSQL 18.6 on Linux x64 in 2m 44.185s, including the generated
  solution's Native AOT/backend tests. The final Release build has zero warnings
  and errors in 16.78s. API freshness retains 170 pages/2,254 members; the site
  builds 212 pages and checks with zero errors, warnings or hints.
  Plain root `dotnet test` passes all six modules against PostgreSQL 18.6 on Linux
  x64: 7,462 passed, zero failed and one existing Windows-only cleanup skip in
  10m 29.539s. Hosted CI run 36249125032 for `eacf84b` also passes all three
  complete platform suites: Ubuntu 24.04 x64/PostgreSQL 18.6 has 7,462 passed
  and one platform skip in a 23m 24s job; Windows Server 2025 x64/PostgreSQL
  17.11 has 7,461 passed and two platform skips in 23m 20s; macOS 15
  ARM64/PostgreSQL 18.6 has 7,460 passed and three platform skips in 22m 52s.
  Quality, runtime and documentation jobs pass too. Every job stays below its
  configured timeout and the authorized 40-minute maximum.

  General typed consumer calls, active signature/export selection, aligned call
  storage, compiler-specific shims, callbacks, variadics, globals/hooks, remaining
  feature inventories and the full PostgreSQL/platform matrix remain required.

- 2026-09-26 — Added the internal native-object import selector needed to compile
  only referenced raw call bodies from one complete shared declaration graph.
  It reads relocatable ELF, COFF/BigObj and Mach-O metadata, retains undefined
  generated accessor names, normalizes platform C-name decoration and verifies
  object format, architecture and byte order against the selected header target.
  Bounded table reads reject malformed metadata and invalid UTF-8 in selected
  names. Definitions, local symbols, common storage and unused declarations do
  not select bodies. The resulting immutable import set is deterministic.

  The selector validates the entire graph, including unselected roots, before
  emitting the selected fixed bodies and pure address accessors. Globals,
  variadics, unprototyped functions and unknown names still fail explicitly.
  Obtaining an address does not execute PostgreSQL; invoking its body still
  requires the existing native error guard. Bodies and accessors have hidden
  visibility on Unix and no Windows export annotation.

  Independent compiler objects cover ELF, COFF and Mach-O, 32/64-bit targets,
  weak/hidden imports, empty selections and MSVC's forced BigObj output.
  Independently encoded fixtures exercise auxiliary records, extended ELF
  section indices, byte order, duplicate names, malformed tables, every fixture
  truncation, unchanged inputs and valid recovery. A real consumer object links
  against the selected generated C and executes exact results and call counts;
  unused bodies need no link target. Actual Linux and Windows Native AOT objects
  also retain the exact imports independently reported by LLVM's symbol reader.
  These object and standalone C checks do not establish PostgreSQL backend
  behavior or a general managed consumer API.

  All build-tool tests pass on Linux: 672 passed, zero failed and two Windows-only
  skips in 5.627s. All 65 focused native-call/import cases pass on Windows x64
  in 1.983s. The Release build has zero warnings and errors in 25.83s; API
  freshness retains 170 pages/2,254 members, and the site builds 212 pages and
  checks with zero errors, warnings or hints. Plain root `dotnet test` passes
  all six modules against PostgreSQL 18.6 on Linux x64: 7,499 passed, zero failed
  and two Windows-only skips in 10m 26.724s.

  Contributor documentation records the internal boundary. Typed companion
  methods, active signature/export selection, aligned call storage and SDK
  post-ILC integration remain required, alongside compiler-specific shims,
  callbacks, variadics, globals/hooks, remaining feature inventories and the
  complete PostgreSQL/platform matrix. Public guides retain supported APIs.

- 2026-09-26 — Enforced IDE0251 as an error for every repository C# project in
  the root `.editorconfig`, with `csharp_style_prefer_readonly_struct_member`
  enabled at error severity. Recorded the requirement in `AGENTS.md` and
  contributor documentation. Fixed all twelve eligible private members: two
  CBOR reader helpers and ten native-object reader helpers, including dependent
  members identified after the initial fixes. No diagnostics are suppressed or
  relaxed, and the consumer templates retain their existing policies.

  Focused verification passes 82 serialization cases, 64 native-call/import
  cases plus one Windows-only skip on Linux, and all 65 native cases on Windows
  x64. These modifier changes retain the existing serialization and native
  import behavior without changing public API signatures or native layouts.

  The main Release build passes with zero warnings and errors in 58.12s. API
  freshness retains 170 pages/2,254 members; the site builds 212 pages and checks
  with zero errors, warnings or hints. Plain root `dotnet test` with IDE0251
  enabled passes all six modules against PostgreSQL 18.6 on Linux x64:
  7,499 passed, zero failed and two Windows-only skips in 10m 12.972s.

- 2026-09-26 — Separated the complete binding validator from node-specific
  layout and formatting code. Node views and the hidden generated-call runtime
  contract now use the same active extension identity and PostgreSQL-major
  check beneath the native error guard. Validation is repeated for each current
  callback; it cannot be reused across nested extension providers. Mismatches
  retain SQLSTATE `0A000` with a diagnostic that applies to any generated binding.

  | Requirement | Concrete witnesses |
  |---|---|
  | Exact identity bytes and major, without normalization or cached success | `GeneratedCallBindingPreservesContract` checks transport, unchanged inputs and repeated requests |
  | Missing, masked, ended and worker scopes; nested-provider restoration | `GeneratedCallBindingRequiresActiveScope` checks rejection before transport and exact provider order |
  | Owned native diagnostic release and retry | `GeneratedCallBindingErrorsReleaseDiagnosticsAndRecover` checks all diagnostic fields, three releases and subsequent validation |
  | Backend identity/major rejection, unchanged result and same-session recovery | `GeneratedRawCallsValidateActiveBindingAndRecover` exercises seven mismatch partitions around successful native aggregate calls |

  Focused verification passes 48 runtime cases and 15 generator cases. The
  published Native AOT extension passes all 48 raw-call and node backend cases
  on PostgreSQL 18.6/Linux x64 in 1m 43.717s, including the seven new mismatch
  cases. These tests use the current node companion and standalone generated
  call bodies; they do not establish a complete typed raw-call companion.

  The Release build passes with zero warnings and errors in 13.84s. API
  freshness retains 170 pages/2,254 members; the site builds 212 pages and checks
  with zero errors, warnings or hints. Plain root `dotnet test` passes all six
  modules on PostgreSQL 18.6/Linux x64: 7,512 passed, zero failed and two existing
  Windows-only skips in 10m 00.050s.

  General typed methods, header/signature identity consistency, aligned argument
  storage, active export selection and SDK post-ILC integration remain required.
  Compiler-specific shims, callbacks, variadics, globals/hooks, remaining feature
  inventories and the complete PostgreSQL/platform matrix remain unfinished.
  Contributor documentation describes the common internal check; public guides
  continue to describe only the supported consumer surface.

- 2026-09-26 — Raised the Windows full-suite CI timeout from 35 to the
  authorized maximum of 40 minutes after
  [CI run 36252581940](https://github.com/willibrandon/ankus/actions/runs/36252581940)
  for `c05209a` hit that limit while executing the PostgreSQL suite. Its five
  completed Windows modules passed; integration did not finish, so that run
  supplies no complete Windows backend validation. Linux passed 7,499 cases with
  two platform-specific skips; macOS passed 7,497 with four platform-specific
  skips. Both had zero failures. Documentation and quality checks passed too.
  Linux remains at 30 minutes and macOS at 25 minutes. The complete test suite
  remains enabled in each job; analyzer enforcement and platform coverage are
  unchanged. The next Windows run must complete within the 40-minute maximum.

- 2026-09-26 — Added agreement checks between the independent selected-header
  signature trees and their measured native type graph before record publication
  or call-body emission. Every root is checked, including unselected functions
  and globals and an empty body selection. Written and canonical types must
  preserve scalar/typedef identity, qualifiers, nested pointers and arrays,
  tag kind/completeness, prototype shape and calling convention. Equal size and
  alignment cannot make incompatible native types interchangeable.

  The comparison preserves C parameter adjustment, Boolean spelling and array
  element qualification. It retains the distinction between an unspecified
  parameter list and a fixed prototype; unprototyped calls remain unsupported.
  Anonymous typedef observations must consistently bind to one declaration.

  | Requirement | Concrete witnesses |
  |---|---|
  | Same-size integer/float/unsigned substitutions reject | `NativeCallContractsRejectSameSizeTypeChanges` |
  | Nested pointers, array extents, callbacks, qualifiers and tag identities agree | `NativeCallContractsRejectDifferentParameterTypes` |
  | Both written and canonical prototypes constrain arguments | `NativeCallContractsValidateWrittenAndCanonicalTypes` |
  | Anonymous typedef edges retain declaration identity | `NativeCallContractsPreserveAnonymousIdentity` |
  | Prototype, calling convention, declaration kind, typedef spelling and nesting failures reject | `NativeCallContractsRejectChangedIdentity` |
  | Unselected functions/globals remain validated, including empty selection | `NativeCallContractsValidateUnselectedSymbols` |
  | Qualified multidimensional arrays and adjusted function parameters execute correctly | `NativeCallContractsPreserveCompilerTypeSemantics` |

  All build-tool tests pass on Linux x64: 699 passed, zero failed and two existing
  Windows-only skips in 5.602s. The production validator also accepts all 9,224
  saved PostgreSQL 18.6/Linux x64 signatures and all 8,848 available saved
  PostgreSQL 17.7/Windows x64 signatures in written and canonical form. These
  archived observations verify compiler representation agreement; they are not
  a fresh Windows or PostgreSQL backend run. A separate current Windows x64
  run passes all 56 focused native-call/import cases with .NET 10.0.12, Clang
  and MSVC in 2.787s. These are standalone compiler/execution tests, not a
  complete Windows backend suite.

  Release passes with zero warnings and errors in 29.08s. API freshness retains
  170 pages/2,254 members; the site builds 212 pages in 3.00s and checks with zero
  errors, warnings or hints. Plain root `dotnet test` passes all six modules on
  PostgreSQL 18.6/Linux x64: 7,539 passed, zero failed and two existing Windows-only
  skips in 10m 14.319s, including the packaged Native AOT/backend paths.

  Prior hosted [CI run 36254734532](https://github.com/willibrandon/ankus/actions/runs/36254734532)
  for `a666022` passes Linux x64/PostgreSQL 18.6 with 7,512 cases and two existing
  platform skips in a 23m 03s job, and macOS ARM64/PostgreSQL 18.6 with 7,510 cases
  and four existing platform skips in a 20m 52s job. Both have zero failures.
  Quality, runtime and documentation jobs pass; Windows is still running at the
  pre-commit check and does not yet supply complete backend evidence.
  `AGENTS.md` now records the requested workflow: continue while CI runs, check
  and record previous outcomes before each commit, and resolve reported failures
  as work proceeds. The hard per-job maximum remains 40 minutes.

  These checks do not independently prove every transitive record member or
  distinguish anonymous declarations if all corresponding observations change
  together. Complete compiler checks of member/layout projections, typed managed
  methods, aligned argument storage, native/linkage identity, active export
  selection and SDK post-ILC integration remain required, along with the existing
  compiler-specific shim, callback, variadic, global/hook, feature-inventory and
  PostgreSQL/platform-matrix work. Contributor documentation records this
  internal boundary; public guides retain the supported consumer surface.

- 2026-09-26 — Recorded the completed hosted checks for `97d6431` before the
  next milestone. [CI run 36256704901](https://github.com/willibrandon/ankus/actions/runs/36256704901)
  and [documentation run 36256704952](https://github.com/willibrandon/ankus/actions/runs/36256704952)
  both pass, including quality and all three runtime jobs.

  | Full-suite platform | PostgreSQL | Passed | Failed | Existing platform skips | Job duration |
  |---|---|---:|---:|---:|---|
  | Linux x64 | 18.6 | 7,539 | 0 | 2 | 22m 09s |
  | macOS ARM64 | 18.6 | 7,537 | 0 | 4 | 17m 53s |
  | Windows x64 | 17.11 | 7,539 | 0 | 2 | 36m 39s |

  Each platform ran the complete suite against its real PostgreSQL server.
  Windows completed within the authorized 40-minute maximum; its limit remains
  40 minutes, Linux remains 30 and macOS remains 25. No tests were sharded or
  removed to meet those limits. This is platform evidence for the implemented
  surface, not completion of the unresolved full-port requirements above.

  The preceding `a666022` Windows job in
  [run 36254734532](https://github.com/willibrandon/ankus/actions/runs/36254734532)
  was superseded by the `97d6431` push after 30m 43s. Its five completed modules
  passed; its unfinished integration module did not establish backend parity.
  The completed `97d6431` run now supplies that milestone's Windows evidence.

- 2026-09-26 — Added independent native compiler and executable verification for
  collected record graphs. The internal `binding-record-checks` command rebuilds
  C type declarations against the selected PostgreSQL headers, checks named
  member types and offsets, complete sizes and alignments, typedef/canonical
  compatibility, record identity and enum representation/constants. Prepared
  bytes read through real named bitfields verify physical offsets, widths and
  signedness without relying on generated-source text as proof.

  Publication occurs only after compilation and execution succeed. Compiler
  errors, physical-layout failures and late cancellation preserve the prior
  verified source, remove owned staging and permit a subsequent successful run.
  Cancelled native processes and their output streams are joined before cleanup.
  Target checks are shared with native call generation.

  Standard `va_list` bridges Clang's private spelling to the selected native
  compiler. Function redeclarations preserve C parameter adjustment on MSVC;
  canonical comparisons remove only parameter-level qualification. PostgreSQL's
  `pg_spin_delay_impl` macro fallback receives an addressable C shim, and generated
  call bodies execute that implementation without dropping the catalog entry.
  A separate adversarial probe also exposed acceptance of a self-canonical field
  alias. The shared graph validator now rejects canonical spelling wrappers and
  detects alias/wrapper cycles, including types reachable only through fields.
  Enum constants compare sign independently of numeric equality, preventing C's
  unsigned conversions from accepting `-1` as an unsigned 64-bit maximum or the
  reverse. A real compiler probe reproduced both failures before the fix.

  | Requirement | Concrete witnesses |
  |---|---|
  | Same-size substitutions, qualifiers, arrays, callbacks, offsets, packing and constants reject | `NativeRecordChecksRejectChangedMembers` |
  | Bitfield position, width and signedness agree; 65-bit values cannot truncate into 64-bit evidence | `NativeRecordChecksRejectChangedBitfields`, `NativeRecordChecksRejectWiderNativeBitfields` |
  | Recursive storage, wide fields, integer extrema, vectors, atomics and complex values retain compiler representation | `NativeRecordChecksPreserveRecursiveStorage`, `NativeRecordChecksPreserveWideBitfields`, `NativeRecordChecksPreserveAnnotatedTypes` |
  | Enum signedness/width/constants and distinct record identities remain checked | `NativeRecordChecksRejectEnumRepresentationChanges`, `NativeRecordChecksRejectWrappedEnumConstants`, `NativeRecordChecksRejectMergedDeclarations` |
  | Adjusted callbacks, qualified prototypes and standard variadic storage compile | `NativeRecordChecksRetainCompilerParameterAdjustment`, `NativeRecordChecksPreserveQualifiedFunctionParameters`, `NativeRecordChecksPreserveStandardVariadicStorage` |
  | Field-only canonical corruption and self/mutual wrapper cycles reject | `NativeRecordChecksRejectFieldAliasCorruption` |
  | Failed verification/cancellation preserves prior output and supports recovery | `NativeRecordChecksPublishOnlyVerifiedContracts`, `NativeRecordChecksValidateCommandBoundaries` |
  | Function and macro implementations execute once; invalid argument counts cannot enter | `NativeCallBodiesPreserveCompilerMacroFallback` |

  Fresh compiler/executable verification of the complete saved observations passes
  for PostgreSQL 18.6/Linux x64 (1,038 declarations, 9,224 roots) and PostgreSQL
  17.7/Windows x64 (993 declarations, 8,848 available roots). Complete native body
  compilation also passes for all 8,629 available fixed functions on Linux and
  8,289 on Windows. These use the installed native headers and Clang/MSVC; they
  are standalone compiler checks, not another complete backend run. The build
  module passes 735 cases with zero failures and two existing Windows-only skips
  on Linux in 6.598s. All 92 focused native record/call/import cases pass on Windows
  x64 with .NET 10.0.12 in 6.717s.

  Release passes with zero warnings/errors in 25.37s. API freshness retains
  170 pages/2,254 members; the site builds 212 pages in 2.62s and checks with zero
  errors, warnings or hints. Plain root `dotnet test` passes all six modules on
  PostgreSQL 18.6/Linux x64: 7,575 passed, zero failed and two existing Windows-only
  skips in 9m 51.443s, including the published Native AOT/backend paths.
  The pre-commit recheck confirms `97d6431` CI run 36256704901 and documentation
  run 36256704952 remain successful; their complete platform outcomes are recorded
  above. The milestone adds 36 test cases; analyzer enforcement and CI timeout
  limits remain unchanged.

  The verifier explicitly rejects unnameable anonymous containers: promoted
  leaves alone cannot establish their hidden size/alignment. Collection and
  managed projection still retain those containers. MSVC enum compatibility
  cannot independently prove nominal enum identity; source observations retain
  that identity. Complete generic verification and automatic typed-companion
  enforcement remain required, along with typed managed methods, aligned argument
  storage, native/linkage identity, active export selection, SDK post-ILC
  integration, other compiler shims, callbacks, variadics, globals/hooks, complete
  feature inventories and the PostgreSQL/platform matrix. Contributor docs record
  the internal command and these limits; no unsupported consumer API is advertised.

- 2026-09-26 — Added typed managed native-call generation beside the complete
  measured record/enum graph. C bodies and C# methods share one validated fixed
  call model, preserving declared storage, native parameter adjustment and true
  void results. Companion identity includes the complete header catalog's native
  names, linker aliases and metadata as well as the target and record graph.
  Graph-only/node generation retains its existing identities. Invalid selections
  and corrupt unselected roots reject even when no methods are requested.

  Each method revalidates the active binding before importing a pure native body
  address, then invokes the existing guarded raw-call boundary. Descriptors and
  exact argument bytes share independently aligned storage. Frames up to 4 KiB
  use bounded stack storage; larger frames use checked native allocation lengths
  and allocator-matched `finally` cleanup. Native zero-size objects retain distinct
  logical CLR values while transporting zero bytes; true void uses no result
  destination. Pointer and callback lifetime obligations remain explicit.

  Executable boundary tests found and fixed unnecessary C# member hiding and an
  intermediate overflow that rejected the final representable frame length.
  Windows compiler execution also exposed MSVC's function-conditional `typeof`
  decay behavior. Adjusted function storage now addresses the original written
  function type directly, retaining its typedef identity; array adjustment still
  preserves otherwise inaccessible native typedef storage. Empty-record tests
  retain the native ABI's actual zero/nonzero storage on each target.

  | Requirement | Concrete witnesses |
  |---|---|
  | Full native/linker identity, deterministic selection, shared declarations and complete validation | `ManagedCallIdentityPreservesNativeSymbols`, `ManagedCallIdentityIgnoresDictionaryOrder`, `ManagedCallsRetainCompleteValidation` |
  | Exact records, integer extrema, enums, Boolean values, addresses and extended floating bytes | `ManagedCallsPreserveNativeValues` |
  | Written array/function adjustment and actual native callback execution | `ManagedCallsPreserveAdjustedArguments` |
  | Actual 64-byte alignment for stack and heap frames; stack boundaries and 300 arguments | `ManagedCallsAlignNativeFrames`, `ManagedCallsBoundStackStorage`, `ManagedCallsBoundManyArguments` |
  | Final representable 32/64-bit lengths reject only the first overflowing byte | `ManagedCallFramesRejectUnrepresentableStorage` |
  | True void, distinct empty objects and C#/accessor name collisions | `ManagedCallsPreserveEmptyValues`, `ManagedCallsPreserveMemberNames` |
  | Missing/nested wrong bindings reject before lookup; native errors and allocation failure preserve cleanup and recovery | `ManagedCallsValidateActiveBinding`, `ManagedCallsRevalidateNestedBinding`, `ManagedCallsRecoverFromNativeErrors`, `ManagedCallsRecoverFromAllocationFailure` |
  | Real LibraryImport generation, Native AOT linking, exact execution and removal of unavailable unused imports | `PublishedManagedCallsUseNativeAccessors` |

  The Native AOT witness publishes and runs a standalone executable, then reads
  its actual ILC object and checks that only the two used accessors select native
  bodies. Native function execution and exact results are real; error transport
  uses a scoped public-ABI test provider, not PostgreSQL ERROR/backend execution.
  Counting allocator collaborators in the focused compiler tests still allocate
  and free real native bytes, making omitted or mismatched cleanup observable.

  The build module passes 759 cases with zero failures and two existing
  Windows-only skips on Linux x64 in 7.187s. All 82 focused native call cases pass
  on Windows x64 with .NET 10.0.12 in 6.169s, including the standalone Native AOT
  executable. The milestone adds 24 cases. Release passes with zero warnings and
  errors in 37.28s; API freshness retains 170 pages/2,254 members. The site builds
  212 pages in 2.85s and checks with zero errors, warnings or hints. Plain root
  `dotnet test` passes all six modules against PostgreSQL 18.6/Linux x64:
  7,599 passed, zero failed and two existing Windows-only skips in 9m 52.994s,
  including the published Native AOT/backend paths already in the suite.

  Prior-commit CI was checked while development continued: `1234b75` CI run
  36262971887 and documentation run 36262971802 both succeeded. Complete platform
  jobs passed on PostgreSQL 18.6/Linux x64 (7,575 passed, zero failed, two existing
  Windows-only skips; 22m30s), PostgreSQL 18.6/macOS ARM64 (7,573 passed, zero
  failed, four existing platform skips; 18m03s), and PostgreSQL 17.11/Windows x64
  (7,575 passed, zero failed, two existing platform skips; 36m56s). Quality and
  all runtime jobs also passed. Timeouts remain Linux 30, macOS 25 and Windows
  40 minutes; no job exceeds the user's hard 40-minute limit.
  The final pre-commit recheck confirms every job and both runs remain successful.

  Contributor documentation records this internal emitter. Automatic combined
  node/call companion generation, independent complete record verification,
  active-extension exports, SDK post-ILC native compilation/linking and published
  PostgreSQL execution of these generated methods remain required. Compiler
  shims, callback/lifetime APIs, variadics, globals/hooks, complete feature
  inventories and the PostgreSQL 13–19/platform matrix also remain unfinished.
  General raw bindings are not yet advertised as a supported consumer API.

- 2026-09-26 — Preserved nested native `typeof` contracts while preparing the
  combined node/function companion. Structured Clang type collection now follows
  both expression and type operands to their compiler-resolved types, retaining
  typedefs, qualifiers, array shape and function prototypes. `typeof_unqual`
  removes top-level qualification while preserving pointee qualification.

  The libclang reader recognizes the original wrapper spelling: asking for an
  unqualified type first can desugar a qualified operand and hide the wrapper.
  Signature comparison reconciles libclang's canonical `typeof` resolution with
  the structured AST's written typedefs at that boundary. Written parameter
  qualification remains checked; canonical prototype adjustment cannot silently
  accept a lost `const`. No diagnostic or analyzer enforcement was relaxed.

  | Requirement | Concrete witnesses |
  |---|---|
  | Nested type/expression operands preserve aliases, top-level versus pointee qualification, arrays and prototypes | `HeaderTypesPreserveTypeofSemantics`; generated checks compile and execute with Clang and MSVC, and a changed native qualifier fails compilation |
  | Same-size but incompatible qualifiers, scalars, records, arrays and callbacks reject | Nine compiler-produced pairs in `TypeofSignaturesRejectChangedContracts`; original contracts validate after rejection |
  | Written parameter qualification survives canonical comparison | `TypeofParametersRetainWrittenQualification` rejects removal of the observed qualifier |
  | Generated C#/C calls preserve exact aggregate bytes without evaluating type operands | `ManagedCallsPreserveTypeofArguments` checks high-bit values and unchanged input through real native execution; the referenced type operand remains an undefined extern |

  Research collection now combines every available raw inventory root with every
  required node root in one actual compiler graph. Complete native compiler and
  executable record checks pass on PostgreSQL 18.6/Linux x64 (9,724 roots and
  1,335 declarations) and PostgreSQL 17.7/Windows x64 (9,340 roots and 1,284
  declarations). Managed projection of those observations compiles without
  warnings or errors, retaining 8,629 and 8,289 fixed methods respectively.
  Actual top-level header discovery independently reproduces both existing raw
  selections: all 9,224 inventory entries on Linux, and 8,848 available entries
  plus 37 explicitly absent entries on the installed Windows headers. These are
  compiler observations, not new backend/platform parity claims.

  The isolated full managed projection is approximately 19 MB and took 15–25s
  to compile locally with warm NuGet/compiler caches. This is not a cold-machine
  or hosted CI baseline. Shared, content-verified compiled artifacts and measured
  invalidation/concurrency behavior remain necessary before expanding every SDK
  consumer's companion. Research scripts and staging stay outside tracked source;
  owned large ASTs are removed after observation and verification.

  This milestone adds 12 test cases. All 99 focused native call/typeof cases pass
  on Windows x64 with .NET 10.0.12 in 9.539s. Final Release passes with zero
  warnings/errors in 13.86s. API freshness retains 170 pages/2,254 members; the
  site builds 212 pages in 3.30s and checks with zero errors, warnings or hints.
  Plain root `dotnet test` passes all six modules against PostgreSQL 18.6/Linux
  x64: 7,611 passed, zero failed and two existing Windows-only skips in
  10m 13.963s, including the published Native AOT/backend paths in the suite.

  The pre-commit CI audit confirms `a2e0ab1` CI run 36266240042 and documentation
  run 36266240028 succeeded while development continued. All quality/runtime
  jobs passed. Complete backend platform jobs passed on PostgreSQL 18.6/Linux
  x64 (7,599 passed, zero failed, two existing Windows-only skips; 18m56s),
  PostgreSQL 18.6/macOS ARM64 (7,597 passed, zero failed, four existing platform
  skips; 20m00s), and PostgreSQL 17.11/Windows x64 (7,599 passed, zero failed,
  two existing platform skips; 33m16s). Timeouts remain Linux 30, macOS 25 and
  Windows 40 minutes; no job exceeds the user's hard 40-minute limit.

  Contributor documentation describes the compiler boundary. Production combined
  collection, explicit target availability, verified artifact sharing, SDK
  post-ILC native compilation/linking and typed calls under the real PostgreSQL
  guard remain open. The complete feature inventory, compiler shims, callbacks,
  variadics, globals/hooks and PostgreSQL 13–19/platform matrix remain required;
  this prerequisite does not close the full raw-binding or faithful-port scope.

- 2026-09-26 — Raised the macOS full-suite CI timeout from 25 to 30 minutes
  after the preceding run exhausted its budget during integration testing.
  Linux remains at 30 minutes and Windows at 40; the hard 40-minute cap and
  complete per-platform suite remain unchanged.

  The previous commit `8646654` was audited while independent SDK work continued.
  CI run 36268363214 completed with Linux and Windows successful; macOS was
  cancelled at its 25-minute limit with an AOT compiler still running. No test
  failure was reported before cancellation: the five completed macOS modules
  passed, but the integration module did not finish and is not platform proof.
  Complete PostgreSQL 18.6/Linux x64 validation passed 7,611 cases with zero
  failures and two existing platform skips in 23m31s. PostgreSQL 17.11/Windows
  x64 passed 7,611 cases with zero failures and two existing platform skips in
  36m10s. Quality and all runtime jobs passed. Documentation run 36268363251
  also passed. The next macOS run must establish the completed suite outcome.

  The isolated timeout change passes Release with zero warnings/errors in
  27.82s. The documentation site builds all 212 pages in 3.48s and checks with
  zero errors, warnings or hints. After correcting the isolated checkout's
  missing runtime payload/package setup, plain root `dotnet test` passes all
  six modules on PostgreSQL 18.6/Linux x64: 7,611 passed, zero failed and two
  existing Windows-only skips in 11m 11.393s. The final pre-commit CI recheck
  confirms the previous outcomes above. No implementation, analyzer setting,
  test selection or public API changes are included in this timeout adjustment.

- 2026-09-26 — Fixed production native-probe cleanup on Windows after CI could
  not delete a just-executed `checks.exe`. The node collector and record-check
  publisher now share the bounded cleanup routine used by their tests. Cleanup
  starts after native tools and diagnostic streams finish, retries only known
  Windows file-release errors, and still reports persistent access failures.
  Cancellation does not interrupt removal of owned staging.

  `NativeProbeCleanupWaitsForReleasedFile` exercises a real file handle denying
  delete sharing. `NativeProbeCleanupDoesNotHideAccessDenial` retains a read-only
  file after the bounded failure, and `NativeProbeCleanupRejectsMissingDirectory`
  preserves the missing-directory error. `NativeRecordChecksPublishOnlyVerifiedContracts`
  verifies failed compilation, failed executable checks and late cancellation
  preserve prior output and permit successful recovery. All seven focused cases
  pass on Windows x64/.NET 10.0.12 in 2.297s; Linux passes six with the existing
  Windows-only access-denial skip in 1.942s.

  The prior commit `0d72775` was audited while independent SDK work continued.
  CI run 36271508065 passed PostgreSQL 18.6/Linux x64 (7,611 passed, zero failed,
  two existing skips; 15m25s) and PostgreSQL 18.6/macOS ARM64 (7,609 passed, zero
  failed, four existing skips; 17m37s), plus quality and all runtime jobs.
  PostgreSQL 17.11/Windows x64 failed during temporary executable cleanup:
  the build module passed 772 cases and failed one; integration passed 3,318
  cases with two existing skips. The other four unit modules did not run after
  the build-module failure, so the failed Windows run is not full platform
  validation. Its job finished in 35m29s, below the 40-minute limit.

  The isolated repair passes Release with zero warnings/errors in 29.62s.
  API freshness retains 170 pages/2,254 members; the site builds 212 pages in
  8.06s and checks with zero errors, warnings or hints. Plain root `dotnet test`
  passes all six modules on PostgreSQL 18.6/Linux x64: 7,611 passed, zero failed
  and two existing Windows-only skips in 10m 18.861s. The final pre-commit audit
  confirms the prior CI outcomes above; the latest documentation run,
  36268363251 for `8646654`, remains successful. The next CI run must establish
  the repaired complete Windows suite outcome. No analyzer or timeout setting
  was relaxed.

  Subsequent CI run 36274752988 for `de46baf` has passed PostgreSQL 18.6/Linux
  x64 (all six modules: 7,611 passed, zero failed, two existing skips; job
  22m54s) and PostgreSQL 18.6/macOS ARM64 (all six modules: 7,609 passed,
  zero failed, four existing skips; job 21m23s). Quality, all runtime jobs and
  documentation run 36274753012 have passed. PostgreSQL 17.11/Windows x64
  subsequently passes all six modules: 7,611 passed, zero failed and two existing
  integration skips. Its job finishes in 37m21s, below the 40-minute cap;
  integration takes 31m 36.133s. The repaired run is successful on every job.

- 2026-09-26 — Connected the SDK's shared node/function collection,
  compiled companion cache and post-ILC native-body selection. The collector
  explicitly partitions available and absent top-level declarations, validates
  one complete graph and runs independent native record checks before emitting
  managed declarations. GCC verification now retains the alignment of incomplete
  array typedefs through a completed unevaluated extern witness; the regression
  also rejects a changed over-aligned typedef.

  Compiled artifacts are protected by content validation and cross-process
  leases, with atomic cache publication and consumer-owned output copies.
  Compiler preparation resolves current references/analyzers before lookup;
  same-timestamp input changes, new inputs, malformed artifacts and failed
  production cannot certify stale output. Linux tests cover real competing
  compiler processes, consumer cleanup, compiler failure/recovery and changed
  source/runtime content. Ten cache cases plus the combined node/function
  contract witness pass locally. Twenty-four focused cache, compiler and
  availability cases pass on Windows x64/.NET 10.0.12 in 1m 55.408s; this preceded
  the additional failed-replacement preservation case. The Windows preparation
  cost needs measurement and improvement before complete SDK validation. Bounded
  parallel content reads retain every hash check; the expanded 26-case Windows
  selection passes in 1m 34.894s with fresh compiler processes. The corresponding
  Linux cache/compiler selection passes 12 cases in 13.491s. These are focused
  test timings, not cold/warm SDK build baselines.

  Companion restore now uses the consuming project's resolved feeds, ordered
  NuGet configuration files, package directory and fallback folders. NuGet itself
  applies credentials and source mappings; the build helper no longer substitutes
  nuget.org. Configuration content participates in cache validation. The new
  `CompiledCompanionsHonorOfflineFeedConfigurationAndSourceMapping` test restores
  from a local feed into an empty package cache, rejects a changed source mapping
  before reusing an artifact, preserves the previous consumer assembly and
  recovers after the policy is corrected. It passes on Linux; all three compiler
  process tests pass on Windows x64/.NET 10.0.12 in 42.476s. Full SDK and platform
  validation of this change remains in progress.
  The expanded selection also checks an explicit `RestoreSources` override.
  Four compiler cases pass on Windows in 1m 57.566s; the Linux cache/compiler
  selection passes 14 cases in 13.588s after adopting the shared cleanup helper.
  Restore inherits the consumer's environment for credentials and provider
  plugins; compilation runs separately with controlled settings. The command
  verifies that the selected SDK retained the consumer's restore settings.

  During this work, CI run 36271508065 for timeout commit `0d72775` completed
  successfully on PostgreSQL 18.6/Linux x64 (7,611 passed, zero failed, two
  existing platform skips; 15m25s) and PostgreSQL 18.6/macOS ARM64 (7,609 passed,
  zero failed, four existing platform skips; 17m37s). Quality and all runtime
  jobs passed. Windows subsequently failed when deleting a just-executed native
  probe during record-check publication. Its build module passed 772 cases and
  failed one; the integration module passed 3,318 cases with two existing skips
  on PostgreSQL 17.11. The remaining unit modules did not run after the build
  module failure, so this run does not establish complete Windows platform
  validation. Commit `de46baf` contains the separately verified cleanup repair;
  its new CI run passes all three complete platform suites as recorded above.

  The Hello SDK build passes with 1,335 shared declarations and 8,629 fixed
  methods on PostgreSQL 18.6/Linux x64. A packaged two-project SDK test publishes
  selected native accessors and executes exact high-bit aggregate and native
  node values in PostgreSQL. Its first error-recovery attempt stopped because
  the isolated test cluster excluded PL/pgSQL from its library search path;
  the corrected test passes in 5m 52.790s, including owned native SQLSTATE,
  detail and hint, memory-context restoration, same-session successful retry,
  cross-project type sharing, clean/rebuild and relocated intermediate outputs.
  A subsequent Linux run with consumer restore settings passes in 6m 07.938s.
  The corresponding packaged test passes on PostgreSQL 18.1/Windows x64 with
  .NET 10.0.12 in 10m 11.368s, using a separate Windows checkout after a shared
  Linux/Windows output-directory setup failure. These are focused test results.
  Server-log review then exposed a nonempty SPI stack warning after the caught
  PL/pgSQL error on both platforms. Returning the expected values does not prove
  complete backend recovery: this remains unresolved and requires an explicit
  recovery scope that preserves raw PostgreSQL call semantics and resource
  lifetimes. It must be fixed and tested before this SDK milestone is complete.
  The new notice assertion reproduces the gap on PostgreSQL 18.6/Linux x64:
  the packaged test fails on one SPI warning in 5m 23.699s. An explicit
  `PgTransaction.RunInSubtransaction` recovery callback is now implemented in
  the existing native transaction guard. Its unmanaged thunk contains managed
  exceptions until native rollback completes; raw calls keep their existing
  behavior outside the scope. A raw error marks the innermost scope for rollback
  even when caught, preventing subsequent SQL/raw calls until recovery. The
  focused runtime selection initially passes 26 cases in 863ms, including
  original exception identity, nested recovery and failed-scope access. The
  packaged backend test then passes on PostgreSQL 18.6/Linux x64 in 6m 26.955s
  with partial-write rollback, nested recovery, failed-context expiry and no
  SPI warning. The expanded runtime selection passes 28 cases in 953ms,
  including native failures before callback entry and after callback success.
  SPI session disposal inside a failed scope now leaves abandoned native frames
  for rollback instead of attempting to finish the wrong connection. Expanded
  backend checks add enclosing/inner SPI sessions, managed-exception rollback
  and successful subtransaction-context lifetime; their platform validation is
  in progress. The 28 runtime cases also pass on Windows x64/.NET 10.0.12
  in 143ms. The expanded packaged witness passes on PostgreSQL 18.6/Linux x64
  in 6m 58.525s; its server log contains no warning or error. The corresponding
  PostgreSQL 18.1/Windows x64 run passes in 11m 31.503s with no warning or error
  in the packaged extension's server log. These precede the added witness for
  rejecting recovery scopes during transaction callbacks. Plain root `dotnet test`
  then passes all six modules on PostgreSQL 18.6/Linux x64, including that
  restriction: 7,644 passed, zero failed and two existing Windows-only skips in
  19m 29.800s. The packaged recovery log has no nonempty SPI stack warning.

  Release builds with zero warnings/errors in 56.91s. Generated API pages now
  contain 2,256 members across 170 pages and pass freshness verification. The
  site builds all 212 pages in 4.32s and checks with zero errors, warnings or
  hints. The README and transaction guide document explicit recovery and its
  restrictions. Remaining SDK acceptance work is still pending.
  An isolated PostgreSQL 18.6/Linux x64 SDK consumer with empty build, companion,
  NuGet package and HTTP caches measures 49.306s for its cold build and 15.921s
  for first publication, using the preinstalled SDK/runtime payload. An actual
  source edit measures 14.790s to build and 16.342s to publish; unchanged build
  and publication take 13.549s and 15.658s. The unchanged build spends 9.799s
  in native declaration generation and 1.518s resolving its compiled companion.
  These are local single-consumer baselines, not hosted CI or Windows timings.
  The corresponding PostgreSQL 18.1/Windows x64 baseline with .NET 10.0.12
  measures 72.794s cold build, 25.365s first publication, 22.359s changed-source
  build, 25.155s changed-source publication, 23.816s unchanged build and 26.068s
  unchanged publication. These sequential measurements used a separate owned
  checkout and fresh build, companion, NuGet package and HTTP caches.
  Source reuse now keys the preprocessed tokens/macros, effective Clang frontend
  command, native tool contents and independently measured node layout. Every
  hit still compiles and executes the native declaration checks. Three focused
  preprocessing cases pass on Linux, including same-timestamp changes, include
  precedence, relocation, failure/recovery and changed semantic compiler options
  with identical preprocessed text. The first Linux comparison, before the
  additional semantic-command check, measures 59.520s cold, 11.206s first publish,
  8.605s changed-source build, 10.087s changed-source publish, 7.253s unchanged
  build and 9.549s unchanged publish. Only repeated builds improve in this run;
  final platform measurements and acceptance remain pending.
  The semantic-configuration witness initially fails on Windows because the
  test forwarded the driver configuration switch through the frontend option
  prefix. Passing the configuration and command-reporting switches directly to
  the driver fixes that boundary. All three preprocessing cases then pass on
  Windows x64/.NET 10.0.12 in 540ms; the expanded Linux header, record-check,
  cache and preprocessing selection passes 53 cases with no failures or skips
  in 2.677s. Final complete-suite validation remains pending.
  With semantic-command validation included, the Windows comparison measures
  83.772s cold build, 21.591s first publication, 19.820s changed-source build,
  21.253s changed-source publication, 19.357s unchanged build and 18.466s
  unchanged publication. Warm companion resolution still takes 12.311s; source
  reuse takes 3.667s. A separate complete-SDK hashing experiment reproduces every
  content hash and shows only a small warm difference from pooled reads, so no
  hash validation was removed or replaced on that basis.

  Native-body publication now retains immutable source/object pairs and
  atomically replaces the linker manifest after compilation. Seven Linux cases
  pass in 2.026s, including actual compiler failure, missing output, late
  cancellation, executable old-output preservation and recovery, empty selection,
  and unknown accessor/argument rejection before tool discovery. Native provider
  object/archive linking and the packaged SDK's explicit native archive are
  undergoing verification; no server-export-only restriction is imposed because
  valid symbols can come from the extension or a linked library.
  The standalone object/archive checks pass on Linux (nine publication/provider
  cases in 1.933s) and Windows (12 cases including preprocessing in 1.618s).
  The first packaged archive witness then finds an actual SDK ordering defect:
  the archive precedes its generated caller, leaving `_PG_output_plugin_init`
  unresolved when PostgreSQL loads the extension. Generated call objects now
  precede the existing linker arguments, preserving their relative order and
  allowing the provider archive to satisfy those references. The unchanged
  backend assertion then passes on PostgreSQL 18.6/Linux x64 in 3m 49.277s,
  including the archive with spaces in its path, exact native values and
  warning-free recovery in the same backend session. The failed attempt is
  retained as the regression's evidence.
  The expanded Windows selection passes discovery and cache-layout rejection,
  but its archive publication fails with MSVC's misleading `.obj` input error.
  An isolated real-linker probe identifies the new immutable object's long
  absolute path as the cause. An initial extended-path probe could fall back to
  a same-named local object; the executable regression invalidates that approach,
  and the prefix workaround is removed. Compilation now uses short owned
  temporary directories, and immutable objects live in the configured shared
  binding cache instead of beneath deep consumer intermediates. The atomic
  manifest candidate stays beside its destination, even across filesystems.
  Four executable MSVC cases exercise long consumer paths and reuse after
  consumer cleanup. All 15 focused Windows publication/command cases pass in
  1.368s; Linux passes 11 with four Windows-only skips in 2.081s. The first final
  Linux suite also finds
  an outdated command-arity case: ten source arguments are now valid because
  the shared cache is optional. Its rejection case is updated to eleven, while
  packaged tests exercise all ten valid arguments. That suite is superseded by
  a complete rerun after these fixes.

  The repaired packaged SDK/native-provider witness passes on PostgreSQL
  18.1/Windows x64 with .NET 10.0.12: one passed, zero failed or skipped in
  8m 21.200s. It includes exact native values, explicit archive linking, rollback,
  nested scope recovery, lifetime invalidation and the expected transaction-
  callback rejection. The packaged server log contains the intentional rejection
  and no SPI stack warning. Final Release passes with zero warnings/errors in
  1m 02.55s; API freshness retains 170 pages/2,256 members, the site builds 212
  pages in 3.29s and checks with zero errors, warnings or hints. Final plain root
  `dotnet test` passes all six modules on PostgreSQL 18.6/Linux x64: 7,656 passed,
  zero failed and six Windows-only skips (including the four new MSVC path cases),
  7,662 total, in 13m 45.061s. Integration finishes in 13m 44.434s. No shared
  output rebuild occurred during this full run.

  The final Linux SDK baseline runs after both platform tests finish, with fresh
  owned build, binding, NuGet package and HTTP caches and preinstalled SDK/runtime
  payloads. On PostgreSQL 18.6/.NET 10.0.11 it measures 49.595s cold build,
  9.628s first publication, 8.137s changed-source build, 9.775s changed-source
  publication, 6.983s unchanged build and 8.975s unchanged publication. These are
  individual wall-time observations, not a statistical benchmark or hosted CI
  prediction. Repeated builds improve against the earlier baseline; cold-build
  improvement is not established, and the Windows cold measurement is slower.

  Immediately before this milestone's commit, the previous hosted outcomes were
  checked again: [CI run 36274752988](https://github.com/willibrandon/ankus/actions/runs/36274752988)
  and [documentation run 36274753012](https://github.com/willibrandon/ankus/actions/runs/36274753012)
  for `de46baf` are successful, with every quality, runtime and full platform job
  green. The complete Linux/macOS/Windows counts and versions are recorded in
  the preceding cleanup milestone; the earlier Windows failure is resolved.
  No newer run is pending. New hosted validation must establish this SDK
  milestone's complete platform outcomes while independent porting continues.
  Job limits remain Linux 30 minutes, macOS 30 minutes and Windows 40 minutes;
  the hard maximum remains 40 minutes and analyzer enforcement is unchanged.

  | Requirement | Concrete witnesses |
  |---|---|
  | Complete header-availability partition and independent native layout | `AvailabilityRetainsCompleteOrderedPartition`, `EmptyHeadersRetainAbsentInventory`, `InvalidAvailabilityCannotHideAsAbsence`, `InvalidInventoryCannotHideAsAbsence`, `NativeRecordChecksPreserveIncompleteArrayAlignmentInGcc` |
  | Leased cache entries, content invalidation, cancellation and failed replacement | `CacheRebuildsChangedContent`, `CacheOwnershipProtectsReadersAndCancelsWaiters`, `CacheFailureCleansStagingAndRecovers`, `CacheRejectsInputsChangedDuringProduction`, `CacheFailurePreservesPreviousEntry` |
  | Real compiler processes share output while respecting consumer restore policy | `CompiledCompanionsShareAcrossProcessesAndConsumerCleanup`, `CompiledCompanionsInvalidateContentAndRecoverFromCompilerFailure`, `CompiledCompanionsHonorOfflineFeedConfigurationAndSourceMapping` |
  | Current preprocessing and semantic compiler options determine reuse | `NativePreprocessingTracksContentAndIncludeResolution`, `NativePreprocessingTracksEffectiveCompilerConfiguration`, `NativePreprocessingRejectsCompilerFailuresAndRecovers` |
  | Native publication retains executable old output and resolves used providers | `NativeLinkPublicationPreservesExecutableOutputAndRecovers`, `NativeLinkCommandValidatesImportsBeforeToolDiscovery`, `NativeLinkCommandRejectsInvalidArguments`, `NativeCallImportsResolveExplicitNativeProviders` |
  | Deep Windows consumer paths and consumer cleanup retain linkable objects | Four `NativeLinkPublicationSupportsLongWindowsConsumerPaths` cases link and execute the retained shared object after deleting consumer output directories of lengths 256, 260, 261 and 300 |
  | Managed exception containment, original identity and independent nested scopes | `RecoveryScopesRequireCallbackAndBackend`, `RecoveryScopesPreserveResultsAndExceptionIdentity`, `RawFailureCannotBeSwallowedOrPoisonRecoveredParent`, `RecoveryScopesPreserveNativeBoundaryFailures` |
  | Actual packaged SDK linking, backend values, rollback, lifetimes and recovery | `SdkSharesNativeTypesAcrossProjectsAndPublishesThem` checks the explicit archive, exact high-bit/native node values, owned diagnostics, writes, context expiry, callback restrictions and absence of SPI warnings; `SdkBindingDiscoveryLeavesNativeLinkInputsDeferred` checks build-stage separation |
  | Cache-hit native verification, consumer preservation and corrected retry | Expanded `PackagedNodeBindingFailurePreservesCompanionAndRecovers` supplies a false field offset with a matching cache hash and requires the native compiler to reject it; it passes on PostgreSQL 18.6/Linux x64 in 3m 10.175s |

  Local native-provider/backend checks, complete Linux validation and final
  documentation verification pass for this SDK change. Its complete hosted
  platform suites must still run after publication of the commit. Header availability
  is distinct from server exports: ordinary linker/loader resolution must retain
  valid extension and native-library providers, consistent with pgrx.
  Compiler shims, callbacks, variadics,
  globals/hooks, the full feature inventory and PostgreSQL/platform matrix
  remain required for the faithful port.

### Selected-platform declaration kinds and block formatting

[The macOS ARM64 job in CI run 36282996348](https://github.com/willibrandon/ankus/actions/runs/36282996348/job/108518475322)
for `d46a308` failed during the build, before the full tests could run. PostgreSQL
18 declares `pg_popcount32` as a function on ARM64 and can expose it as a mutable
dispatch-pointer global on x64. Availability discovery incorrectly required the
selected headers to retain the reference inventory's declaration kind.

The reference inventory now supplies names, while the selected headers supply
declaration kinds as well as exact native types. The standalone header command
and combined SDK collection use those observed kinds. Duplicate conflicting
declarations, malformed observations, changes during collection and incompatible
reconstructed native types still fail validation. This repair adds no warning
suppression or analyzer relaxation.

`AvailabilityUsesSelectedHeaderDeclarationKinds` checks both transitions without
mutating the inventory. `HeaderAvailabilityPreservesPlatformDeclarationKinds`
compiles both function and dispatch-pointer forms, executes exact return-value
checks and rejects incompatible signatures. The focused selection passes all
14 cases on Linux x64 in 1.243s. `PackagedBuildToolCollectsSelectedHeaderTypes`
now includes the real `pg_popcount32` declaration and asserts its ARM64 function
identity. New hosted verification is pending.

All tracked C# block bodies now use separate brace lines, including short
conditionals, loops, exception handlers and lambdas. Properties with `get` plus
`set` or `init` have separate accessor lines, including visibility modifiers.
The initial formatting pass incorrectly expanded 268 simple getter-only
declarations. These are restored to one line, including partial and interface
properties and auto-properties with initializers. The formatter now preserves
compact declarations, and `AGENTS.md` explicitly records those exceptions.
A check of all tracked C# accessors reports no remaining violations, and the
complete whitespace-format verification passes. A syntax-tree and token-value
comparison verifies that the 256 files changed only for formatting retain their
code structure and values. The block check also finds no remaining compact C#
blocks. Final Release after the getter correction passes with zero warnings/errors
in 1m 02.52s. API freshness passes for 170 pages/2,256 members; the site builds
212 pages in 3.58s and checks with zero errors, warnings or hints. The first
plain root test suite found an over-specific new test expectation: PostgreSQL's
`uint32` includes an intermediate `uint32_t` typedef on Linux. The assertion now
preserves the public alias while following the typedef chain to check the exact
unsigned scalar type. That incomplete run requires validation after the correction;
it is not full-suite success. The corrected packaged regression passes on
PostgreSQL 18.6/Linux x64: one passed, zero failed or skipped in 2m 47.683s.
The final plain root complete suite passes all six modules on PostgreSQL
18.6/Linux x64: 7,657 passed, zero failed, and six Windows-only skips, 7,663 total,
in 14m 36.225s. Integration completes in 14m 35.669s. The getter correction changes
whitespace only, with syntax and token equivalence checked; shared build outputs
were not rebuilt while this suite ran. The final Release result above validates
the corrected source afterward.

The original hosted Linux job also reached its 30-minute limit during integration
testing. Its build and all five unit modules had passed; complete backend evidence
was interrupted. The Windows job also reached its 40-minute limit after its
11m 52.43s build and all five unit modules passed, interrupting integration tests.
Following the user's explicit request to increase that limit, Windows now allows
45 minutes. Linux allows 35 minutes and macOS remains at 30. Every platform
retains the complete suite. The user also authorizes future increases when measured
runs need them as the suite grows; current limits and their reasons must be recorded.
Hosted CI does not block independent development. Prior run outcomes must be
checked and recorded before both committing and pushing.

The 7,663 cases span six projects: 4,343 across the five unit/generator projects
and 3,320 integration cases. All six modules pass in the complete local run above.

Immediately before committing this repair, prior hosted outcomes were checked
again: [CI run 36282996348](https://github.com/willibrandon/ankus/actions/runs/36282996348)
is terminal with the macOS declaration failure and the Linux/Windows timeout
outcomes described above. Quality and all three runtime jobs succeeded.
[Documentation run 36282996370](https://github.com/willibrandon/ankus/actions/runs/36282996370)
succeeded, including deployment. No newer run is pending. New hosted suites must
establish the repaired complete platform outcomes; the local regression witnesses
do not replace macOS or Windows platform evidence. The remaining full-port scope
from the preceding milestone remains required.

### Selected-header helpers and CI cache reuse

The handwritten pgrx helper layer now supplements the pinned foreign inventory
without changing generated reference JSON. The descriptors cover alignment,
memory contexts, transaction IDs, buffers, pages, tuples and spinlock primitives
for PostgreSQL 13–19, preserving macro-to-inline transitions and the removal of
`SpinLockFree` in 19. Existing native prototypes remain authoritative. Required
supplemental functions must appear as top-level functions; missing, conflicting,
nested or wrong-kind declarations fail collection.

Macro prototypes participate in compiler type discovery. Their implementations
are enabled only for selected native call bodies and for independent body
verification, including source-cache hits. This avoids unused internal functions
without suppressing warnings or compiling unrelated helpers into published
objects. A standalone PostgreSQL 18.6/Linux x64 header command collects and
compiles all 47 helper contracts with Clang 21. This is compiler evidence, not
proof of their backend behavior or of other supported PostgreSQL versions.

`HeaderHelpersRespectSelectedMajorAndExistingDeclarations` verifies all seven
version partitions; companion cases reject unsupported majors and global
collisions while retaining foreign aliases. The `RequiredHelpers*` cases preserve
the ordered availability partition and reject missing or invalid contracts.
`NativeHeaderMacrosPreserveValuesAndPointerContracts` compiles and executes
alignment boundaries, single evaluation, tuple addresses, masks, page predicates
and a bounded native lock sequence; independent compilation rejects lost
`volatile` pointer qualifications. All 18 focused cases pass on Linux x64 in
1.982s. The complete build-tool module passes 830 cases, with zero failures and
six Windows-only skips (836 total), in 18.788s.

The first packaged SDK/backend attempt failed during consumer compilation because
`PageHeaderData` was absent from the generated declarations: page helpers expose
an untyped address, so that storage was not reachable through their signatures.
Helper-required types now enter the same compiler-measured graph through explicit
native object roots, including cache verification. No managed layout is invented.
`SupplementalRecordRootsPreserveNativeStorage` compares generated size, alignment,
field offset and values with an independent native C program for an otherwise
unreferenced record. Companion cases reject duplicate and invalid root names and
permit recovery. This six-case selection passes in 2.821s after correcting the
new harness to access the explicitly implemented native-alignment interface.
The expanded 18 helper cases pass in 1.968s.
The combined helper and native-record selection also passes all 24 cases on
Windows x64 with MSVC 18.10.1/.NET 10.0.12 in 2.752s. Release passes with zero
warnings or errors in 1m 12.30s. API freshness passes for 170 pages/2,256 members;
the site builds 212 pages in 3.79s and checks with zero errors, warnings or hints.
The repository accessor and block checks find no formatting violations.

Subsequent backend validation found two additional issues before publication.
Windows full-body validation used the Clang discovery frontend against native
MSVC headers, exposing incompatible intrinsic pointer declarations. Helper bodies
now use the selected native compiler, while Clang still independently measures
declarations; no diagnostics are disabled. The first Linux execution crashed
because the new witness passed `PgMemoryContext.Id` as a native address. That
property is an opaque registry token. The witness now obtains the actual owner
with `GetMemoryChunkContext`, independently checks the managed context identity
after switching, and restores the previous native owner. XML remarks and the raw
guide clarify the distinction. These failed attempts are not backend evidence;
both corrected witnesses require successful execution.
The corrected build-tool module passes 835 cases with zero failures and six
Windows-only skips (841 total), in 21.469s. The Release integration build also
passes with zero warnings or errors in 56.16s. The standalone packaged-header
witness now includes `TYPEALIGN`, checking its public/native identity and exact
address-width result contract as well as compiling its body.

The corrected packaged SDK/backend and standalone header witnesses both pass on
PostgreSQL 18.6/Linux x64 in 4m 44.647s and PostgreSQL 18.1/Windows x64 in
8m 39.210s, two passed with zero failures or skips on each platform. They execute actual page and tuple values,
SQL NULL, allocation owners, context restoration, lifetime expiry and recovery
after a helper raises PostgreSQL ERROR. The plain root complete suite passes all
six modules on PostgreSQL 18.6/Linux x64: 7,680 passed, zero failed, and six
Windows-only skips (7,686 total), in 14m 15.498s. Integration completes in
14m 14.949s. A subsequent reference recheck corrected `BufferIsValid` for
PostgreSQL 13–15, where it remains a macro until PostgreSQL 16. Seven additional
native fixtures compile and execute the macro/inline boundary with exact local,
invalid and shared buffer values; their final execution is recorded separately
below because they were added after the complete run began. The new fixture's
inline declarations initially triggered Clang's unused-function diagnostic during
discovery. An independently invoked native function-pointer witness now retains
those inline definitions without suppressing the warning. The fourteen-case
version/validity selection passes with zero failures or skips in 2.755s.
The corrected complete build-tool module passes 842 cases with zero failures and
six Windows-only skips (848 total) in 19.417s. The repository now contains 7,693
cases; the seven late cases have module-level execution evidence in addition to
the complete 7,686-case run above. Final solution Release passes with zero
warnings/errors in 1m 14.93s, followed by a clean Release build of the corrected
test fixture in 1.01s. The final site builds 212 pages in 4.58s and checks with
zero errors, warnings or hints. The same fourteen version/validity cases pass on
Windows x64 in 995ms. Final API freshness covers 170 pages/2,256 members, and the
complete whitespace verification passes.

The preceding repair's [CI run 36285915891](https://github.com/willibrandon/ankus/actions/runs/36285915891)
has passed quality and all runtime jobs, but macOS reached its 30-minute limit
after a 26m 10.15s Release build. Its unit module also reported
`CompiledCompanionsShareAcrossProcessesAndConsumerCleanup` and
`NativePreprocessingTracksContentAndIncludeResolution` failures. Both reproduce
on Linux with a symbolic-link temporary root: identical native/managed inputs
acquired different cache identities because tools reported physical paths while
the cache normalized logical paths. Both staging paths now resolve all directory
aliases before compilation and hashing. The existing physical-path resolver is
shared with generated project path mapping. Four affected cases pass under the
linked temporary root in 6.996s; the persistent tests now create linked staging
themselves on Unix. All seven cache regression cases pass in 22.213s, and the
same seven cases pass on Windows x64 in 1m 26.212s. The final Release build
passes with zero warnings/errors in 1m 16.73s. Final API freshness still covers
170 pages/2,256 members; the site builds 212 pages in 3.25s and checks without
errors, warnings or hints. Final accessor and block checks also pass.
Fresh macOS evidence is still required.

The hosted Linux job reached its 35-minute limit during integration testing. Its
Release build passed in 4m 31.24s and every unit module passed without failures.
Linux now allows 40 minutes and macOS 35; Windows remains 45. The cache defect is
fixed alongside the measured timeout adjustment, and every platform retains its
complete suite. Windows also reached its 45-minute limit during integration;
its Release build passed in 11m 16.31s and all five unit modules passed. Its
limit remains unchanged while the cache changes receive hosted measurement.

Following the user's cache-first request, quality and platform test jobs now
cache the ordinary NuGet package directory using `actions/cache`. Keys separate
operating systems, architectures and jobs and hash the SDK, project and MSBuild
dependency files. Earlier dependency keys can seed restores; normal restore and
every build/test still run. Packaged-consumer tests retain their isolated NuGet
directories, and repository build outputs are not cached. The first successful
run populates the package caches; savings need a later hosted cache-hit run.
The existing pinned Native AOT runtime cache remains in place. No sharding or
additional CI jobs are introduced.
[Documentation run 36285915863](https://github.com/willibrandon/ankus/actions/runs/36285915863)
passed. Immediately before committing, both preceding run outcomes were checked
again; all jobs are terminal and no newer run is pending. Fresh hosted execution
must establish complete platform outcomes and cache savings. The full port
remains incomplete: callbacks, globals/hooks, variadics, higher-level shared-memory
locking, the broader feature inventory and the full platform/version matrix
remain required.

### Guarded native global access

The SDK companion now exposes `NativeGlobals` from actual available native global
declarations. Synthetic node/helper type roots remain type-discovery inputs.
PostgreSQL 18.6/Linux x64 collects 579 globals into the shared declaration
contract. Complete values have guarded getters, mutable objects have setters,
and every object has an explicit `DangerousAddressOf_name()` operation. Native
const qualification follows aliases and embedded by-value fields without making
a mutable pointer to const storage read-only. Incomplete arrays and objects have
address access without invented extents. Zero-size objects retain distinct
logical managed values and transport no CLR storage bytes.

Every operation validates the active callback and exact binding before native
lookup, then enters the existing PostgreSQL error guard. Ordinary objects retain
all bytes, including padding; qualified loads/stores use native C value
operations and array elements retain their native shape. Compound value copies
do not promise atomic snapshots. Thread-local addresses are resolved within each
invoked native body. Function-pointer globals retain native addresses only;
managed hook registration, callback lifetime and atomic/locking APIs remain
separate full-port requirements.

Native AOT selects individual read/write/address bodies from actual object
imports in a namespace distinct from function imports. Pure accessors perform no
global access. Unknown operations and names, writes to const storage, incomplete
value accesses and foreign targets fail before publication. Unused globals do
not require definitions. Global-only selections still require the native tool
and preserve the previous linker manifest when discovery fails.

| Requirement | Evidence |
|---|---|
| Qualifiers, aliases, complete/incomplete storage and deterministic identity | `NativeGlobalSelectionsPreserveObjectQualification`, `NativeGlobalSelectionsRejectInvalidContractsAndRecover` |
| Exact native values, padding, arrays, union representations and original addresses | `NativeGlobalBodiesPreserveValuesAndRejectInvalidFrames` executes independently declared C objects, including high-bit and NaN representations |
| Invalid frame preservation and corrected retry | The same native witness checks count, null, size, alignment and result failures before comparing unchanged bytes |
| Current managed values, read-only API shape and pointer identity | `ManagedGlobalsPreserveNativeValuesAndIdentity` |
| Callback/identity rejection before native lookup, nesting and recovery | `ManagedGlobalsValidateEveryAccessAndRecover` |
| Large value ownership on success, native error and allocation failure | `ManagedGlobalsReleaseLargeFramesAndRecover` observes real allocation/free counts, zero retained buffers and unchanged native state |
| Generated-name collisions, zero-byte transport and thread-local storage | `ManagedGlobalsPreserveMemberNames`, `ManagedGlobalsPreserveEmptyArrays`, `ManagedGlobalsResolveThreadLocalStoragePerAccess` |
| Actual native import selection, rejection and global-only link behavior | `NativeGlobalImportsSelectReferencedOperations`, `NativeGlobalImportsRejectInvalidOperationsAndTargets`, expanded `NativeLinkCommandValidatesImportsBeforeToolDiscovery` |
| Native AOT imports and trimming | Expanded `PublishedManagedCallsUseNativeAccessors` executes real getters/setters/address access and leaves unused functions/globals undefined |
| Real SDK and PostgreSQL access, restoration and recovery | Expanded `SdkSharesNativeTypesAcrossProjectsAndPublishesThem`, executed on Linux and Windows below |

The first expanded boundary run caught an unnecessary C# `new` modifier on a
global named `Finalize`. The corrected naming case passes without suppression.
The initial twelve-case global/AOT selection passes on Windows x64 with
MSVC 18.10.1/.NET 10.0.12: twelve passed, zero failed/skipped, in 6.458s. Initial
solution Release passes with zero warnings/errors in 1m 17.90s. Subsequent
strengthening covers containing-type collisions, selection identity and the
global-only link command; complete evidence follows. These focused cases are
not a full-suite or full platform/version parity claim.

The final complete build-tool module passes 853 cases with zero failures and six
Windows-only skips (859 total) in 18.330s, including the strengthened cases above.
Final solution Release passes with zero warnings/errors in 1m 02.68s. The Linux
packaged SDK/backend witness passes on PostgreSQL 18.6 in 5m 02.394s. It checks
`MyProcPid` against `pg_backend_pid()`, reads/writes `extra_float_digits` against
SQL `current_setting`, switches/restores the actual `CurrentMemoryContext`, and
verifies global access after a recovered division-by-zero error. The site builds
212 pages in 3.25s and checks with zero errors, warnings or hints; API freshness
covers 170 pages/2,256 members. Accessor and block checks pass.
The same packaged SDK/backend witness also passes on PostgreSQL 18.1/Windows
x64 in 9m 20.328s, with zero failures or skips. This exercises actual imported
PostgreSQL global storage and the native guard under MSVC, as well as the shared
managed declarations, SQL-observed mutation, restoration and session recovery.

During this milestone, [CI run 36288886694](https://github.com/willibrandon/ankus/actions/runs/36288886694)
passed quality and all three runtime jobs. Its full PostgreSQL 18/Linux x64 job
passed all 7,687 executable cases with zero failures and six Windows-only skips
(7,693 total), finishing in 36m 29s. Release took 4m 50.98s and integration took
30m 21.872s. This was a NuGet cache miss; the job saved the package cache after
success. [Documentation run 36288886773](https://github.com/willibrandon/ankus/actions/runs/36288886773)
also passed.

The same run's macOS ARM64 job reached its 35-minute limit during integration.
Release passed in 7m 15.01s and every unit module passed, including the two
previously failing cache cases. No complete macOS integration result is claimed.
Because a timed-out job never reaches the combined cache action's successful
post-job save, platform jobs now use standard `actions/cache/restore` and
`actions/cache/save` around an explicit normal NuGet restore before testing. This
allows a later run to reuse packages even if integration reaches the limit.
Local plain solution restore succeeds. Full builds/tests still execute normally;
limits, job counts and test distribution are unchanged. Actual cache savings
remain unmeasured. The Windows x64/PostgreSQL 17 job also reached its existing
45-minute limit during integration; Release passed in 12m 13.57s and all unit
modules passed. It likewise had a package-cache miss and saved no package cache.
The run is terminal with a cancelled overall conclusion; no failed assertion was
reported by either timed-out platform. The cache-save adjustment applies to both.

Final type-identity review found that qualified zero-size records and arrays
received different managed logical types despite having the same native value
shape. The expanded `ManagedGlobalsPreserveEmptyArrays` reproduces three C#
assignment failures for const/mutable and volatile/mutable pairs. Empty values
now share their declaration or element/count identity while native qualification
still controls access. All fourteen focused global, import, Native AOT and empty
function-value cases pass in isolated Linux outputs in 5.060s. The earlier full
local run was cancelled before completion so the final plain-root suite could
run against this correction; it is not reported as a passing complete run.
The corrected fourteen-case selection also passes on Windows x64 in 6.616s with
zero failures/skips. Corrected solution Release passes with zero warnings/errors
in 1m 08.45s; the PostgreSQL 18.6 companion retains the same native contract
identity. Final whitespace, accessor and block checks pass.

The final plain-root `dotnet test` passes all six modules on PostgreSQL
18.6/Linux x64: **7,698 passed, zero failed, six Windows-only skips, 7,704 total**,
in 13m 46.223s. Integration completes in 13m 45.612s and includes the corrected
packaged SDK/global witness. This is the complete current local suite, including
the eleven new global cases and the expanded existing Native AOT/link/backend
cases. Earlier partial and cancelled runs do not substitute for this result.

Immediately before committing, the previous CI and documentation outcomes were
checked and recorded again: quality, runtime, full Linux and documentation passed;
macOS and Windows reached their recorded limits during integration. All previous
jobs are terminal. The early cache-save change still needs fresh hosted execution
and cache-hit timing; no speedup is claimed yet. The full port remains incomplete:
guarded indirect calls, managed callback/hook lifetime and chaining, variadics,
atomic/locking APIs, the remaining feature inventories and the complete
PostgreSQL/platform matrix remain required.

### CI package-cache restore correction

[CI run 36292247916](https://github.com/willibrandon/ankus/actions/runs/36292247916)
reused the Linux test and quality NuGet caches. The macOS ARM64 test job restored
packages in 26 seconds and saved its first package cache in 16 seconds before
starting the full suite. The cache service confirms the 836,369,566-byte entry;
its reuse and complete-job savings still need measurement. All three runtime
jobs and [documentation run 36292247845](https://github.com/willibrandon/ankus/actions/runs/36292247845)
passed.

The Windows test job failed before running tests: the new standalone solution
restore inherited Visual Studio's `Platform=x64`, producing MSB4126 for the
invalid `Debug|x64` solution configuration. The existing C# CI driver clears that
environment variable for its child .NET commands, but the separate restore step
does not pass through that driver. The restore command now explicitly selects
the solution's `Any CPU` platform. The same inherited environment reproduces the
failure on Linux and Windows; the corrected command succeeds on both, including
a 2.594-second Windows restore. This is restore evidence, not a Windows test-suite
result. The cache keys, analyzers, full-suite jobs and timeouts are unchanged.

The exact workflow command also passes through Windows PowerShell with the same
inherited environment (1.842s); its unchanged predecessor still fails with
MSB4126. Local Release passes with zero warnings/errors in 1m 10.16s. API
freshness covers 170 pages/2,256 members; the site builds 212 pages in 2.95s and
checks with zero errors, warnings or hints. Hosted quality passes in 8m 16s
with a confirmed package-cache hit, compared with the preceding run's 8m 40s.
This single comparison includes other source changes and is not an isolated
measurement of cache savings. Complete platform-suite timings remain pending.

Final plain-root `dotnet test` on PostgreSQL 18.6/Linux x64 passes all six modules:
**7,698 passed, zero failed, six Windows-only skips, 7,704 total**, in 14m 00.722s.
Integration takes 13m 59.676s. Immediately before committing, the live CI outcomes
were checked and recorded again: quality, all runtime jobs and documentation
passed; Windows failed at the restore step corrected here; Linux and macOS full
suites are still in progress. No additional failure is reported. Their unfinished
runs do not count as passing platform evidence. The cache correction proceeds
without waiting for hosted completion, as requested; the remaining full-port
requirements above are unchanged.

### Guarded native function-pointer calls

The selected-header companion now represents native function pointers as typed
readonly values across fields, globals, arrays, parameters and results. Aliases
and object qualifiers share their canonical function identity. Types retain
measured size/alignment and binding metadata. Their borrowed addresses can be
inspected or stored; copying one does not establish ownership or extend the native
target's lifetime.

Complete fixed prototypes expose an instance `Invoke` with the exact native
argument and result types. Every call validates the active backend and binding,
then rejects a null target before allocating a frame or looking up native code.
The existing aligned frame transport carries the current target as its first
value. A native body validates every descriptor and invokes the actual C function
pointer beneath the PostgreSQL error guard. Native calling conventions and
aggregate ABI remain the native compiler's responsibility. Large frames release
their owned storage on success and error; zero-byte native values preserve their
logical type without copying CLR placeholder bytes.

Pure body-address accessors have a distinct import namespace. Post-ILC selection
emits only the signatures the Native AOT consumer actually invokes, retaining
complete graph and object-target validation. Unused callback declarations create
no native link dependency. Variadic, unprototyped and incomplete-result signatures
retain typed address transport without an invented invocation API. This milestone
does not register, root or guard managed callback implementations.

| Requirement | Verified test boundary |
|---|---|
| Canonical aliases, qualifiers, adjusted parameters and nested signatures | `IndirectCallsPreserveCompleteSignatureIdentity`, `ManagedIndirectCallsPreserveValuesAndIdentity` |
| Exact integer/aggregate/void ABI, live target changes, unchanged rejected state and corrected retry | `IndirectBodiesPreserveValuesAndRejectFrames` executes native C with independent expected bits, doubles, arrays and side-effect counts |
| Active scope, nested binding mismatch, null target and owned diagnostic recovery | `ManagedIndirectCallsValidateEveryInvocationAndRecover` |
| 64-aligned 8,192-byte values, native error, allocation failure and exact release counts | `ManagedIndirectCallsReleaseLargeFramesAndRecover` |
| Empty native values, reserved names and unsupported signature transport | `ManagedIndirectCallsPreserveEmptyValuesAndNames`, seven `ManagedIndirectCallsPreserveReservedNames` cases, `ManagedIndirectPointersRetainUnsupportedSignatures` |
| Actual object imports, complete validation, finite linking and retry | `IndirectImportsSelectReferencedSignatures`, `IndirectImportsRejectInvalidSignaturesAndTargets`, `IndirectCallsRejectInvalidSelectionsAndRecover` |
| Actual Native AOT accessor calls and unused callback trimming | Expanded `PublishedManagedCallsUseNativeAccessors` publishes and runs the executable, inspects its ILC object and regenerates the exact selected native bodies |
| Indirect-only tool discovery, preserved output and runtime target rejection | Expanded `NativeLinkCommandValidatesImportsBeforeToolDiscovery` and `RawCallStatusesRejectInvalidContractsAndRecover` |

The shared-frame refactor first passes 71 existing cases in 5.581s. The final
complete Linux build-tool module passes **870 cases, zero failures, six
Windows-only skips, 876 total**, in 19.447s. The focused runtime protocol passes
11 cases in 1.081s. Nineteen native/managed/Native AOT/link cases pass on Windows
x64 with MSVC 18.10.1 and .NET 10.0.12 in 7.130s, with zero failures or skips.
The first solution Release passes with zero warnings/errors in 1m 17.02s.
Backend execution and final complete-suite/documentation verification are pending.

The first packaged Linux and Windows witnesses expose an existing fixture's
integer assignment/comparison of native callback fields. With typed pointers,
that consumer fails to compile. The fixture now constructs explicit address
values and checks `IsNull`/`DangerousGetAddress`; the typed API remains intact.
Those failed runs supply no backend evidence and require corrected execution.

Corrected consumer compilation then exposes an invocation-generation defect:
temporary node probe globals were chosen as C type anchors but are absent at
native link time. Anchors now prefer actual typedefs/tags and their transitive
fields before global expressions. `IndirectBodiesUseHeaderTypesWithoutProbeGlobals`
collects named and embedded anonymous records through probe globals, compiles
without those globals, and invokes a real aggregate transform with exact result,
input preservation and side-effect assertions. All 36 focused anchor/record
cases pass in 2.906s. Complete graph validation remains enforced.
The corrected complete build-tool module passes 871 cases with zero failures and
six Windows-only skips (877 total) in 18.865s. Changed-source whitespace checks
and the repository's accessor/block checks pass.
The corrected Windows native/managed/Native AOT/link selection passes all twenty
cases with zero failures or skips in 5.379s. Final solution Release passes with
zero warnings/errors in 1m 06.32s. The site builds 212 pages in 3.25s and checks
with zero errors, warnings or hints; API freshness passes for 170 pages/2,256
members.

Assertion and behavior-gap review checks exact outputs, rejected side effects,
ownership and corrected retries. It caught an initially nondistinguishing native
target-change witness: addition and XOR produced the same expected value. The
corrected XOR witness produces different bits and passes. Source-to-assertion
review does not claim empirical mutation execution or full platform parity.

The corrected packaged SDK witness passes on PostgreSQL 18.6/Linux x64 with
zero failures/skips in 4m 39.346s. It initializes a real `FmgrInfo`, saves its
`PGFunction` in context-owned native storage, constructs an exact measured call
frame, and invokes PostgreSQL's integer division function. It checks result 42,
negative datum bits `FFFFFFFFFFFFFFD6`, native SQLSTATE `22012` under an explicit
subtransaction, unchanged target identity, successful reuse and later SQL. The
complete sequence passes twice on the same connection. The older linked-provider
fixture also passes with typed pointer fields. The same packaged SDK/backend
witness passes on PostgreSQL 18.1/Windows x64 with zero failures/skips in
8m 51.978s.

Final plain-root `dotnet test` passes the complete PostgreSQL 18.6/Linux x64
suite: **7,717 passed, zero failed, six Windows-only skips, 7,723 total**, in
13m 29.290s. Integration takes 13m 28.727s. This includes the final anchor
correction, all new indirect-call cases, and the packaged SDK/backend witness.
The focused Windows runs establish their stated scope; a complete hosted run
is still required for each platform.

The cache-first CI measurement now has a complete Linux result. [CI run
36293364770](https://github.com/willibrandon/ankus/actions/runs/36293364770) for
`9269e72` passes the complete PostgreSQL 18.6/Linux x64 suite: **7,698 passed,
zero failed, six Windows-only skips, 7,704 total**. The job takes 31m 02s,
including a 4m 11.29s Release build and 25m 31.834s integration module. The earlier
uncached Linux job took 36m 29s; this single overall comparison is about 15%
faster, but includes source changes and does not isolate cache savings. Both
Linux and macOS reuse their package caches and restore packages in three seconds.
Windows completes the corrected restore and saves its first package cache before
tests. Its Release build passes in 10m 19.91s and all five unit modules pass;
integration then reaches the existing 45-minute limit without a reported failed
assertion. This is still a cache-miss run. The next hosted run must establish the
first Windows cache-hit timing before further optimization decisions.

The cache-hit macOS ARM64 run still reaches its 35-minute limit during integration.
Its Release build passes in 7m 00.39s and all five unit modules pass; no failed
assertion is reported. Following the user's explicit request, every job in the CI,
documentation and release workflows now has a 60-minute timeout. This replaces
the previous platform-specific limits and is recorded in `AGENTS.md`.
Commit `aed39c7` publishes this timeout change independently while local full-suite
validation continues. Previous CI outcomes were checked and recorded immediately
before both its commit and push.
The cache-first observation is retained: caching works, but it does not by itself
bring macOS within 35 minutes. Every platform keeps the full suite, and no new
jobs or test sharding are introduced. A fresh hosted run must establish complete
macOS and Windows evidence under the adjusted limit.

That CI run is now terminal: quality, all three runtime jobs and the full Linux
suite passed; macOS and Windows timed out as recorded. The previous run
36292247916 is terminal with its corrected Windows restore failure and superseded
Linux/macOS jobs cancelled. Documentation run 36292247845 passed. Cancelled and
timed-out suites are not counted as passing platform validation.

Immediately before this milestone's commit, [CI run
36296260478](https://github.com/willibrandon/ankus/actions/runs/36296260478) for
the timeout-only commit `aed39c7` has all three runtime jobs passing, with quality
and all platform suites still running. No failed check is reported. Its
[documentation run](https://github.com/willibrandon/ankus/actions/runs/36296260503)
has passed. These live outcomes were checked and recorded; development proceeds
without treating unfinished platform suites as successful evidence.

Managed callback/hook registration, ownership and chaining, variadic invocation,
atomic/locking APIs, the remaining feature inventories and the complete
PostgreSQL/platform matrix remain required for the faithful port.

### Managed native callback entry points — in progress

The native callback layer now selects static wrappers from actual accessor
imports, retaining a distinct managed target identity for each canonical native
signature. The native compiler owns argument passing and return ABI. Wrappers
transport exact value addresses and sizes to a separate dispatcher. Each accessor
accepts its statically compiled managed handler once, retains a stable native
address, and rejects NULL or replacement targets without changing the handler.
Repeated identical registration is idempotent. Unused wrappers introduce no
native link dependency. The source-generator API and native/managed dispatchers
are now connected, with end-to-end validation still in progress; this is not yet
a completed consumer callback-registration feature.

The compiler witness exposed a selected-header collection gap for macro-qualified
calling-convention attributes. The independent header tree now retains the
compiler's effective convention and verifies it against the measured graph.
Unknown conventions, incomplete wrappers and mismatched observations still fail.
Qualified result types use named typedefs to retain their native value contract
without redundant return qualifiers; empty results initialize writable byte
storage explicitly. These fix the underlying compiler errors without suppression.

`CallbackImportsSelectIndependentTargets` executes real C scalar/aggregate/void
wrappers, checks distinct same-signature targets, stable address registration,
preserved invocation after rejected replacement, and
absence of unused dependencies. `CallbackImportsRejectIncompatibleContracts`
checks malformed imports, unsupported signatures, repeated target identities,
foreign objects, invalid unselected roots and corrected retry.
`NativeCallbackWrappersPreserveSpecialStorage` executes wide integers, extended
precision, 64-byte alignment, empty types, const-qualified results, adjusted array
parameters and nested callbacks under the selected native convention. It also
compiles the complete graph's independent C checks and rejects a changed convention
in the independently collected header tree.

The current Linux build-tool suite passes **886 tests, zero failed, six
Windows-only skips, 892 total**, in 17.673s. Native dispatcher stubs in these tests
prove the C ABI only; managed exception handling, PostgreSQL error recovery, hook
chaining, shared preload and worker startup remain required for this feature.

The source generator now resolves `[PgNativeCallback(nameof(Handler))] on static
partial getter-only properties to exact synchronous handlers, including private
methods in partial classes, structs and records. `ANKUS021` rejects invalid
declarations without suppression. A generated nested type owns the unmanaged
dispatcher, so a throwing user type initializer runs inside the exception guard.
The property supplies its statically compiled dispatcher address to the native
accessor; no named unmanaged export roots unused handlers.

The dispatcher checks the binding and complete frame before user effects,
preserves zero-byte values without copying CLR placeholder bytes, and restores
nested memory, backend, configuration and logging capabilities. The native side
shares fork-host entry with GUC hooks and raises PostgreSQL errors after managed
return. Initialization and postmaster/worker transitions still require the
planned real-backend evidence.

Focused Linux runtime validation passes **18 tests, zero failed**, in 851ms;
generator validation passes **26 tests, zero failed**, in 3.206s, including
compiled dispatch, malformed frames, owned managed errors, retry and the throwing
type-initializer case. The native/compiler selection passes **16 tests, zero
failed** on Linux in 1.750s and on Windows x64/.NET 10.0.12 with Visual Studio
18.10.1 in 1.089s. Empty native values are checked against the selected compiler's
actual `sizeof`, not an assumed cross-platform empty-struct size.

The first packaged PostgreSQL attempt stopped in assembly setup: moving shared
fork helpers into the common preamble gave the extracted allocator fixture
unused functions under `-Werror`. Those helpers now sit beside their actual
callers. A corrected packaged Native AOT test is running; its planned callback
trimming, independent targets, borrowed-value expiry and PostgreSQL error/recovery
checks are not yet counted as passing evidence. Complete suite, documentation,
API freshness, hook chaining, preload and worker validation remain outstanding.

For the preceding committed milestone `95031c5`, hosted CI run 36296692977 now
has passing quality, all three runtime jobs and full Linux/macOS suites; Windows
is still running. Documentation run 36296693000 passed. These live results are
recorded without treating unfinished Windows validation as successful.

The complete current generator module passes **2,002 tests, zero failed**, in
15.842s, and the complete runtime module passes **1,541 tests, zero failed**, in
2.072s. These runs reuse the built outputs while the packaged fixture owns its
publishes. Hosted macOS ARM64/PostgreSQL 18.6 completed the previous milestone's
full job in 47m50s; its integration module passes 3,318 tests with two existing
platform skips in 39m12.347s. The 60-minute limit accommodates this measured run.

The corrected packaged attempt passed fixture startup, then rejected the emitted
callback accessors: `LibraryImportGenerator` cannot process another generator's
newly emitted partial methods in the same compilation. The callback generator
now emits the final blittable `DllImport` extern with exact spelling and Cdecl
for its native-address-only accessor. This adds no runtime marshalling and
preserves the SDK's direct native import selection. Compiler tests now compile
that original emitted import; only managed-dispatch execution substitutes the
accessor. The actual packaged publish and runtime witness must pass before this
interop correction is considered verified.

That retry publishes and executes the Native AOT callbacks: exact independent
results and addresses, managed PostgreSQL diagnostics, native division-by-zero
recovery and a subsequent guarded query all produce their expected values. The
test still fails because it incorrectly expects a raw `DangerousBorrow` view to
expire at callback return. Its documented lifetime is the explicitly selected
context generation. The corrected witness checks survival after return followed
by invalidation on owner reset; production ownership semantics are unchanged.
Executor hook chaining, standard fallback, error recovery and explicit restoration
have also been added to the packaged consumer and await execution.

The preceding milestone's hosted run 36296692977 is now entirely successful.
Windows x64/PostgreSQL 17 uses its first package-cache hit, passes Release in
10m24.57s and its integration module in 47m44.701s (3,318 passed, two existing
platform skips). Its complete job takes 59m48s, leaving little margin under the
60-minute limit. Linux x64/PostgreSQL 18.6 passes its complete job in 35m47s,
including all 3,320 integration tests in 29m05.438s. macOS evidence is recorded
above. No unfinished or timed-out job is included in these successful results.

The corrected packaged consumer now passes on Linux x64/PostgreSQL 18.6:
`SdkSharesNativeTypesAcrossProjectsAndPublishesThem` completes with one passed,
zero failed or skipped, in 3m48.379s. Actual Native AOT linking succeeds despite
an unused callback referencing an undefined native symbol. Real backend calls
retain independent same-signature addresses through GC, exact 64-bit results,
owned managed/native diagnostics, raw-borrow survival until context reset, and
same-session recovery. Two installed executor hooks execute in order `1,2,3,4`,
reach the standard executor fallback, reject one query with exact owned
diagnostics, recover on the next query, and restore the prior hook idempotently.
The assertion checks that subsequent queries do not increment the removed hook.

The complete generator module, including the final direct import and additional
native result/parameter shapes, passes **2,015 tests, zero failed**, in 17.313s.
The complete Build module passes **886 tests, zero failed, six Windows-only
skips**, in 19.960s. Windows packaged callbacks, initialization transitions,
shared preload/workers, callback input-lease coverage, full-root verification
and public callback documentation remain outstanding for this milestone.

The current solution Release build passes with zero warnings and errors in
59.49s. API generation now produces 171 pages and 2,258 members, exposing the
callback attribute while retaining the existing filter for generated interop
contracts. The documentation site builds all 213 pages in 3.26s, and its check
reports zero errors, warnings and hints. Windows packaged callback/hook validation
is running against the refreshed owned checkout; no result is claimed yet.

Windows x64/PostgreSQL 18.1 now passes the same packaged Native AOT callback and
executor-hook test: one passed, zero failed or skipped, in 7m27.294s. This proves
the direct generated import, used-only callback linking, exact results, error
ownership/recovery and explicit hook chaining/restoration on Windows as well as
Linux. It does not yet establish shared-preload or parallel-worker behavior for
the new callbacks. The latest hosted CI and documentation for `95031c5` were
rechecked and remain successful.

The packaged consumer is being extended to invoke a callback during
`PgInitialize`, install its executor chain there, and report exact initialization
and callback values from actual parallel workers. The same published library is
also tested under shared preload with two independent backends. Assertions
require launched workers, exclude leader-evaluated rows, retain one initializer
per process (or inherited postmaster state), and check unchanged leader state.
This lifecycle extension is running locally; no passing result is claimed yet.
The README and raw-values guide now describe the static managed callback API,
explicit hook ownership and native error/lifetime contracts.

The lifecycle extension now passes on Linux x64/PostgreSQL 18.6: the packaged
test completes with one passed, zero failed or skipped, in 3m53.817s. It invokes
the callback during initialization, verifies a single initialization and exact
result, then executes the installed executor chain in ordinary backends and
under shared preload. Real parallel worker plans launch workers; every one of
30,000 returned row evaluations belongs to a worker rather than the leader.
Worker initialization identities and results are exact, and the leader's state
is unchanged afterward. Two separately connected preloaded backends retain the
postmaster initializer and independently restore their own hook chains.
Windows PostgreSQL 17 execution of these lifecycle cases is now running.

Additional generated-dispatch tests execute scalar, enum, pointer,
function-pointer and aggregate results with unaligned output and surrounding
sentinels. Successful and throwing handlers expire their own callback input
leases, retain the enclosing lease until its callback ends, and use fresh leases
on retry. Twelve malformed native metadata cases and four missing-handler cases
report `ANKUS021` without a callable dispatcher. The focused generator run passes
57 tests with zero failures or skips in 3.452s; an additional empty-native-value
case and complete-module verification follow before milestone acceptance.

The final generator module, including the empty-native-value dispatch case,
passes **2,034 tests, zero failures or skips**, in 18.144s. The updated public
site builds 213 pages in 3.52s; its check reports zero errors, warnings and hints.

Windows PostgreSQL 17.7 exposes a pre-existing native compatibility defect during
fixture publication: `EXTENSIONOID` is unavailable in that installation's
headers. The preceding hosted Windows run used PostgreSQL 17.11, so its success
does not establish 17.7 compatibility. Extension-owned type lookup now uses the
public `get_extension_schema` API on PostgreSQL 16 and later. For 13–15, where
that routine is private, it uses PostgreSQL's indexed `pg_extension` catalog
scan, closes the scan/relation, and retains the missing-extension error. The
read-only PostgreSQL 13–18 reference headers establish these API boundaries;
actual version/platform execution is still reported separately. The Windows
17.7 retry and full local verification are pending this correction.

The correction passes a complete Release build with zero warnings/errors in
59.32s. Windows 17.7 has now executed both
`EnumExtensionRelocationAndReinstallationFollowCatalogIdentity` and
`CustomTypeOnlyExtensionTracksRelocationAndReinstallation` successfully; its
packaged callback lifecycle test is still running. The plain root suite is also
running, with all five non-integration modules already passing and the six
existing Windows-only Build skips recorded separately.

Final review identified two additional witnesses before callback acceptance.
Exact `async void` signatures now have ordinary and partial-method rejection
cases, independent of incompatible return-type rejection. Worker snapshots also
require actual executor-hook order before the first row, rather than only an
installed callback address. The current running binaries predate those last
assertions. They need a subsequent narrow run and final root verification.
In particular, pre-18 Windows defers managed initialization during library/GUC
restoration; its effect on the first worker executor hook remains unproven and
must be resolved without regressing snapshot or GUC-restoration behavior.
No commit has been made for this unfinished callback milestone.

The Windows 17.7 retry is now complete: all three selected tests pass, zero
failures or skips, in 8m26.536s. This includes enum/custom-type relocation and
the seven-field callback initialization/preload/parallel-worker witness. The
stronger executor-order assertion was added afterward and is now being run
against a freshly compiled test in the separate Windows checkout. The Linux
full-suite process remains active; its shared outputs are not being rebuilt.

That plain root run has now completed on Linux x64/PostgreSQL 18.6:
**7,808 passed, zero failed, six Windows-only skips, 7,814 total**, in
14m21.172s. Integration passes in 14m20.515s. This result covers the callback
implementation and catalog compatibility correction, but predates the final
two async-void diagnostic cases and first-worker executor-order assertion.
Those additions remain under narrow verification before the final milestone run.

Both exact async-void rejection cases pass, zero failures/skips, in 2.055s.
The stronger Windows 17.7 worker check fails as intended: the first worker row
observes an empty executor order instead of `1,2,3,4`. The earlier seven-field
witness proved callback invocation and registration but missed this ordering
defect. Windows defers managed initialization until worker state is restored,
so installing a hook from that phase is too late for the first executor entry.

The repair separates immediate native registration (`PgModuleLoad`) from the
existing `PgInitialize` phase. Module registration must run in library load order
before any worker query, with SQL unavailable while PostgreSQL is restoring its
worker state. Existing SQL-capable initialization and GUC replay retain their
deferred behavior. This phase separation is being implemented and tested; the
callback milestone remains uncommitted until the stronger witness passes.

The immediate registration phase now compiles alongside or without a deferred
initializer. Generator validation rejects invalid signatures, duplicate phases
and mixed attributes. The complete generator module passes 2,048 tests with no
failures or skips; Release passes with zero warnings/errors. API generation
produces 172 pages and 2,259 members, and the documentation check reports no
errors, warnings or hints. These results precede the provider-composition
refinement described below.

Narrow Linux x64/PostgreSQL 18.6 validation passes all 23 initialization and GUC
worker cases. The packaged callback case cannot start because its setup's SDK
pack process exits with code 135 while the temporary filesystem is full; this is
not a passing callback result. Stale generated compiler staging has been preserved
in verified archives and removed from temporary storage before retrying.
Windows x64/PostgreSQL 17.7 also passes those 23 existing cases. The callback case
now passes the previously failing first-worker executor-order assertion and
ordinary-worker SQL initialization. It then exposes an incorrect shared-preload
test expectation: a preloaded Windows worker initializes before a transaction
exists, so its initial SQL capability is unavailable. The expectation now
distinguishes shared preload from backend-loaded worker restoration without
weakening the hook-order assertion.

Further review identifies a composition gap: a consuming extension needs the
native dispatcher even when all callback declarations live in a referenced
provider. A generated assembly capability now propagates that requirement
through references, including an ordinary intermediary, while each assembly's
initialization methods remain local. Both new generator cases pass. The existing
packaged fixture now places all native callbacks in its provider and calls them
through the consuming extension's SQL and initialization methods, reusing the
same publication and backend scenarios. It also checks direct and nested SQL
availability during early registration. These stronger backend checks and the
final full suite are pending; no new platform pass or complete-port claim is made.

The stronger packaged fixture now passes on Linux x64/PostgreSQL 18.6: one
passed, zero failed/skipped, in 4m14.113s. All native callback declarations live
in the referenced provider, while the consuming extension owns SQL exports,
configuration and both initialization phases. Thirteen-field snapshots check
the first worker hook, exact process/initialization identity and direct/nested
registration SQL capability. Failed registration and later initialization retry
independently with exact owned diagnostics, finally counts and same-session
recovery. Shared preload and two independent backends pass. Complete generator
validation passes 2,050 tests without failures/skips in 17.281s; Release passes
without warnings/errors. Windows execution and the final root suite remain
pending. Documentation checks and API freshness pass; the site builds 214 pages.

Windows x64/PostgreSQL 17.7 now passes the same final provider/phase/preload/worker
fixture: one passed, zero failed/skipped, in 8m19.145s. This includes the original
first-worker executor-order regression and the direct/nested early-worker SQL
capability checks, with initialization SQL becoming available after ordinary
worker restoration. Both platform witnesses pass; the plain root test suite is
running before committing this milestone.

The final plain root `dotnet test` run passes on Linux x64/PostgreSQL 18.6:
**7,824 passed, zero failed, six Windows-only skips, 7,830 total**, in
13m49.027s. Integration completes in 13m48.333s and all five unit modules pass.
The final Release build has zero warnings/errors; API freshness checks 172 pages
and 2,259 members, documentation checks report zero errors/warnings/hints, and
the final site rendering produces 214 pages. No warning suppression, analyzer
reduction, test sharding or timeout increase beyond the requested 60 minutes was
introduced. Full-port requirements and the remaining platform/version matrix
remain open.

Immediately before committing this milestone, the previous `95031c5` runs are
checked again: [CI 36296692977](https://github.com/willibrandon/ankus/actions/runs/36296692977)
and [Docs 36296693000](https://github.com/willibrandon/ankus/actions/runs/36296693000)
are both completed successfully. The prior timeout-only commit's documentation
run passed and its superseded CI run was cancelled. Cancelled runs are not
counted as passing validation. These outcomes are recorded before the commit;
they will be checked again immediately before pushing.

### Shared storage and lightweight locks — implementation in progress

The next required runtime area now has ordinary static `PgLwLock<T>` descriptors,
explicit `PgSharedMemory.Initialize` registration, deferred unmanaged value
initializers, and disposable shared/exclusive guards. The native bridge requests
selected-header shared storage and named lock tranches, chains request/startup
hooks, preserves existing values on attachment, and uses monotonic acquisition
identities so an expired guard cannot release a replacement lock. Callback exit
releases forgotten guards. Shared data contains unmanaged bytes and layout
identity; managed initializer references remain process-local.

Direct Runtime validation currently passes eleven cases covering exact names,
type identity and values, deferred/default initialization, failed registration
retry, initializer diagnostics, guard copies, disposal, nested callback cleanup,
and foreign-thread/provider rejection. One generator case passes for ordinary
registration and selected-header hook ordering. A first real-server attempt
identified an incorrect preload flag name during native compilation; this is
fixed against the PostgreSQL 18.6 headers, and the real-server test is running.
The native guards also account for shared locks released by PostgreSQL error
recovery when restoring interrupt holdoffs; this requires actual backend proof.

The public shared-memory guide and README describe this API, but platform
validation is not yet complete and this milestone is not committed. Contention,
failure/recovery, Windows attachment, Release/full-suite/docs/API checks and
review remain pending. Atomics, bounded shared collections, spinlock conveniences,
static/dynamic background-worker APIs and the complete PostgreSQL/platform matrix
remain required for the faithful port. No full-parity claim is made.

The preceding callback milestone's Docs run 36304890456 completed successfully.
CI run 36304890457 has successful quality/runtime jobs and all three platform
test jobs still running at the latest check. Fresh outcomes will be recorded
again before a commit and push.

The first actual Linux x64/PostgreSQL 18.6 witness now passes in 3m49.622s.
Independent backends observe exact signed/unsigned values and persistent shared
writes; managed errors, native division errors, recursive acquisition rejection,
forgotten/expired guards and replacement after subtransaction abort all recover.
A statement timeout still fires after recovery, proving interrupts were not left
disabled. Full Runtime and generator suites pass: 1,555 and 2,051 respectively,
with zero failures/skips. Stronger startup-name/factory failure and observed
reader/writer contention checks are running on Linux and Windows PostgreSQL 17.7.
Review also preserves ordered initializer dependencies: a later factory can read
an earlier initialized value in the postmaster; waiting before PostgreSQL process
initialization is rejected instead of allowing a native PANIC. Those latest
changes still require rerunning the backend witnesses before final verification.

The stronger failure cases exposed two concrete defects, both still under final
verification. On Linux, a failing shared-memory factory hung during startup:
native cleanup called the managed diagnostic allocator after parking the
postmaster runtime. Shared-memory/native-callback dispatch and configuration
hook frames now keep the runtime active through owned-buffer cleanup, including
native error unwinding. On Windows PostgreSQL 17.7, the first shared read crashed:
`GetNamedLWLockTranche` consults a postmaster-private request array unavailable in
the child. Following PostgreSQL's shared-address contract and pglogical's attach
pattern, the shared header now retains the actual shared lock, validates that
address on attachment, and registers its wait-event name in each process. No
process-local managed/native registration pointer is placed in shared storage.

The regression fixture now also checks previous-startup-hook ordering and its
owned failure diagnostics, ordered initializer dependencies, and a rejected then
successful postmaster configuration reload. Expanded Linux and Windows witnesses
must finish successfully before this milestone is eligible to commit; the earlier
Linux pass does not prove these later additions.

The recovery runs now pass the expanded startup/attachment/contention cases on
both systems: Linux x64/PostgreSQL 18.6 in 4m35.878s, and Windows x64/PostgreSQL
17.7 in 7m05.437s, one test passed with zero failures/skips on each. This confirms
the formerly hanging factory failure and Windows attachment crash are repaired.
Documentation checking reports zero errors/warnings/hints. The final lifecycle
test additionally verifies native startup-hook errors, postmaster configuration
reload rejection/recovery, and recreation of shared storage after an owned test
backend crashes. These additions are running before the full validation gates.

The previous callback commit's hosted CI run 36304890457 is now terminal:
quality and all runtime jobs pass; Linux passes in 31m14s and macOS in 41m44s.
Windows is cancelled at 60m07s with the explicit annotation that the job exceeded
its one-hour execution limit. All five Windows unit modules finished successfully;
integration had not completed. Docs run 36304890456 passed. The Windows log shows
an 11m21s solution build before integration starts, despite a NuGet cache hit.
The 60-minute limit remains unchanged. This timeout is unresolved; build-time
investigation is continuing without suppressing checks or sharding the suite.

Final shared-memory review also removes callback roots as soon as a lock lease
is released or acquisition fails, so long-running callbacks do not accumulate
disposed leases. A weak-reference lifetime witness joins the direct checks:
all 15 shared-memory Runtime cases pass with zero failures/skips. The latest
Release build passes with zero warnings/errors; the site builds 219 pages,
documentation checks report zero errors/warnings/hints, and API freshness
verifies 176 pages and 2,269 members. Full Linux validation and the final Windows
lifecycle witness are running. The reload fixture now writes the isolated
cluster's configuration directly because `ALTER SYSTEM` correctly validates
and rejects the deliberately bad value before a postmaster reload can occur.

The final Windows x64/PostgreSQL 17.7 shared-memory lifecycle witness passes in
7m42.215s, one test passed with zero failures/skips. It covers prior startup-hook
ordering and owned error cleanup, rejected then accepted postmaster reloads,
independent backend attachment, observed reader/writer contention, native and
managed recovery, stale/replaced guards, and reinitialization after an owned
backend crash. This focused pass does not replace the hosted full Windows suite,
whose preceding run still timed out at one hour.

The completed root `dotnet test` run passes on Linux x64/PostgreSQL 18.6:
**7,841 passed, zero failed, six Windows-only skips, 7,847 total**, in
14m55.579s. Integration passes in 14m54.887s and all five unit modules pass.
This includes the final startup-hook, configuration-reload, crash-recovery and
lease-retention changes. Release, API freshness, documentation checking and
site generation passed for the same implementation. Atomics, bounded shared
containers, spinlocks, high-level workers and the complete PostgreSQL/platform
matrix remain required; the faithful port is not complete.

Immediately before committing, the previous hosted outcomes are checked again:
[CI 36304890457](https://github.com/willibrandon/ankus/actions/runs/36304890457)
remains cancelled because Windows exceeded one hour; its quality, runtime,
Linux and macOS jobs passed.
[Docs 36304890456](https://github.com/willibrandon/ankus/actions/runs/36304890456)
passed. The preceding `95031c5` CI and Docs runs also passed. The unresolved
Windows timeout is recorded without treating it as successful validation.
Local binary-log analysis identifies native binding preparation as the dominant
Windows build cost; parallel builds are already functioning. Reducing that cost
within the existing 60-minute limit is the next CI repair priority. No standards
are reduced and the full suite remains in each platform job. These outcomes
will be refreshed again immediately before pushing.

### Bounded package-consumer execution

The shared-memory milestone is committed as `9a8e907`. Its hosted Docs run
36309864488 passed; CI 36309864480 has successful quality/runtime jobs with
all three platform suites still running at the latest observation. The preceding
Windows one-hour timeout remains unresolved. All workflow limits remain 60 minutes.

Two small build experiments did not establish useful timeout savings: larger
hash buffers saved only hundredths of a second on warm Windows SDK reads, and
ordinary versus graph-scheduled warm solution builds took 59.164s and 57.838s.
All compared builds retained the full solution and analyzers. Neither change
was adopted. Direct phase measurement instead shows one cold Windows companion
compile taking 30.398s of 32.453s total, with verified warm reuse taking 1.793s.
These are local Windows x64/SDK 10.0.401/.NET 10.0.12 measurements using existing
caches and PostgreSQL 18.1 headers, not hosted CI timing improvements.

The package-consumer class currently serializes many independent publishes
because pre-18 tests overwrite the same extension control files in one staged
PostgreSQL installation. The new fixture reserves two concurrent consumer slots,
each with an independent staged installation on pre-18 servers. PostgreSQL 18+
continues to use per-consumer extension directories. Tests that change the shared
sample's build settings remain serial, and shared reference resolution is guarded.
All tests, analyzer settings and platform jobs are retained. The integration
project builds with zero warnings/errors; same-name Windows PostgreSQL 17 consumer
cases, the complete Linux suite and the full Windows consumer class must still
pass before this change is ready to commit. No CI speedup is claimed yet.

The focused Windows x64/PostgreSQL 17.7 run now passes **6/6**, with zero
failures/skips, in 7m51.770s. It exercises parallel enum-only and schema-only
consumers with the same extension name, an independent packaged MSTest consumer,
and the serial invalid-build-setting cases. Two independent staged control-file
directories were observed while the consumer tests ran, and class cleanup
completed successfully. The full Release build passes with zero warnings/errors;
the documentation build produces 219 pages, site checking reports zero
errors/warnings/hints, and API freshness verifies 176 pages/2,269 members.
All five Linux unit modules have passed. Linux integration and the complete
Windows package-consumer class are still running; this focused result does not
prove the complete Windows suite or resolve the hosted timeout by itself.

The completed root `dotnet test` run passes on Linux x64/PostgreSQL 18.6:
**7,841 passed, zero failed, six Windows-only skips, 7,847 total**, in
10m49.964s. The preceding full run took 14m55.579s with the same test counts;
this local comparison includes cache and concurrent workload differences and is
not a hosted CI speedup measurement. The complete Windows package-consumer class
remains under validation before committing.

The first complete Windows x64/PostgreSQL 17.7 consumer run passed all 74
cases in 14m58.817s, but class cleanup failed because a reusable MSBuild worker
still had the fixture's `Microsoft.Testing.Platform.MSBuild.dll` loaded. Native
process inspection confirmed the retained assembly belonged to this fixture.
Consumer subprocesses now disable MSBuild node reuse so their temporary package
assemblies can be removed. Cleanup errors remain failures; the complete class
must pass again before committing. The hosted shared-memory milestone has now
passed Linux in 39m13s and macOS in 46m32s; Windows remains in progress.

The same hosted CI run 36309864480 is now terminal: Windows again exceeded its
one-hour limit at 60m07s. Quality, all runtime jobs, Linux and macOS passed;
Docs run 36309864488 also passed. This run predates the bounded consumer fixture.
The timeout remains unresolved until a complete hosted Windows run proves the
change sufficient. The timeout limit, complete test coverage and analyzer
standards are unchanged.

After the worker-lifetime correction, final root `dotnet test` again passes on
Linux x64/PostgreSQL 18.6: **7,841 passed, zero failed, six Windows-only skips,
7,847 total**, in 10m30.871s (integration 10m30.132s). The final Release build
passes with zero warnings/errors in 1m09.99s; documentation checking has zero
errors/warnings/hints, site generation produces 219 pages, and API freshness
verifies 176 pages/2,269 members. The Windows consumer rerun remains active.

The final Windows x64/PostgreSQL 17.7 consumer run now passes **74/74**, with
zero failures/skips, in 18m05.184s. Class cleanup succeeds and the fixture's
temporary package, project and staged-server root is removed. This repairs the
retained MSBuild task observed in the initial run. The earlier six-case Windows
focus and the complete Linux suite also passed. These local results validate
test isolation and cleanup, not the duration of a complete hosted Windows job.
The new hosted run must still establish whether two concurrent consumers provide
sufficient headroom within the unchanged one-hour limit. No checks are omitted,
sharded or suppressed. Atomics, bounded shared collections, spinlocks, high-level
workers and the complete PostgreSQL/platform matrix remain required full-port work.

Immediately before committing, hosted outcomes are refreshed and recorded:
[CI 36309864480](https://github.com/willibrandon/ankus/actions/runs/36309864480)
is cancelled because Windows exceeded one hour; its quality, runtime, Linux and
macOS jobs passed. [Docs 36309864488](https://github.com/willibrandon/ankus/actions/runs/36309864488)
passed. The preceding `8f2d1e0` CI also timed out on Windows while its Docs passed;
the `95031c5` CI and Docs passed. These outcomes will be checked and recorded again
immediately before pushing. The next hosted run must validate the full platform
suites with the bounded consumer fixture; the earlier timeout is not treated as
successful validation.

### Shared scalar atomics — Linux and Windows validation

The implementation adds static `PgAtomic<T>` descriptors registered through
`PgSharedMemory.Initialize`. Primitive and enum scalars retain their exact bits
through .NET `Interlocked` reads, exchanges and comparisons. Integer arithmetic
wraps with return-new semantics; integer and Boolean bitwise updates return the
old value. This scalar surface does not complete pgrx's general shared aggregate
and immutable-view contracts.

The native protocol distinguishes atomic values from lock-protected storage,
reserves padding for narrow atomic instructions, and compiles its admission-slot
layout against the selected PostgreSQL headers. Managed operations do not call
PostgreSQL from worker threads. Native attachment publishes the address after
initialization; shutdown closes reader admission before retiring the segment.
Windows attaches during shared-memory startup, and Unix children reset inherited
process-local reader counts before publishing their own process identity.

The final direct Runtime focus passes 37 cases, including existing lock cases,
invalid initializer buffers and the reader-limit boundary. Both generator cases
pass. Arithmetic retries release admission between individual compare/exchange
attempts so retirement does not depend on another process ceasing updates.
README and the public shared-memory guide describe the API, exact update semantics
and remaining scope. Public documentation builds 221 pages, site checks have zero
errors/warnings/hints, and API freshness verifies 178 pages and 2,286 members.

The stronger native run caught an unused C helper during a retirement experiment.
The final native retirement path calls its helpers directly and does not require
resuming a dormant runtime; warning enforcement is unchanged. A permanently
running explicit postmaster thread was rejected by the Unix runtime's checkpoint.
Inspection of PostgreSQL's postmaster rules and the pinned runtime confirms that
Unix checkpoints require those threads to finish, rather than parking arbitrary
user stacks. The cross-platform witness therefore retains one timer callback,
which the runtime can drain and resume, and excludes inherited child copies with
its captured owner PID. This still exercises active Windows readers and an
original callback accessing replacement storage. The public initialization guide
now explains the preload thread lifetime contract.

The explicit-thread variant passes on Windows x64/PostgreSQL 17.7 in 7m23.052s.
The final timer-based native witnesses pass 2/2 on Linux x64/PostgreSQL 18.6 in
4m01.575s and 2/2 on Windows x64/PostgreSQL 17.7 in 7m04.799s, with zero
failures/skips and successful fixture cleanup. They prove independent backend
and managed-thread updates, exact scalar bits, unchanged state after failed CAS,
error/rollback persistence, and the original postmaster timer callback writing
its captured epoch into replacement storage. The complete root `dotnet test` run
passes on Linux x64/PostgreSQL 18.6: **7,865 passed, zero failed, six Windows-only
skips, 7,871 total**, in 10m41.815s (integration 10m41.059s). The preceding hosted
CI run for `d6b5cd5` is fully successful: Linux 31m04s, macOS 38m21s and Windows
54m25s. Docs 36313857910 also passes. This resolves the earlier Windows timeout;
all limits remain one hour and no suites are omitted or sharded.

The final Release solution build passes on Linux with zero warnings/errors in
58.85s. Windows Release solution and affected generator/integration builds also
pass. These local gates include the final native retirement implementation and
timer witness. The hosted scalar-atomic platform run is still required.

| Requirement | Concrete evidence |
|---|---|
| Supported types and invalid inputs | `AtomicDescriptorsRejectInvalidContracts`, `AtomicScalarOperationsPreserveExactBits`: invalid names/types fail, every scalar width and enum retains exact exchange/CAS values and surrounding bytes |
| Registration and initialization | `AtomicRegistrationPreservesIdentityAndInitialization`, `AtomicRegistrationFailureCanRetryWithoutOldInitializer`, `AtomicInitializerRejectsInvalidStorageBeforeCallingFactory`: exact identity/kind/size, deferred/idempotent initialization, stale factory rejection, zero/default retry and untouched invalid buffers |
| Admission and address lifetime | `AtomicRegistrationRejectsInvalidAccessStorage`, `AtomicAccessRejectsUnpublishedOrInvalidStorage`, `AtomicAdmissionAcceptsLastAvailableReader`, `AtomicAdmissionPreservesActiveLeaseAndReplacementSegment`: invalid slots, process ownership, reader limits, closed admission, balanced disposal and replacement addresses |
| Arithmetic and bitwise return contracts | `AtomicArithmeticPreservesDotNetReturnAndWrapContracts`: every integer width wraps, arithmetic returns the new value and integer/Boolean bitwise updates return the old value |
| Floating-point bit identity | `AtomicFloatingPointOperationsPreserveNaNPayloadsAndSignedZero`: distinct NaN payloads and signed zero remain distinct comparands |
| Managed thread access | `AtomicOperationsWorkAcrossThreadsWithoutBackendCalls`: exact total under contention, no worker errors, no PostgreSQL requests after registration and zero retained admissions |
| Generated contracts | `AtomicRegistrationCompilesWithSelectedHeaderLifetime`: compiling C# consumer, selected-header layout, publication/retirement order, process attachment and native request shape |
| Native processes and replacement storage | `SharedAtomicsPreserveValuesAcrossProcessesAndThreads`: packaged Native AOT on the two stated platforms; first backend access on a managed worker, two synchronized backends, owned startup errors, transaction/error persistence and captured timer epoch after a test-owned backend crash |
| Existing lock behavior | `SharedMemoryLocksPreserveValuesAcrossBackendsAndFailures` passes alongside the new atomic witness on both platforms |

General shared aggregate/immutable views, bounded shared containers, spinlocks,
high-level background workers and the complete PostgreSQL/platform matrix remain
required. Scalar support and these focused platform witnesses do not complete
the faithful port.

Immediately before this milestone's commit, hosted outcomes were refreshed:
[CI 36313857919](https://github.com/willibrandon/ankus/actions/runs/36313857919)
and [Docs 36313857910](https://github.com/willibrandon/ankus/actions/runs/36313857910)
for `d6b5cd5` both pass. The preceding `9a8e907` and `8f2d1e0` CI runs remain
recorded as Windows timeouts; their Docs runs passed. Outcomes are checked and
recorded again immediately before pushing. The new run must establish the scalar
milestone's complete hosted platform results.

### Shared aggregates and inline atomic fields — Linux and Windows validation

Read-only pgrx `atomics.rs` and `shmem.rs` require general immutable shared data
and interior synchronization in addition to scalar descriptors. `PgShared<T>`
now supplies a synchronous `scoped in T` callback with native address admission.
Ordinary unmanaged fields remain readonly; inline `PgAtomicValue<T>` fields
provide the supported scalar Interlocked operations in an eight-byte aligned
slot. Callbacks cannot return or capture a borrowed reference in safe C#.
Copying a field copies its storage, so documentation demonstrates updating the
original field through the callback parameter. Multiple fields do not become an
aggregate transaction.

The native protocol adds a distinct aggregate kind, reuses process attachment
and close/drain retirement, permits arbitrary unmanaged sizes, and allocates no
LWLock for aggregate views. Final direct Runtime validation passes 52 cases including
existing scalar/lock behavior; focused generator/compiler validation passes seven.
The tests check exact wide/inline-array data, readonly field updates, NaN payloads
and signed zero, wrapping arithmetic and return values, packed-layout rejection,
nested/throwing callbacks, balanced admission and concurrent worker updates.

README and the public shared-memory guide describe the API and lifetime contract.
The generated API reference includes the aggregate, inline atomic value and
scoped reader delegate. Delegate pages now show their documented signature and
parameters without compiler-generated asynchronous invocation methods; links to
synthetic members resolve to the declaration itself.
The preceding scalar milestone `77e9965` is pushed; Docs 36318146663 passes and
CI 36318146632 is running. Bounded containers, embedded spinlocks, high-level
workers and the full platform/version matrix remain required.

The first native aggregate/scalar/lock focus passes **3/3** on Linux x64,
PostgreSQL 18.6, in 5m42.599s. It proves exact immutable and inline-array values,
original atomic field updates across independent backends and managed threads,
startup error ownership, throwing callbacks and same-session recovery, rollback
persistence, rejection of unaligned packed atomic fields, and the original
postmaster timer using the recreated segment. The Windows Release solution build
passes with zero warnings/errors in 3m23.19s. The Windows x64/PostgreSQL 17.7
aggregate/scalar/lock run now passes **3/3**, zero failures/skips, in 9m22.618s,
with successful fixture cleanup. The final Linux Release solution build passes
with zero warnings/errors in 1m10.82s. The final direct focus includes default
aggregate/cell initialization and immediate arithmetic-state assertions.

The final site build produces 225 pages, site checks have zero
errors/warnings/hints, and API freshness verifies 182 pages/2,305 rendered
members. The complete root `dotnet test` run passes on Linux x64/PostgreSQL 18.6:
**7,885 passed, zero failed, six Windows-only skips, 7,891 total**, in
11m17.409s (integration 11m16.780s). The full port is not complete.

| Requirement | Concrete evidence |
|---|---|
| Names, null input and registration | `SharedViewsRejectInvalidContracts`, `SharedViewsPreserveInitializationAndIdentity`: exact argument names, no invalid native calls, deferred/idempotent factory, aggregate kind, type digest and size |
| Immutable values, inline arrays and defaults | `SharedViewsReadExactAggregateValues`, `SharedViewsInitializeDefaults`, `SharedViewInitializersRejectInvalidStorage`: exact Guid/Int128/decimal and five-byte array contents, zero initialization, untouched invalid buffers and valid retry |
| Scoped lifetime, nesting and recovery | `SharedViewsReleaseAdmissionAndObserveReplacement`: nested counts, original exception identity, preserved writes, balanced admission, closed-reader rejection and replacement address |
| Inline scalar bits, alignment and copies | `AtomicValuesPreserveExactScalarBits`, `AtomicValuesCompareFloatingPointBits`, `AtomicValuesRejectInvalidStorage`, `AtomicValuesUpdateReadonlyAggregateFields`: all widths/enum, NaN/signed-zero comparisons, surrounding bytes, unsupported defaults, misalignment and actual readonly-field versus copied storage |
| Arithmetic and concurrent updates | `AtomicValuesPreserveArithmeticContracts`, `SharedViewsSupportManagedThreads`: wrapping arithmetic, exact return/stored values, four-thread total, preserved metadata and no worker PostgreSQL calls |
| Generated and language boundaries | `SharedAggregateRegistrationEmitsNativeAccessProtocol`, `SharedAggregateCallbacksPreserveScopedReadonlyReferences`: positive compilation, selected native kind/attachment/retirement protocol and compiler rejection of ordinary field mutation, borrowed-span escape and reference capture |
| Native shared behavior | `SharedAggregatesPreserveValuesAcrossProcessesAndThreads`: packaged Native AOT on the two stated platforms, independent backend PIDs, first access on a managed worker, synchronized threads, startup errors, error/rollback persistence, packed-field rejection and an original timer using the replacement segment |
| Existing shared families | `SharedAtomicsPreserveValuesAcrossProcessesAndThreads` and `SharedMemoryLocksPreserveValuesAcrossBackendsAndFailures` pass alongside aggregates on both platforms |

Bounded shared containers, embedded PostgreSQL spinlocks, high-level background
workers and the complete PostgreSQL/platform matrix remain required. Passing this
shared-memory milestone does not establish completion of the faithful port.

Immediately before committing, hosted outcomes were refreshed and recorded:
[CI 36318146632](https://github.com/willibrandon/ankus/actions/runs/36318146632)
for `77e9965` has passed quality, all runtime jobs, Linux in 21m56s and macOS in
34m54s; its full Windows job remains active. [Docs 36318146663](https://github.com/willibrandon/ankus/actions/runs/36318146663)
passed. The preceding `d6b5cd5` CI/Docs runs both passed; the older `9a8e907`
Windows timeout remains recorded as cancelled, not successful. These outcomes
are checked and recorded again immediately before pushing. Development continues
without waiting for the remaining hosted job; the new run must establish the
aggregate milestone's complete hosted platform results.

### Bounded shared collections — Linux and Windows validation

The next shared-memory phase ports the `pgrx-examples/shmem` fixed `Vec`,
`Deque` and `FnvIndexMap` operations. `PgFixedList<T>`, `PgFixedDeque<T>` and
`PgFixedMap<TKey, TValue>` now borrow spans and count/head metadata stored in
ordinary unmanaged inline-array owners. Copies of an owner are independent;
copies of a view alias the owner's fields. The existing `PgLwLock<T>` copy and
explicit publication boundary preserves native error/lock validation without
exposing mutable native references after PostgreSQL releases a lock.

Lists support bounded append/range append, insertion, ordered or swap removal,
indexing and popping. Deques support both ends and circular wraparound. Owned
snapshots and draining let SQL iterators outlive a guard without borrowing its
storage. Maps retain insertion order until swap removal, preserve original
keys on replacement, accept replacement at capacity, and repair collision
chains using cached stable hashes. Their scalar defaults normalize equal NaNs,
signed zeros and decimal scales; custom keys require deliberate process-stable
equality and hashing. `README.md` and the shared-memory public guide describe
the storage and publication contracts.

The focused Runtime run passes **29/29** cases in 894ms. The packaged native
witness uses custom SQL values, 400-element list/deque buffers and a four-entry
map, following pgrx's example capacities. Its first Linux/PostgreSQL 18.6 attempt
exposed a witness query requesting Npgsql binary output for the text-only custom
type; the SQL now casts nullable results to text. Subsequent Linux and Windows
runs exposed the same existing set-result conversion defect: the native wrapper restored
the caller's function identity before converting each returned row, so a
custom type declared without an explicit schema could not be resolved. The
row conversion now retains the set function's identity through conversion and
restores it in `PG_FINALLY`. The witness retains its custom SQL type and adds
array results, extension relocation under a restricted search path, explicit
materialization, iterator-error cleanup and same-session recovery. The repaired
Linux x64/PostgreSQL 18.6 native focus passes **25/25** cases, including the new
witness and existing `CustomTypeTests`, in 3m57.880s. The same focus passes
**25/25** on Windows x64/PostgreSQL 17.7 in 6m21.202s. Both have zero failures
and skips. The related generator/compiler focus passes **20/20** in 2s487ms,
including five borrowed-collection compiler cases and the set-conversion
regression. Final Release builds pass with zero warnings/errors: Linux
1m06.06s and Windows 2m23.32s. Documentation builds 231 site pages with zero
check errors/warnings/hints; API freshness verifies 188 pages and 2,367 rendered
members. The complete root `dotnet test` run passes on Linux x64/PostgreSQL 18.6:
**7,921 passed, zero failed, six Windows-only skips, 7,927 total**, in
11m30.624s (integration 11m30.056s). This milestone adds 36 test cases.

| Requirement | Focused evidence |
|---|---|
| Bounded list operations, ordering and exterior bytes | `FixedListPreservesCapacityAndOrder`, `FixedListRejectsInvalidIndices`, `FixedListRejectsInvalidStateAndBounds` |
| Overlapping appends and view versus owner identity | `FixedListCopiesOverlappingRanges`, `FixedListViewsRespectStorageIdentity` |
| Deque ends, wraparound, empty/full/copy boundaries | `FixedDequePreservesWrappedOrder`, `FixedDequeMatchesOrderedModel`, `FixedDequeRejectsInvalidStateAndBounds` |
| Allocation-free view attachment and standard enumeration | `FixedMapAttachmentAndUpdatesDoNotAllocate`, `FixedDequeEnumeratorsRejectInvalidPositions`, enumeration assertions in the direct list/deque/map order cases |
| Map collisions, replacement at capacity, swap ordering and recovery | `FixedMapPreservesCollisionsCapacityAndOrder`, `FixedMapMatchesOrderedModel`, `FixedMapRejectsInvalidIndicesBeforeMutation`, `FixedMapComparerFailuresPreserveStorage`, `FixedMapRejectsInvalidStateAndBounds` |
| Stable hashes, equivalent representations and custom keys | `FixedComparersProduceStableHashVectors`, `FixedComparersPreserveEquivalentKeys`, `FixedComparersRequireExplicitCustomKeyHashing` |
| Inline owner compilation and rejected reference escapes | `FixedCollectionOwnersCompileWithSharedLocks`, `FixedCollectionViewsPreserveBufferLifetimes` |
| Custom set-result conversion retains schema identity and restores its caller | `SetRowsRetainFunctionIdentityDuringCustomTypeConversion`; packaged witness checks relocated custom rows/arrays, materialization and iterator-error cleanup on both stated platforms |
| Real backend visibility, errors, draining, rollback and segment recreation | `BoundedSharedCollectionsPreserveValuesAcrossBackends` — passes on Linux x64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.7 |

Embedded spinlocks, high-level background workers and the complete
PostgreSQL/platform matrix remain required for the full port.

Immediately before committing, hosted outcomes were refreshed and recorded:
aggregate [CI 36320463333](https://github.com/willibrandon/ankus/actions/runs/36320463333)
and [Docs 36320463356](https://github.com/willibrandon/ankus/actions/runs/36320463356)
both pass. CI includes the full Linux suite in 33m42s, macOS in 38m01s and Windows
in 40m04s, plus quality and every runtime job. The preceding scalar CI's Windows
job was superseded and cancelled; its other jobs and Docs passed. Older Windows
timeouts remain historical failures, resolved by subsequent complete platform
runs. All workflow jobs retain the requested 60-minute timeout. Outcomes are
checked and recorded again immediately before pushing, and the new hosted run
must establish this collection milestone's complete platform results.

### Embedded and local PostgreSQL spinlocks — Linux and Windows validation

`PgSpinLockValue<T>` embeds the selected PostgreSQL `slock_t` representation and
an unmanaged protected value inside an admitted `PgShared<T>` aggregate.
`PgSpinLock<T>` provides stable pinned local storage for ordinary backend-owned
values. `PgSpinLockGuard<T>` owns one acquisition and copies or immediately
replaces its protected value. Ten successive updates match pgrx's spinlock
example; the inline form preserves process-shared storage across backends.
Native initialization, acquisition and pre-19 state queries use the selected
server headers. PostgreSQL 19 removed `SpinLockFree`; state queries reject it
before native entry while lock acquisition and release remain available.

Guard allocation, callback registration and release-function preparation finish
before acquisition. Value access performs no PostgreSQL call, and disposal uses
the prepared native release entry point. The guard retains its local pinned
storage, validates callback/thread/provider/process ownership, rejects recursive
acquisition and expires after disposal. Aliases cannot release a subsequent
owner's acquisition. Shared-read exit releases forgotten guards before the
shared admission ends; callback exit releases spinlocks before lightweight locks.
Default, detached and unaligned inline cells fail before native entry. Other
backend operations are rejected while a spinlock is held. Managed terminal
diagnostics can still unwind and release the guard before reaching PostgreSQL.

Twenty direct Runtime cases pass, and the complete Runtime suite passes 1,642
tests. Three generator/compiler cases check the public contracts, selected-header
bridge and forbidden reference escape. The new packaged Native AOT witness
passes on Linux x64/PostgreSQL 18.6 and Windows x64/PostgreSQL 17.7. It verifies
pinned local storage through collection, exact updates across callbacks, two
backends contending for 5,000 updates, rollback persistence, managed ERROR
cleanup, forgotten guards, foreign-thread rejection, and segment recreation
after the owning backend is killed. The Linux/Windows recovery pair also reruns
the existing lightweight-lock crash witness: Linux 2/2 in 4m11.401s; Windows 2/2
in 7m51.840s, both with zero failures/skips. The first Linux witness exposed a
missing explicit generic result on a throw-only callback; that compilation
failure was corrected before these passing runs.

| Requirement | Evidence |
|---|---|
| Exact native storage and updates | `SpinValuesInitializeExactStorage`, `SpinGuardsPreserveUpdatesAndOwnership` |
| Pinned local ownership and stale aliases | `LocalSpinLocksRetainStableOwnedStorage`, `SpinGuardsReleaseOnceAndRejectStaleAccess` |
| Callback/read cleanup and managed errors | `SpinScopesReleaseForgottenGuards`, `SpinSharedReadsReleaseBeforeAdmission`, `SpinManagedDiagnosticsUnwindAndRecover` |
| Provider/thread/range/default/alignment rejection | `SpinGuardsRejectForeignAndRecursiveAccess`, `SpinReadAdmissionRejectsIncompleteRanges`, `SpinValuesRequireAdmittedStorage`, `SpinSharedValuesRequireInitializationAndOriginalStorage`, `SpinValuesRejectInvalidStorage` |
| Critical-section backend rejection and failed acquisition | `SpinGuardsRejectBackendCalls`, `SpinAcquisitionFailurePreservesStorageAndScope` |
| Selected PostgreSQL version and compiled API boundaries | `SpinStateQueriesHonorSelectedMajor`, `SpinLocksCompileWithLocalAndInlineSharedValues`, `SpinGuardValuesPreserveReferenceBoundaries` |
| Original shared storage and replacement-segment isolation | `SpinSharedReadsRecoverFromErrorsAndReplacement`, `SharedSpinLocksPreserveValuesAcrossBackends` |

Static assertion and pseudo-mutation review covers all 19 new test methods,
24 cases, including direct value/error/lifetime assertions and the native
witness. No executed mutation campaign or line-coverage result is claimed.
The first completed full Linux run passes 7,945 tests, zero failures and six
Windows-only skips, 7,951 total, in 12m31.139s. The final full run includes the
Windows FATAL transport assertion repair below and passes the same counts in
12m55.115s (integration 12m54.533s). Release builds pass without
warnings/errors on Linux and Windows. API freshness verifies 191 pages and
2,375 rendered members; the documentation site builds 234 pages and checks with
zero errors, warnings or hints.

An initial local Release build lost an MSBuild worker to SIGBUS while the
temporary filesystem was full. Moving an idle owned checkout intact to
disk-backed temporary storage freed 2.2 GB. No user files or processes were
removed. The final Release build passes in 1m03.11s with zero warnings/errors.

The preceding collection milestone's hosted
[CI 36325012375](https://github.com/willibrandon/ankus/actions/runs/36325012375)
passes quality, all runtime jobs and the full macOS suite (34m41s). Linux fails
the shared-memory recovery witness (34m32s job); Windows reports the GUC assign
termination mismatch and reaches its one-hour job timeout (60m09s).
[Docs 36325012376](https://github.com/willibrandon/ankus/actions/runs/36325012376)
passes. These failures are recorded separately from local passing evidence.

The Linux server log identifies a finalizer retirement acknowledgement failure.
The runtime used to publish retirement admission before the new request number
and exit flag. A finalizer could acknowledge the old number and then observe
the new exit request. Runtime fork commit
`134b853ff766627327405b2fb1f5c0d74266e4b6` publishes the complete request before
closing admission; Ankus now selects payload `10.0.11-ankus.4`. The rebuilt
Release runtime passes 2,048 retirement/restart cycles and the existing two-round
parent/child fork probe. The unrepaired control passes the same stress probe;
the race is identified from source ordering, not a deterministic stress
reproduction. Linux native recovery validation above uses the repaired runtime.
Windows focused spinlock validation uses the preceding payload, whose Unix-only
retirement code is inactive there. Hosted validation of the new payload remains
required. Prior hosted outcomes were checked and recorded immediately before
the runtime fork commit and again before its push.

The Windows assign-only test now associates the terminal operation with a
unique application name and requires the server log's exact FATAL/38000 message.
It also checks that the failed connection is closed and a pre-existing observer
still executes SQL and retains its own GUC value. A normal PostgreSQL exception
must retain the exact severity, SQLSTATE and message. Only Windows may instead
report the specific nested connection-reset socket exception; the server-log
and observer assertions remain mandatory. No server log was available from the
cancelled hosted job, so the transport mismatch alone is not claimed as proof
that its backend terminated correctly. The repaired GUC preload group passes
11/11 on Windows x64/PostgreSQL 17.7 in 4m31.090s, with zero failures/skips.
The final full Linux suite passes as recorded above. The broader local Windows
integration run on PostgreSQL 17.7 finishes with 3,317 passes, six allocator
failures and two Linux-only skips, 3,325 total, in 22m38.975s. The failures require
the fixes in PostgreSQL 17.11 (the hosted CI version).
The older installation is valid for the focused spinlock/GUC evidence above, but
does not establish complete suite support at that patch level. An isolated
PostgreSQL 17.11 validation installation was prepared from EDB's server archive.
The same spinlock-milestone source snapshot now passes the complete integration
module against it: 3,323 passed, zero failures, two Linux-only skips, 3,325 total,
in 23m45.280s. This resolves all six old-patch allocator failures. Existing
PostgreSQL installations are unchanged. No test assertion or version restriction
was weakened.

The hosted Windows console shows approximately 14 minutes building before test
execution. Existing NuGet/runtime caches remain enabled; native binding caches
validate toolchain/input content and are local to each hosted machine. The
one-hour full-suite timeout remains unresolved pending measured hosted repair
results. No timeout is increased, test omitted, diagnostic suppressed or suite
sharded to hide this outcome.

Safe in-place guard access for nested synchronization-bearing values, high-level
background workers, and the complete PostgreSQL 13–19/platform matrix remain
full-port requirements. The copied guard value API does not claim parity with
pgrx's complete dereference/composition surface.

Immediately before the spinlock commit, hosted outcomes were checked again:
CI 36325012375 is completed with the Linux recovery failure, Windows FATAL
transport failure and one-hour timeout recorded above; quality, all runtime
jobs and macOS passed. Docs 36325012376 passed. The preceding aggregate CI
36320463333 and Docs 36320463356 both passed. Local repair evidence and pending
hosted/full Windows validation remain explicitly separate. Outcomes are checked
and recorded again immediately before pushing this milestone.

### Scoped spinlock guard reads — Linux and Windows validation

`PgSpinLockGuard<T>.Read` borrows the original protected value through a scoped
readonly callback. Inline atomics and nested spinlocks therefore operate on
their original local or shared bytes. Copied values retain independent storage.
The parent rejects replacement and disposal through every alias until its
outermost reader returns. Child spinlocks expire before the reader's address
admission ends, including exceptional exits; the parent remains held until its
normal disposal or callback cleanup. Pinned local storage stays rooted through
the complete read. Existing backend-call restrictions remain enforced.

| Requirement | Evidence |
|---|---|
| Exact original fields, atomics and independent copies | `SpinReadsPreserveOriginalStorage` |
| Exact address bounds and withdrawal on return | `SpinReadsBoundTheirOriginalAddressAdmission` |
| Nested locks and forgotten child expiry | `SpinReadsComposeNestedLocks` |
| Parent alias rejection across nested readers | `SpinReadsPreventReplacementAndRelease` |
| Exception identity, cleanup and later reuse | `SpinReadsUnwindAndRecover` |
| Forbidden backend entry and owned terminal diagnostics | `SpinReadsPreserveCriticalSectionBoundaries` |
| Pinned owner lifetime through compacting collection | `SpinReadsRetainPinnedOwners` |
| Null, expired, foreign-provider and foreign-thread rejection | `SpinReadsValidateCallbacksAndOwnership` |
| Release before shared admission and replacement isolation | `SpinGuardReadsReleaseBeforeSharedAdmission` |
| Compiled nested values and rejected reference escapes | `SpinGuardReadersCompileNestedValues`, `SpinGuardReadersPreserveReferenceBoundaries` |
| Native visibility, contention, rollback and crash recreation | Extended `SharedSpinLocksPreserveValuesAcrossBackends` |

All nine direct cases and three compiler cases pass; the complete Runtime suite
passes 1,651/1,651 on Linux in 2.199s and Windows in 1.336s. The packaged Native
AOT witness passes on Linux x64/PostgreSQL 18.6 in 4m52.491s and Windows
x64/PostgreSQL 17.11 in 6m58.501s, both with zero failures/skips. It checks 5,000 updates
from two backends, exact nested atomic/lock values, independent local backend
state, managed ERROR cleanup, rollback persistence and replacement storage after
crash recovery. The final plain `dotnet test` run passes **7,957 tests, zero
failures and six Windows-only skips, 7,963 total**, in 12m28.651s; integration
finishes in 12m28.061s. These results do not establish the complete
PostgreSQL/platform matrix.

Release builds pass with zero warnings/errors: Linux in 1m19.51s and Windows in
3m15.38s. API freshness verifies 191 pages/2,376 members, the documentation site
builds 234 pages, and
site checking reports zero errors/warnings/hints. Static assertion and
pseudo-mutation review maps the reader's value, error, ownership and lifetime
outcomes to the tests above; no executed mutation campaign or coverage metric
is claimed. The larger nested value exposed a test-fixture capacity error: its
replacement region now has an independent bounded allocation region with both
leading and trailing canaries. No assertion or diagnostic severity was reduced.

Original-storage lightweight-lock access, safe mutable guard composition,
high-level background workers and the complete PostgreSQL 13–19/platform matrix
remain full-port requirements.

Immediately before committing, hosted outcomes were checked and recorded:
[CI 36330404311](https://github.com/willibrandon/ankus/actions/runs/36330404311)
passes all runtime builds, quality, Linux (36m11s) and macOS (39m56s); Windows
is still running. [Docs 36330404255](https://github.com/willibrandon/ankus/actions/runs/36330404255)
passes. The older collection CI's Linux recovery and Windows transport/timeout
failures remain recorded above. The subsequent hosted Linux pass and local
Windows full integration pass provide repair evidence; the hosted Windows
outcome is still pending. Outcomes are checked and recorded again before push.
Every job retains the requested 60-minute timeout and the complete platform
suite. Development continues while hosted validation runs.

### Scoped lightweight-lock guard reads — implementation and validation

Shared and exclusive `PgLwLock<T>` guards now expose original protected storage
through `Read`. Nested atomic and spinlock fields retain their original bytes;
ordinary fields are readonly and `Value` remains a copy. Reader depth protects
the parent against alias replacement/disposal. Child spinlocks expire before
the original reference's admission ends, including exceptional exits.

The native address protocol checks the storage kind, acquisition token, current
lock ownership and exact size without allocating, raising PostgreSQL ERROR,
processing interrupts or invoking callbacks. It runs outside the native error
recovery envelope, after the ErrorContext-cleanup restriction. A stale guard
therefore cannot cause PostgreSQL to release an outer reader's lock. Same-guard
recursion and nested reads of other already-held guards work. Invalidating
backend calls are rejected until the final reader returns; terminal managed
diagnostics still unwind normally. Ordinary copied guard access outside these
callbacks keeps its existing backend/error behavior.

| Requirement | Evidence |
|---|---|
| Exact original fields, independent copies and both guard modes | `LwLockReadsPreserveOriginalStorage` |
| Nested reader depth, alias protection and exact address boundaries | `LwLockReadsProtectParentOwnership` |
| Cross-guard reads and preserved outer state after failed admission | `LwLockReadsComposeAlreadyHeldGuards` |
| Exception identity, child expiry, retained writes and parent reuse | `LwLockReadsUnwindAndRecover` |
| Backend-call restrictions, owned diagnostics and restored access | `LwLockReadsPreserveBackendBoundaries` |
| Missing/misaligned/wrong-sized addresses and native failure | `LwLockReadsValidateAdmission` |
| Null, expired, foreign-provider and foreign-thread rejection | `LwLockReadsValidateOwnership` |
| Compiled consumer use and rejected reference capture/mutation | `LwLockGuardReadersCompile`, `LwLockGuardReadersRejectReferenceEscapes` |
| Concurrent original readers, nested cells, rollback and recovery | Extended `SharedMemoryLocksPreserveValuesAcrossBackendsAndFailures` |

The twelve new direct cases pass. Complete Runtime suites pass 1,663/1,663 on
Linux (2.129s) and Windows (1.233s); the affected compiler focus passes 4/4,
including three new cases. The native witness passes on Linux x64/PostgreSQL
18.6 in 4m49.217s total (4m48.387s test-module duration). It requires two
simultaneous shared readers, 5,000 updates across independent sessions, exact
atomic/spinlock values, alias rejection, preserved error diagnostics and writes,
rollback persistence, stale acquisition rejection inside a replacement's reader,
and fresh values after crash recovery. The same native witness passes on Windows
x64/PostgreSQL 17.11 in 7m37.757s, with zero failures/skips. Final plain
`dotnet test` passes **7,972 tests, zero failures and six Windows-only skips,
7,978 total**, in 11m51.041s; integration finishes in 11m50.365s.

Release builds pass with zero warnings/errors on Linux (1m14.34s) and Windows
(3m06.46s). API freshness verifies 191 pages/2,378 members; the site builds
234 pages and checks with zero errors/warnings/hints. README, the public
shared-memory guide and generated API pages describe the original-reference
and lifetime rules. Static assertion and pseudo-mutation review maps values,
ownership, errors and recovery to the tests above; no executed mutation campaign
or coverage percentage is claimed. No diagnostics or assertions were weakened.

Safe mutable guard composition, high-level background workers and the complete
PostgreSQL 13–19/platform matrix remain required for full parity.

Immediately before committing, hosted outcomes were checked and recorded:
[CI 36333403089](https://github.com/willibrandon/ankus/actions/runs/36333403089)
passes every runtime build, quality, full Linux (31m18s) and full macOS
(30m04s); Windows is still running. [Docs 36333403092](https://github.com/willibrandon/ankus/actions/runs/36333403092)
passes. The previous Windows transport/timeout failure remains visible above,
with local repair evidence and full hosted validation explicitly separated.
Outcomes are checked and recorded again immediately before push. All jobs keep
the requested 60-minute limit and full platform suites; development continues
while hosted validation runs.

### Scoped guard mutations — implementation and validation

`PgLwLockExclusiveGuard<T>` and `PgSpinLockGuard<T>` now expose `Mutate` through
`PgSharedMutator<T, TResult>`. The callback receives a scoped mutable reference
to original storage and can return an owned result. Ordinary fields, inline
atomic operations and bounded-collection views update their original bytes.
Updates remain visible when a callback throws or a SQL transaction rolls back.
Copied `Value` reads remain snapshots.

Aliases cannot start a conflicting original read or mutation, replace the value
or release the parent while mutation is active. Mutation cannot begin during a
parent reader. An overlapping child spinlock cannot be acquired or queried,
even when an enclosing or newer readonly frame admits the same storage.
Disjoint regions remain available. A separate `Read` callback provides child
lock access while keeping the same parent acquisition held between callbacks.
Spinlock mutations retain pinned owners; lightweight-lock mutations block
backend calls until the callback returns. Native write-address admission also
checks exclusive acquisition independently, without native ERROR recovery.

| Requirement | Evidence |
|---|---|
| Exact original fields, atomics, detached copies and pinned owner | `SpinMutationsPreserveOriginalStorage`, `LwLockMutationsPreserveOriginalStorage` |
| Alias exclusion, both read/mutation directions and later reuse | `SpinMutationsProtectParentOwnership`, `LwLockMutationsProtectParentOwnership` |
| Exception identity, immediate writes and retained parent | `SpinMutationsUnwindAndRecover`, `LwLockMutationsUnwindAndRecover` |
| Cross-guard mutation and failed inner admission | `LwLockMutationsComposeAlreadyHeldGuards` |
| Missing, misaligned, undersized, oversized and failed native replies | `LwLockMutationsValidateAdmission` |
| Null, disposed, foreign-provider and foreign-thread rejection | `SpinMutationsValidateOwnership`, `LwLockMutationsValidateOwnership` |
| Backend exclusion, exact owned diagnostics and restored entry | `SpinMutationsPreserveBackendBoundaries`, `LwLockMutationsPreserveBackendBoundaries` |
| Exact overlap/adjacency bounds and restored surrounding admissions | `MutationsExcludeOverlappingSpinlockAdmissions` |
| Shared nesting, child ownership and outer callback expiry | `SpinMutationsComposeWithSharedStorage` |
| Original bounded-queue elements/metadata and exceptional persistence | `GuardMutationsPreserveBoundedViews` in both guard modes |
| Compiled APIs, shared-guard rejection and reference escape boundaries | `GuardMutatorsCompile`, `GuardMutatorsRejectInvalidAccess` |
| Cross-backend values, contention, rollback, errors and crash recovery | Extended `SharedMemoryLocksPreserveValuesAcrossBackendsAndFailures`, `SharedSpinLocksPreserveValuesAcrossBackends` |

All 27 new direct cases pass. Complete Runtime suites pass 1,690/1,690 on Linux
in 2.199s and Windows in 1.269s. The compiler focus passes 5/5, including four
new cases. The initial PostgreSQL 18.6/Linux x64 native pair passes 2/2 in
4m57.696s. After adding queue assertions, the final full Linux suite passes
**8,003 tests, zero failures and six Windows-only skips, 8,009 total**, in
12m54.774s (integration module 12m54.157s). The final Windows x64/PostgreSQL
17.11 native pair passes 2/2 with zero failures/skips in 7m53.356s. Both final
runs include the queue assertions.

Release builds pass with zero warnings/errors: Linux 1m30.99s and Windows
3m35.90s. API freshness verifies 192 pages/2,380 members, the site builds
235 pages, and site checking reports zero errors/warnings/hints. README, the
public shared-memory guide and generated API pages describe the callback and
borrowing rules; the queue example now updates original storage directly.
Static assertion and pseudo-mutation review maps every contract above to exact
values, ownership changes or errors. No executed mutation campaign or coverage
percentage is claimed. No assertion or diagnostic severity was reduced.

Immediately before committing, the preceding reader
[CI 36335688901](https://github.com/willibrandon/ankus/actions/runs/36335688901)
passes quality, all runtime jobs and full Linux (35m12s); full macOS and Windows
remain in progress. [Docs 36335688785](https://github.com/willibrandon/ankus/actions/runs/36335688785)
passes. Earlier superseded runs and the prior Windows timeout/repair evidence
remain recorded above. Outcomes are checked and recorded again before push;
running or cancelled jobs do not establish full platform proof. All workflow
jobs retain the requested 60-minute limit and complete unsharded suites.

High-level background workers, the remaining full-port API/behavior audit and
the complete PostgreSQL 13–19/platform matrix remain required.

### Background workers — implementation and validation

`[PgBackgroundWorker]` generates an exported PostgreSQL `void(Datum)` entry
bound to an accessible synchronous static C# `void(nuint)` method. ANKUS022
rejects invalid signatures, inaccessible/generic containers, conflicting metadata
and invalid or duplicate native symbols. The entry establishes native signal
handlers and backend capabilities, unwinds managed code and its cleanup before
raising owned PostgreSQL errors, and uses the existing fork-aware runtime.
No runtime patch or package-version change is needed for this milestone.

`PgBackgroundWorkerOptions` preserves the by-value argument, startup/restart
timing, database flag, notification PID and text. Selected headers determine
field capacities. Dynamic metadata rejects PostgreSQL's lossy ASCII replacement;
static metadata and extra text preserve strict UTF-8. Native registration rejects
invalid preload/backend phases. Observation handles own native storage within
the originating callback, thread and process. Explicit disposal and callback
return release that storage while leaving independently registered workers alive.

`PgBackgroundWorker` provides current registration identity, native signal
observation, latch waits, reload, name/OID connections and synchronous transaction
callbacks. Managed exceptions abort and retain their identity; native startup,
query and commit errors return owned diagnostics after cleanup. Later transactions
can succeed. Native signal handlers only record flags and wake PostgreSQL's latch.

| Requirement | Evidence |
|---|---|
| Exact options, durations, words, UTF-8 and defaults | `WorkerOptionsPreserveValues`, `WorkerOptionsRejectInvalidValues`, `WorkerRegistrationPreservesDefaultsAndRestartBoundaries`, `WorkerDynamicMetadataRejectsLossyConversion` |
| Registration, lifecycle, notification outcomes and malformed replies | `WorkerRegistrationValidatesReplies`, `WorkerHandlesPreserveLifecycle`, `WorkerHandlesRejectInvalidReplies`, `WorkerWaitsPreserveOutcomes` |
| Callback/thread/provider lifetime and disposal without termination | `WorkerHandlesValidateOwnership`, native detached-worker heartbeats across SQL callbacks |
| Owned identity, names/OIDs, signals and exact timeout bounds | `WorkerContextPreservesIdentityAndConnections`, `WorkerIdentityRejectsMalformedNativeText`, `WorkerSignalsAndTimeoutsPreserveBoundaries` |
| Transactions, exception identity, owned diagnostics, cleanup and recovery | `WorkerTransactionsPreserveBoundaries`, `WorkerTransactionsRejectInvalidCallbacks`, `WorkerOperationsPreserveOwnedErrors` |
| Valid declarations, composed phases, symbol boundaries and diagnostics | `WorkerEntriesCompile`, `WorkerEntriesPreserveSymbolBoundaries`, `WorkerEntriesComposeWithInitializationAndSql`, `WorkerEntriesRejectInvalidDeclarations` |
| Actual startup/shutdown, shared state, commit/rollback, restart, exhaustion and capacities | `BackgroundWorkersRegisterAndShareState` |
| Public sample observes a newly created database in another process | `BackgroundWorkerSamplePublishesSharedObservations` |
| Packaged worker scaffold runs all six generated managed/backend tests | `NewBackgroundWorkerSolutionRunsManagedAndBackendTests` |

The 57 direct cases pass; the complete Windows Runtime module passes
1,747/1,747 in 1.599s. All 25 worker compiler cases pass; the complete generator
module passes 2,100/2,100 in 22.838s. Three focused native worker cases pass on
Linux x64/PostgreSQL 18.6 in 5m42.664s. After the final preload guard and stronger
sample observation, plain full `dotnet test` passes **8,088 tests, zero failures
and six Windows-only skips, 8,094 total**, in 13m08.957s (integration 13m08.173s).
Windows x64/PostgreSQL 17.11 passes lifecycle and scaffold in the first native
run; the sample passes its corrected rerun in 6m34.755s. These are focused Windows
results, not a claim that the complete current Windows suite has run locally.

Initial native compilation exposed selected-header signal parameters and a
memory-only bridge's dependency on an SPI typedef; both were corrected. Runtime
execution exposed dynamic text sanitization and a sample observation race;
validation now rejects lossy input and waits for the exact new committed count.
The first full run was stopped after an existing spinlock source-slice assertion
included the new worker bridge; it now bounds the spinlock operation precisely
while retaining its no-interrupt assertion. A Windows publication assertion now
exempts only the manifest's native library from the unwanted-Ankus-DLL check.
The final passing runs include these repairs; no analyzer severity was reduced.

Release passes with zero warnings/errors on Linux (final 1m02.53s) and Windows
(3m21.18s and 3m18.79s before the last native guard; final test rebuild/publication
also pass). Generated API freshness verifies 200 pages/2,437 members; site build
produces 244 pages and checking reports zero errors/warnings/hints. README,
public worker/initialization/testing guides, generated API pages, the database
observer sample and `ankus new --background-worker` describe the implemented
consumer workflows. Static assertion and pseudo-mutation review found and filled
direct-test gaps in owned errors, malformed text, default transport, symbol
capacity and dynamic sanitization; no executed mutation or coverage percentage
is claimed.

Postmaster-death observation, SIGCHLD, additional connection failures/roles,
registration allocation faults and the complete PostgreSQL 13–19/platform matrix
remain required, alongside the full-port feature inventory above.

Immediately before committing, the 2026-09-27 19:25 UTC check confirms preceding
[CI 36338481778](https://github.com/willibrandon/ankus/actions/runs/36338481778)
and [Docs 36338481755](https://github.com/willibrandon/ankus/actions/runs/36338481755)
are terminal and successful. Quality, every runtime job and the complete Linux
(36m22s), macOS (47m17s) and Windows (56m50s) suites pass. Outcomes are checked
and recorded again immediately before push. All workflow jobs retain the
requested one-hour limit and full suites; work continues while new CI runs.

### Background workers — signals, connection failures and postmaster death

Worker-only and native-callback-only libraries enter an assembly-specific empty
Native AOT export before the generated native initializer enables fork support.
This starts the runtime without requiring a user `[PgInitialize]` method or
adding database/error-binding dependencies to a GUC-only consumer. Libraries
with `[PgModuleLoad]` already enter managed code through that callback. The
existing user initialization order and guarded diagnostics remain intact.
The runtime fork and package remain unchanged.

| Requirement | Evidence |
|---|---|
| Worker-only and referenced native-callback initialization | `WorkerEntriesCompile` and `NativeCallbacksInReferencedProvidersRetainConsumerDispatch` check semantic declarations and native invocation before fork enablement; the published worker-only death fixture loads and runs without a user initialization method |
| Actual child-signal delivery and selective consumption | `BackgroundWorkersRegisterAndShareState` sends SIGCHLD through the selected platform's native signal API twice, requires two acknowledged consumptions and an immediately cleared second read, while retaining reload/interrupt/termination and transaction checks |
| Eight name/OID database/role combinations | `BackgroundWorkerConnectionsPreserveRolesAndFailures` compares independent SQL OIDs with each worker's identity, bootstrap/explicit-role privileges, no-database behavior, denied table access and later successful transactions |
| Eleven process-ending connection failures and retry | The same test distinguishes null from empty names, missing database/role names and OIDs, NOLOGIN, denied CONNECT and missing database-access registration; each requires the worker's own PID, exact server SQLSTATE/message, stopped state, a live parent and a different healthy replacement worker |
| Postmaster death while waiting | `BackgroundWorkerPostmasterDeathStopsWaiters` observes actual native wait events, validates the test-owned postmaster identity before killing it, requires exact latch/handle death outcomes after child exit, then recovers and cleanly stops the owned cluster |
| Detached-worker slot reclamation | `BackgroundWorkersRegisterAndShareState` retains exactly three available slots before and after detached-worker checks; the final assertion permits up to 30 seconds for process exit and postmaster reclamation |

The complete generator suite passes 2,100/2,100 with zero skips in 26.068s.
Focused Linux x64/PostgreSQL 18.6 signal/lifecycle and death tests pass; the
corrected connection matrix passes in 4m14.417s. Windows x64/PostgreSQL 17.11
passes the connection matrix and death test across separate reruns; the final
death run passes 1/1 in 6m20.163s. The final Windows lifecycle/signal/reclamation
run passes 1/1 in 6m47.069s, including the retained exact three-slot assertion.
The final plain full Linux `dotnet test` passes **8,090 tests, zero failures and
six Windows-only skips, 8,096 total**, in 13m31.323s (integration 13m30.727s).
It includes the final bounded-capacity assertion and restart cleanup. An earlier
clean full run also passed 8,090 tests in 14m30.501s before the final capacity
assertion was restored.

Native execution exposed the missing first managed entry in worker-only preload.
Test repairs also include explicit Npgsql OID parameters, running catalog-backed
superuser checks inside a transaction, exact PostgreSQL diagnostic text and the
native wait-event spelling. Windows restart cleanup avoids inherited redirected
output handles and requires a new postmaster PID with a ready state before
opening a connection; pg_ctl can otherwise accept a recent stale PID file.
The initial complete Linux attempt finished every module with 8,090 successes
and six platform skips, but its overall Aborted status after cancellation is
retained as such, rather than counted as final verification.

Production Release builds pass on Linux (1m05.38s) and Windows (1m30.16s) with
zero warnings/errors. The final integration-test Release rebuilds also pass with
zero warnings/errors on Linux (5.07s) and Windows (5.91s).
API freshness verifies 200 pages/2,437 members; the site builds 244 pages and
checks with zero errors/warnings/hints. The public worker guide documents
explicit-role permissions, process-ending connection failures, signal coalescing
and worker-only declarations. Registration allocation faults and the full
PostgreSQL 13–19/platform matrix remain required. No warning or analyzer
severity is suppressed or reduced.

Immediately before committing, the 2026-09-27 21:00 UTC check confirms
[CI 36344340281](https://github.com/willibrandon/ankus/actions/runs/36344340281)
and [Docs 36344340280](https://github.com/willibrandon/ankus/actions/runs/36344340280)
are terminal. Docs, quality, every runtime-package job and macOS pass; Ubuntu
and Windows fail only the detached-worker capacity assertion repaired in this
milestone. Both job logs were inspected, and the exact final assertion now
passes on Linux and Windows. Outcomes are checked and recorded again immediately
before push. New hosted CI is required for this commit's full platform evidence.

### Background workers — registration allocation faults and recovery

The native allocator fixture now observes the selected worker owner's actual
allocation methods. It wraps that context alone, forwards the selected version's
allocator signature, and restores the original method table before deletion so
PostgreSQL's AllocSet cache cannot retain a fixture wrapper. It compiles the exact
emitted worker bridge and does not copy PostgreSQL's private handle layout.

`WorkerRegistrationAllocationFailuresRecover` has four cases, each repeated twice
on the same backend:

| Boundary | Required observations |
|---|---|
| Owner creation | Exact owned `53200` diagnostic, no created owner or allocation, unchanged handle identity and caller context |
| Registry-entry allocation | First allocation fails, one owner is created and deleted, no retained registry entry or consumed identity |
| PostgreSQL observation-handle allocation | Second allocation fails after publication, one consumed identity, deleted owner and empty registry; a separate worker's PID and argument witness prove it actually started, followed by termination and process exit |
| Handle identity exhaustion | Exact `54000` before owner creation or allocation; the controlled counter is restored for subsequent test operations |

Every case checks message/detail/hint ownership through diagnostic release,
empty native result fields, restored context and registry state, then starts a
real replacement worker. Startup, exact argument 42, distinct process identity,
shutdown, handle release and process exit must all succeed. Final SQL observes
the original backend PID, value 42 and zero worker-owner contexts.

The PostgreSQL-handle case verifies a registration side effect rather than an
atomic rollback: PostgreSQL publishes the worker before allocating its handle.
Ankus releases its local owner and registry when that allocation raises an error,
but the worker can still run. The public worker guide and generated API remarks
describe this behavior and the consequence for retries. Production worker code
and the Native AOT runtime fork/package are unchanged.

Focused execution passes 4/4 with zero skips on Linux x64/PostgreSQL 18.6 in
1m38.413s and Windows x64/PostgreSQL 17.11 in 3m38.883s. The first Linux attempt
failed native compilation because GCC could inline a context counter across
PostgreSQL's setjmp boundary; the counter is now volatile. The final Windows run
includes that correction. No warning is disabled. The fixture compiles with
warnings as errors on both targets.

Release passes with zero warnings/errors in 1m21.62s. API freshness verifies
200 pages/2,437 members; the site builds 244 pages and checks with zero
errors/warnings/hints. Static assertion and pseudo-mutation review checks each
case's sensitivity to incorrect registration outcomes, identity progression,
context or registry leaks, changed worker arguments and failed recovery. This
is not an executed mutation score or coverage percentage. The final plain full
Linux `dotnet test` passes **8,094 tests, zero failures and six Windows-only
skips, 8,100 total**, in 14m03.413s (integration 14m02.799s). The complete
PostgreSQL 13–19/platform matrix and remaining full-port inventory remain required.

Immediately before committing, the 2026-09-27 21:38 UTC check confirms that
[CI 36350125488](https://github.com/willibrandon/ankus/actions/runs/36350125488)
has passed quality and every runtime-package job; the full Linux, macOS and
Windows suites remain in progress without a reported failed job.
[Docs 36350125478](https://github.com/willibrandon/ankus/actions/runs/36350125478)
passes. Pending suites are not counted as platform proof. Outcomes are checked
and recorded again immediately before push. All jobs retain the requested
one-hour timeout and full unsharded suites.

### Transaction callbacks — prepared transactions and actual parallel workers

The existing callback implementation now has direct and native execution
evidence for preparation and parallel completion. Five direct terminal-event
rows require selected callbacks in registration order, every unused outer and
subtransaction receipt inactive, safe disposal and no repeated invocation.
The complete direct callback class passes 17/17 on Linux (982ms) and Windows
(161ms).

The published test extension supplies ten cases against real PostgreSQL:

| Requirement | Execution evidence |
|---|---|
| Preparation ends callback ownership | `PreparedTransactionCallbacksEndWithPreparation` commits and rolls back from a distinct backend; exact pre/prepare order, cancelled and later registrations, collected captures, prepared database/role/identity, initially invisible rows and exact resolved writes are required |
| Pre-prepare rejection recovers | `PrePrepareFailureAbortsAndRecovers` preserves managed and native diagnostics, including a caught native failure; finally execution, no prepared identity or writes, released captures and healthy callbacks on the original backend are required |
| Post-prepare failure retains durability | `PrepareFailureRetainsDurablePreparedTransaction` observes managed finally before PANIC, disconnected sessions, actual crash recovery, the surviving prepared transaction, exact commit/rollback outcomes and healthy later callbacks |
| Parallel success and failure preserve worker semantics | `ParallelTransactionCallbacksPreserveWorkerOutcomes` reads all 30,000 distinct values or the exact row/pre-commit error; actual foreign worker PIDs, exact per-worker terminal files, no pending receipts, denied SQL capability and healthy replacement workers are required |

Parallel capture checks force collection both while callbacks are registered and
after terminal cleanup. Atomic test-owned witness files survive worker exit and
are removed with each owned cluster. Replacement queries must satisfy the same
independent terminal observations as the initial successful query. The complete
native class passes 10/10 on Linux x64/PostgreSQL 18.6 (2m11.216s) and Windows
x64/PostgreSQL 17.11 (3m50.737s). The final forced-collection refinement passes
the three affected cases on Linux in 2m17.312s and Windows in 3m43.067s.
The public transaction guide documents preparation lifetime, durability after
cleanup failure and parallel callback phases.

The user-reported macOS [job 108713921584](https://github.com/willibrandon/ankus/actions/runs/36352480777/job/108713921584)
fails after 16m55s when strict Clang compiles the allocation fixture. Its
`SIGNAL_ARGS` handler ignored `postgres_signal_arg`, which produced
`-Werror,-Wunused-parameter`. Assembly initialization therefore fails 3,332
integration cases before they execute; two Linux-only cases skip. The repaired
handler checks `SIGTERM`, preserves errno and wakes the latch, with explicit
handling of PostgreSQL 19's additional metadata parameter. The exact strict
Clang command rejects the original complete fixture and accepts the regenerated
repair. Actual allocation cases pass 4/4 on Linux x64/PostgreSQL 18.6
(1m34.949s) and Windows x64/PostgreSQL 17.11 (3m11.515s). No warning is disabled,
and the runtime fork/package are unchanged. Fresh macOS backend execution is
still required.

The preceding [CI 36350125488](https://github.com/willibrandon/ankus/actions/runs/36350125488)
was superseded by the allocation milestone push: Linux, macOS and Windows were
cancelled after 36m37s, 36m39s and 36m29s respectively. Quality, every runtime
job and [Docs 36350125478](https://github.com/willibrandon/ankus/actions/runs/36350125478)
passed. Cancelled platform suites are not counted as platform proof.

Final Release passes with zero warnings/errors in 55.95s. API freshness verifies
200 pages/2,437 members; the site builds 244 pages and checks with zero
errors/warnings/hints. Assertion and pseudo-mutation review led to the forced-GC,
exact SQL-capability diagnostic and replacement-worker witness refinements;
it does not establish an executed mutation score or coverage percentage.
The final plain full Linux `dotnet test` passes **8,108 tests, zero failures
and six Windows-only skips, 8,114 total**, in 14m00.034s (integration
13m59.424s), against PostgreSQL 18.6.
The complete PostgreSQL 13–19/platform matrix and remaining full-port inventory
remain required.

Immediately before committing, the 2026-09-27 22:28 UTC check of
[CI 36352480777](https://github.com/willibrandon/ankus/actions/runs/36352480777)
confirms quality, every runtime-package job and the complete Ubuntu suite
(41m27s) pass. macOS fails only during the fixture compilation repaired here;
Windows is still running. [Docs 36352480612](https://github.com/willibrandon/ankus/actions/runs/36352480612)
passes. The failed macOS job log was inspected and its exact diagnostic was
reproduced and repaired without suppression. Pending Windows and new macOS
execution are not counted as platform proof. Outcomes are checked and recorded
again immediately before push. One-hour timeouts and full unsharded suites remain.

### Native method tables — field-named callback values

An unnamed function-pointer signature previously exposed only its collected
graph-index type name. Adding an unrelated native root can change that index,
so a managed method-table handler could not use a declaration-derived callback
name. The binding writer now additionally emits `<Record>_<Field>Callback`
values. Each retains the existing canonical pointer representation, selected
header signature and native type identity. Implicit conversions preserve the
exact address, and supported `Invoke` methods delegate to the existing guarded
canonical call. Native field storage and canonical signature selection stay
shared. No additional native entry path or runtime patch is introduced.

The same naming applies to callback array elements, unions and typedef-backed
anonymous records. Existing naming rules resolve collisions. Variadic,
unprototyped, incomplete-result and layout-only bindings retain addresses without
inventing an unsupported invocation signature. The public README and raw-value
guide describe the types, their conversions and their borrowed lifetime.

| Requirement | Execution evidence |
|---|---|
| Declaration-derived names survive graph changes | `ManagedFieldCallbacksKeepNamesAcrossGraphs` compiles one unchanged consumer against two graphs, requires different canonical signature indices, and executes both native targets with exact results and addresses |
| Storage, conversions and signature shapes | `ManagedFieldCallbacksPreserveStorageAndInvocation` executes typedef, union, array and anonymous-record fields, including reassignment and high-bit addresses; `ManagedFieldCallbacksPreserveEmptyAndAggregateSignatures` verifies native side effects, void/empty signatures, aggregate endpoints and unchanged by-value input |
| Unsupported calls and collisions | `ManagedFieldCallbacksKeepUnsupportedSignaturesAddressOnly`, `ManagedFieldCallbacksWithoutHeadersRetainOnlyAddresses` and `ManagedFieldCallbacksAvoidNameCollisions` compile actual consumers, retain exact values, reject unsupported invocation surfaces and preserve colliding native declarations |
| Admission, owned errors and retry | `ManagedFieldCallbacksValidateInvocationAndRecover` rejects absent backend scope and null targets before invocation, retains every owned diagnostic field and verifies no rejected-call native side effect followed by an exact healthy result |
| Real native method-table storage | `NativeFieldCallbackRoundtripPreservesStorage` assigns a generated managed callback to `CustomExecMethods.ExecCustomScan`, reads it back, invokes both field-specific and canonical values, and requires shared native state mutation and exact slot addresses |
| Native boundary and same-session recovery | `NativeFieldCallbackErrorsUnwindAndRecover` verifies managed/native diagnostic ownership, callback and outer finally execution, a healthy retry and the original SQL backend PID; the caught-SPI partition requires exact diagnostics and successful SQL within the same callback |

All seven binding cases pass on Linux (3.255s) and Windows (3.004s after final
fixture formatting). All four actual PostgreSQL cases pass on Linux
x64/PostgreSQL 18.6 (1m57.756s) and Windows x64/PostgreSQL 17.11 (4m19.817s).
The first native attempt had an incorrect caught-SPI expectation: ordinary SPI
rolls back its internal subtransaction and permits recovery inside the callback.
The corrected test requires that documented behavior and exact diagnostic
content; no production behavior was changed to accommodate it. Native C test
helpers use distinct external names to avoid a libc symbol collision and the
body-skipping collector's legitimate unused-static-function diagnostic.

Final Release builds pass with zero warnings/errors on Linux (1m18.60s) and
Windows (4m25.71s). API freshness verifies 200 pages/2,437 members; the site builds
244 pages and checks with zero errors/warnings/hints. Static source pairing,
assertion review and pseudo-mutation review informed the cases; they do not
establish a coverage percentage or executed mutation score. Final plain full
Linux `dotnet test`, against PostgreSQL 18.6, passes **8,119 tests, zero failures
and six Windows-only skips, 8,125 total**, in 14m48.009s (integration
14m47.274s). This includes the final native fixture formatting.

This milestone exercises callback transport through real native method-table
storage. Actual custom-scan planner/executor registration, plans, lifecycle,
rescan, explanation and parallel protocols remain required, along with the
remaining raw API inventory and complete PostgreSQL 13–19/platform matrix.
All analyzer requirements and one-hour full-suite platform jobs remain intact.

Immediately before committing, the 2026-09-27 23:13 UTC check of
[CI 36355439020](https://github.com/willibrandon/ankus/actions/runs/36355439020),
for the macOS fixture repair at `2dbdb3e`, confirms quality and every runtime
package job pass; all three platform suites are still running without a reported
failure. [Docs 36355439000](https://github.com/willibrandon/ankus/actions/runs/36355439000)
passes. No pending platform suite is counted as completed evidence. The prior
run's Windows cancellation and repaired macOS failure are recorded above.
Outcomes are checked and recorded again immediately before push.

### Custom scans — real trace provider and executor boundaries

`Ankus.Examples.CustomScans` uses the selected-header native API and field-named
callbacks to register an `Ankus Trace` provider. Its relation hook preserves the
previous hook, retains each ordinary or partial sequential path as a child and
keeps the original costs and parameters. Real CustomScan plans preserve child
qualification and projection shape; native states embed the exact selected-header
CustomScanState prefix. Begin/end and rescan delegate to the child executor.
EXPLAIN reports per-node rows, calls and rescans. Native method tables and names
belong to a child of TopMemoryContext, with cleanup before failed publication
and backend lifetime after successful registry publication.

The fixture publishes and installs the standalone Native AOT sample, keeping its
module registration and planner hook separate from the general test extension's
deliberately failing initializers. The project is included in the solution and
builds in Release. Its public guide and README describe session loading,
the default-off GUC, method-table lifetime, native slot contracts, error cleanup
and optional capabilities.

| Requirement | Actual backend evidence |
|---|---|
| Paths, plans, child rows and projection | `TraceScanReturnsExactRows` requires a real Custom Scan/Seq Scan plan, exact projected integer extremes, text, SQL NULL, filtering, EOF and lifecycle counts |
| Empty, singleton and early stop | Three `TraceScanHandlesResultBoundaries` cases check complete exact result sequences and execution/cleanup counts |
| EXPLAIN versus execution | `TraceScanExplainsExecution` checks zero observations before execution and independent exact child/custom rows and counters after ANALYZE |
| Parameters and rescans | `TraceScanRescansParameterizedChildren` requires six exact ordered threshold/value pairs, actual loops, rescans and reclaimed state |
| Cached method lifetime | `TraceScanPreparedPlansRetainMethods` retains one plan across transactions and disabling future tracing, with exact repeated results; its additional collection probe runs in the separate test extension |
| Backward reads | `TraceScanReadsBackward` checks exact forward/backward/forward row identities through a scroll cursor |
| Errors and recovery | Two `TraceScanErrorsUnwindAndRecover` cases require exact managed/native diagnostics, managed unwind, query-context cleanup and a healthy exact result in the original backend |
| Real parallel execution | `TraceScanRunsInParallelWorkers` requires actual launched workers, a parallel-aware child, non-leader process IDs, all 30,000 values exactly once and a healthy subsequent query |
| Concurrent updates | Two `TraceScanRechecksConcurrentUpdates` cases observe an actual row-lock wait, then verify replacement tuples that still qualify or no longer qualify, exact stored/returned values and context cleanup |

The first eight-case run exposed a real PostgreSQL assertion during outer
projection: returning the child's heap slot violated the custom node's virtual
slot expectation. The access callback now uses ExecCopySlot into its own scan
slot and clears that slot at EOF. EvalPlanQual recheck fetches the replacement
through the child into the same slot. The final thirteen-case Linux
x64/PostgreSQL 18.6 run passes with zero failures/skips in 1m51.629s. All thirteen
cases also pass on Windows x64/PostgreSQL 17.11 in 3m51.105s. Those focused runs
used linked sample source. The first full Linux attempt then exposed fixture
coupling: its early planner-hook registration retried a deliberately failed
general-extension initializer during the rollback test's ordinary table query.
The run was cancelled after recording the failure. The fixture now loads the
standalone sample as a separate extension; the existing initialization test and
callback initialization rules remain unchanged. All 26 combined custom-scan and
initialization cases pass on Linux x64/PostgreSQL 18.6 in 2m30.708s and Windows
x64/PostgreSQL 17.11 in 3m39.787s after the separation.
Two test expectations were also corrected: PostgreSQL sum(bigint) returns
numeric, and EXPLAIN's Actual Rows can use decimal JSON notation. Namespace DDL
in other tests correctly invalidates prepared plans in every backend, so only
the exact single-plan lifetime witness runs serially; its assertion is retained.

Final Release builds after fixture separation pass with zero warnings/errors on
Linux (1m02.25s) and Windows (2m49.39s). API freshness verifies 200 pages/2,437
members. The site builds 245 pages and checks with zero errors/warnings/hints.
Final plain full Linux `dotnet test` passes **8,132 tests, zero failures and six
Windows-only skips, 8,138 total**, in 13m58.212s (integration 13m57.534s).
Assertion and pseudo-mutation review covers exact values, result boundaries,
diagnostics, lifecycle and plan shape; no executed mutation score or coverage
percentage is claimed.

The trace provider does not advertise mark/restore or its own shared-memory
protocol. Provider-owned DSM, mark/restore, reparameterization, independent
predecessor-hook/registry-error witnesses, the remaining raw API inventory and
the complete PostgreSQL 13–19/platform matrix remain required full-port work.
No analyzer standard, warning severity, runtime patch or CI timeout changes.

The preceding [CI 36355439020](https://github.com/willibrandon/ankus/actions/runs/36355439020)
was superseded by the field-callback milestone push. Linux, macOS and Windows
jobs conclude cancelled after 42m25s, 42m28s and 42m24s respectively. Ubuntu's
retained log prints all six passing module summaries (8,108 passed, six skips),
including 3,344 integration cases in 36m03.476s, before cancellation; its job
conclusion remains cancelled. The other two platform logs have no final
integration summary, so no complete platform pass is claimed. Quality, all
runtime jobs and [Docs 36355439000](https://github.com/willibrandon/ankus/actions/runs/36355439000)
pass.

For `c01ed61`, [CI 36357983412](https://github.com/willibrandon/ankus/actions/runs/36357983412)
now has complete Ubuntu and macOS passes. Ubuntu reports 8,119 passed and six
platform-specific skips in a 42m37s job (integration 35m53.990s). macOS reports
8,116 passed and nine platform-specific skips in a 52m55s job (integration
45m16.124s). Both have 8,125 total tests and zero failures. The macOS result
confirms the signal-handler compilation repair against a real PostgreSQL server.
Windows reaches the 60-minute limit and concludes cancelled after 60m34s,
including runner cleanup. Its annotation explicitly reports the one-hour
maximum. The build passes with zero warnings/errors in 15m28.99s; every unit
module passes, but the integration suite has no final result. Its two Linux-only
skips are not evidence that the remaining integration cases completed.
Quality, all runtime-package jobs and
[Docs 36357983399](https://github.com/willibrandon/ankus/actions/runs/36357983399)
pass.

The Windows build log shows roughly five minutes of cold native-header
collection before generating the shared companion. CI previously retained
NuGet and runtime caches but discarded the existing content-verified binding
cache. Platform jobs now restore that cache by platform/PostgreSQL version,
build, save it, then execute every test module. Saving before execution keeps
the cache even when the later suite times out. The combined `runtime-test`
command remains available; two explicit CI phases reuse the same build and
test implementations, selected PostgreSQL environment and compiler path.
Native preprocessing, ABI checks, dependency/artifact hashes and all analyzer
standards remain enabled. The CI app compiles, the workflow parses with all
three platform entries and the correct restore/build/save/test order, and
MSBuild resolves the configured cache directory from the environment. No
hosted speedup or successful Windows full-suite result is claimed yet; the
first cold run will populate the new cache. The one-hour timeout is unchanged.
Immediately before committing, the 2026-09-28 00:21 UTC check confirms
CI 36357983412 is terminal: Ubuntu and macOS pass, Windows is cancelled by its
explicit one-hour timeout, and quality/all runtime jobs pass. Docs 36357983399
passes. The Windows log and timeout annotation were inspected; the cache change
addresses observed cold binding work without claiming a completed Windows run.
Outcomes are checked and recorded again immediately before push.

### Custom scans — provider-owned parallel shared memory

The trace provider now preserves parallel awareness on partial paths and
implements `EstimateDSMCustomScan`, `InitializeDSMCustomScan`,
`ReInitializeDSMCustomScan`, `InitializeWorkerCustomScan` and
`ShutdownCustomScan`. Its coordinate block contains only selected-header
`pg_atomic_uint64` values. PostgreSQL owns the block and the real child scan's
work distribution; no managed reference or process-private pointer is shared.
Each worker borrows its local mapping. Reinitialization resets shared counters
and increments the execution generation independently of local rescan order.

Shutdown copies observations into private state and clears the borrowed address.
End repeats that operation safely, while query-context error cleanup never
accesses DSM. Full scans report exact combined rows/calls and actual attachment/
shutdown counts. Early LIMIT snapshots can precede worker completion; each
counter is a separate atomic observation. Waiting for worker completion inside
child shutdown would risk deadlock before the parent detaches full tuple queues.
The public guide and sample README explain these ownership and snapshot limits.
Existing per-backend counters retain their original eight-field contract.

| Requirement | Real PostgreSQL evidence |
|---|---|
| Complete worker execution, with and without leader participation | `TraceScanRunsInParallelWorkers` requires actual launched workers, parallel-aware custom and child plans, exact combined rows/calls, attachments/shutdowns, generation one, process witnesses and every input value exactly once |
| Reinitialization after complete and early-stopped execution | `TraceScanReinitializesParallelState` requires three actual Gather loops and generation three; complete executions return the exact 30,000-value set on every loop and reset shared totals, while early loops return 97 unique in-range values each with bounded snapshots |
| Never-started, singleton and partial results | `TraceScanStopsParallelExecutionEarly` covers LIMIT 0, 1 and 97; the zero case has no initialized DSM or workers, nonzero cases use real workers, and every case subsequently runs a complete healthy parallel scan |
| Worker error transport and leader recovery | `TraceScanRecoversFromParallelErrors` preserves exact managed/native diagnostics and the parallel-worker context, observes leader query-owner cleanup after rollback, and requires healthy replacement workers and the exact complete row set on the same backend |
| No available workers | `TraceScanRunsWithoutAvailableWorkers` retains a Gather with planned workers but zero launches, exact leader process identity and rows, initialized shared counters, and zero attachment/shutdown counts even with ordinary leader participation disabled |

The 22 custom-scan cases pass on Linux x64/PostgreSQL 18.6 in 2m03.685s
(module 2m02.758s). All 35 custom-scan and initialization cases pass on Windows
x64/PostgreSQL 17.11 in 4m17.626s. The initial lifecycle attempt exposed a JSON
number parsing expectation and worker-pool interference: EXPLAIN's actual-row
numbers are read as doubles, and this suite runs independently of other tests
which compete for the same bounded worker pool or invalidate cached plans.
Exact worker, result and error assertions remain. No diagnostic is suppressed;
the redundant conversion reported by IDE0004 was removed.

Final Release builds pass with zero warnings/errors on Linux (1m10.00s) and
Windows (2m12.42s). API freshness verifies 200 pages/2,437 members; the site builds
245 pages and checks with zero errors/warnings/hints. Final plain full Linux
`dotnet test` passes **8,141 tests, zero failures and six Windows-only skips,
8,147 total**, in 15m07.371s (integration 15m06.346s), against PostgreSQL 18.6.
Assertion and pseudo-mutation review checks independent
values, exact lifecycle transitions, worker identity and error outcomes; no
coverage percentage or executed mutation score is claimed.

Mark/restore, reparameterization, independent predecessor-hook/registry-error
witnesses, remaining raw APIs and the complete PostgreSQL 13–19/platform matrix
remain required full-port work. The runtime fork/package, analyzer modes and
one-hour full-suite CI limits are unchanged. The current hosted run has saved
binding caches on all three platforms before testing; a later restored-cache
run is still needed to measure the timing benefit.

Immediately before committing, the 2026-09-28 01:03 UTC check of
[CI 36361967411](https://github.com/willibrandon/ankus/actions/runs/36361967411)
at `d42ddcc` confirms quality, every runtime-package job and the complete macOS
ARM64/PostgreSQL 18 suite pass. macOS reports 8,129 passed, zero failures and
nine platform-specific skips, 8,138 total, in a 37m19s job (integration
30m23.987s). Its Release build passes in 5m24.82s. This validates the initial
standalone trace provider on macOS; the new DSM change still requires its own
hosted macOS result. Ubuntu and Windows remain in progress without a reported
failure. [Docs 36361967454](https://github.com/willibrandon/ankus/actions/runs/36361967454)
passes. All three newly saved binding-cache entries are present in GitHub;
this first cold run does not prove a restored-cache speedup. Outcomes are
checked and recorded again immediately before push.

### Index-backed custom scan positions

The trace provider wraps native index and index-only paths as well as sequential
paths. It preserves each child's costs, pathkeys, qualifications and parameters.
Mark/restore capability comes from `ExecSupportsMarkRestore` on the real child
path and is not advertised for parallel partial paths. Backward capability comes
from `ExecSupportsBackwardScan` on the finished child plan. Native guarded
`ExecMarkPos` and `ExecRestrPos` calls retain the access method's own position;
the provider keeps no saved tuple or borrowed slot pointer. EXPLAIN now includes
per-node `Trace Marks` and `Trace Restores` counts without changing the existing
eight backend lifecycle counters.

Twelve new backend cases exercise both index families. Duplicate-key Merge Joins
require real custom mark/restore callbacks on the inner child, with no intervening
Sort or Materialize masking support, and exact six nullable/UTF8 result pairs.
Index-only scans use covering indexes and VACUUM and require zero heap fetches.
Backward cursors and parameterized rescans verify exact ordered values and
native owner cleanup. Managed/native errors preserve exact diagnostics and
recover on the original backend. Parallel index cases require real workers,
all 30,000 unique values, exact shared lifecycle counts and no mark/restore use.

All 34 custom-scan cases pass on Linux x64/PostgreSQL 18.6 in 1m54.751s
(module 1m53.827s); all 47 combined custom-scan and initialization cases pass
on Windows x64/PostgreSQL 17.11 in 3m47.398s. Release builds pass with zero
warnings/errors on Linux (1m11.53s) and Windows (2m03.81s). API freshness
verifies 200 pages/2,437 members; the site builds 245 pages and checks with zero
errors/warnings/hints. The README, sample guide and public custom-scan guide
describe the actual supported native capabilities. Final plain full Linux
`dotnet test`, with the package-fixture reuse change also present in the verified
working tree, passes **8,153 tests, zero failures and six Windows-only skips,
8,159 total**, in 11m18.383s (integration 11m17.598s). No full-port completion
is claimed.

After the prior milestone's push, the complete Ubuntu x64/PostgreSQL 18 suite
at `d42ddcc` also passed in [CI 36361967411](https://github.com/willibrandon/ankus/actions/runs/36361967411):
8,132 passed, zero failures, six platform-specific skips, 8,138 total, with
a 40m54s job and 34m45.965s integration module. Its macOS result above remains
successful. Windows was superseded after 41m25s, without a completed integration
result; this was cancellation, not a timeout or platform pass. The newer
`35c7970` platform suites are still running at the 2026-09-28 01:26 UTC check;
quality, runtime jobs and [Docs 36364593994](https://github.com/willibrandon/ankus/actions/runs/36364593994)
pass. This run's build steps took 5m27s on Ubuntu, 7m41s on macOS and
13m51s on Windows; these observations do not establish a cache speedup.

Reparameterization of meaningful private state, independent predecessor-hook
and registry-error witnesses, remaining raw APIs and the complete PostgreSQL
13–19/platform matrix remain required. Runtime identity and all enforced
analyzers are unchanged.

The 2026-09-28 01:48 UTC pre-commit check of
[CI 36364594021](https://github.com/willibrandon/ankus/actions/runs/36364594021)
at `35c7970` confirms quality, every runtime job and the complete Ubuntu
x64/PostgreSQL 18 suite pass: 8,141 passed, zero failures, six platform skips,
8,147 total, in a 41m49s job (integration 35m42.554s). macOS and Windows
remain in progress without a reported failure. Docs 36364593994 passes.
These hosted results cover the preceding DSM milestone, not the new index or
package-fixture changes. Outcomes are checked again before the next commit
and before pushing both locally verified changes.

### Reuse package fixtures without repeating cold binding builds

The CI timing investigation identifies unnecessary work in packaged backend
tests: eleven consumers each created a fresh NuGet directory. The installed
build tool's path participates in native binding source identity, and package
paths also participate in managed compilation identity. Reinstalling identical
packages in a new location therefore repeated binding collection and compilation.
Integration already shares its assembly fixture and runs alongside unit modules;
adding more parallel jobs is not needed for this fix.

Nine backend consumers now reuse their existing class-owned package directory.
The two explicit GUC cold-restore contracts still start with separate empty
package directories. Every consumer remains outside the repository, publishes
its own Native AOT extension, verifies package-only references and no repository
style imports, and runs its real PostgreSQL assertions. Concurrency remains two
consumer slots, pre-18 installations stay isolated, and class cleanup removes
the owned package directories. Production cache identity and native validation
are unchanged; no test, analyzer or platform job is removed or suppressed.

The same three representative Linux x64/PostgreSQL 18.6 cases pass before and
after, with zero failures/skips: 5m03.583s becomes 3m53.082s, a 70.501s (23.2%)
reduction including fixture setup. Source-cache entries for setup and those
consumers fall from four to one. This is a local comparison on SDK 10.0.400/
.NET 10.0.11 with existing machine caches, not a hosted CI speedup claim.
Individual test durations include semaphore waits and should not be summed.
All eleven affected consumers pass on Windows x64/PostgreSQL 17.11 in
8m39.187s, including both retained cold-cache cases. The Windows test project
build has zero warnings/errors; Linux solution Release also passes with zero
warnings/errors in 1m09.92s.

CI now writes one TRX report per test module and uploads available reports on
every outcome, providing individual durations for subsequent investigations.
The automation app compiles and validates its pinned runtime identity; the
exact report flags pass all 22 PgConfig cases and produce the expected TRX.
Contributor and engineering guides describe fixture reuse and report locations.
Final plain full Linux `dotnet test` passes **8,153 tests, zero failures and six
Windows-only skips, 8,159 total**, in 11m18.383s (integration 11m17.598s),
including the twelve new index-scan cases recorded above. API freshness verifies
200 pages/2,437 members; the site builds 245 pages and checks with zero
errors/warnings/hints. Full unsharded suites, the one-hour job limit and all
analyzer standards remain unchanged.

The previous Ubuntu log confirms that the GitHub binding-cache archive restored
from `d42ddcc`, but its first source contract was collected again and its
integration module still took 35m42.554s. Archive restoration alone is not proof
of reused compiler outputs or a speedup. The bounded fixture change addresses
repeated work within a test run without weakening production cache validation;
its hosted timing effect remains to be measured.

Immediately before this commit, the 2026-09-28 01:49 UTC recheck of
[CI 36364594021](https://github.com/willibrandon/ankus/actions/runs/36364594021)
still has successful quality, runtime and full Ubuntu jobs, with macOS and
Windows in progress and no reported failure. Docs 36364593994 passes. The
preceding index milestone is committed separately as `4b32cc3`; this fixture
milestone records the combined working tree's complete verification. CI is
checked and recorded again immediately before push without waiting for the
remaining hosted jobs.

### Custom-scan parameter ownership and partition-child remapping

The trace provider now retains copied direct outer variables from parameterized
path clauses in native private path state. Its
`ReparameterizeCustomPathByChild` callback uses the selected-header
`adjust_appendrel_attrs_multilevel` contract and the child's top parent to map
those variables across the complete partition ancestry. It returns new native
nodes rather than changing another path's expressions. Plan creation moves
expressions into `custom_exprs`, where PostgreSQL performs its ordinary
outer-variable and plan-reference adjustments; only the native remap count
remains in opaque private plan data.

EXPLAIN reports `Trace Parameters` through PostgreSQL's deparser and ancestor
plan context, plus `Trace Parameter Remaps`. These observations never evaluate
the original clauses. Placeholder evaluation barriers remain the real child's
responsibility; the diagnostics retain only direct outer variables. Native
child execution, filtering, values and existing lifecycle counters are preserved.

| Contract | Native evidence |
|---|---|
| Actual provider transformation | Four partitionwise Nested Loop cases require one remap and exact per-child key/cutoff names, with reordered columns, a dropped-column hole and optional intermediate partitioned parents |
| Native index boundaries | The same cases execute real index/index-only children, repeated parameter rescans and zero heap fetches for covering index-only paths |
| Values and absence | Seven exact typed rows retain duplicate matches, NULL cutoff/text, unmatched outer rows and UTF8 labels; unparameterized outer paths omit parameter diagnostics |
| Copyable plan ownership | Two forced-generic prepared-plan cases preserve names and exact full/subset/empty/repeated results after collection, without another planning callback |
| Volatile semantics | Native untraced/traced execution produces identical expected rows and sequence-call counts; EXPLAIN without execution cannot advance the sequence |
| Error ownership and recovery | Managed and native errors in parameterized partition children preserve SQLSTATE/message/detail/hint, unwind every managed call, reclaim aborted states and allow exact healthy rows in the original backend |

All 43 custom-scan cases pass on Linux x64/PostgreSQL 18.6 in 1m52.567s
(module 1m51.635s). All 56 combined custom-scan and initialization cases pass
on Windows x64/PostgreSQL 17.11 in 4m09.809s. Release builds have zero
warnings/errors: Linux 1m09.92s, Windows 3m12.61s; the final Windows test-project
rebuild also passes cleanly. API freshness verifies 200 pages/2,437 members,
and the documentation site builds 245 pages with zero check diagnostics.
The README, sample guide and public custom-scan guide describe ownership and
parameter transformations without adding repository maintenance commands.
Final plain full Linux `dotnet test` passes **8,162 tests, zero failures and six
Windows-only skips, 8,168 total**, in 11m08.131s (integration 11m07.029s).

The selected PG17.11/18.6 headers agree on the implemented helper contract.
Read-only reference inspection identifies a different PG13–15 helper ABI using
relation-id sets rather than relation pointers; adapting and executing that
contract remains required work. Independent hook/registry boundaries, remaining
raw APIs and the complete PostgreSQL 13–19/platform matrix remain required.
Runtime identity, analyzer standards, full unsharded CI suites and the one-hour
job limit are unchanged.

Immediately before committing, the 2026-09-28 02:19 UTC check of
[CI 36367477559](https://github.com/willibrandon/ankus/actions/runs/36367477559)
at `8d23bc0` confirms successful quality and all three runtime jobs. Its full
Linux, macOS and Windows suites are still running without a reported failure;
[Docs 36367477538](https://github.com/willibrandon/ankus/actions/runs/36367477538)
passes. Hosted savings from package-directory reuse are not yet measured.
The previous `35c7970` run finished as cancelled overall: its full Ubuntu result
above passed, while macOS and Windows were superseded, not timed out or proven
successful. CI is checked and recorded again immediately before push; ongoing
hosted jobs do not block this independently verified milestone.

### Independent custom-scan hooks and registry boundaries

Explicit test-extension probes now install a predecessor relation hook before
the standalone trace provider loads and exercise PostgreSQL's real method
registry. The production provider's existing behavior satisfies these native
contracts. The probes use the generated typed callbacks, guarded methods and
backend-owned method tables; they require no additional extension project,
production friend assembly or automatic hook installation in the general test
extension. Public and sample guides now explain name limits, backend lifetime,
rollback semantics, predecessor ordering and error propagation.

| Contract | Native evidence |
|---|---|
| Predecessor ordering and exact arguments | `TraceScanChainsPredecessorBeforeWrapping` checks disabled/enabled/disabled stages, original paths and pointer identity against actual planner arrays at range-table index two, real EXPLAIN nodes and exact nullable/UTF8 rows |
| Nested managed/native failures | Four `TraceScanPredecessorErrorsUnwindAndRecover` cases check exact diagnostics, managed finally execution, no executor state after rejected planning, original backend PID and healthy retry with either enabled setting |
| Native name and transaction lifetime | Four `TraceScanRegistryOwnsNamesBeyondTransactions` cases retain empty, singleton and 63-byte ASCII/UTF8 names, exact native addresses and live owner contexts after SQL rollback and collection |
| Case sensitivity and backend isolation | `TraceScanRegistryUsesCaseSensitiveBackendNames` retains two distinct cased names and owners; neither entry is present in another backend |
| Missing names | `TraceScanRegistryDistinguishesMissingMethods` checks optional null, required 42704 diagnostics and same-backend recovery |
| Native length rejection and cleanup | Four `TraceScanRegistryRejectsLongNativeNames` cases check 64/65-byte ASCII/UTF8 names, exact XX000 diagnostics, no leaked attempted owner, absent rejected entries and healthy results in the original backend |
| Duplicate registration and cached methods | `TraceScanRegistryDuplicatesPreserveCachedPlans` checks 42710, unchanged original table identity, reclaimed attempted storage and exact cached-plan execution after error, collection and disabling future tracing |
| Actual module-load collision | `TraceScanRegistryCollisionsRejectModuleLoad` reserves the native name before loading the sample, then checks repeated 42710 failures, unchanged registered table, reclaimed sample method owners, preserved predecessor and exact healthy rows in the original backend |

The length-rejection cases catch the actual ERROR inside a temporary PL/pgSQL
function and return stacked diagnostics unchanged, then verify backend identity
and cleanup. This permits native same-session recovery despite the pinned
Npgsql driver's policy of closing connections for class XX errors. Native
context assertions use `pg_backend_memory_contexts.ident`; successful retained
owners provide a positive witness for the failed-owner zero-count checks.

All 73 combined custom-scan and initialization cases pass on Linux
x64/PostgreSQL 18.6 in 2m13.335s (module 2m12.626s), and on Windows
x64/PostgreSQL 17.11 in 3m40.975s. Linux Release takes 1m10.21s and Windows
Release takes 2m10.72s, both with zero warnings/errors. API freshness verifies 200 pages/
2,437 members; the site builds 245 pages and checks with zero diagnostics.
Final plain full Linux `dotnet test` passes **8,179 tests, zero failures and six
Windows-only skips, 8,185 total**, in 10m46.793s (integration 10m46.216s).

The documentation hosting checklist was stale. The current
[Docs 36369419508](https://github.com/willibrandon/ankus/actions/runs/36369419508)
has successful build and deployment jobs. Direct HTTP checks on 2026-09-28
confirm the [public site](https://willibrandon.github.io/ankus/) returns 200 with
the matching canonical URL; its [sitemap index](https://willibrandon.github.io/ankus/sitemap-index.xml)
references a live sitemap containing 244 URLs under that canonical base.
The tracker now marks this existing hosting requirement verified.

Older parameter-helper ABI adaptation, other raw hook protocols, remaining
raw APIs and the complete PostgreSQL 13–19/platform matrix remain required.
Runtime identity, analyzer standards, complete unsharded CI suites and their
one-hour job limit are unchanged.

Immediately before committing, the 2026-09-28 03:16 UTC check of
[CI 36369419494](https://github.com/willibrandon/ankus/actions/runs/36369419494)
at `2ea0de1` confirms quality, all runtime jobs and both full Ubuntu and macOS
suites pass. Ubuntu x64/PostgreSQL 18 reports 8,162 passed, zero failures and
six platform skips in a 34m57s job (integration 28m02.838s). macOS
ARM64/PostgreSQL 18 reports 8,159 passed, zero failures and nine platform skips
in a 45m38s job (integration 35m32.998s). These results validate the preceding
parameter-remapping milestone and include the package-fixture reuse change;
they do not yet cover this milestone's seventeen new cases. Windows remains
in progress without a reported failure; Docs 36369419508 passes. The preceding
`8d23bc0` CI run was superseded before its platform suites completed, with
successful quality/runtime jobs and Docs. Cancelled suites are not platform
proof. CI is checked and recorded again immediately before pushing.

The bounded timing investigation stops at the existing package reuse fix and
retained per-module timing reports. The latest Ubuntu integration observation
is 7m39.716s shorter than the preceding completed run at `35c7970`; other
changes between those commits prevent attributing the entire difference to
reuse. The controlled three-case local comparison above remains the measured
23.2% improvement. Further CI tuning requires a concrete failing outcome or
timing bottleneck; no sharding, timeout increase or validation reduction is added.

The 2026-09-28 03:16 UTC pre-push recheck observes Windows finish successfully,
making CI 36369419494 fully green. Windows x64/PostgreSQL 17 reports 8,165
passed, zero failures and three platform skips, 8,168 total, in a 53m41s job
(integration 38m03.125s). Its completed job log was inspected. The unpublished
milestone is amended only to record this newly completed platform result;
source and validation inputs are unchanged. All three platforms now have
complete hosted evidence with the package reuse fix and the existing one-hour
limit. This closes the bounded CI investigation without additional tuning.

### Selected-major compilation and older custom-scan parameters

The shared SDK targets now append an exact `ANKUS_PG13` through `ANKUS_PG19`
compiler symbol after consumer properties are evaluated. The custom-scan
provider selects PostgreSQL 13–15's relation-id-set helper arguments and
PostgreSQL 16 and later's relation pointers; it checks the unsigned-to-signed
variable-index conversion only for PostgreSQL 13–14. Public build settings,
the custom-scan guide, README and sample guide describe this contract.

Actual PostgreSQL 15.19 release headers exposed a separate helper defect:
`BufferGetPageSize` uses its argument only in assertions. The wrapper now
explicitly consumes it before invoking the same macro, preserving native
behavior without disabling diagnostics or adding a runtime validity check.
Six native executable cases retain configured page size, assertion evaluation
counts, guarded results and rejected-call state for PostgreSQL 13–15 with
assertions enabled and disabled. All sixteen combined helper and SDK symbol
cases pass on Linux in 1.581s. Initial Release validation passes with zero
warnings/errors on Linux (1m21.51s) and Windows (3m32.87s); final checks are pending.

Isolated PostgreSQL 13.23 and 15.19 installations were built from official
SHA256-verified release archives. The initial PG15 backend attempt was stopped
by a test-fixture assumption: Bump allocators are unavailable before PG17.
The registry-fault fixture now uses an explicitly observed AllocSet control on
older servers while retaining the Bump control on PG17 and later. Bump-specific
StringInfo/List probes reject unsupported servers explicitly; their tests check
the exact diagnostic, savepoint recovery, cleanup and original backend identity.
No unsupported Bump behavior is counted as executed. All 102 affected backend
cases pass on Windows x64/PostgreSQL 17.11 in 6m30.851s; the sixteen helper/SDK
cases also pass there in 1.018s without skips.

The expanded Linux PostgreSQL 15.19 check executed 102 cases: 91 passed and
eleven parallel custom-scan cases failed, without skips. All nine parameter
remapping, prepared-plan, volatile-expression and parameter-error cases passed,
as did the packaged SDK and allocation fixtures. PostgreSQL 15 restores worker
GUCs in its library-loading transaction, where an initialization snapshot causes
`failed to initialize transaction_deferrable to 0`. Simply extending the current
Windows deferral to Linux is insufficient: it then exposes PostgreSQL 15's
prohibition on internal subtransactions in a parallel operation. That experiment
ran 112 cases (including configuration workers), with 95 passed and 17 failed,
and is not included in the implementation. A complete older-worker SQL/error
boundary remains required; neither dropping the guard nor suppressing these
failures is acceptable. The public custom-scan guide records this limitation.
No CI suite is reduced. PostgreSQL 13.23 is provisioned but has no backend
execution evidence in this milestone.

Final focused PostgreSQL 15.19 verification passes all 46 SDK, initialization,
allocator and direct partition-remapping cases in 3m32.527s, then all five
remaining prepared-parameter, volatile-expression and parameter-error cases in
2m11.893s, with zero failures or skips. These are scoped contracts, not full
PostgreSQL 15 platform proof. Final Windows Release passes with zero
warnings/errors in 2m49.48s. Final Linux Release passes with zero warnings/errors
in 1m14.68s; API freshness checks 200 pages/2,437 members, the site builds 245
pages and its check reports zero diagnostics. Final plain full Linux `dotnet
test` against PostgreSQL 18.6 passes **8,195 tests, zero failures and six
Windows-only skips, 8,201 total**, in 11m37.486s (integration 11m36.805s).
Read-only PostgreSQL 16.15 source inspection
confirms the same parallel internal-subtransaction restriction; PostgreSQL 17.11
permits it. This is a separate version boundary from the PG16 parameter-helper
ABI change and must be handled in the remaining older-worker implementation.
The full PostgreSQL/platform matrix and all other full-port requirements remain
required work.

The completed macOS job in
[CI 36373174861](https://github.com/willibrandon/ankus/actions/runs/36373174861)
emits Homebrew's warning that LLVM 20 was installed without replacing LLVM 18's
global links. Its log explicitly verifies Homebrew Clang 20.1.8 from the LLVM 20
installation. `ConfigureHeaderFrontend` verifies the required frontend option
and prepends that installation's `bin` directory to both the current process
PATH and subsequent Actions steps. The full macOS suite passes; global Homebrew
relinking is unnecessary, and no warning filtering or diagnostic suppression is
introduced.

The 2026-09-28 04:16 UTC check immediately before committing observes
[CI 36373174861](https://github.com/willibrandon/ankus/actions/runs/36373174861)
at `bd83955` with successful quality, all three runtime jobs, and full Ubuntu
and macOS jobs. Ubuntu x64/PostgreSQL 18 reports 8,179 passed, zero failures
and six skips in a 33m24s job (integration 27m05.286s). macOS ARM64/PostgreSQL
18 reports 8,176 passed, zero failures and nine skips in a 45m56s job
(integration 36m34.937s). Both completed job logs were inspected. Windows
x64/PostgreSQL 17 remains in progress without a reported failure;
[Docs 36373174857](https://github.com/willibrandon/ankus/actions/runs/36373174857)
passes. These hosted results cover the preceding hook/registry milestone;
the local evidence above covers this change. CI is checked and recorded again
immediately before pushing. The bounded CI timing investigation remains closed;
the one-hour limits and complete unsharded suites are unchanged.

### Older parallel worker recovery

PostgreSQL 13–17 loads worker libraries before restoring configuration in the
same transaction on Unix as well as Windows. The existing deferral now applies
on each platform, so immediate provider registration does not acquire a snapshot
before PostgreSQL restores `transaction_deferrable`. PostgreSQL 13–16 also
rejects internal subtransactions during parallel execution. A shared native
managed-call boundary permits successful worker SQL while
retaining the first unrecoverable native error until managed code unwinds.
Subsequent SQL and raw calls in that failed scope must not appear to recover;
PostgreSQL receives the original error only after managed dispatch returns.
Modern per-operation SPI rollback and transaction callback behavior retain
their existing contracts, verified by the full PostgreSQL 18 suite and targeted
PostgreSQL 17 worker regressions.

The first affected Linux x64/PostgreSQL 15.19 run passes **83 tests, zero failures
and zero skips** in 3m05.585s, covering `CustomScanTests`, `InitializationTests`
and `GucParallelTests`, including the previously failing worker scenarios.
Dedicated SQL/raw caught-error and successful session/plan/cursor probes then
pass five cases in 2m15.811s. An expanded affected run passes **90 tests, zero
failures and zero skips**, in 2m18.816s, including long Unicode error messages,
detail/hint preservation, owned plan/allocation/context disposal, native cleanup
callbacks and managed `finally` receipts. All **2,100 generator tests** pass in
26.245s after updating exact emitted-call contracts for the common boundary.
The final affected PG15 scope passes **94 tests, zero failures and zero skips**
in 2m27.353s. This includes original/replacement errors, full repeated diagnostic
copies, actual outer-subtransaction recovery after entering parallel mode, and
all three parallel transaction callback outcomes. The packaged
`BackgroundWorkersRegisterAndShareState` test additionally passes in 3m19.977s:
both worker connection modes swallow a parallel SQL error, receive that original
error after whole-transaction rollback, then commit the next transaction and
complete their existing shared-state and signal checks. Windows x64 Release
builds with zero warnings/errors in 2m48.10s. Debian 13 x64 Release builds with
zero warnings/errors in 1m12.64s. API freshness checks 200 pages/2,437 members;
the 245-page site builds and its check reports zero diagnostics. Windows x64
PostgreSQL 17.11 passes all 95 affected regression cases, with zero failures or
skips, in 6m33.772s. Final plain `dotnet test` on Debian 13 x64/PostgreSQL 18.6
passes **8,203 tests, zero failures and six Windows-only skips**, 8,209 total,
in 11m58.958s. These targeted PostgreSQL 15/17 runs are not full suites for those
versions or validation of the entire platform matrix.

| Required boundary | Actual PostgreSQL 15 evidence |
| --- | --- |
| Restored worker settings and initialization | `BackendLoadedWorkersRestoreTypedValuesAndRegenerateExtras`, `WorkerRestoreFailurePreservesLeaderAndRecovers` |
| Parallel custom scan execution and errors | `TraceScanRunsInParallelWorkers`, `TraceScanRunsParallelIndexChildren`, `TraceScanRecoversFromParallelErrors` |
| Nested SPI sessions, retained plans and cursor exhaustion | `ParallelSqlSessionsRetainPlansAndCursorValues` |
| Original diagnostic, blocked follow-up calls, disposal, finally and leader recovery | Six `ParallelCaughtNativeFailureUnwindsAndLeaderRecovers` cases |
| An actual enclosing rollback permits subsequent SQL | `EnclosingRecoveryScopeRestoresParallelState` |
| Background worker rollback and next committed transaction | `BackgroundWorkersRegisterAndShareState`, both connection modes |

PostgreSQL 13/14's older GUC setter contract still needs implementation and real
backend validation: these versions lack the later role-bearing setter API.
The full PostgreSQL 13–19/platform matrix and other full-port requirements remain
required work; this milestone does not defer or replace them.

The preceding hosted run
[CI 36373174861](https://github.com/willibrandon/ankus/actions/runs/36373174861)
was superseded after `8de4aaf` was pushed: its previously successful Ubuntu and
macOS results stand, but the Windows integration job was cancelled while still
running, without a final successful integration summary. This was cancellation,
not a timeout or complete Windows proof. The replacement
[CI 36377069223](https://github.com/willibrandon/ankus/actions/runs/36377069223)
had successful runtime, quality, Ubuntu and macOS jobs at the recovery commit;
Windows was still running and its documentation deployment passed. Ubuntu passes
8,195 tests with six platform skips in 32m48s; macOS passes 8,192 with nine platform skips in
40m56s. Neither reports a failure, and both run all 8,201 cases from `8de4aaf`.
The macOS Homebrew annotation about LLVM 20 not being linked
alongside installed LLVM 18 does not indicate compiler fallback: the log verifies
the explicitly selected LLVM 20.1.8 compiler before building, and the full test
job passes. Global Homebrew linking is unnecessary because the selected formula's
bin directory is exported to the test process. No additional CI tuning, timeout
changes or runtime patch changes are part of this work.

### SDK default-symbol fixture isolation

The mandatory pre-push check then found the completed Windows failure in
[CI 36377069223](https://github.com/willibrandon/ankus/actions/runs/36377069223):
`SelectedMajorRetainsConsumerConstants` expected the unset SDK default
`ANKUS_PG18`, but its temporary MSBuild project inherited CI's
`AnkusPostgresMajor=17`. The SDK correctly honored that environment value.
The fixture now clears the inherited property before importing SDK defaults;
later consumer overrides and command-line property precedence remain checked.
No production default, assertion or diagnostic standard changes.

The same environment reproduces the original failure on Linux: nine cases
pass and the default row fails. After the fix, all ten
`SdkDefineConstantsTests` cases pass with zero failures/skips on Linux in
2.287s and Windows x64 in 0.563s, with `AnkusPostgresMajor=17` inherited on both.
The hosted Windows integration module itself passed 3,406 cases with two
Linux-only skips in 43m08.738s. Its Build module had 913 passes, one failure and
one Linux-only skip; subsequent unit modules were not reached. The job ended
after 55m41s, below its 60-minute limit.

Final plain full Linux x64 `dotnet test` against PostgreSQL 18.6 passes
**8,203 tests, zero failures and six Windows-only skips**, 8,209 total, in
12m21.632s. Release builds with zero warnings/errors in 1m23.29s. The preceding
API freshness and site checks remain applicable; this repair changes only the
test fixture and this progress record. The 2026-09-28 05:34 UTC pre-commit
check confirms the preceding CI run is complete with only the diagnosed Windows
failure; all other jobs and Docs 36377069224 pass. The local repair is verified
on Linux and Windows; replacement hosted results remain pending. Previous CI
outcomes are checked and recorded again immediately before pushing.

### PostgreSQL 13/14 native contracts and independent memory observations

Real PostgreSQL 13.23 compilation exposed version-specific native declarations:
older function-call argument expansion and parse nodes, missing direct header
includes, the pre-15 GUC setter without role OIDs, and the transaction-ID datum
reader. The generated bridge now uses each selected header's actual contract.
The custom-scan sample reads the older `Value` union on PostgreSQL 13/14 and the
dedicated `Integer` node on later versions. Planner declaration evidence uses
PostgreSQL's native LIKE implementation and matching support routine, available
throughout the supported version range.

The first executable Linux x64/PostgreSQL 13.23 affected run passes 177 of 206
cases, with 29 failures and no skips, in 3m13.686s. PostgreSQL 14.20 passes 204
of the same 206 cases, with two failures and no skips, in 2m59.517s. The failures
identify fixture assumptions about newer polymorphic signatures, Memoize,
memory-context catalogs and session-preload transactions. These are diagnostic
runs, not passing version validation.

The test extension now installs an explicit `ankus_test_memory.contexts` view.
PostgreSQL 14 and later retain their actual catalog as the source. PostgreSQL 13
uses the standalone native allocator fixture to traverse real backend contexts
and collect native allocator counters, independently of Ankus's memory APIs.
Existing ownership, cleanup and byte-accounting assertions query that view,
including in separately created encoding databases. No observations are replaced
by constants or empty results.

PostgreSQL 13/14 session preload happens outside a transaction; PostgreSQL 15+
moved it inside the startup transaction. The initializer fixture reads its mode
through a typed GUC, explicitly witnesses SQL rejection outside a transaction,
and retains real SQL execution where available. Source-priority tests exercise
captured superuser context on 13/14 and current parameter grants on 15+, retaining
independent session/local history checks. The public initialization,
configuration and function-call recovery guides describe those native boundaries.
The expanded checks and final milestone gates are recorded below. Full
PostgreSQL 13–19/platform proof and the remaining faithful-port requirements
are still required.

The expanded runs each execute 335 cases: **314 pass and 21 fail, with no
skips**, on PostgreSQL 13.23 in 2m25.105s and PostgreSQL 14.20 in 2m31.372s.
All earlier GUC, preload, function-call, enum and custom-scan failures are fixed.
The remaining failures are memory tests assuming PostgreSQL 16+ alignment and
chunk-offset limits. Read-only pgrx source confirms its aligned allocator is also
restricted to PostgreSQL 16+. Older-version cases now check exact `0A000`
diagnostics, native context cleanup, savepoint recovery and successful ordinary
allocations. Adoption/transaction ownership still runs on every version, using
native-default alignment on 13/14 and 4,096-byte alignment on 16+. The valid
pre-16 maximum block configuration remains accepted. Newer assertions retain
their complete aligned-allocation success and invalid-size contracts; no test
is skipped and no production limit is relaxed.

The corrected Linux x64/PostgreSQL 13.23 scope now passes **all 335 cases, zero
failures and zero skips**, in 2m29.605s. This includes independent native memory
inventory/size/growth/deletion checks, older privilege transitions, real parallel
worker restoration and recovery, session preload, function metadata and calls,
custom-scan parameter remapping and exact transaction-ID/enum behavior. API
freshness checks 200 pages/2,437 members; the 245-page documentation site builds
and its check reports zero diagnostics.

The same corrected scope passes **335 cases, zero failures and zero skips** on
Linux x64/PostgreSQL 14.20 in 2m35.159s. Both versions execute the following
boundaries; these are affected-scope results, not full-suite platform proof.

| Required boundary | Backend evidence on both PostgreSQL 13.23 and 14.20 |
| --- | --- |
| Native calls, overloads, defaults, exact identity and error recovery | `FunctionCallTests`, including `DefaultExpressionsRemainTypedAndExecuteOnce` and `ErrorsPreserveDiagnosticsRollbackAndSameBackendRecovery` |
| Transaction IDs and enum ownership | `TransactionIdsRemainDistinctFromOidsAcrossOwners`, `EnumOwnershipPathsPreserveIdentity`, `EnumGuardedRecoveryPreservesStateAndCleansContexts` |
| Original privilege context, source precedence and stacked restoration | `PlaceholderAdoptionRetainsOriginalSetterPrivileges`, `PlaceholderMaskedAndLocalStatesRetainTheirOwnPrivileges`, both startup-source tests |
| Startup without SQL, failure cleanup and a healthy subsequent connection | `SessionPreloadInitializesBeforeFirstFunction`, `SessionPreloadFailureUnwindsAndPreservesServer` |
| Worker settings, parallel diagnostics and actual parameter remapping | `GucParallelTests`, `CustomScanTests` |
| Independent native allocation observation and ownership transitions | `NativeInventoryProvesOwnedAndBorrowedContextLifetimes`, all sizing/growth cases, `AdoptedAllocationsRespectSubtransactionOwnership` |
| Native version limits and recovery after rejection | All older over-alignment rows assert the exact error, unchanged context inventory, ordinary allocation values and original backend identity |

The final Release build completes with zero warnings/errors in 1m15.23s, and
all 2,100 generator tests pass without skips in 23.282s. A broader PostgreSQL
14.20 diagnostic run executes all 8,209 discovered tests: 8,005 pass, 198 fail,
and six Windows-only cases skip, in 3m20.064s. Of the failures, 83 share one
package-fixture initialization failure because the isolated validation checkout
lacked the runtime package payload. That checkout now has the matching immutable
payload; its completed package rerun is recorded below. The other 115 failures require
further version-specific fixture and native-contract work, including allocator
availability/accounting, numeric scales, temporal values and diagnostics, node
formatting, prefix behavior, parameter grants, and event triggers. This is
recorded failure evidence, not full PostgreSQL 14 validation. The complete
supported version and platform matrix and remaining full-port scope remain
required.

After staging the runtime package correctly, the PostgreSQL 14 package scope
executes all 83 cases: **79 pass, four fail, zero skip**, in 10m06.958s.
The remaining package failures are selected-header declarations in a generated
consumer, two prefix-reservation expectations, and a worker readiness query
using the PostgreSQL 17+ wait-event spelling. These are additional required
compatibility work, separate from the 335 passing affected cases above.

Final plain full Linux x64 `dotnet test` against PostgreSQL 18.6 passes **8,203
tests, zero failures and six Windows-only skips**, 8,209 total, in 13m00.338s
(integration 12m59.699s). This run overlaps independent PostgreSQL 14 validation
in a separate checkout and is not a cold-cache performance measurement. No
runtime patch, CI timeout, sharding or diagnostic-standard changes are included.

The 2026-09-28 06:17 UTC pre-commit check records
[CI 36382510666](https://github.com/willibrandon/ankus/actions/runs/36382510666)
at `4c7fe76` with successful quality, all three runtime jobs, Ubuntu and macOS;
Windows remains in progress without a reported failure. Ubuntu's completed log
reports 8,203 passes, zero failures and six platform skips in a 34m07s job
(integration 27m38.273s). [Docs 36382510680](https://github.com/willibrandon/ankus/actions/runs/36382510680)
also passes. These hosted results cover the preceding recovery/SDK-fixture
commit; the local evidence above covers this milestone. Previous run outcomes
are checked and recorded again immediately before pushing.

### Native GUC version boundaries and worker observations

The next older-version pass preserves PostgreSQL 13/14's warning-only prefix
checks, exact `42704` diagnostics, retained placeholders and rollback history.
PostgreSQL 15+ keeps the complete `42602` warning/removal/reservation assertions.
Both paths retain case-sensitive prefix checks, LATIN1 conversion, shared
preload, repeated loading, and package reinstall evidence. Privilege tests use
parameter grants where available and actual superuser authorization on older
servers, keeping visibility privileges independent from write access.

GUC lifetime snapshots now distinguish the absence of `GUCMemoryContext` before
PostgreSQL 16 from measured context bytes on newer servers. Independent libc
allocation witnesses, repeated growth bounds, exact hook state, managed object
collection, and temporary-context cleanup remain required on both paths; missing
memory observations are not replaced with zero. Worker death readiness uses
the native wait-event spelling selected at PostgreSQL 17. Its focused PostgreSQL
14.20 case passes with actual postmaster termination and both exact death
receipts, one test without failures or skips in 3m16.707s.

The complete affected GUC/worker scope passes **91 tests, zero failures/skips**
on Linux x64/PostgreSQL 14.20 in 5m03.902s. The first PostgreSQL 13.23 run passes
90 of the same 91 cases, exposing another native boundary: PostgreSQL 13 calls
the report show hook during `SET`, while PostgreSQL 14+ defers parameter reporting
until the command finishes. The corrected case checks exact `38000` diagnostics,
rollback to the original value and a subsequent successful change in the same
PostgreSQL 13 backend. Newer servers retain the FATAL expectation for reporting
after an autocommitted `SET`. The public configuration guide explains that
difference. The final PostgreSQL 13.23 affected run passes all **91 tests with
zero failures/skips** in 4m58.120s. PostgreSQL 15.19 passes the same **91 tests
with zero failures/skips** in 5m41.723s, exercising the newer privilege and
prefix rules with the older malloc-backed GUC storage. Release builds with zero
warnings/errors in 1m17.04s; API freshness passes for 200 pages and 2,437 members;
the site builds all 245 pages and its check reports zero errors/warnings/hints.
The final plain full Linux x64/PostgreSQL 18.6 run passes **8,203 tests, zero
failures and six Windows-only skips**, 8,209 total, in 13m24.279s (integration
13m23.345s). This overlaps independent validation in a separate checkout and
is not a cold-cache timing baseline. The remaining full-port requirements and
full version/platform evidence remain required.

The final report-show case also passes on PostgreSQL 14.20 in a subsequent
36-case affected run with zero failures/skips (2m39.856s). The other 35 cases
validate the next, separate memory-test corrections in an isolated checkout;
those follow-up changes are not part of this GUC milestone.

The 2026-09-28 06:58 UTC pre-commit check records
[CI 36385855551](https://github.com/willibrandon/ankus/actions/runs/36385855551)
at `40bdb22` with successful quality, all three runtime jobs and Ubuntu;
macOS and Windows remain in progress without a reported failure. Ubuntu's
completed log confirms 8,203 passes, zero failures and six platform skips in
a 32m07s job (integration 25m51.688s).
[Docs 36385855549](https://github.com/willibrandon/ankus/actions/runs/36385855549)
passes. The earlier `4c7fe76` run was superseded: its completed Ubuntu and macOS
results stand, while Windows was cancelled without a completed-suite result.
Previous outcomes are checked and recorded again immediately before pushing.

## Interval construction overflow on older PostgreSQL versions

The complete Linux x64/PostgreSQL 13.23 diagnostic run executes all 8,209 cases:
8,014 pass, 189 fail and six Windows-only cases skip, in 10m06.541s
(integration 10m04.314s). This run includes the separate, still-uncommitted
memory-test follow-up described above. It establishes remaining failures,
not full PostgreSQL 13 parity. Those failures include native feature/version
boundaries, older SQL syntax and genuine interval construction overflow.

PostgreSQL 13–16's `make_interval` can silently overflow month, day and
microsecond arithmetic. The generated native boundary now checks those
intermediate operations with PostgreSQL's checked arithmetic helpers before
calling the original constructor. PostgreSQL 17+ continues to use its native
checks. The guard follows upstream's
[interval overflow correction](https://github.com/postgres/postgres/commit/b2d55447a563036579d6777f64a7483dceeab6ea),
retains native fractional-second rounding and exact error diagnostics, and
runs inside the existing native error boundary. No backend longjmp crosses
managed frames. The interval API remarks and public date/time guide document
overflow rejection on every supported version.

The new `IntervalFactoryTests` add 48 backend cases. Together with all 11
existing `TemporalConvenienceErrorsPreserveState` rows, the affected scope
passes **59 tests, zero failures/skips** on Linux x64/PostgreSQL 13.23 in
3m02.078s, PostgreSQL 14.20 in 2m47.472s and PostgreSQL 15.19 in 3m12.145s.

| Required boundary | Executed assertions |
| --- | --- |
| Exact month/day/time values | Decode all 16 native `interval_send` bytes; compare the three fields with independent expected literals, including signed endpoints and mixed signs |
| Checked field arithmetic | Reject positive/negative year and week multiplication, month/day addition, and overflowing intermediates even when a later component could cancel them |
| Seconds and accumulated time | Preserve valid 64-bit boundary values; reject out-of-range conversions and minute/second sums with exact `22008` diagnostics |
| Floating-point behavior | Check native half-microsecond rounding; distinguish nonfinite inputs (`22008`) from finite multiplication overflow (`22003`) with exact messages |
| Cleanup and backend recovery | Each rejected input verifies savepoint rollback, 50 caught errors and `finally` executions, stable diagnostics, zero additional independently observed native contexts, retained writes/prepared plan, a later exact interval and the same backend's successful SQL call |

Assertion and static mutation reviews retain independent binary oracles and
observable error/recovery checks; no executed mutation or coverage percentage
is claimed. Release builds with zero warnings/errors in 1m13.63s; API generation
and freshness pass for 200 pages/2,437 members; the documentation site builds
245 pages and its check reports zero errors/warnings/hints.
The final plain full Linux x64/PostgreSQL 18.6 run passes **8,251 tests, zero
failures and six Windows-only skips, 8,257 total**, in 13m05.496s (integration
13m04.354s), including all new interval cases on the modern native path. This
overlaps independent version verification and is not a cold-cache timing
baseline. The full PostgreSQL 13–19/platform matrix and the other faithful-port
requirements remain open.

A separate follow-up corrects twenty SELECT column aliases in ten test files:
PostgreSQL 13 requires `AS` before keyword aliases such as `value` and `label`.
All existing assertions remain, and all **261 affected cases pass with zero
failures/skips** on Linux x64/PostgreSQL 13.23 in 4m23.726s, including actual
package relocation, rollback and reinstall. That patch remains separate from
this interval milestone. A further temporal-query correction lets 23 of 26
PostgreSQL 13 field cases pass; three timezone-minute comparisons still fail
and remain under investigation. Those results do not establish a completed
temporal or full older-version milestone.

The 2026-09-28 07:34 UTC pre-commit check records
[CI 36389229425](https://github.com/willibrandon/ankus/actions/runs/36389229425)
at `f972fdd`: quality and all three runtime jobs pass; Ubuntu, macOS and Windows
test jobs remain in progress without a reported failure.
[Docs 36389229439](https://github.com/willibrandon/ankus/actions/runs/36389229439)
passes. The earlier `40bdb22` macOS job subsequently passed in 37m52s
(integration 29m19.177s); its Windows job was cancelled when superseded and
does not supply a completed-suite result. Previous outcomes are checked and
recorded again immediately before pushing.

## Exact storage and field checks on older PostgreSQL versions

The next compatibility milestone applies the previously verified test corrections.
Native box ownership, cloning, raw transfer, offset views and transaction cleanup
now use native-default alignment on PostgreSQL 13–15, which do not support
over-aligned allocation. PostgreSQL 16+ retains the original 64/4,096-byte
alignment. Payload, padding, shallow-pointer, allocation-policy, owner and stale
view assertions remain intact; dedicated allocation cases still require exact
unsupported-feature errors and recovery on older servers.

The Generation replacement test keeps a live 32-byte control before measuring
large-block release. PostgreSQL 13/14 lazily allocate a regular block for small
chunks; previously the shrink's first regular block obscured part of the released
large block. The original two-MiB release bound and independent native/catalog
byte equality remain required. A new assertion proves the control's address and
every byte survive both resizes before reset. The 36-case focused memory/recovery
scope passes without failures/skips on Linux x64/PostgreSQL 13.23 in 2m25.849s,
14.20 in 2m39.856s and 18.6 in 3m02.547s. That scope includes an unchanged GUC
reporting recovery control.

Twenty explicit `AS` additions let PostgreSQL 13 execute existing custom-type,
serialized/polymorphic value, tuple, array, aggregate and packaged lifecycle
checks. All 261 affected cases pass with zero failures/skips on Linux
x64/PostgreSQL 13.23 in 4m23.726s, retaining every assertion. Temporal field
queries now cast integral microseconds before `mod` and truncate timezone-minute
extraction before comparing whole-minute components. This handles PostgreSQL
13's floating-point extraction results without rounding remaining seconds into
an extra minute. The final 26 affected timestamp/time/timezone cases pass with
zero failures/skips in 1m38.318s, resolving the three comparisons recorded in the
preceding milestone. The public memory and date/time guides clarify those
accounting and whole-component contracts.

The complete Linux x64/PostgreSQL 13.23 diagnostic now records **8,138 passes,
113 failures and six Windows-only skips, 8,257 total**, in 11m28.625s
(integration 11m26.624s). All managed test modules pass. Remaining failures
include native feature availability, older TOAST inspection syntax and node
formatting, numeric and temporal boundaries, selected-header provider contracts
and event triggers. Index enumeration also exposes a production defect: its
physical descriptor has no type OID on PostgreSQL 13, but the tuple transport
attempts to resolve type zero. These results establish remaining work, not full
older-version parity.

The final plain full Linux x64/PostgreSQL 18.6 regression passes **8,251 tests,
zero failures and six Windows-only skips, 8,257 total**, in 12m23.357s
(integration 12m22.248s). Release builds with zero warnings/errors in 1m47.04s;
API freshness passes for 200 pages/2,437 members; the site builds 245 pages and
its check reports zero errors/warnings/hints. The test timing overlaps isolated
older-version work and is not a cold-cache benchmark. Other full-port
requirements and the complete PostgreSQL 13–19/platform matrix remain required.

The 2026-09-28 07:58 UTC pre-commit check records
[CI 36392466233](https://github.com/willibrandon/ankus/actions/runs/36392466233)
at `277b031`: quality and all three runtime jobs pass; Ubuntu, macOS and Windows
test jobs remain in progress without a reported failure.
[Docs 36392466283](https://github.com/willibrandon/ankus/actions/runs/36392466283)
passes. The preceding `f972fdd` platform jobs were cancelled when superseded and
provide no completed-suite evidence. Previous outcomes are checked and recorded
again immediately before pushing.

## Physical relation descriptors without a row type

The PostgreSQL 13 diagnostic exposed a real index-descriptor defect. Its relcache
uses `pg_class.reltype` directly, so index descriptors carry `InvalidOid` (zero).
PostgreSQL 14's upstream change `f3faf35f370` uses `RECORDOID` for relations
without a catalog row type. pgrx exposes the native descriptor identity; Ankus
must preserve it as well. The native transport now avoids looking up type zero,
and an internal physical-metadata reader accepts that identity without treating
it as a tuple value. The ordinary tuple decoder still rejects malformed zero
identities. Tuple/array creation and typed SPI parameter binding reject a
descriptor without a row type before backend access.

Six new managed cases exercise independent literal transport, exact detached
metadata, malformed physical identities and typed-value rejection. Six new
backend cases compare table, ordinary/expression/partitioned index, sequence and
TOAST descriptors against independent catalogs after the relation closes,
including dropped slots, domain types, collation, typmods, physical order and
Unicode names. All **44 managed tuple cases pass**, including the six new cases.
All **44 relation backend cases pass without failures/skips** on Linux
x64/PostgreSQL 13.23 in 2m32.520s and PostgreSQL 14.20 in 2m57.276s,
including the original failed index enumeration. The public relation/composite
guides and generated API document absent row identities and factory rejection.

The factory assertions require the exact row-type error message, distinguishing
that rejection from a later missing-backend error of the same exception class.
Assertion and static mutation reviews cover identity substitution, metadata
changes, malformed transport acceptance, guard removal and ownership after
release; no executed mutation or coverage percentage is claimed.

Final Release builds with zero warnings/errors in 1m19.68s. API generation and
freshness pass for 200 pages/2,437 members; the site builds 245 pages and its
check reports zero errors/warnings/hints. Plain full Linux x64/PostgreSQL 18.6
`dotnet test` passes **8,263 tests, zero failures and six Windows-only skips,
8,269 total**, in 12m46.442s (integration 12m45.807s). This overlaps isolated
version checks and is not a cold-cache timing baseline. The full PostgreSQL
13–19/platform matrix and other faithful-port work remain open.

A separate follow-up retains all existing TOAST and native node-format assertions
while selecting PostgreSQL 13's available compression observations and older
native token spellings. All **30 affected cases pass without failures/skips**
on Linux x64/PostgreSQL 13.23 in 2m54.716s and 14.20 in 2m23.448s.
PostgreSQL 15.19 also passes all four affected native-format cases in 2m41.705s,
covering its newer CollateExpr spelling with the older empty-token behavior.
That patch and a full-range date-oracle correction remain separate from this
descriptor milestone; neither establishes a complete older-version result.

The 2026-09-28 08:21 UTC pre-commit check records
[CI 36394682622](https://github.com/willibrandon/ankus/actions/runs/36394682622)
at `bfa6bcc`: quality and all three runtime jobs pass; Ubuntu, macOS and Windows
test jobs remain in progress without a reported failure.
[Docs 36394682505](https://github.com/willibrandon/ankus/actions/runs/36394682505)
passes. The preceding `277b031` platform jobs were cancelled when superseded
and do not provide completed-suite evidence. Previous outcomes are checked and
recorded again immediately before pushing.

## Native storage, diagnostic text and full-range date observations

TOAST integration checks now select explicit `pglz` compression and inspect its
method on PostgreSQL 14+, while PostgreSQL 13's single compression method is
proved through independently observed stored size. Text, bytea and long JSON
string payloads must be smaller than their uncompressed byte lengths. Numeric
storage is compared with a native arithmetic result's uncompressed binary size,
so compact numeric representation alone cannot satisfy the compression check.
External TOAST storage, exact Unicode/binary content, seven-byte packed numeric
storage and all 50,000 decimal digits with their scale remain asserted.

Native node-format checks retain the selected server's complete output:
PostgreSQL 13/14's `COLLATE` spelling becomes `COLLATEEXPR` in 15, and empty
strings share the null `<>` token until 16. The runtime continues to expose
PostgreSQL's actual diagnostic text. The public raw-values guide documents these
version differences. Exact nested fields, UTF8/LATIN1 encoding and escaping,
eight repeated recursion errors, formatting-context reclamation and same-backend
recovery remain required. All 30 affected node/TOAST cases pass without
failures/skips on Linux x64/PostgreSQL 13.23 in 2m54.716s and 14.20 in
2m23.448s; all four affected formatting cases also pass on 15.19 in 2m41.705s.

The PostgreSQL 13 full-range date oracle now reads native ISO date output and
uses native date subtraction for Julian/Unix days and exact epoch seconds.
Its `EXTRACT(date)` operation converts through timestamp and therefore cannot
observe dates near the maximum supported date. PostgreSQL 14+ retains native
date extraction. All fourteen existing boundary, BC/leap-year and epoch inputs
remain in the test. DateStyle setup executes separately from the parameterized
query, preserving the driver's single-statement parameter contract.

All **14 date-boundary cases pass without failures/skips** on Linux
x64/PostgreSQL 13.23 in 2m13.204s, including the full-range maximum and BC leap
dates. Release builds with zero warnings/errors in 1m38.87s. Generated API
freshness passes for 200 pages/2,437 members; the site builds 245 pages and its
check reports zero errors/warnings/hints. Plain full Linux x64/PostgreSQL 18.6
`dotnet test` passes **8,263 tests, zero failures and six Windows-only skips,
8,269 total**, in 12m34.903s (integration 12m34.281s). This run overlaps an
isolated version diagnostic and is not a cold-cache timing baseline.

The full Linux x64/PostgreSQL 13.23 diagnostic records **8,161 passes,
102 failures and six Windows-only skips, 8,269 total**, in 11m16.599s
(integration 11m14.866s). All managed test modules pass. Remaining failures
include allocator availability/alignment, numeric and temporal version
contracts, selected-header package assumptions, event-trigger availability and
a parallel function-local GUC query timeout. That query previously completed
in under a second; its 30-second timeout and callback-extra validation failures
during cancellation cleanup require investigation rather than a larger timeout.
The complete PostgreSQL 13–19/platform matrix and other faithful-port work
remain required.

The 2026-09-28 08:43 UTC pre-commit check records
[CI 36396998055](https://github.com/willibrandon/ankus/actions/runs/36396998055)
at `85e00bc`: quality and all three runtime jobs pass; Ubuntu, macOS and Windows
test jobs remain in progress without a reported failure.
[Docs 36396998230](https://github.com/willibrandon/ankus/actions/runs/36396998230)
passes. The preceding `bfa6bcc` platform jobs were cancelled when superseded
and do not provide completed-suite evidence. Previous outcomes are checked and
recorded again immediately before pushing.

## Selected-header native provider and worker-phase evidence

The packaged binding fixture no longer assumes that every supported header
declares `_PG_output_plugin_init` or uses the newer `Integer` node structure.
PostgreSQL 13–15 uses an explicit `LibraryImport`/`DirectPInvoke` for the
fixture-owned initializer, linked through its existing native archive. That
initializer only updates callback storage and cannot raise PostgreSQL `ERROR`.
PostgreSQL 16+ retains generated guarded `NativeMethods` coverage. PostgreSQL
13/14 reads the selected `Value.val.ival` layout; 15+ reads `Integer.ival`.
Signed minimum, maximum, zero and ordinary values, native node tags and matching
`pfree` cleanup remain checked. The public build-settings guide explains the
selected-header boundary and the error-guard obligation for explicit imports.

Worker lifecycle snapshots now require early module-registration SQL to be
unavailable on every platform before PostgreSQL 18. This matches native library
restoration ordering and the existing production deferral, rather than assuming
that only Windows restores libraries early. Ready-phase SQL, nested callback
capabilities, exact native values, per-process ownership, hook ordering, preload,
initialization retry, shared type identity, rebuild and clean assertions remain
intact.

On Linux x64/PostgreSQL 13.23, the complete packaged binding case and all eighteen
existing GUC worker cases pass **19 tests, zero failures and zero skips** in
5m13.437s. PostgreSQL 15.19 passes the complete package case in 4m40.362s,
covering its newer node structure with the earlier native import and worker
registration contracts. Release builds with zero warnings/errors in 1m27.59s.
API freshness passes for 200 pages/2,437 members; the site builds 245 pages with
zero check errors/warnings/hints. Plain full Linux x64/PostgreSQL 18.6
`dotnet test` passes **8,263 tests, zero failures and six Windows-only skips,
8,269 total**, in 12m45.819s (integration 12m44.779s). Concurrent isolated
version checks mean this is not a cold-cache timing baseline.

The earlier PostgreSQL 13 function-local GUC timeout does not recur: that case
passes in 0.721s. Its cause remains unresolved. However, inspection of the
passing scope's server log finds additional assignment-extra `FATAL` failures
while workers clean up after expected forbidden configuration changes. Two
stronger assertions in an isolated follow-up now fail on unchanged production
code on PostgreSQL 15.19 in 2m20.824s, specifically detecting those unexpected
cleanup failures after the original error and leader-recovery checks succeed.
Native deferred-restoration history and its value/extra ownership are under
investigation. This independently reproduced cleanup defect is not established
as the cause of the timeout, and neither issue is deferred from full-port scope.
The complete PostgreSQL 13–19/platform matrix and other port requirements remain
open; focused passes do not replace the earlier complete diagnostic inventory.

The 2026-09-28 09:03 UTC pre-commit check records
[CI 36399145420](https://github.com/willibrandon/ankus/actions/runs/36399145420)
at `9df3bdc`: quality and all three runtime jobs pass; Ubuntu, macOS and Windows
test jobs remain in progress without a reported failure.
[Docs 36399145419](https://github.com/willibrandon/ankus/actions/runs/36399145419)
passes. The preceding `85e00bc` platform jobs were cancelled when superseded
and do not provide completed-suite evidence. Previous outcomes are checked and
recorded again immediately before pushing.

## Checked worker configuration history

Native PostgreSQL 15 observations confirm the worker cleanup defect: deferred
restoration initially stores integer `37` with no hook extra, and replay later
installs checked extra while pushing the unchecked value/extra pair into native
transaction history. A subsequent worker error restores that invalid pair and
causes an additional assignment failure. The two strengthened original worker
SET/SET LOCAL cases fail on this cleanup error before the production correction.

The bridge now retains the checked worker value together with its extra in the
history entry introduced by replay. This includes check-hook normalization and
the selected headers' source/context/role metadata. It preserves unrelated
history and PostgreSQL's current/reset/boot/history references, freeing replaced
string or extra storage only when no native field retains it, with the matching
version's allocator. Assignment callbacks still receive their accepted data
without rechecking or suppression. The public configuration guide describes
worker normalization and rollback-history behavior.

The original eighteen GUC worker cases pass on Linux x64/PostgreSQL 15.19 in
2m06.813s after the correction. Expanded error-path cases cover all five setting
kinds, default null, empty/Unicode text, and worker-only normalization to Unicode
or null. They retain the original SQLSTATE, native message/file/context, full
leader state, exact worker values and extras, and same-session recovery, and
reject additional assignment failures in the server log. All **26 expanded
worker cases pass with zero failures/skips** on PostgreSQL 15.19 in 1m58.287s.
An extra-only mutation passes all six unchanged-value cases but fails all four
worker-normalized Unicode/null cases in 2m34.091s, proving that the tests require
both the accepted value and its extra. The mutation is removed. Release builds
with zero warnings/errors in 1m34.63s; API freshness passes for 200 pages/2,437
members. The site builds 245 pages and its check reports zero errors/warnings/
hints. All **96 configuration cases pass with zero failures/skips** on Linux
x64/PostgreSQL 13.23 in 2m54.559s. Final plain `dotnet test` on Linux
x64/PostgreSQL 18.6 passes **8,271 tests, zero failures and six Windows-only
skips, 8,277 total**, in 12m48.733s (integration 12m47.732s). These runs used
existing local caches; overlapping version diagnostics are not a cold-cache
performance baseline. The refreshed full PostgreSQL 13 diagnostic is in progress.
The separate earlier 30-second query stall has not been attributed to this
cleanup defect; the full version/platform matrix and other port requirements
remain open.

Before committing this milestone, `316d390` CI run `36401239486` has passed
quality and all three runtime jobs; all three complete platform test jobs remain
in progress without a reported failure. Its Docs run `36401239525` passes.
The preceding `9df3bdc` CI run `36399145420` is cancelled after being superseded:
quality and runtime jobs passed, and all three platform jobs were cancelled.
These cancelled jobs do not supply completed-suite evidence. Previous outcomes
are checked and recorded again immediately before pushing.

## Native allocator and list capability contracts

The complete Linux x64/PostgreSQL 13.23 diagnostic after checked worker history
records **8,171 passes, 100 failures and six Windows-only skips, 8,277 total**,
in 10m22.304s (integration 10m19.933s). The earlier GUC query stall does not recur
in this run, which is insufficient to close the intermittent-timeout follow-up.

Allocator, allocation-lifecycle, item-pointer, and list tests now distinguish
selected-header capabilities. PostgreSQL before 16 must reject over-alignment
and transaction-ID lists with exact feature-not-supported diagnostics; checks
then verify ordinary allocation or exact OID cells in the same backend. Slab
alignment rejection retains the original pointer, length, bytes, and owner.
Unavailable aligned lifecycle requests release their temporary owner before a
complete ordinary-alignment lifecycle checks native/catalog accounting.

On PostgreSQL before 17, Bump fixture construction must fail before managed
callback entry. Tests verify its exact error, partial native-context cleanup,
savepoint and transaction completion, encoding, and backend recovery. The
checks preserve the fixture parent's top-transaction lifetime: it survives
savepoint rollback and is reclaimed by explicit deletion or transaction end.
The encoded diagnostic check distinguishes the managed callback's notice from
unrelated extension-installation warnings on older servers. Older-version cases
establish Bump unavailability, not Bump ownership behavior.
The supported-server paths retain all existing Bump value, alias, callback,
diagnostic, and lifetime assertions. No rows are skipped or diagnostics relaxed.
The public allocation and list guides document SQLSTATE `0A000` for unavailable
features.

All **143 affected cases pass with zero failures/skips** on Linux x64/PostgreSQL
13.23 in 1m56.305s and PostgreSQL 15.19 in 2m25.506s. Final Release builds with
zero warnings/errors in 1m08.24s. API freshness passes for 200 pages/2,437 members;
the site builds 245 pages and its check reports zero errors/warnings/hints.
Final plain `dotnet test` on Linux x64/PostgreSQL 18.6 passes **8,271 tests,
zero failures and six Windows-only skips, 8,277 total**, in 12m50.740s
(integration 12m50.159s). These cached local runs overlap independent version
checks and are not cold-cache performance measurements. The remaining full-port
requirements and complete older-version/platform validation stay open.

Immediately before this commit, `d51e4e5` CI run `36404648450` has successful
quality and all three runtime jobs; all three full platform test jobs remain in
progress without a reported failure. Docs run `36404647733` passes. Predecessor
`316d390` CI run `36401239486` was cancelled when superseded: quality and runtime
jobs passed, and all three platform jobs were cancelled. Cancelled suites do not
supply completed platform evidence. Previous outcomes are checked and recorded
again immediately before pushing.

## Native numeric rejection contracts

Numeric constraint and primitive-conversion cases now verify the selected
server's actual feature boundaries. PostgreSQL 13 rejects numeric infinity text
with `22P02` and floating-point infinity conversion with `0A000`. PostgreSQL
13/14 reject negative scales and scales above precision with `22023`. Separate
native SQL and generated function calls must produce the same SQLSTATE,
message, detail, and hint; both errors are rolled back, followed by an exact
`1.24` constrained result and the same backend PID. Ordinary overflow cases use
the same independent native comparison with `22003` on every version.

Successful supported-version cases retain their exact numeric binary output,
floating-point bits, signed zero, subnormal, NaN, generic-integer, and nullable
assertions. No rows are removed or skipped. The public numeric guide documents
the version-dependent rejection diagnostics.

All **67 numeric contract cases pass with zero failures/skips** on Linux
x64/PostgreSQL 13.23 in 1m48.104s, PostgreSQL 14.20 in 2m10.725s and PostgreSQL
15.19 in 2m00.676s. Release passes with zero warnings/errors in 1m19.28s; API
freshness passes for 200 pages/2,437 members; the site builds 245 pages and its
check reports zero errors/warnings/hints. Final plain full PostgreSQL 18.6
testing passes **8,271 tests, zero failures and six Windows-only skips, 8,277
total**, in 12m42.534s (integration 12m41.919s).

The refreshed complete Linux x64/PostgreSQL 13.23 diagnostic executes all
8,277 cases: **8,209 pass, 62 fail and six Windows-only cases skip**, in
11m39.081s (integration 11m36.711s). This reduces the prior 100 failures by 38
across the allocator/list and numeric milestones. Its implementation, test and
build sources match this milestone. Remaining failures concern temporal, numeric,
array, range, JSON and event-trigger version contracts. The earlier GUC query
stall does not recur; its previously affected function-settings case passes in
0.615s, but the intermittent stall's cause remains unproven. Overlapping local
runs are not a cold-cache timing baseline. The full PostgreSQL 13–19/platform
matrix and other faithful-port requirements remain open.

The immediate pre-commit check records `0360620`
[CI 36407464525](https://github.com/willibrandon/ankus/actions/runs/36407464525)
with successful quality and all three runtime jobs; all three full platform
test jobs remain in progress without a reported failure.
[Docs 36407464523](https://github.com/willibrandon/ankus/actions/runs/36407464523)
passes. The superseded `d51e4e5` CI run `36404648450` is cancelled: quality and
runtime jobs passed, and the three unfinished platform jobs were cancelled.
Those cancelled suites do not supply completed platform evidence. Previous
outcomes are checked and recorded again immediately before pushing.

## Selected-version temporal boundaries

Existing temporal cases now preserve signed 64-bit interval endpoints through
independent binary representations, including raw interval parameter input for
all-maximum components. They compare selected-version BC constructor and infinity
parser diagnostics with independent native calls and verify exact values in the
same backend after rollback. Unsupported infinity admission remains distinct from
executing an infinity operation on a supporting server.

All finite values and SQL NULL still traverse every existing ownership path.
Before PostgreSQL 17, the two infinity input cases instead assert exact rejected
admission; actual managed infinity writes separately assert `0A000`. Sentinel-shaped
finite components retain their full fields and both successful writes on older
servers. PostgreSQL 17+ retains the existing sentinel-collision rejection and
rollback assertions. Session-setting checks compare exact timestamp bytes, avoiding
a decimal read of PostgreSQL 13's floating-point `EXTRACT` result. The public
date/time guide clarifies BC construction and interval infinity availability.

The initial TemporalConvenienceTests/TemporalDatumTests scope passes all **144
cases, zero failures/skips**, on Linux x64/PostgreSQL 13.23 in 2m13.562s. The
expanded four-class scope includes TemporalOperationTests and TemporalParityTests:
all **405 cases pass with zero failures/skips** on PostgreSQL 13.23 in 1m37.214s,
14.20 in 2m34.569s and 15.19 in 2m29.860s. Existing fifty-iteration managed
catch/finally, context-cleanup, retained-write and prepared-plan assertions remain.

Release passes with zero warnings/errors in 1m25.74s. API freshness passes for
200 pages/2,437 members; the site builds 245 pages and its check reports zero
errors/warnings/hints. Final plain full Linux x64/PostgreSQL 18.6 testing passes
**8,271 tests, zero failures and six Windows-only skips, 8,277 total**, in
12m39.426s (integration 12m38.796s). These overlapping local runs do not establish
a cold-cache timing baseline. No cases are removed or skipped and no diagnostics
are suppressed. Other older-version contracts, a refreshed complete PostgreSQL
13 run, the GUC query-stall investigation, and full port/platform requirements
remain open.

Immediately before committing, `e401d77`
[CI 36409892102](https://github.com/willibrandon/ankus/actions/runs/36409892102)
has successful quality and all three runtime jobs; all three full platform test
jobs remain in progress without a reported failure.
[Docs 36409891997](https://github.com/willibrandon/ankus/actions/runs/36409891997)
passes. Superseded `0360620` CI run `36407464525` is cancelled: quality/runtime
jobs passed and all three unfinished platform suites were cancelled, which does
not supply completed platform evidence. Outcomes are checked and recorded again
immediately before pushing.

## Selected-version value and event contracts

Numeric, array, range and scalar JSON checks now exercise unavailable values
explicitly without losing finite values, NULLs, shapes or ownership paths.
Native and generated-function admission errors are compared separately, followed
by exact finite-value recovery in the same backend. Numeric temporal extraction
receives an independently supplied binary interval at the full component limits;
it no longer depends on an older server's overflowing decimal-seconds parser.
The selected server's native extraction value remains the independent oracle.
JSON failures invoke the actual converter before checking its property path,
native cause, fifty managed unwind cycles, context cleanup and retained writes.

Login cases on PostgreSQL 13–16 verify exact rejected attachment, absent catalog
and audit entries, usable independent physical connections and a subsequent real
managed DDL callback. They do not claim execution of an unavailable login handler.
The access-method rewrite case verifies PostgreSQL 13/14 syntax rejection leaves
the table, access method and file identity unchanged, then exercises a supported
rewrite and its exact callback metadata. Supported-server assertions remain.
Public array, range, JSON and event-trigger guides describe these boundaries.

On Linux x64/PostgreSQL 13.23, all **237 numeric/array/range/JSON cases pass with
zero failures/skips** in 2m16.047s; all **42 event cases pass with zero
failures/skips** in 1m46.477s. The combined six-class scope passes all **279
cases with zero failures/skips** on PostgreSQL 14.20 in 2m06.806s and PostgreSQL
15.19 in 2m21.970s.

The refreshed complete PostgreSQL 13.23 run passes **8,271 tests, zero failures
and six Windows-only skips, 8,277 total**, in 11m42.791s (integration
11m41.184s). Its implementation, test and build sources match this milestone.
All 62 failures from the previous complete diagnostic are resolved. The earlier
GUC query stall does not recur: its function-settings case passes in 0.591s,
but the intermittent stall's cause remains unproven.

Final plain `dotnet test` on Linux x64/PostgreSQL 18.6 passes **8,271 tests,
zero failures and six Windows-only skips, 8,277 total**, in 12m53.136s
(integration 12m52.154s). Release passes with zero warnings/errors in 1m18.68s.
API freshness passes for 200 pages/2,437 members; the site builds 245 pages and
its check reports zero errors/warnings/hints. These overlapping cached local
runs are not cold-cache performance measurements. No cases are removed or
skipped and no analyzer standards are relaxed. Complete PostgreSQL 14 testing
is underway; the remaining PostgreSQL 13–19/platform matrix, intermittent GUC
stall investigation and other full-port requirements remain open.

Immediately before this commit, `9597c0c`
[CI 36411852707](https://github.com/willibrandon/ankus/actions/runs/36411852707)
has successful quality and all three runtime jobs; all three full platform
test jobs remain in progress without a reported failure.
[Docs 36411852844](https://github.com/willibrandon/ankus/actions/runs/36411852844)
passes. Superseded `e401d77` CI run `36409892102` is cancelled: quality/runtime
jobs passed and the three unfinished platform suites were cancelled. Cancelled
suites do not supply completed platform evidence. Outcomes are checked and
recorded again immediately before pushing.
