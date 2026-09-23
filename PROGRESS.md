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
  pgrx's supported versions. Only PostgreSQL 18 on Linux x64 has been exercised so far.
- **Developer experience**: ordinary .NET projects, source generators, `dotnet publish`, and `dotnet test`,
  with development tooling corresponding to `cargo pgrx`.

## Environment

| Item | Value |
|---|---|
| .NET SDK | 10.0.400; `global.json` uses `rollForward: latestMajor` |
| C toolchain | clang 21 + lld; GCC 14 used for the local PostgreSQL build |
| Primary test target | **PostgreSQL 18** |

## Current verified milestone

The latest milestone adds checked PostgreSQL memory contexts and palloc allocations, native reset
invalidation, callback-independent handle identity, and guarded memory access across generated
callbacks. It adds 44 runtime, 16 generator, and 20 backend cases. Plain `dotnet test` passes
3638 cases on PostgreSQL 18.6/Linux x64. Exact reset, encoding, ownership, error-recovery and
transaction/iterator evidence is mapped below. Full memory/GUC/preload parity, the remaining port
inventory, and the platform/version matrix remain incomplete.

The non-incremental Release build has zero warnings/errors. XML documentation, source style,
documentation build/type checks, and generated API freshness checks pass.

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
- **`dotnet test`**: **3638 passed, 0 failed, 0 skipped** on Linux x64 with PostgreSQL 18.6.
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

Custom/disabled function SQL templates, declared custom-type providers, future type-family edges, and standalone
schema extraction remain required work. These tests establish the implemented installation graph, not full parity
with every pgrx SQL-entity feature.

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

The implementation follows `/home/brandon/src/pgrx/pgrx/src/aggregate.rs`, pgrx's aggregate examples
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
The forking postmaster is rejected natively before Native AOT can initialize runtime threads.

| Requirement | Concrete evidence |
|---|---|
| Init-only native publication and SQL/export separation | `InitializationOnlyExtensionCompilesWithNativeLoaderExports`, `InitializationOnlyExtensionLoadsWithoutSqlFunctions` |
| Declaration validation and deterministic mixed artifacts | `InvalidInitializationDeclarationsAreRejected`, `MultipleInitializationCallbacksAreRejected`, `InitializationRejectsSqlFunctionAndResultMetadata`, `InitializationRejectsAttributesThatPreventManagedInvocation`, `InitializationMixedDeclarationsPreserveSqlAndDeterministicManifest`, `InitializationCallbackSymbolsAreAssemblyScoped`, `InitializationIsAbsentUnlessExplicitlyDeclared` |
| Per-backend first-use/LOAD lifecycle | `InitializationRunsOncePerBackend` exercises two independent backends and repeated LOAD, both with explicit LOAD and first-function entry |
| Exceptions, owned long/Unicode diagnostics, finally and retry | `InitializationFailureUnwindsAndCanRetry` runs 50 managed, structured PostgreSQL, or recursive failures, followed by a successful 51st attempt with exact counter/diagnostic assertions |
| SQL rollback and managed state lifetime | `InitializationFailureRollsBackSqlAndPreservesCallerState` verifies savepoint rollback, earlier writes, successful retry, and initialized managed state after outer transaction rollback |
| Native error recovery and nested initialization | `InitializationSpiErrorsPreserveCallerState` catches division/recursive-load errors while preserving a prepared statement and transaction writes; `InitializationCanLoadAnotherExtension` proves nested Native AOT initialization and continued SPI |
| Startup snapshot and failure isolation | `SessionPreloadInitializesBeforeFirstFunction`, `SessionPreloadFailureUnwindsAndPreservesServer` verify preload before client SQL, finally logging before connection failure, a healthy existing backend, and a corrected new connection |
| Postmaster rejection before managed callback | `SharedPreloadRejectsManagedInitialization` verifies actionable native failure and absence of the sample's managed notice |
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
foundation; arbitrary managed postmaster initialization remains a required full-port item.

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
adopted placeholder value. Native-only declarations can run before postmaster fork; managed runtime
entry occurs in individual backends. A GUC-only library and separate check-only, assign-only and
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
| Small library publication and no managed postmaster entry | `GucOnlyLibraryLoadsAndRegisters`, `HooksOnlyLibraryRunsItsCheckInBackend`, `AssignOnlyLibraryPreservesOldValueAndRestoration`, `ShowOnlyLibraryReadsNativeStorageWithoutRecursion`, `SharedPreloadRejectsHooksWithoutManagedInitializer`, `SharedPreloadRejectsMetadataWithoutDatabaseEncoding` |
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
preload metadata/defaults/labels must currently be ASCII; hooks and managed initializers are rejected
before managed entry in a forking postmaster. PG18's native DisallowInFile flag blocks ALTER SYSTEM
but does not by itself reject manually supplied custom UserSet file values; tests and public docs
preserve this observed behavior.

Remaining full-port work is explicit: raw placeholder behavior,
arbitrary managed postmaster hooks, mixed-encoding shared-preload metadata, complete source
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

Remaining full-port scope includes raw placeholders, arbitrary managed postmaster callbacks,
mixed-encoding preload metadata, remaining source/privilege combinations, worker propagation,
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

Remaining full-port work includes safe explicit treatment of raw placeholder storage, arbitrary
managed postmaster callbacks, mixed-encoding shared-preload metadata, the rest of the pgrx runtime
and tooling inventory, and actual PostgreSQL 13–19 beta validation on Windows/Linux/macOS. The native
CUSTOM_PLACEHOLDER flag assumes PostgreSQL-owned string-placeholder layout and lifetime; exposing it
on arbitrary typed declarations would violate those assumptions. Existing typed configuration
options continue to reject that internal flag. This milestone does not establish full GUC or pgrx parity.

### Work in progress

Initialization/GUC research identified a Native AOT hosting constraint: managed entry starts runtime
threads, and the initialized runtime cannot safely survive the postmaster's fork into a backend.
Reference evidence is in the read-only `runtime` v10.0.0 bootstrap/thread/finalizer sources and PostgreSQL
`dfmgr.c`/GUC sources, cross-checked at supported release tags. Backend initialization now has the
native postmaster guard and focused evidence above. Native-only declarative GUC registration now runs before managed startup;
arbitrary managed postmaster hooks require additional architecture and remain a full-parity requirement.
No hook is silently skipped or represented as implemented by this initialization foundation.

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

### Local read-only reference repos (absolute paths)

- **pgrx** → `/home/brandon/src/pgrx` — the port's reference implementation. Key paths:
  - `/home/brandon/src/pgrx/pgrx/src/` — runtime modules (`spi.rs`, `datum/`,
    `guc.rs`, `memcx.rs`, `trigger_support/`, `iter.rs`, `bgworkers.rs`, …)
  - `/home/brandon/src/pgrx/pgrx-macros/src/lib.rs` — the proc macros (`pg_extern`, `pg_trigger`, `pg_aggregate`, …)
  - `/home/brandon/src/pgrx/cargo-pgrx/` — CLI model to mirror (`new/init/build/schema/test/run/package`)
  - `/home/brandon/src/pgrx/pgrx-examples/` — example set to mirror in `samples/`
  - `/home/brandon/src/pgrx/pgrx-tests/`, `/home/brandon/src/pgrx/pgrx-unit-tests/` — test strategy reference
  - `/home/brandon/src/pgrx/v18-ONE-COMPILE-CHANGELOG.md` — one-compile `.pgrxsc` schema model (our `.ankusc` analogue)
- **postgres** → `/home/brandon/src/postgres` — ABI source of truth. Tags:
  `REL_13_23`…`REL_18_6`, `REL_19_BETA3`. Key files are `src/include/fmgr.h`,
  `src/backend/utils/fmgr/dfmgr.c`, `src/backend/utils/fmgr/fmgr.c`, and
  `src/include/utils/elog.h`.
- **runtime** → `/home/brandon/src/runtime` — .NET runtime source (Native AOT: `src/coreclr/nativeaot/`, PAL: `src/coreclr/pal/src/`).
- **roslyn** → `/home/brandon/src/roslyn` — compiler source (function-pointer grammar, source generators).
- **msbuild** → `/home/brandon/src/msbuild` — build conventions and type-style rules, compared with `runtime`.
- **sdk** → `/home/brandon/src/sdk` — .NET SDK and CLI conventions.

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

The complete memory port still requires managed reset/drop callback registrations and rooted-object
cleanup; transient context sizing, aligned/huge and generic typed allocation factories; explicit
raw adoption/ownership transfer and UTF-8 C-string helpers; `PgNativeBox`/node allocation; virtual
`MemCx` parameters and raw/custom-type datum integration; broader phase/cleanup/error witnesses;
and actual PostgreSQL 13–19 beta/Windows/Linux/macOS validation. These remain required work, together
with the full inventory below; this milestone does not establish complete memory or pgrx parity.

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
| `#[pg_aggregate]` + `Aggregate` trait | `[PgAggregate]`, typed static callbacks, `PgAggregateState<T>` and `PgAggregateContext` | Implemented for current concrete types; polymorphic/raw and custom base types remain required; PostgreSQL 18.6/Linux x64 evidence above |
| `#[pg_operator]` | `[PgOperator]`, backing function, planner options and SQL dependencies | Implemented for supported types; PostgreSQL 18.6/Linux x64 evidence above |
| `#[pg_cast]` | `[PgCast]`, three contexts, typmod/explicitness arguments and SQL dependencies | Implemented for supported types; PostgreSQL 18.6/Linux x64 evidence above |
| `extension_sql!` | `[assembly: PgSql]`, `[assembly: PgSqlFile]`, named graph dependencies | Inline/file SQL, ordering, bootstrap/final and relocation implemented; declared type-provider integration pending |
| `#[derive(PostgresType)]` (custom base types) | generated CBOR storage, JSON text I/O, custom storage/I/O, binary send/receive | ☐ |
| `composite_type!`, `PgHeapTuple` | `PgHeapTuple`, `PgTupleDescriptor`, and `[PgCompositeType]` | Owned dynamic tuples, arrays, sets and SPI implemented; validation below |
| `#[derive(PostgresEnum)]` | `[PgEnum]`/`[PgEnumLabel]`, generated DDL/mappings, scalar/array SPI and `PgEnums` catalog helpers | Implemented; PostgreSQL 18.6/Linux x64 evidence above |
| Type mapping (`FromDatum`/`IntoDatum`) | `Datum` converters for built-in and user-defined SQL types | Partial: scalars, text/bytea/UUID/JSON, nullable forms |
| `Spi` | typed commands/results, sessions, prepared statements, cursors, tuple access | Partial: atomic commands, scoped sessions/plans, typed results, cursors, row edits, quoting and JSON EXPLAIN |
| `PgError` | `PgException` + logging helpers | Owned diagnostics, context, objects, positions/location; `PgLog` severities and structured reporting |
| `pgrx::guc` | `[PgGucInt/Real/String/Bool/Enum]` (registered in `_PG_init`) | ☐ |
| `background_worker` | `BackgroundWorker` registration (C# `void(Datum)` via function pointer) | ☐ |
| `palloc`/`MemoryContextManager` | `PgMemoryContext`, `PgAllocation` | Checked context/byte allocation and callback binding implemented; remaining memory API and version/platform requirements listed above |
| `pgrx::rel` (`PgRelation`) | `PgRelation`, `PgIndex` | ☐ |
| `iter`, `pg_sys` tuple-store APIs | generated native materialization with spill and bounded row storage | Set results implemented; standalone tuple-store API pending |
| `callbacks` (transaction/subtransaction callbacks) | scoped callback registration and cleanup | ☐ |
| `pg_catalog`, `PgOid`, built-in OIDs | catalog and type/function lookup APIs | ☐ |
| `pg_sys::elog` and logging macros | PostgreSQL logging and full diagnostics | `PgLog` levels, filtering, diagnostics, managed unwind and native terminal reporting; PG18 Linux verified |
| `pgrx::pg_sys` (raw FFI) | versioned native bindings and guarded entry points | ☐ |
| `nodes`, `pg_sys` custom scan bindings | Custom scan providers, node types, callbacks, and supporting APIs | ☐ |
| `cargo pgrx` CLI | .NET tool and standard SDK commands; full command inventory below | ☐ |
| `cargo pgrx schema` (one-compile, `.pgrxsc`) | metadata-only schema generation and standalone extraction | Partial: build-time assembly metadata extraction |
| pgrx-examples | `samples/` mirroring the example set | ☐ |

## Repository-derived parity inventory

Reference: `/home/brandon/src/pgrx`, commit `70383e884582d1bcc7cd681d10886b995a2830cb`,
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
| `test` | Backend test discovery, filters, expected errors, configuration, rollback, and supported-major matrix | Partial: canonical `dotnet test`, scaffolded managed/backend tests, reusable publish/load fixture; attribute-generated backend tests, CLI forwarding and matrix pending |
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
| `pg_extern` / `pgrx` | Names, schemas, overloads, strictness, defaults, named arguments, variadics, polymorphic/raw inputs and results | Partial: synchronous supported scalar/array/enum types, SETOF/TABLE, names, fixed schemas, overloads, explicit/inferred strictness, named/defaulted arguments, variadics; polymorphic/raw types pending |
| Function options (`extern_args.rs`) | Create-or-replace, immutable/stable/volatile, security invoker/definer, parallel modes, cost, support functions, dependencies, search path | Implemented declaration options, existing planner support references and explicit named SQL/schema/function dependencies; future entity families pending |
| `pg_schema`, `search_path` | Schema declarations, qualification, nested declarations, lookup/search-path semantics | Implemented for functions and standalone schemas, including owned/existing schemas, named graph dependencies, per-call search paths and non-relocatable metadata; future type-family integration pending |
| `extension_sql!`, `extension_sql_file!` | Inline/file SQL, entity requirements, bootstrap/finalize positioning, declared created entities | Inline/file SQL, named requirements/before constraints, bootstrap/final, file-change invalidation and SQL-only native packages implemented; declared created-type providers pending |
| `pgrx(sql = ...)` | Custom/disabled SQL generation and SQL generation callbacks/equivalents | Pending |
| `default!`, `name!`, `composite_type!` | SQL default arguments, named table/aggregate fields, named composite type resolution | SQL argument names/defaults, TABLE fields and concrete aggregate inputs/direct arguments implemented; named composite resolution implemented |
| `SetOfIterator`, `TableIterator` | SETOF and TABLE results, nullability, tuple metadata, iteration cleanup on early exit/error | Implemented for supported scalar/array/enum columns, named tuples and explicit column overrides; streaming/materialized execution, interruption and owned resource cleanup validated on PG18/Linux |
| `pg_trigger` | Row/statement and before/after/instead-of triggers; event/argument metadata; OLD/NEW tuple access and modification | Implemented for supported tuple types, with guarded transition-table SPI; PostgreSQL 18.6/Linux x64 verified |
| `pg_aggregate`, `AggregateName` | Transition/final/combine/serialize/deserialize; moving/inverse states; ordered-set/hypothetical; initial states, sort and parallel options | Implemented for concrete supported types, including native ownership, worker transport, ICU/custom ordering and lifecycle recovery; polymorphic/raw/heterogeneous ANY and full matrix remain required |
| `pg_operator` and option attributes | Operator name, commutator, negator, selectivity/join support, hashes/merges, and schema dependencies | Implemented for supported types, including binary/prefix operators, separate graph IDs, exact references and declaration diagnostics; custom base-type operands and matrix validation remain required |
| `PostgresEq`, `PostgresOrd`, `PostgresHash` | Equality, order and hash functions, operator classes/families and index use | Pending |
| `pg_cast` | Explicit/assignment/implicit casts and generated SQL | Implemented for supported source/target types, including nullable values, arrays and optional typmod/explicit arguments; custom base-type families and matrix validation remain required |
| `pg_test`, `pg_bench` | Generated in-backend tests/benchmarks, discovery and expected-error metadata | Pending |
| `pg_guard`, `initialize`, module magic | Guarded callbacks, bootstrap, panic/exception boundaries, module name/version and ABI checks | Partial: function exports, native guards, module magic, backend `[PgInitialize]` with retry/recursion handling and session-preload snapshots, native-only GUC preload; arbitrary managed postmaster initialization remains required |
| SQL entity graph and metadata | Type/function/schema dependencies, cycle diagnostics, SQL translation hooks, section encoding/decoding, ELF/PE/Mach-O extraction | Partial: deterministic SQL/schema/enum/function/operator/cast graph with aliases, dependency diagnostics, bootstrap/final edges and managed assembly metadata; future type-family graph edges, translation hooks and standalone extraction pending |

The operator option attributes are `opname`, `commutator`, `negator`, `restrict`, `join`, `hashes`, and
`merges`. GUC-specific derives/hooks are tracked with GUCs below. PostgreSQL event callbacks and owned
descriptive metadata are implemented as documented above. Full custom-scan support remains required
alongside the source-level macro inventory.

### Datum conversions and user-defined types

| Source | Required behavior | Status |
|---|---|---|
| `datum/{from,into,unbox,borrow}.rs`, `nullable.rs`, `callconv.rs` | Conversion contracts, typed OIDs, SQL NULL distinct from zero, owned/borrowed lifetimes and argument/return ABI | Partial: built-in scalar/text/bytea/UUID/JSON transport |
| `datum/{bytea_type,varlena}.rs`, `varlena.rs`, `toast.rs` | Bytes/text, C strings, packed/compressed/external TOAST, encoding, alignment, custom varlena layouts | Partial: text/bytea including TOAST and server encoding |
| `array.rs`, `array/`, `datum/array.rs` | Arrays, dimensions/lower bounds, null elements, owned and borrowed iteration, variadic arrays | Owned arrays and vectors implemented for supported scalar/enum/composite types, with shape/subscripts/NULL handling, explicit composite identity and C# params variadics. Raw borrowed views and custom base-type elements pending |
| `datum/{anyarray,anyelement,internal}.rs` | Polymorphic datums, resolved element OIDs, internal/pointer-bearing values | Pending |
| `datum/{numeric,numeric_support/}` | Arbitrary precision and constrained numeric types, arithmetic, rounding, conversion, exceptional values | Implemented value/constraint surface: full-range `PgNumeric`, exact decimal adapters, arithmetic, rescaling, exceptional values, owned SPI conversion, JSON, declarative boundary constraints, primitive casts, generic integer conversion, mixed operators and summation. Cross-version/platform evidence remains pending |
| `datetime.rs`, `datetime/` | Date, time, timestamp, timestamp with timezone, time with timezone, interval; infinities, ranges, arithmetic and time zones | Partial: full-range types, exact conversions, function/SPI transport, native parsing/formatting/arithmetic/parts/truncation/zones/clocks, exact numeric extraction, comparisons, operators, component/unit factories, precision modifiers, explicit-zone ISO and JSON; detached field/epoch/raw factories, native zone-offset lookup, interval-zone overloads and owned timeofday text. Full raw bindings and the PostgreSQL/platform matrix remain required |
| `datum/{json,uuid,inet,geo,range}.rs` | JSON/JSONB, UUID, network, geometric and range datums with their operations | Partial: UUID, owned JSON/JSONB, inet/cidr, checked .NET network mappings, seven geometric datums, owned vertex collections and six typed range families/operations implemented; dedicated geometric operation wrappers, custom range subtypes and multiranges pending |
| `heap_tuple.rs`, `htup.rs`, `tupdesc.rs`, `datum/tuples.rs` | Named/anonymous composites, tuple descriptors, access/mutation, dropped/null attributes, tuple ownership | Owned dynamic tuples and descriptors implemented with strict edits, physical slots, nested arrays, domains/typmods, SQL bindings, SETOF/TABLE and SPI; raw heap interfaces and the platform/version matrix remain required |
| `PostgresEnum`, `enum_helper.rs` | Label/OID mappings, schema lookup, generated enum DDL, enums in containers | Implemented through attributes, closed generated mappings, guarded live catalog helpers and all supported array/SPI paths; composite fields and arrays validated; custom base-type containers and matrix validation remain required |
| `PostgresType`, `inoutfuncs.rs` | Custom base types with default CBOR in-memory/on-disk serialization and JSON human-readable input/output | Pending |
| `inoutfuncs`, `pgvarlena_inoutfuncs` type options | Custom textual representation, custom in-memory/on-disk layouts, alignment and manual datum conversion | Pending |
| `pg_binary_protocol` | Generated send/receive functions, binary protocol/COPY round-trips and invalid-input diagnostics | Pending |
| `postgres_type_variants` example/tests | All four custom-type paths, enum/struct variants, related derives and SQL override options | Pending |

Custom base types and PostgreSQL composite types have distinct storage and I/O contracts; both require
complete implementations. AOT serialization must use statically generated metadata/converters.

### Runtime and PostgreSQL internals

| Source modules | Required behavior | Status |
|---|---|---|
| `spi.rs`, `spi/{client,query,tuple,cursor}.rs` | Sessions; read-only/read-write queries; typed parameters/results; tuple mutation; owned/borrowed prepared plans; keep/free; cursors, fetch, detach/find by name; scalar helpers and quoting | Partial: guarded commands, scoped sessions/plans, typed results, cursors, local tuple edits, quoting and JSON EXPLAIN; extensible/raw datum conversion and multi-column scalar helpers pending |
| `memcx.rs`, `memcxt.rs`, `palloc.rs`, `palloc/`, `pgbox.rs`, `layout.rs` | Context selection/creation/switch/reset/delete; allocation/reallocation; context-bound cleanup; owned/borrowed server pointers | Pending |
| `fcinfo.rs`, `callconv.rs`, `fn_call.rs` | Function call context, collation, argument types/nulls, direct/named calls and result ownership | Partial: generated wrappers read basic arguments/results |
| `list.rs`, `list/`, `stringinfo.rs` | PostgreSQL lists and string/binary buffer operations with native ownership | Pending |
| `rel.rs`, `itemptr.rs`, `pg_catalog/`, `namespace.rs`, `wrappers.rs` | Relation/index access and locks, tuple locations, function/type catalog lookups, namespaces and type resolution | Pending |
| `xid.rs`, `callbacks.rs` | Transaction identifiers, transaction/subtransaction callbacks, unregister and error cleanup | Pending |
| `guc.rs`, `PostgresGucEnum`, `pg_guc_hook` | Bool/int/real/string/enum settings, contexts/flags/bounds, hidden/named enum entries, check/assign/show hooks and structured errors | Partial: native-backed typed declarations, hooks/extra, prefixes/logging, source/privilege/transaction/reload semantics, actual worker propagation, bounded lifetime measurements, cold package consumers and native-only preload verified above. Raw-placeholder treatment, managed postmaster callbacks, mixed-encoding preload and the full matrix remain required |
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
| Native AOT safety | Trim/AOT-clean consumers; deterministic cleanup on exceptions, native errors, cancellation and recursive callbacks | PG18 Linux x64 function/SPI cases pass; remaining APIs/targets pending |
| PostgreSQL 13, 14, 15, 16, 17, 18, 19 beta | Per-major builds against that server's headers, version-specific APIs/gating and complete backend tests | 18.6 only |
| Windows, Linux, macOS | Native builds, exports/loading, lifecycle, encoding, toolchain and installer tests for each supported RID | Linux x64 only |
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
    - [ ] Complete extensible/raw SPI datum conversion and multi-column scalar helpers
   - [x] Backend `_PG_init` bootstrap, guarded exceptions/retry, recursive-load rejection and session preload (PostgreSQL 18.6/Linux x64)
   - [ ] Memory contexts; full shared-preload parity and managed postmaster initialization; remaining guarded PostgreSQL APIs
- [ ] **P2 — Source generator** (`Ankus.Generators`)
    - [x] `[PgFunction]` → per-function dispatcher + `pg_finfo` shim emission + DDL metadata
    - [x] Scalar/text/bytea conversions, inferred strictness, `T?` NULL handling, SQL overloads
     - [x] Scalar arrays, vectors, dimensions/lower bounds, NULL elements and SQL variadics
    - [x] `[PgSchema]`, explicit function options, SETOF and named TABLE results
    - [ ] Remaining datum mappings and polymorphic/raw signatures
  - [ ] `.ankusc` metadata section (JSON) embedded in the `.so`; `ankus schema`
- [ ] **P3 — Extension features**
  - [x] custom installation SQL, binary/prefix operators and explicit/assignment/implicit casts
  - [x] row and statement triggers for supported tuple types
  - [x] event triggers with owned DDL/drop/rewrite metadata and login callbacks
  - [x] aggregates for supported concrete types, owned managed states, worker transport, moving windows and native ordering
  - [ ] polymorphic/raw and custom base-type aggregate signatures, heterogeneous ordered-set VARIADIC ANY
  - [ ] generated equality/order/hash operator classes
  - [x] enum declarations, label/catalog helpers, nullable/scalar/array conversions and SQL dependencies
  - [x] owned named/anonymous composites, descriptors, nested arrays, SETOF/TABLE and SPI bindings
  - [ ] custom base types (CBOR/JSON, custom storage/I/O, binary send/receive)
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
    - [ ] Public NuGet release after full parity and platform/version validation
- [ ] **P5 — Multi-version matrix**
   - [ ] PostgreSQL 13–18 (+19 beta) and Windows/Linux/macOS validation matrix
- [ ] **P6 — Examples + docs**
    - [x] Astro/Starlight documentation site, using `/home/brandon/src/ilrepl/docs` as a read-only design reference
    - [x] User-facing guides for extension authors; repository workflows and design notes live in `docs/contributing/`
    - [x] Concise guides, short explanations, and restrained formatting for the implemented APIs
    - [x] Verify site build, navigation, links, search, and desktop/mobile layouts
     - [x] Package-based setup guide, validated with isolated NuGet consumers
     - [ ] Public hosting and canonical site URL/sitemap
  - [ ] `samples/` mirroring pgrx-examples (aggs, gucs, triggers, bgworker, customscan…)
    - [x] README and verified datum-boundary design notes (`docs/contributing/native-boundary.md`)
    - [ ] Complete getting-started, API, deployment, and ported-feature documentation
    - [x] Generated public API reference from XML comments, following `/home/brandon/src/dotsider/src/Dotsider.DocGenerator`
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
