# API reference generation

The generator uses DocFX metadata from the compiled assemblies and XML comments,
then converts it to Starlight Markdown. The pipeline follows Dotsider's generator.

From the repository root:

```console
dotnet run --project src/Ankus.DocGenerator -c Release
```

The input assemblies are `Ankus.Runtime`, `Ankus.PgConfig`, and `Ankus.Testing`.
DocFX's public API filter applies, with `EditorBrowsable(Never)` types excluded
so generated interop contracts do not appear in the author reference.

Pages go to `docs/src/content/docs/api/generated/`. Edit XML comments in the C#
source and regenerate. The handwritten API index lives outside that directory.
The generator only replaces or removes files bearing its marker and leaves
unchanged files untouched.

Check that the reference is current without writing pages:

```console
dotnet run --project src/Ankus.DocGenerator -c Release -- --check
```

`pnpm build` in `docs/` regenerates the API reference before building the site.
Generation includes signatures, generic parameters, returns, exceptions, remarks,
examples, and cross-reference links. Overloads receive distinct, stable anchors.
Intermediate metadata is written beneath `artifacts/api-metadata/` and removed
after generation.

C# token colors are configured in `docs/astro.config.mjs` for both site themes.
`docs/src/syntax/grammars.mjs` supplies shared corrections for typed C# `using`
declarations and PostgreSQL's `SHOW` command. It extends the pinned Shiki
grammars without changing their handling of namespace imports, strings or comments.
After changing syntax highlighting, run `pnpm exec astro build --force` from
`docs/` to invalidate Astro's cached Markdown rendering.
