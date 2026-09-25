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
with a hard 15-minute timeout per job; cold-cache behavior must be measured too.
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

User-defined range subtype registration, multiranges and range-specific JSON converters remain pending.

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
| Registry failure, native allocator ERROR/NULL, adoption retry and record cleanup | `RegistryExhaustionPrecedesBumpStorageAndPreservesLivePayload`, `SlabAllocatorErrorReleasesUnpublishedReservationAndAllowsRetry`, `NoOomNullReleasesUnpublishedReservationAndAllowsRetry`, `FailedAdoptionRetainsRawOwnershipUntilSuccessfulRetry` |

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
| `#[pg_guard]` | automatic at export boundary and guarded native API calls | Partial: export/datum boundaries and SPI execution |
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
| Type mapping (`FromDatum`/`IntoDatum`) | Typed converters and explicit raw PostgreSQL values | Built-ins, declared enum/custom-codec mappings, raw PgDatum bindings and reusable PgDatumType scalar/vector/shaped-array readers/writers are implemented for documented callback, parameter, raw-read and typed scalar-result paths. Nested/generic mapping forms, ordinary row/composite conversions, unsafe native-address typed results, broader metadata forms and complete matrix validation remain required. |
| `Spi` | typed commands/results, sessions, prepared statements, cursors, tuple access | Partial: atomic commands, scoped sessions/plans, typed results, cursors, row edits, quoting and JSON EXPLAIN |
| `PgError` | `PgException` + logging helpers | Owned diagnostics, context, objects, positions/location; `PgLog` severities and structured reporting |
| `pgrx::guc` | `[PgGucInt/Real/String/Bool/Enum]` (registered in `_PG_init`) | ☐ |
| `background_worker` | `BackgroundWorker` registration (C# `void(Datum)` via function pointer) | ☐ |
| `palloc`/`MemoryContextManager`, `PgBox`, `PBox` | `PgMemoryContext`, `PgAllocation`, `PgMemoryCallback`, `PgNativeBox<T>`, `PgContextValue<T>`, `PgNativeReference<T>` | Checked contexts, virtual context parameters, typed/aligned allocation, sized native ownership and borrowed references, exact copies, raw transfer, transient sizing, borrowed Slab/Generation/Bump and controlled native failure witnesses, cancellable cleanup and actual huge-size allocation/resize implemented; datum/node APIs and full version/platform requirements listed above |
| `pgrx::rel` (`PgRelation`) | `PgRelation`, `PgIndex` | ☐ |
| `iter`, `pg_sys` tuple-store APIs | generated native materialization with spill and bounded row storage | Set results implemented; standalone tuple-store API pending |
| `callbacks` (transaction/subtransaction callbacks) | `PgTransaction` outer/subtransaction registration with cancellable receipts | Partial: all event mappings, typed subtransaction IDs and callback lifetimes implemented; commit/abort/savepoint behavior verified on PostgreSQL 18.6/Linux x64; two-phase, parallel-worker and matrix execution pending |
| `pg_catalog`, `PgOid`, built-in OIDs | catalog and type/function lookup APIs | ☐ |
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
| `new` | Generate an ordinary extension project, control/configuration defaults, functions, and discoverable backend tests | Ordinary solution scaffold implemented with package-based SDK, CPM, managed/native MSTest cases, explicit names/output, and existing-file preservation. Background-worker template awaits worker API |
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
| `pg_extern` / `pgrx` | Names, schemas, overloads, strictness, defaults, named arguments, variadics, polymorphic/raw inputs and results | Synchronous supported types, SETOF/TABLE, names, fixed schemas, overloads, strictness, named/defaulted arguments, variadics, polymorphic signatures, explicit raw/internal bindings and reusable mapped scalar/array callback slots implemented. Nested/generic and broader mapping forms and the full platform/version matrix remain required. |
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
| `pg_guard`, `initialize`, module magic | Guarded callbacks, bootstrap, panic/exception boundaries, module name/version and ABI checks | Partial: function exports, native guards, module magic, backend and shared-preload `[PgInitialize]` with retry/recursion handling; Linux x64 fork behavior verified, remaining platform/version matrix required |
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
| `datum/{json,uuid,inet,geo,range}.rs` | JSON/JSONB, UUID, network, geometric and range datums with their operations | Partial: UUID, owned JSON/JSONB, inet/cidr, checked .NET network mappings, seven geometric datums, owned vertex collections and six typed range families/operations implemented; dedicated geometric operation wrappers, custom range subtypes and multiranges pending |
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
| `list.rs`, `list/`, `stringinfo.rs` | PostgreSQL lists and string/binary buffer operations with native ownership | Pending |
| `rel.rs`, `itemptr.rs`, `pg_catalog/`, `namespace.rs`, `wrappers.rs` | Relation/index access and locks, tuple locations, function/type catalog lookups, namespaces and type resolution | Pending |
| `xid.rs` | Transaction identifier wrappers and conversions | Implemented: distinct `PgTransactionId`/xid scalar and array datum contracts, pgrx-compatible invalid-to-NULL output, wrap-aware full-ID expansion and typed callback-only `PgSubtransactionId`; PostgreSQL 18.6/Linux x64 executed, PG13–19 headers source-reviewed, remaining matrix pending |
| `callbacks.rs` | Transaction/subtransaction callbacks, unregister and error cleanup | Partial: all event mappings, one-shot/repeating lifetimes, cancellation, nested dispatch and guarded errors implemented; two-phase, parallel-worker and matrix execution pending |
| `guc.rs`, `PostgresGucEnum`, `pg_guc_hook` | Bool/int/real/string/enum settings, contexts/flags/bounds, hidden/named enum entries, check/assign/show hooks and structured errors | Partial: native-backed typed declarations, hooks/extra, prefixes/logging, source/privilege/transaction/reload semantics, actual worker propagation, bounded lifetime measurements, cold package consumers and managed preload verified above. Raw-placeholder treatment, mixed-encoding preload and the full matrix remain required |
| `bgworkers.rs` | Static/dynamic workers, startup/restart/shutdown, handles, signals/latches and backend connections | Pending |
| `shmem.rs`, `atomics.rs`, `lwlock.rs`, `spinlock.rs` | Shared memory registration, synchronization, atomics, lock lifecycle and preload initialization | Pending |
| `nodes.rs`, `pgrx-pg-sys/src/node.rs` | Node tags/type checks, allocation, conversion/string output, planner/executor node access | Pending |
| `pg_sys` hooks and `pgrx-examples/hooks` | Planner/executor, utility, parse, authentication and other exposed hooks; chaining and version-specific callback signatures | Pending |
| `pg_sys` custom scan structures/functions | Provider registration, paths/plans/states, executor lifecycle and supporting node/tuple APIs | Pending |
| `ffi.rs`, `pg_sys.rs`, `pgrx-pg-sys/src/submodules/{ffi,panic,pg_try,thread_check}.rs` | Native call guards, nested recovery, thread affinity, interrupts, deterministic managed cleanup | Partial: function and SPI boundaries; general-purpose guarded APIs pending |
| `pgrx-pg-sys/src/submodules/{elog,errcodes,panic,ffi,pg_try}.rs` | All log levels and SQLSTATE values; full diagnostics/context/object/location fields; catch/filter/rethrow behavior | Partial: all pgrx log levels, owned diagnostics, managed catch/filter/rethrow and unwind; named SQLSTATE catalog pending |
| `pgrx-pg-sys/src/{include,include.rs,cshim.rs,libpq.rs,port.rs,cstr.rs}` | PG13–19 functions, globals, constants, structs, unions, callbacks, inline/macro shims and string utilities | Pending: full raw API; only targeted generated native calls exist |
| `pgrx-pg-sys/src/submodules/{datum,oids,transaction_id,htup,tupdesc,utils,cmp,sql_translatable}.rs` | Built-in OIDs, raw datum/tuple access, identifier helpers, comparison and SQL type metadata | Partial: selected scalar OID mappings |
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

The `samples/Ankus.Examples.Hello`, `samples/Ankus.Examples.Enums`, `samples/Ankus.Examples.Operators`, `samples/Ankus.Examples.Sets` and `samples/Ankus.Examples.Composites`
samples are validated. Full example parity is pending.

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
    - [ ] Nested/generic mappings, ordinary row/composite conversions, broader mapping metadata and additional serialization/native shapes
  - [ ] `.ankusc` metadata section (JSON) embedded in the `.so`; `ankus schema`
- [ ] **P3 — Extension features**
  - [x] custom installation SQL, binary/prefix operators and explicit/assignment/implicit casts
  - [x] row and statement triggers for supported tuple types
  - [x] event triggers with owned DDL/drop/rewrite metadata and login callbacks
  - [x] aggregates for supported concrete and polymorphic types, owned managed states, worker transport, moving windows and native ordering
  - [x] general raw aggregate signatures with checked type identity and state ownership
  - [x] strongly typed custom base-type aggregate signatures
  - [ ] heterogeneous ordered-set VARIADIC ANY
  - [x] generated equality/order/hash operator classes for declared types/enums with independent family SQL controls (manual mappings remain)
  - [x] enum declarations, label/catalog helpers, nullable/scalar/array conversions and SQL dependencies
  - [x] owned named/anonymous composites, descriptors, nested arrays, SETOF/TABLE and SPI bindings
  - [x] generated custom base types with explicit storage/text codecs and binary send/receive
  - [x] custom SQL text with generated CBOR storage and optional NULL-input errors
  - [x] packed native custom-type payloads with custom SQL text and copied managed transport
  - [x] checked native PgVarlena borrowing, copy-on-write, cloning and explicit datum transfer for packed layouts
  - [ ] Complete default CBOR/JSON custom-type serialization (concrete contracts and tagged variants implemented; additional shapes remain), broader native layouts and remaining borrowed storage APIs
  - [x] Typed GUCs/hooks/extras, prefixes/logging, source/privilege/worker/lifetime/package witnesses on PostgreSQL 18.6/Linux x64
  - [ ] Remaining GUC raw/preload parity and complete version/platform validation; background workers
- [ ] **P4 — Tooling** (`ankus` dotnet tool)
  - [x] Packable `Ankus.Tool`, top-level entry point, System.CommandLine 2.0.12
  - [x] `init`, `info`, `build`, `publish`, and `install` commands, registered installations and explicit overrides
  - [x] `new` creates version-matched extension/MSTest solutions with CPM and discoverable native tests
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
     - [ ] Public hosting and canonical site URL/sitemap
  - [ ] `samples/` mirroring pgrx-examples (aggs, gucs, triggers, bgworker, customscan…)
    - [x] README and verified datum-boundary design notes (`docs/contributing/native-boundary.md`)
    - [ ] Complete getting-started, API, deployment, and ported-feature documentation
    - [x] Generated public API reference from XML comments, following the `Dotsider.DocGenerator` design
- [ ] **P7 — Custom scan + nodes**
   - [ ] Full custom scan provider API, native callbacks, and lifecycle integration
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
  builds ran sequentially. Hosted validation for this correction is pending.
  Private malformed request guards receive source review only; no forwarding
  shim, empirical mutation or coverage percentage is claimed. Raw composite
  layout provenance and the remaining full-port requirements remain open.
