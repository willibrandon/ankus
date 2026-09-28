# .NET compatibility and runtime servicing

The [public .NET support policy](../src/content/docs/reference/dotnet-support.md)
describes the extension-author contract. This document records the implementation
plan and evidence required to make that contract supportable. .NET 11 work is
pending; it is not evidence of current compatibility.

## Basis for the policy

Microsoft's [library targeting guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/cross-platform-targeting)
distinguishes broad managed-library compatibility from libraries using modern
APIs and Native AOT. Ankus belongs to the latter category. Do not add old TFMs
or .NET Standard solely to increase the apparent support range.

[EF Core](https://learn.microsoft.com/en-us/ef/core/what-is-new/) aligns servicing
with .NET lifecycles while choosing its target framework separately: EF Core 9
targets .NET 8, whereas EF Core 10 targets .NET 10.
[Npgsql 10](https://www.npgsql.org/doc/release-notes/10.0.html) drops the already
unsupported .NET 6 target to reduce compatibility code. These examples support
explicit target and lifecycle decisions, not a rule that every package must
target every .NET release. Ankus additionally owns a native runtime fork, so
managed assembly compatibility cannot establish its support matrix.

Keep ordinary managed packages on the lowest supported target their APIs need.
Add multi-targeting where framework-specific APIs, dependencies, analyzers or
validation require it. Independently map every supported extension TFM to its
own validated Native AOT compiler and runtime identity. A `net10.0` assembly being
referenceable from `net11.0` does not validate that mapping.

## Current state and immediate requirements

The repository and generated projects select SDK 10.0.400 or a later stable
10.0 feature band/patch through `latestFeature`; previews are excluded. CI
explicitly installs 10.0.400. This selection must remain separate from the
runtime included in published extensions.

`eng/Ankus.Ci.cs` currently pins upstream `v10.0.11`, fork commit
`134b853ff766627327405b2fb1f5c0d74266e4b6`, runtime package
`10.0.11-ankus.4`, and ILCompiler `10.0.11`. The SDK target and per-RID runtime
package properties carry matching identities. Microsoft lists 10.0.12 as the
current patch at this review. Updating the installed SDK alone does not close
that servicing gap. Rebase and validate the fork on the current upstream patch
before publishing `0.1.0`; recheck for newer releases before publication.

The SDK checks compiler/package/RID agreement, but does not yet provide a
supported-TFM table or an early diagnostic for every unsupported target.
Implement that validation before introducing a second runtime line. Until
then, only the documented `net10.0` path is a supported development target.

## Adding .NET 11

1. Use an isolated writable runtime checkout based on the selected .NET 11
   release tag/commit. Keep reference clones read-only. Review each existing
   patch against upstream changes; remove a local change only when upstream
   provides the required behavior and its tests pass.
2. Build matching runtime payloads for Linux x64, Windows x64, macOS ARM64 and
   macOS x64. Record the upstream base, fork commit, patch revision, ILCompiler
   version, target framework and RID. Never combine a .NET 10 payload with an
   .NET 11 compiler or let an unknown target fall back to the .NET 10 payload.
3. Extend the SDK's existing selection and identity checks for `net10.0` and
   `net11.0`. Audit framework references, runtime packs, compiler packages,
   generated binding companions and cache identities together. A changed runtime
   or compiler must invalidate affected native outputs.
4. Validate SDK 11 building `net10.0` separately from SDK 11 building `net11.0`.
   Exercise packed consumers, the installed tool, source generators, templates,
   native MTP tests and analyzer enforcement. Keep stable .NET 10 SDK builds
   working. Do not infer that a newer managed host is a replacement for the
   runtime required by the command-line tool or test host.
5. Run the complete unsharded suite against real PostgreSQL on every declared
   platform, including release-only macOS x64, and complete the required
   PostgreSQL major matrix. Pay particular attention to Unix preload/fork,
   Windows worker startup, GC/thread repair, signal handling, native error
   unwinding, cleanup and same-session recovery. Reusing the patch cleanly or
   passing a smoke test is insufficient.
6. Publish compatibility results and update templates/defaults only after the
   gate passes on the GA release. Until then, preview/RC checks are explicit
   experiments. If .NET 11 reaches GA before `0.1.0`, complete this gate as part
   of the initial release; an unreleased .NET version need not block it.

Start with explicit compatibility runs using existing automation. Add a small
scheduled/manual preview lane if useful; it must record failures without
weakening stable checks. Stable runtime/compiler changes require the affected
full platform suites. A newly supported .NET major becomes part of the required
CI/release matrix. Reuse caches, run independent jobs in parallel, retain the
60-minute per-job limit and measure durations; do not shard or substitute smoke
tests for platform evidence.

## Servicing a supported runtime line

Review upstream servicing and security releases monthly and when an out-of-band
fix is announced. Track the latest upstream patch, the embedded patch and any
gap for each supported major. Prioritize security fixes; do not claim that a
custom payload receives Microsoft's automatic runtime updates.

For each update, rebase the minimal patch, rebuild the native payloads and update
the pinned compiler/runtime identities together. Use immutable package versions
and new cache identities. Review the upstream changes and record exact platform,
PostgreSQL and package validation. Release notes must identify the embedded
runtime and tell extension authors to rebuild and redeploy. No fixed turnaround
SLA is promised; an unresolved security or compatibility blocker must be visible.

Retain each declared .NET major through its upstream end of support in at least
one supported Ankus release line. Prefer a shared codebase and create a servicing
branch only when necessary. Do not maintain every historical Ankus minor: users
must take the latest servicing update on their supported line. .NET support
removal is announced in release notes and occurs in a breaking release (a minor
release while Ankus is pre-1.0, a major release afterward), with an upgrade path.
If the newest line raises its minimum before an older .NET major reaches end of
support, its previous Ankus line still needs servicing through that date.

Keep reducing the fork where upstream improvements permit it. Removing the fork
requires the same PostgreSQL behavior evidence as introducing it; it is not a
prerequisite for supporting .NET 11.
