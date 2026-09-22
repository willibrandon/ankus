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

- `Ankus.slnx` contains the runtime, source generator, native build tool, native sample,
  PostgreSQL discovery, test infrastructure, and five developer-visible MSTest projects.
- **`dotnet test`**: **754 passed, 0 failed, 0 skipped** on Linux x64 with PostgreSQL 18.6.
- Test infrastructure lives in `tests/Ankus.Testing`; executable tests live in
  `tests/Ankus.IntegrationTests`, `tests/Ankus.Examples.Hello.Tests`, `tests/Ankus.PgConfig.Tests`,
  `tests/Ankus.Generators.Tests`, and `tests/Ankus.Runtime.Tests`.
- The sample contains ordinary `[PgFunction]`-attributed `Add` and `Greet` methods. Ankus generates
  managed dispatchers, native entry points, module magic, finfo, datum conversions, and SQL.
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
- The 580 integration cases include installed-tool workflows, numeric/temporal storage and operations, scalar bounds, signed zero and NaN bit patterns,
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
- `PgNumeric` owns canonical numeric output with full precision and display scale. It supports PostgreSQL
  arithmetic, rescaling, transcendental routines, NaN/infinities, and backend-independent equality/order/hash.
  Generated decimal adapters and typed SPI conversions reject overflow, rounding, and underflow; finite integer
  conversion uses `BigInteger`. The native scalar signature dispatcher is shared with temporal routines.
  PostgreSQL 14+ temporal `Extract` returns numeric directly; PG13 retains its floating-point extraction limits.
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
Verified on PostgreSQL 18.6 / Linux x64. Remaining temporal parity includes additional component factories,
arithmetic operators and interval sign/absolute-value conveniences, precision-rounded
clock helpers, explicit-zone ISO output, and temporal JSON serialization. Other server versions/platforms
remain unvalidated. PG13 date extraction uses its narrower timestamp cast; PG14+ uses native date extraction.

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

### Numeric API evidence

Reference surface: `pgrx/src/datum/{numeric.rs,numeric_support/}` and PostgreSQL
`src/backend/utils/adt/numeric.c`. The decimal conversion guard accounts for the documented
round-to-nearest behavior of `Decimal.Parse/TryParse` when an input exceeds decimal precision;
a successful parse alone does not establish an exact conversion.
Verified on PostgreSQL 18.6 / Linux x64. Remaining numeric parity includes declarative precision/scale
constraints on function boundaries, JSON serialization, and additional primitive/generic numeric conveniences.
Other server versions/platforms remain unvalidated.

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

### Work in progress

The generated API currently supports accessible, synchronous static methods with by-value
`bool`, `sbyte`, `short`, `int`, `long`, `uint` (OID), `float`, `double`, `decimal`, `string`, `byte[]`, `Guid`, `PgJson`, `PgJsonb`, `PgNumeric`,
and the .NET/full-range PostgreSQL temporal types. Nullable forms and `void` results are supported.
Strictness follows argument nullability.
The native library, control file, and versioned SQL are published and installed through PostgreSQL's extension mechanism.
Full `[PgTest]` generation, provisioning/lifecycle/package tooling, extension upgrade scripts, more data types,
the remaining SPI and PostgreSQL APIs, and the PG13–19 matrix remain pending. The MSBuild import is repository-local;
an independently consumable NuGet SDK has not been packaged yet. PostgreSQL discovery is
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
| `#[pg_extern]` | `[PgFunction]` + source generator (exports, DDL, metadata) | Partial: scalar/text/bytea/UUID/JSON, nullability, overloads |
| `#[pg_schema]` | `[PgSchema("name")]` | ☐ |
| `#[pg_guard]` | automatic at export boundary and guarded native API calls | Partial: export/datum boundaries and SPI execution |
| SETOF / TABLE (`SetOfIterator`, `TableIterator`) | generated streaming and materialized set/table results | ☐ |
| `#[pg_trigger]` | `[PgTrigger]` | ☐ |
| `#[pg_event_trigger]` | `[PgEventTrigger]` | ☐ |
| `#[pg_aggregate]` + `Aggregate` trait | `[PgAggregate]` + `IAggregate<TState>` (init/transition/combine/final, (de)serializable) | ☐ |
| `#[pg_operator]` | `[PgOperator]` (+ SQL DDL) | ☐ |
| `#[pg_cast]` | `[PgCast]` (+ SQL DDL) | ☐ |
| `extension_sql!` | `[ExtensionSql]` attribute / `.sql` files | ☐ |
| `#[derive(PostgresType)]` (custom base types) | generated CBOR storage, JSON text I/O, custom storage/I/O, binary send/receive | ☐ |
| `composite_type!`, `PgHeapTuple` | named/anonymous composite tuples and generated managed mappings | ☐ |
| `#[derive(PostgresEnum)]` | `[PostgresEnum]` on C# enums + generator (CREATE TYPE) | ☐ |
| Type mapping (`FromDatum`/`IntoDatum`) | `Datum` converters for built-in and user-defined SQL types | Partial: scalars, text/bytea/UUID/JSON, nullable forms |
| `Spi` | typed commands/results, sessions, prepared statements, cursors, tuple access | Partial: atomic commands, scoped sessions/plans, typed results, cursors, row edits, quoting and JSON EXPLAIN |
| `PgError` | `PgException` + logging helpers | Owned diagnostics, context, objects, positions/location; `PgLog` severities and structured reporting |
| `pgrx::guc` | `[PgGucInt/Real/String/Bool/Enum]` (registered in `_PG_init`) | ☐ |
| `background_worker` | `BackgroundWorker` registration (C# `void(Datum)` via function pointer) | ☐ |
| `palloc`/`MemoryContextManager` | `PgMemoryContext`, `Palloc` | ☐ |
| `pgrx::rel` (`PgRelation`) | `PgRelation`, `PgIndex` | ☐ |
| `iter`, `pg_sys` tuple-store APIs | managed tuple-store integration | ☐ |
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
| `new` | Generate an ordinary extension project, control/configuration defaults, functions, and discoverable backend tests | Pending |
| `init` | Install/build supported PostgreSQL versions or register existing installs; persist configuration and toolchain options | Partial: discovery/configuration in `src/Ankus.PgConfig`; provisioning pending |
| `info` | Installation path, `pg_config` path, and exact PostgreSQL version queries | Partial: library discovery; CLI pending |
| `start`, `stop`, `status` | Manage version-specific persistent development clusters, ports, logs, and lifecycle | Partial: isolated test lifecycle in `tests/Ankus.Testing`; development CLI pending |
| `run`, `connect` | Build/install/load an extension and connect through `psql` or configured client, including `pgcli` | Pending |
| `test` | Backend test discovery, filters, expected errors, configuration, rollback, and supported-major matrix | Partial: canonical `dotnet test`; generated backend tests and matrix pending |
| `bench` | Attribute-driven benchmarks running inside PostgreSQL and result reporting (`pgrx-bench`) | Pending |
| `regress` | PostgreSQL regression SQL/expected-output suites and diagnostics | Pending |
| `schema` | Schema generation from one compilation, standalone extraction, ordering/dependencies, custom SQL, output options | Partial: `src/Ankus.Build` reads managed metadata without loading extension code |
| `install` | Install libraries, control files, schema and upgrade scripts into selected PostgreSQL paths | Partial: PG18 test fixture installation; general installer pending |
| `package` | Produce a relocatable installation tree for a selected version/target with custom library naming | Partial: publish output; distribution command pending |
| `get` | Query extension control properties and derived extension metadata | Pending |
| `cross` / `pgrx-target` | Export target configuration/binding information and support target-aware build workflows | Pending |
| `upgrade` | Upgrade framework package references, including workspace/central versions and dry-run selection | Pending; distinct from PostgreSQL extension SQL upgrades |

Additional tooling sources: `cargo-pgrx/src/{manifest,metadata}.rs`, command options in each command file,
`pgrx-pg-config/src/`, `pgrx-bindgen/src/`, and installation/upgrade fixtures in `cargo-pgrx/tests/`.
The framework also requires versioned extension SQL upgrades, custom/versioned shared-library names,
control-file settings, dependency handling, and deterministic packaging.

NuGet delivery remains pending: one extension-author package supplying runtime/generator/build integration,
a .NET tool package, reusable backend-testing packages, and isolated consumer tests that use only packed
artifacts. Public publication requires full feature and platform/version validation.

### Source generation, schema, and extension declarations

Primary sources: `pgrx-macros/src/lib.rs`, `pgrx-sql-entity-graph/src/`, `pgrx/src/{fcinfo,iter,aggregate}.rs`.

| Feature family | Required behavior | Status |
|---|---|---|
| `pg_extern` / `pgrx` | Names, schemas, overloads, strictness, defaults, named arguments, variadics, polymorphic/raw inputs and results | Partial: synchronous scalar/text/bytea/UUID/JSON, names, overloads, inferred strictness |
| Function options (`extern_args.rs`) | Create-or-replace, immutable/stable/volatile, security invoker/definer, parallel modes, cost, support functions, dependencies, search path | Pending |
| `pg_schema`, `search_path` | Schema declarations, qualification, nested declarations, lookup/search-path semantics | Pending |
| `extension_sql!`, `extension_sql_file!` | Inline/file SQL, entity requirements, bootstrap/finalize positioning, declared created entities | Pending |
| `pgrx(sql = ...)` | Custom/disabled SQL generation and SQL generation callbacks/equivalents | Pending |
| `default!`, `name!`, `composite_type!` | SQL default arguments, named table/aggregate fields, named composite type resolution | Pending |
| `SetOfIterator`, `TableIterator` | SETOF and TABLE results, nullability, tuple metadata, iteration cleanup on early exit/error | Pending |
| `pg_trigger` | Row/statement and before/after/instead-of triggers; event/argument metadata; OLD/NEW tuple access and modification | Pending |
| `pg_aggregate`, `AggregateName` | Transition/final/combine/serialize/deserialize; moving/inverse states; ordered-set/hypothetical; initial states, sort and parallel options | Pending |
| `pg_operator` and option attributes | Operator name, commutator, negator, selectivity/join support, hashes/merges, and schema dependencies | Pending |
| `PostgresEq`, `PostgresOrd`, `PostgresHash` | Equality, order and hash functions, operator classes/families and index use | Pending |
| `pg_cast` | Explicit/assignment/implicit casts and generated SQL | Pending |
| `pg_test`, `pg_bench` | Generated in-backend tests/benchmarks, discovery and expected-error metadata | Pending |
| `pg_guard`, `initialize`, module magic | Guarded callbacks, bootstrap, panic/exception boundaries, module name/version and ABI checks | Partial: function exports, native guards, module magic |
| SQL entity graph and metadata | Type/function/schema dependencies, cycle diagnostics, SQL translation hooks, section encoding/decoding, ELF/PE/Mach-O extraction | Partial: basic generated DDL and managed assembly metadata; complete graph/extraction pending |

The operator option attributes are `opname`, `commutator`, `negator`, `restrict`, `join`, `hashes`, and
`merges`. GUC-specific derives/hooks are tracked with GUCs below. PostgreSQL event triggers and full
custom-scan support remain required alongside the source-level macro inventory.

### Datum conversions and user-defined types

| Source | Required behavior | Status |
|---|---|---|
| `datum/{from,into,unbox,borrow}.rs`, `nullable.rs`, `callconv.rs` | Conversion contracts, typed OIDs, SQL NULL distinct from zero, owned/borrowed lifetimes and argument/return ABI | Partial: built-in scalar/text/bytea/UUID/JSON transport |
| `datum/{bytea_type,varlena}.rs`, `varlena.rs`, `toast.rs` | Bytes/text, C strings, packed/compressed/external TOAST, encoding, alignment, custom varlena layouts | Partial: text/bytea including TOAST and server encoding |
| `array.rs`, `array/`, `datum/array.rs` | Arrays, dimensions/lower bounds, null elements, owned and borrowed iteration, variadic arrays | Pending |
| `datum/{anyarray,anyelement,internal}.rs` | Polymorphic datums, resolved element OIDs, internal/pointer-bearing values | Pending |
| `datum/{numeric,numeric_support/}` | Arbitrary precision and constrained numeric types, arithmetic, rounding, conversion, exceptional values | Partial: full-range `PgNumeric`, exact decimal adapters, arithmetic, rescaling, exceptional values and owned SPI conversion. Declarative signature constraints, JSON serialization and additional primitive/generic conveniences remain |
| `datetime.rs`, `datetime/` | Date, time, timestamp, timestamp with timezone, time with timezone, interval; infinities, ranges, arithmetic and time zones | Partial: full-range types, exact conversions, function/SPI transport, native parsing/formatting/arithmetic/parts/truncation/zones/clocks, exact numeric extraction and comparisons. Remaining factories, conveniences, explicit-zone ISO and JSON serialization are listed above |
| `datum/{json,uuid,inet,geo,range}.rs` | JSON/JSONB, UUID, network, geometric and range datums with their operations | Partial: UUID, owned JSON/JSONB and metadata-based serialization; network, geometry and ranges pending |
| `heap_tuple.rs`, `htup.rs`, `tupdesc.rs`, `datum/tuples.rs` | Named/anonymous composites, tuple descriptors, access/mutation, dropped/null attributes, tuple ownership | Pending |
| `PostgresEnum`, `enum_helper.rs` | Label/OID mappings, schema lookup, generated enum DDL, enums in containers | Pending |
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
| `guc.rs`, `PostgresGucEnum`, `pg_guc_hook` | Bool/int/real/string/enum settings, contexts/flags/bounds, hidden/named enum entries, check/assign/show hooks and structured errors | Pending |
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

The minimal `samples/Ankus.Examples.Hello` sample is validated. Full example parity is pending.

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

The 542 passing Ankus tests verify the current milestone, not this entire corpus. Each family still needs
source-case-level mapping to named .NET tests and any additional boundary cases introduced by AOT/native interop.

### Release evidence requirements

| Deliverable | Required evidence | Current evidence |
|---|---|---|
| Full runtime/macro/CLI parity | Source API/option inventory mapped to implemented APIs, behavior tests and examples | Family inventory above; most implementation pending |
| Native AOT safety | Trim/AOT-clean consumers; deterministic cleanup on exceptions, native errors, cancellation and recursive callbacks | PG18 Linux x64 function/SPI cases pass; remaining APIs/targets pending |
| PostgreSQL 13, 14, 15, 16, 17, 18, 19 beta | Per-major builds against that server's headers, version-specific APIs/gating and complete backend tests | 18.6 only |
| Windows, Linux, macOS | Native builds, exports/loading, lifecycle, encoding, toolchain and installer tests for each supported RID | Linux x64 only |
| Ordinary .NET usage | One NuGet reference, attributed methods, `dotnet publish`, discoverable plain `dotnet test` and working tool commands | Repository project references/imports; isolated NuGet consumer pending |
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
      - [ ] Numeric function-boundary constraints, JSON serialization and remaining primitive/generic conveniences
      - [ ] Remaining temporal factories/conveniences, explicit-zone ISO output and JSON serialization
    - [ ] Complete extensible/raw SPI datum conversion and multi-column scalar helpers
   - [ ] Memory contexts; `_PG_init` bootstrap; remaining guarded PostgreSQL APIs
- [ ] **P2 — Source generator** (`Ankus.Generators`)
    - [x] `[PgFunction]` → per-function dispatcher + `pg_finfo` shim emission + DDL metadata
    - [x] Scalar/text/bytea conversions, inferred strictness, `T?` NULL handling, SQL overloads
   - [ ] `[PgSchema]`, explicit function options, SETOF, arrays, remaining datum mappings
  - [ ] `.ankusc` metadata section (JSON) embedded in the `.so`; `ankus schema`
- [ ] **P3 — Extension features**
  - [ ] triggers, event triggers, aggregates, operators, casts, `ExtensionSql`
  - [ ] custom base types (CBOR/JSON, custom storage/I/O, binary send/receive), composites, enums
  - [ ] GUC options; background workers
- [ ] **P4 — Tooling** (`ankus` dotnet tool)
   - [x] Packable `Ankus.Tool`, top-level entry point, System.CommandLine 2.0.12
   - [x] `init`, `info`, `build`, `publish`, and `install` commands, registered installations and explicit overrides
   - [ ] Provisioning/downloads, `new`, `schema`, `test`, `run`, server lifecycle and `package` commands
   - [x] Publish native library, `.control`, and versioned `.sql` artifacts
    - [x] Native/SQL installation and DESTDIR staging with target/artifact validation
    - [ ] Distribution packaging and extension upgrades
    - [ ] NuGet entry package with automatic runtime, generator, and build integration dependencies
    - [x] Local tool package installation and invocation tests
    - [ ] Reusable backend-testing packages
    - [ ] Isolated consumer tests using packed NuGet artifacts
    - [ ] Public NuGet release after full parity and platform/version validation
- [ ] **P5 — Multi-version matrix**
   - [ ] PostgreSQL 13–18 (+19 beta) and Windows/Linux/macOS validation matrix
- [ ] **P6 — Examples + docs**
    - [x] Astro/Starlight documentation site, using `/home/brandon/src/ilrepl/docs` as a read-only design reference
    - [x] User-facing guides for extension authors; repository workflows and design notes live in `docs/contributing/`
    - [x] Concise guides, short explanations, and restrained formatting for the implemented APIs
    - [x] Verify site build, navigation, links, search, and desktop/mobile layouts
    - [ ] Public hosting, canonical site URL/sitemap, and package-based setup guide after NuGet consumer validation
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
