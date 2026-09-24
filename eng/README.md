# Engineering apps

Ankus repository automation uses .NET 10 file-based apps. Run an app from the
repository root with `dotnet run --file`.

| App | Purpose |
| --- | --- |
| `Ankus.Ci.cs` | Validate, build, test, pack, and publish Ankus and its pinned Native AOT runtime. |

`Ankus.Ci.cs` provides these commands:

| Command | Purpose |
| --- | --- |
| `metadata` | Validate and export the pinned runtime identity. |
| `release-metadata` | Validate a release tag and export release metadata. |
| `quality` | Build Ankus and validate generated API and site documentation. |
| `runtime-build` | Build and stage one runtime for CI. |
| `runtime-pack` | Pack a staged runtime for CI. |
| `runtime-test` | Use a staged runtime to run PostgreSQL tests, including the four integration shards used by CI. Shard 1 also runs the unit tests. |
| `unit-test` | Build and run the four unit test modules. |
| `release-managed` | Pack the managed NuGet packages. |
| `release-runtime` | Build and pack one platform runtime package. |
| `publish` | Validate and publish the complete NuGet package set. |

Use `--` before command arguments:

```text
dotnet run --file ./eng/Ankus.Ci.cs -- metadata
```

See the [.NET file-based app documentation](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps)
for SDK behavior.
