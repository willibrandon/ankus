# Engineering apps

Ankus repository automation uses .NET 10 file-based apps. Run an app from the
repository root with `dotnet run --file`.

| App | Purpose |
| --- | --- |
| `Ankus.Ci.cs` | Validate, build, test, pack, and publish Ankus and its pinned Native AOT runtime. |
| `Ankus.SqlStates.cs` | Regenerate or check the named SQLSTATE catalog from pinned PostgreSQL source tags. |
| `Ankus.Oids.cs` | Regenerate or check the version-aware built-in OID catalog from pinned pgrx sources. |
| `Ankus.Bindings.cs` | Regenerate or check per-major native declarations and node cast graphs from pinned pgrx bindings. |

`Ankus.Ci.cs` provides these commands:

| Command | Purpose |
| --- | --- |
| `metadata` | Validate and export the pinned runtime identity. |
| `release-metadata` | Validate a release tag and export release metadata. |
| `quality` | Build Ankus and validate generated API and site documentation. |
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
rules for PostgreSQL 13–19. They contain no assumed platform layouts; native
sizes and offsets must be established using the selected server headers.
The app invokes the `Ankus.Build binding-catalogs` command; parsing and catalog
generation stay inside the build tool without exposing its internals.
