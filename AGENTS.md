# Working on Ankus

Ankus is a faithful port of pgrx to idiomatic .NET Native AOT. The full scope and
verified evidence live in `PROGRESS.md`; do not treat a passing subset as full
parity. Read `README.md`, the relevant progress entries, and related public docs
before changing an area.

## Reference implementations

- `pgrx` is the read-only source for pgrx APIs, examples, and tests.
- `postgres` is the read-only source for PostgreSQL ABI and backend behavior.
- Other available reference repositories include `runtime`, `roslyn`, `sdk`,
  `msbuild`, `NuGet.Client`, `docs`, `dotnet-api-docs`, and `dotsider`.
  Locate available local checkouts before requesting another clone; treat all
  reference repositories as read-only.
- Use the supported PostgreSQL version headers when emitting C; do not assume
  that internal layouts or APIs are identical across versions.

## Implementation conventions

- Prefer ordinary C# APIs, attributes, source generation, NuGet project SDKs,
  `dotnet publish`, and `dotnet test` while preserving PostgreSQL semantics.
- Keep Native AOT consumers free of runtime code generation and unbounded reflection.
- PostgreSQL ERROR/longjmp must never cross a managed frame. Guard backend calls
  in native code, transport owned diagnostics, and raise errors only after managed
  frames have unwound. Match allocators and test cleanup and backend recovery.
- Preserve exact datum values, SQL NULL, type identity, shape, encoding, and
  lifetime contracts. Reject lossy conversions explicitly.
- Follow the `runtime` and `msbuild` type-style convention: use explicit local
  types when the right-hand side does not name the type. `var` is permitted, not
  required, for clear constructors and casts. `.editorconfig` enforces the
  built-in and non-apparent cases through IDE0008; do not enable IDE0007 globally.
- Document public and internal declarations with XML comments. Put XML summary
  opening tags, text, and closing tags on separate lines. Follow the analyzers;
  generic types must not expose static members (CA1000).

## Verification

- Follow pgrx's approach: test values and helpers directly, test generated
  contracts and diagnostics, and execute the published Native AOT extension in
  PostgreSQL to prove backend behavior.
- Tests use the pinned MSTest SDK and native Microsoft.Testing.Platform mode in
  `global.json`. Run the narrow affected scope during development, then plain
  `dotnet test` for the completed change. See `docs/contributing/development.md`
  for prerequisites and fixture behavior.
- Verify observable boundaries, errors, ownership, and same-session recovery;
  test counts and generated-source substrings alone do not prove parity.
- Run a Release build and relevant documentation checks before committing.
  Record exactly which PostgreSQL versions and platforms were actually tested.

## Progress, documentation, and commits

- Update `PROGRESS.md` with implementation, test evidence, and remaining scope
  as work progresses. Keep unresolved full-port requirements visible.
- Update related public guides in `docs/src/content/docs/` and the README when
  appropriate. Generate API pages from source XML comments using
  `dotnet run --project src/Ankus.DocGenerator -c Release`; do not hand-edit them.
- Validate the site with `pnpm build` and `pnpm check` in `docs/`, and check API
  freshness as documented in `docs/contributing/api-reference.md`.
- Commit coherent verified milestones along the way. Preserve unrelated user
  changes. The user has requested local commits and no GitHub publication until
  the faithful port and its required validation are complete.
