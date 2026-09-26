# Engineering apps

Ankus repository automation uses .NET 10 file-based apps. Run an app from the
repository root with `dotnet run --file`.

| App | Purpose |
| --- | --- |
| `Ankus.Ci.cs` | Validate, build, test, pack, and publish Ankus and its pinned Native AOT runtime. |
| `Ankus.SqlStates.cs` | Regenerate or check the named SQLSTATE catalog from pinned PostgreSQL source tags. |
| `Ankus.Oids.cs` | Regenerate or check the version-aware built-in OID catalog from pinned pgrx sources. |
| `Ankus.Bindings.cs` | Regenerate or check native declarations, node cast graphs, header manifests and attribution from pinned pgrx bindings. |

`Ankus.Ci.cs` provides these commands:

| Command | Purpose |
| --- | --- |
| `metadata` | Validate and export the pinned runtime identity. |
| `release-metadata` | Validate a release tag and export release metadata. |
| `quality` | Install PostgreSQL headers, build Ankus and validate generated API and site documentation. |
| `runtime-build` | Build and stage one runtime for CI. |
| `runtime-pack` | Pack a staged runtime for CI. |
| `runtime-test` | Use a staged runtime to run the complete unit and PostgreSQL integration test suites. |
| `header-frontend-check` | Check an explicit Clang executable for the declaration-only frontend required by header collection. |
| `unit-test` | Build and run the five unit test modules. |
| `release-managed` | Pack the managed NuGet packages. |
| `release-runtime` | Build and pack one platform runtime package. |
| `publish` | Validate and publish the complete NuGet package set. |

Use `--` before command arguments:

```text
dotnet run --file ./eng/Ankus.Ci.cs -- metadata
```

`runtime-test` installs and selects LLVM 20 on Linux and macOS, and selects the
Windows runner's LLVM installation. It prints the compiler version and verifies
`-skip-function-bodies` support before building or running tests. The platform
default Clang can be too old even when Native AOT compilation works. Check a local
compiler without installing or changing anything:

```text
dotnet run --file ./eng/Ankus.Ci.cs -- header-frontend-check /path/to/clang
```

See the [.NET file-based app documentation](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps)
for SDK behavior.

Regenerate SQLSTATE constants from an existing read-only PostgreSQL checkout:

```text
dotnet run --file ./eng/Ankus.SqlStates.cs -- /path/to/postgres
dotnet run --file ./eng/Ankus.SqlStates.cs -- /path/to/postgres --check
```

The app reads `src/backend/utils/errcodes.txt` at the pinned PostgreSQL 13–18 and
19 beta tags listed in its source. It makes no network requests or changes to
the reference checkout. The generated union retains native aliases and names
removed from newer server versions. Update the tag list when refreshing the
catalog, then regenerate the API documentation from its XML comments.

Regenerate built-in OID values and native names from an existing read-only pgrx checkout:

```text
dotnet run --file ./eng/Ankus.Oids.cs -- /path/to/pgrx
dotnet run --file ./eng/Ankus.Oids.cs -- /path/to/pgrx --check
```

The app reads PostgreSQL 13–19 catalogs at the pgrx commit pinned in its source.
It preserves per-version membership and native renames; one enum member represents
each numeric value. These catalogs follow pgrx's constant-name heuristic and are
not a list of every built-in database object. The app makes no network requests
and does not modify the reference checkout.

Regenerate the native declaration catalogs using the same read-only checkout:

```text
dotnet run --file ./eng/Ankus.Bindings.cs -- /path/to/pgrx
dotnet run --file ./eng/Ankus.Bindings.cs -- /path/to/pgrx --check
```

The catalogs retain fields, typedefs, enums and pgrx's node inheritance/alias
rules for PostgreSQL 13–19. Separate raw catalogs retain foreign function
signatures, global mutability, callback aliases, variadic arguments and original
linkage, including pgrx-specific C shims. The raw catalogs also retain unevaluated
reference constants; these include platform-dependent values and must not be
used as target measurements. Native sizes, offsets and target constants must
be established using the selected server headers.
The app invokes the `Ankus.Build binding-catalogs` command; parsing and catalog
generation stay inside the build tool without exposing its internals.
See the [catalog inventory](../src/Ankus.Build/Bindings/README.md) for the pinned
declaration counts and remaining runtime binding scope.

The same command refreshes each major's pgrx header include manifest and the
upstream license notice. These inputs are packaged with the build tool so an
installed SDK does not need a pgrx checkout to measure a server's native layouts.

For layout development, compile and run the selected header probe:

```text
dotnet run --project src/Ankus.Build -c Release -- binding-layouts 18 /path/to/pg_config artifacts/binding-layouts/pg18
```

The command writes `native-layout.c`, its executable, raw observations and
validated `native-layout.json`. It measures every node and its embedded value
dependencies, including anonymous unions, arrays and flexible tails, and checks
all node tags against the pinned major. Pointer and C long widths, plain-char
signedness, byte order, sizes, alignments and field offsets come from the selected
headers and compiler. Named enum widths, signedness and every constant are also
validated. The probe runs on the build host; it does not establish a
cross-compilation ABI. An optional fourth argument selects the C compiler; on
Windows a fifth argument supplies semicolon-separated native library directories.
A sixth argument selects the expected runtime identifier and a seventh passes
the compiler target triple. The actual compiler target must match the requested
runtime identifier.

To emit managed declarations and their companion project, use `binding-sources`
with the same arguments. The SDK invokes this command before C# compilation,
builds the generated `Ankus.NativeBindings.csproj`, and references its assembly.
The assembly name includes a hash of the measured declarations so consumers
built against the same contract share native type identity. Generated source
retains its timestamp when unchanged, and normal project clean removes the
companion artifacts. Compiler/header inputs are measured again on each build.

For raw-call development, put one catalog function name per line in a text file
and verify its native signature against an installed server:

```text
dotnet run --project src/Ankus.Build -c Release -- binding-signatures functions.txt 18 /path/to/pg_config artifacts/binding-signatures/pg18
```

The arguments after the function list match `binding-layouts`, including optional
compiler, Windows library directories, runtime identifier and target triple.
The command writes `native-signatures.c`, its executable, raw observations and
validated `native-signatures.json`. C11 generic-selection static assertions check complete prototypes;
the probe measures fixed parameter and result storage without calling backend
functions. Native typedef names remain intact so target headers determine widths.
Nested callback declarations, const-qualified pointers and arrays retain C
declarator precedence. pgrx shim linkage selects the corresponding header function,
without requiring a pgrx-specific export in the PostgreSQL server.

Unknown/duplicate names, incompatible prototypes, unavailable header declarations
and incomplete observations fail explicitly. The probe does not establish export
availability, a managed calling convention, pointer ownership or an error guard.
Some reference declarations still need target-derived type/prototype information,
including anonymous C typedefs, platform types and qualifiers lost in Rust. A PostgreSQL major match
alone does not prove that every installed-header function matches the catalog.
Variadic signatures retain only their fixed arguments; promoted call-site arguments
still require generation. Guarded managed calls and hook registration remain open.

To collect authoritative types from the selected headers, put catalog function
and/or global names in a text file, one per line:

```text
dotnet run --project src/Ankus.Build -c Release -- binding-header-types symbols.txt 18 /path/to/pg_config artifacts/binding-header-types/pg18
```

This developer command requires `clang` on Linux/macOS or `clang-cl.exe` on
Windows. Optional arguments select the Clang executable, Windows library
directories, expected runtime identifier and compiler target triple, in that
order. Windows uses the Visual Studio and SDK include directories. Collection
uses a frontend only, without linking or running target code; matching headers,
compiler includes and target configuration are still required.

Header collection requires Clang 20 or later with the declaration-only
`-skip-function-bodies` frontend option. Select that compiler explicitly or put
it on `PATH`; an older platform-default Clang does not support this command.

The command writes `native-header-types.c`, the compiler's
`native-header-types.ast.json`, reconstructed `native-header-checks.c`, compiler
output in `native-header-checks.txt`, and a normalized `native-header-types.json`.
The AST is a development artifact containing local header paths; the normalized
contract excludes source paths and compiler pointer identities. Its target records
the exact PostgreSQL version, runtime identifier, pointer width, byte order and
Clang major. Its numeric model records plain-char signedness, `wchar_t` size and
signedness, floating radix, and the precision and exponent limits of `float`,
`double` and `long double`. These compiler observations distinguish numeric
representations that have identical object byte sizes. The type graph retains
native typedefs and anonymous records/enums,
qualifiers at each pointer level, fixed/incomplete arrays, both written and
adjusted parameter types, callbacks, variadic/prototype distinctions and no-return
metadata. Functions also retain parameter names and linkage; globals retain
their declared types and thread-local status.
Records and enums retain whether a complete declaration is available; implicit
compiler tags without an exposed declaration keep that state unknown.

Clang inspects declarations and constant expressions without compiling inline
implementation bodies, which can depend on the server's original compiler dialect.
Warnings remain errors. It checks reconstructed declarations against the same headers before
the command writes the final contract. Unsupported type kinds/calling conventions,
invalid observations, target mismatches and compiler errors fail explicitly.
Existing final contracts survive failures before that final write; intermediate
diagnostic files may be replaced. Unlike the reference-prototype probe, collection
can retain minor-release prototype changes, native volatile qualifiers and
anonymous typedefs directly from the selected installation.

Header types do not establish native record layouts, scalar widths, exported
symbol availability, managed calling conventions, pointer ownership or backend
error guards. Integrating these facts with layout measurement, managed call
generation, global access and hook registration remains port work. The consumer
SDK's existing node layout generation is unchanged.

No-return metadata reports an annotation in the selected declaration or type.
It is not inferred from a function's name or implementation. For example,
PostgreSQL 17's MSVC headers omit the `proc_exit` annotation, while PostgreSQL 18
declares it with C11 `_Noreturn`. An absent annotation does not prove that the
function returns.

To measure storage from those authoritative types, run:

```text
dotnet run --project src/Ankus.Build -c Release -- binding-storage symbols.txt 18 /path/to/pg_config artifacts/binding-storage/pg18
```

This command accepts the same optional Clang/toolchain arguments as
`binding-header-types`, collects the semantic contract, then evaluates constants in
`native-storage.c` with the same frontend. It does not execute a target program.
Unevaluated prototype checks and storage expressions do not call PostgreSQL
functions or require backend exports. The observed PostgreSQL version, runtime,
pointer width, byte order, Clang major and numeric model must exactly match the
collected contract.

`native-storage.ast.json` retains the compiler output and `native-storage.txt`
contains the normalized observations. Its version-2 header carries every numeric
identity field; regenerate earlier observations instead of reusing them. The validated
`native-storage.json` retains the header contract and ordered measurements for
fixed parameters, non-void results and globals. Each value records byte size,
alignment, array element stride and integer/enum signedness where applicable.
Alignment can exceed a typedef's size. Incomplete arrays have a null total size
with measured alignment and stride. Opaque records/enums have null size and
alignment; they are not represented as zero-sized objects. A pointer to an opaque
type still has measured pointer storage. A non-void opaque result remains a
result entry with unknown storage, distinct from a void result.
Large selections use batches of 256 symbols with numbered C/AST diagnostic files;
every batch retains the same 512 MiB compiler-output limit and must independently
match the selected target before the final combined contract is written.

Missing, duplicate, extra, contradictory or malformed observations fail before
the final storage contract is written. Intermediate type/diagnostic artifacts may
be replaced. These measurements do not classify a managed aggregate ABI, supply
record fields, establish pointer ownership, promote variadic call-site arguments,
or implement guarded calls and hooks.

To collect the complete transitive record graph for selected symbols, run:

```text
dotnet run --project src/Ankus.Build -c Release -- binding-records symbols.txt 18 /path/to/pg_config artifacts/binding-records/pg18
```

It accepts the same optional compiler, Windows library directories, runtime and
target arguments as `binding-header-types`, followed by an optional absolute
`libclang` library path. Empty strings retain the defaults for earlier optional
arguments. Install the matching library with the compiler: `libclang-20-dev`
beside `clang-20` on Linux, Homebrew `llvm@20` on macOS, or the Windows LLVM
installer. Automatic discovery uses the selected compiler's resource directory,
so compiler shims retain their selected toolchain. A library from an incompatible
compiler build cannot read its serialized AST and fails explicitly.

The command first validates the semantic symbol contract, then saves
`native-header-records.ast` using exactly the same compiler driver, includes,
target and warning settings. A separate process loads that AST and collects
record metadata; it does not reinterpret the headers with different compiler
options. Cancellation terminates the worker and waits for its exit. The AST and
worker request/observation files are diagnostic artifacts that can contain local
paths. The final `native-records.json` couples the existing symbol signatures with
a validated graph whose identities are numeric indices, not native addresses or
source paths. A failed operation preserves the previous final record contract.

The graph retains declared and canonical types, typedef annotations, exact native
size/alignment, array/vector extents, function ABI shapes, struct/union/enum
identity, ordered physical fields, anonymous containers, bit offsets and widths,
flexible arrays and exact signed/unsigned enum values up to 64 bits. Unsupported
enum representations fail instead of truncating values. Anonymous typedefs do
not acquire fabricated C tag names. Padded vectors and over-aligned typedefs
retain their actual storage. Incomplete declarations have unknown storage;
pointers to them still have measured pointer storage. Callback ABI observations
and compiler-printed annotations accompany the richer existing signature
contracts; they do not replace written parameter and declaration metadata.

These facts do not yet classify managed aggregate calls, establish exports or
ownership, transport callbacks across error boundaries, generate promoted
variadic arguments, or implement raw globals and hooks. The consumer SDK's node
generation is unchanged; the complete PostgreSQL-major/platform matrix remains
required port work.

To generate checked native call bodies for a list of fixed-prototype functions:

```text
dotnet run --project src/Ankus.Build -c Release -- binding-call-sources symbols.txt 18 /path/to/pg_config artifacts/binding-calls/pg18
```

This accepts the same arguments and compiler/library prerequisites as
`binding-records`, followed by an optional native body compiler. Clang collects
the declarations; native body compilation defaults to MSVC on Windows, matching
the SDK, and to the selected Clang elsewhere. An explicit MSVC executable must
target the measured architecture and numeric model; generated checks reject a
mismatch, including changed char signedness or long-double precision. Both
compilation stages retain warnings as errors. It writes `native-calls.c` after
compiling the complete generated bodies, including their calls, against the
selected headers. A compiler
failure or cancellation preserves the previous final source. Declaration-only
inspection remains separate from complete-body validation.

Each `ankus_native_call_<symbol>` body accepts native argument addresses and exact
byte lengths, plus a result destination. It checks the complete envelope before
calling the function. Argument addresses must satisfy the measured native
alignment and hold valid C object representations with live referenced storage.
Pointer values remain borrowed, including null pointers where the underlying
native function permits them. The C compiler performs the actual call with its
native scalar/aggregate ABI; there is no managed aggregate calling-convention
guess. Result bytes are copied only after the native function returns. Empty
records retain the selected compiler's size, including zero where applicable.

These bodies are an internal prerequisite for guarded raw bindings. They must
execute beneath the native PostgreSQL error guard on the backend thread, with
owned diagnostic transport and managed unwinding outside that guard. They are
not directly callable managed imports. Callback addresses here refer to native
callbacks; generated managed callback guards and lifetimes remain required.
Variadic and unprototyped calls need explicit call-site type/promotion handling;
globals are not function calls. Those selections and incomplete by-value storage
fail explicitly. Export discovery, guard/consumer integration and the complete
PostgreSQL-major/platform matrix remain required port work.

These declarations provide native fields, enums, embedded values, inline arrays
and explicit flexible-tail access. Checked node ownership/casting/formatting APIs
are available through the runtime; typed pointer/callback fields remain port work.
