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
| `unit-test` | Build and run the five unit test modules. |
| `release-managed` | Pack the managed NuGet packages. |
| `release-runtime` | Build and pack one platform runtime package. |
| `publish` | Validate and publish the complete NuGet package set. |

Use `--` before command arguments:

```text
dotnet run --file ./eng/Ankus.Ci.cs -- metadata
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

The command writes `native-header-types.c`, the compiler's
`native-header-types.ast.json`, reconstructed `native-header-checks.c`, compiler
output in `native-header-checks.txt`, and a normalized `native-header-types.json`.
The AST is a development artifact containing local header paths; the normalized
contract excludes source paths and compiler pointer identities. Its target records
the exact PostgreSQL version, runtime identifier, pointer width, byte order and
Clang major. The type graph retains native typedefs and anonymous records/enums,
qualifiers at each pointer level, fixed/incomplete arrays, both written and
adjusted parameter types, callbacks, variadic/prototype distinctions and no-return
metadata. Functions also retain parameter names and linkage; globals retain
their declared types and thread-local status.

Clang compiles the reconstructed declarations against the same headers before
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

These declarations provide native fields, enums, embedded values, inline arrays
and explicit flexible-tail access. Checked node ownership/casting/formatting APIs
are available through the runtime; typed pointer/callback fields remain port work.
