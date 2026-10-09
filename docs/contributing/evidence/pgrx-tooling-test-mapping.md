# pgrx tooling and compile-time test mapping

This maps the pgrx 0.19.3 test corpora below to the Ankus tests that verify the
same behavior, or records why a case does not apply. With the
[unit-test mapping](pgrx-test-mapping.md), it covers the corpora listed in the
[parity requirements](parity-requirements.md) except the inline unit tests.

- `pgrx-unit-tests/tests/`: `compile-fail/`, `todo/`, `nightly/` and `ui.rs`
- `pgrx-tests/src/`: `framework.rs`, `framework/shutdown.rs` and `proptest.rs`
- `pgrx-bench/src/` and `cargo-pgrx/src/command/bench.rs`
- `cargo-pgrx/tests/*.rs` and `cargo-pgrx/tests/fixtures/`

A case is a pgrx test file or test function, or a named framework or command
function or option; a row that lists several counts each. The audit covers 203
cases, read against Ankus at revision **10c1391** with the working tree of
2026-10-09: 36 compile-time cases, 57 testing-framework cases, 87 benchmark
cases and 23 `cargo-pgrx/tests` cases, in 118 rows. By row, 66 are Covered,
14 Partial, 3 Gap, 11 Different and 24 Rust-specific. Only the same behavior
and edge cases count as covered.

Paths are relative to the repository: **IT** is `tests/Ankus.IntegrationTests/`,
**RT** is `tests/Ankus.Runtime.Tests/`, **GT** is `tests/Ankus.Generators.Tests/`,
**BT** is `tests/Ankus.Build.Tests/` and **PT** is `tests/Ankus.PgConfig.Tests/`.
As in the unit-test mapping, a test is named by its file and method. Guides are
under `docs/src/content/docs/`.

Statuses: **Covered**, **Partial** (what is missing is named), **Gap** (no Ankus
test; the row says whether the feature exists), **Different** (a deliberate
behavior difference, with the guide that documents it) and **Rust-specific** (no
.NET analogue; the replacing .NET guarantee and its test are named).

## Summary

Ankus rejects the same invalid signatures with generator diagnostics. pgrx's
lifetime cases are borrow-checker errors; Ankus replaces them with checked native
lifetimes, where an expired view or handle throws instead of reading freed
memory, and with managed copies. Most of pgrx's `todo` cases, which pgrx expects
not to compile, compile and run in Ankus. The test fixture matches pgrx's cluster
lifecycle, per-test rollback, exact expected-error contract and `CASCADE`
extension creation. The benchmark runner matches
pgrx's measurement, statistics and comparison summaries, but the history report
and console output are thinner, and several `bench` options have no test. The ranked list is at the end.

## Compile-time cases

### compile-fail/

| pgrx case | Status | Ankus evidence |
| --- | --- | --- |
| aggregate-functions-dont-run-forever.rs | Rust-specific | pgrx rejects composite aggregate arguments and states that would outlive the call. Ankus passes them as owned `PgHeapTuple` values and keeps state in the aggregate context until reset: IT CompositesExampleTests CompositesSampleSumsScritches; IT AggregateTests ManagedStateLivesUntilResetAndReleasesExactlyOnce, AggregateSupportFunctionsRequireNativeAggregateContext; RT PgAggregateTests ReleaseInvalidatesBeforeCleanupAndRestoresScopes |
| array-as-non-mutable-args.rs | Covered | GT PgFunctionGeneratorTests ConcreteBorrowedArraysRejectReferencePassing: `ref`, `out`, `in` and `ref readonly` array arguments are ANKUS038 at the modifier |
| arrays-dont-leak.rs | Rust-specific | Borrowed views expire with their owner and copies survive: IT LargeArrayLifetimeTests LargeArrayPrefixBorrowsExpireAndCopiesSurvive (pgrx's 10,000-string array and five borrows, every owner-ending path); IT BorrowedArrayTests BorrowedArrayOwnersInvalidateEscapedViews; a returned borrowed array transfers its storage: IT BorrowedArrayCallbackTests BorrowedArrayReturnsSurviveCallbackCleanup |
| eq-for-postgres_hash.rs | Covered | GT CustomOperatorGeneratorTests InvalidCustomOperatorContractsAreDiagnosed: `[PgHashing]` without `[PgEquality]` is ANKUS418 and a hash method without the `IPgHashable` contract is ANKUS417; mapped types: GT MappedOperatorGeneratorTests InvalidMappedOperatorContractsAreDiagnosed |
| total-eq-for-postgres_eq.rs | Covered | GT CustomOperatorGeneratorTests InvalidCustomOperatorContractsAreDiagnosed: `[PgEquality]` without `IEquatable<T>` for the type itself, including a lookalike `IEquatable<int>`, is ANKUS415 |
| total-ord-for-postgres_ord.rs | Covered | Same test: `[PgOrdering]` without `IComparable<T>`, including a lookalike `IComparable<int>`, is ANKUS416, and ordering without equality is ANKUS418 |
| escaping-spiclient-1209-cursor.rs | Rust-specific | SPI rows are owned copies and a cursor cannot outlive its portal: IT SpiQueryTests MaterializedRowsSurviveSubsequentSpiCalls; IT SpiExampleTests SpiSampleDetachesCatalogAndCursorResults (`issue1209_fixed` repeated without leaking portals); IT SpiCursorTests TransactionEndInvalidatesCursor, ReusedPortalNameDoesNotReviveStaleCursor |
| escaping-spiclient-1209-prep-stmt.rs | Rust-specific | A plan borrowed from an SPI session fails after the session ends unless kept: IT SpiSessionTests ExpiredSessionOwnershipIsRejected, KeptSessionPlanSurvivesTransactionEnd; IT SpiPreparedTests DisposedPlanCannotBeExecutedOrFreedTwice |
| heap-tuples-dont-leak.rs | Rust-specific | Tuple cells are read as owned values and tuples own their slots: RT PgHeapTupleTests TupleAndCloneOwnTheirCellSlots, NestedValuesRemainOwnedAfterNativeTransportRelease; IT CompositeDatumTests StoredCompositeValuesSurviveSourceDeletion |
| invalid_pgcast_options.rs, too_many_cast_options.rs | Rust-specific | `[PgCast]` takes one typed `PgCastContext` argument, so an unknown option or a second context is a C# compile error. An undefined context value is ANKUS074: GT OperatorCastDiagnosticsTests OperatorAndCastErrorsIdentifyTheirAuthoredContract; GT PgOperatorCastGenerationTests InvalidCastSignaturesAreDiagnosed |
| no-arrays-of-arrays.rs | Covered | Nested arrays are ANKUS039 as results and ANKUS040 as arguments: GT PgFunctionGeneratorTests UnsupportedSignaturesAreRejected; nested borrowed views: GT TypedBorrowedArrayGeneratorTests TypedBorrowedArraysRejectUnsupportedElements. `PgFlatArray<T>` requires an unmanaged element, so a flat array of flat arrays does not compile |
| no-static-memcx.rs | Rust-specific | An injected `PgMemoryContext` is a checked handle that fails once its context ends: RT PgMemoryContextTests StaleContextCannotAllocateAndReportsNotAlive; IT MemoryContextTests SavedHandlesSurviveCallbacksAndExpireAtTransactionEnd |
| postgres-strings-arent-immortal.rs | Rust-specific | `string` arguments are managed copies. Borrowed `PgTextView` arguments expire at callback exit, and set results built from them keep their inputs: IT BorrowedBufferCallbackTests BorrowedBufferCallbacksExpireAliasesAndRecover; IT BorrowedBufferTests BorrowedBufferSetsRetainTheirInputs |
| table-iterators-arent-immortal.rs | Rust-specific | TABLE and set results with borrowed columns keep their own snapshots: IT BorrowedBufferResultTests BorrowedBufferTablesReleaseTheirSnapshots; IT CStringResultTests CStringSetResultsPreserveEachRowAndReleaseOwners |
| sql-translatable-invalid-pg-extern-positions.rs: bad_bare_u8 | Covered | GT PgFunctionGeneratorTests UnsupportedSignaturesAreRejected: a bare `byte` argument is ANKUS040 and result ANKUS039; `PgArray<byte>` is ANKUS039 |
| sql-translatable-invalid-pg-extern-positions.rs: bad_unit_arg, bad_result_arg | Rust-specific | C# has no unit-typed parameter or `Result` type; any other unmapped argument type is ANKUS040 at the parameter: GT FunctionSignatureTests FunctionSignaturesHavePreciseDiagnostics |
| sql-translatable-invalid-pg-extern-positions.rs: bad_setof_arg | Covered | GT PgSetGenerationTests SetSupportRetainsOrdinaryFunctionRestrictions: an `IEnumerable<int>` argument is ANKUS040 |
| sql-translatable-invalid-pg-extern-positions.rs: bad_table_arg | Covered | GT PgFunctionGeneratorTests UnsupportedSignaturesAreRejected: a sequence of tuples, the TABLE shape, as an argument is ANKUS040 |
| type-ident-manual-impl-must-define-key.rs | Rust-specific | A mapping's identity is the compiler's type symbol, not a hand-written key: GT ManagedTypeIdentityTests ManagedProviderKeysPreserveCompilerEquality; an unregistered managed provider is ANKUS382: GT PgFunctionGeneratorTests SqlTypeProviderManagedSelectionRequiresRegistration |
| type-ident-non-sql-leaf-types.rs | Covered | An unmapped TABLE column is ANKUS397 (GT PgSetGenerationTests InvalidSetRowShapesAndNamesAreDiagnosed, `(int Id, System.Uri Address)`); an unmapped argument is ANKUS040 (GT FunctionSignatureTests FunctionSignaturesHavePreciseDiagnostics); a nullable unmapped struct argument and result are ANKUS040 and ANKUS039 (GT PgFunctionGeneratorTests UnsupportedSignaturesAreRejected). `Result` is Rust-specific |
| u32.rs | Different | `uint` maps to `oid`, as the type table in `getting-started/functions.md` documents: IT DatumConversionTests ScalarValuesPreservePostgresRepresentation round-trips `0` and `4294967295` |
| variadic-is-dead.rs | Rust-specific | `params` is a parameter modifier, so a variadic result cannot be written. A `params` parameter must map to a SQL array (ANKUS041, GT FunctionSignatureTests FunctionSignaturesHavePreciseDiagnostics), and operators and casts reject one (ANKUS067, GT OperatorCastDiagnosticsTests OperatorAndCastErrorsIdentifyTheirAuthoredContract) |
| spi-prepare-prepared-statement.rs.ignored | Rust-specific | pgrx does not run this case. `Spi.Prepare` accepts SQL text, so preparing a prepared statement is a C# type error; no Ankus test |

### todo/

pgrx expects these cases to fail to compile until it supports them.

| pgrx case | Status | Ankus evidence |
| --- | --- | --- |
| array-as-pbox.rs | Rust-specific | `PBox` has no counterpart. Array arguments are borrowed `PgArrayView<T>` values (IT TypedArrayCallbackTests GeneratedTypedArraysPreserveDeclaredResults), and `PgMemoryContext.CreateFlatArray<T>` builds a context-owned flat array (IT TypedArrayCallbackTests FlatArraysBuildInPlaceAndReturn) |
| busted-exotic-signature.rs | Covered | IT CompositeDatumTests CompositeDefaultsVariadicsAndCastsExecute: a defaulted nullable array of nullable composites, a TABLE column holding an array of composites, composite defaults and defaulted variadic composites; TableColumnsCarryTheirIndividualCompositeTypes; GT PgCompositeGenerationTests CompositeVariadicsAndDefaultsKeepBindings |
| composite-types-broken-on-spi.rs, vec-option-composite_type-nesting-problems.rs | Covered | Composite vectors and arrays with NULL rows are accepted and returned (IT CompositeDatumTests CompositeVectorInputsRetainNullRowsAndValues; IT TypedArrayCallbackTests GeneratedTypedArraysPreserveDeclaredResults), and a non-variadic composite array has a SQL default (IT CompositeDatumTests CompositeDefaultsVariadicsAndCastsExecute) |
| for-dog-in-dogs.rs | Covered | IT CompositeDatumTests CompositeDefaultsVariadicsAndCastsExecute: variadic composites with NULL rows, a defaulted variadic and a defaulted non-variadic composite array, as in `sum_scritches_for_names_array_default`; CompositeVectorInputsRetainNullRowsAndValues |
| random-vec-strs-arent-okay.rs | Covered | Text arrays with NULL cells arrive as `string?[]` or as borrowed `PgArrayView<PgTextView?>` (IT TypedArrayCallbackTests GeneratedTypedArraysPreserveDeclaredResults), and a text array parameter with an empty-array SQL default runs omitted and supplied (IT FunctionDeclarationTests SqlDispatchHonorsDeclarations) |
| roundtrip-tests.rs | Covered | Text and bytea arrays with NULL and empty cells round-trip through borrowed views: IT TypedArrayCallbackTests GeneratedTypedArraysPreserveDeclaredResults; cstring arrays: IT CStringTests CStringArraysPreserveShapeAndCellBytes |

### nightly/ and ui.rs

| pgrx case | Status | Ankus evidence |
| --- | --- | --- |
| nightly/compile-fail/allocation-doesnt-outlive-memcx.rs | Rust-specific | Rust's `allocator_api` has no counterpart. Managed collections live on the garbage-collected heap, and native allocations expire with their context: IT MemoryAllocationTests TransientContextsRestoreAndDeleteAcrossActionNativeAndCleanupFailures; RT PgAllocationTests ContextUsesNativeOwnerAndRejectsStaleIdentity |
| ui.rs: compile_fail, todo and the nightly compile_fail | Rust-specific | trybuild compares compiler output with `.stderr` files. Generator tests compile sources in memory, assert the diagnostic ID, severity, span and message, then check that the repaired source works: GT FunctionSignatureTests FunctionSignaturesHavePreciseDiagnostics |

## Testing framework

### Running a test (framework.rs)

| pgrx case | Status | Ankus evidence |
| --- | --- | --- |
| run_test: one transaction per test, rolled back after success | Covered | IT PostgresTestClusterTests BackendFunctionWritesAreRolledBack; IT ToolCommandBackendTests GeneratedBackendTestsDiscoverExecuteAndRollback (rollback checked after success, an expected error and each failure). Ankus also rolls back after a host failure or cancellation: IT PostgresTestClusterTests FailedCallbackRollsBackWrites, CanceledCallbackRollsBackWrites |
| run_test: the expected error matches the primary message exactly | Covered | IT PostgresTestClusterTests ExpectedErrorMatchesAndClusterRemainsUsable; IT ToolCommandBackendTests GeneratedBackendTestsDiscoverExecuteAndRollback (non-ASCII and quoted messages pass, a different message fails) |
| run_test: an expected error that is not raised fails | Covered | IT PostgresTestClusterTests MissingExpectedErrorFailsAndRollsBack |
| run_test failure report, query_wrapper: SQLSTATE, message, location, detail, hint, schema and table | Covered | `PostgresTestException` wraps the server's `PostgresException`: IT PostgresTestClusterTests WrongExpectedErrorIncludesServerDiagnostics. Its file, line, routine, context, detail, hint, schema and table reach the client: IT PgDiagnosticTests NativeObjectDiagnosticsSurviveCatchAndRethrow, ManagedDiagnosticsReachClientWithoutTruncation, ConstraintViolationPreservesCatalogMetadata |
| format_loglines, get_named_capture: session and server log lines in the failure report | Covered | The exception carries the failing session's log lines, matched by application name (IT PostgresTestClusterTests WrongExpectedErrorIncludesServerDiagnostics; IT ToolCommandBackendTests GeneratedBackendTestsDiscoverExecuteAndRollback), and the postmaster's report about the session's backend with its details when the backend died (IT PostgresTestClusterTests FailedTestReportsItsBackendsTermination) |
| `PGRX_TEST_SKIP` | Different | The host framework reports skips through `[PgTest(IgnoreReason = ...)]` and test filters, and missing prerequisites fail rather than skip (`getting-started/testing.md`). IT ToolCommandBackendTests GeneratedBackendTestsDiscoverExecuteAndRollback reports an ignored case as not executed and rejects running it directly |
| client() | Covered | `PostgresTestCluster.OpenConnectionAsync`: IT ToolCommandBackendTests GeneratedBackendTestsDiscoverExecuteAndRollback; a disposed cluster refuses connections: IT PostgresTestClusterTests DisposeStopsOwnedClusterAndPreservesLogs |

### Setup and installation (framework.rs)

| pgrx case | Status | Ankus evidence |
| --- | --- | --- |
| get_test_framework, initialize_test_framework, dropdb: one setup per test binary, then initdb, start and database creation | Covered | The fixture starts once per test class and its cases share it: IT ToolCommandBackendTests GeneratedBackendTestsDiscoverExecuteAndRollback; IT ToolCommandTests TestingPackageRunsInIndependentMSTestProject, NewSolutionRunsManagedAndBackendTests. A failed setup fails every case and cleans up: IT ToolCommandTests NewSolutionReportsInitializationFailuresAndCleansUp. Each fixture gets a fresh cluster, so there is no database to drop |
| create_extension: `CREATE EXTENSION name CASCADE` | Covered | IT ExtensionDependencyTests FixtureInstallsRequiredExtensionsWithCascade: an extension whose control file requires another starts in the fixture, which creates the dependency first |
| get_extension_name | Covered | The name comes from the published control file, including a custom control file with a fixed schema: IT ToolCommandBackendTests GeneratedBackendTestsDiscoverExecuteAndRollback |
| install_extension: `cargo pgrx install --test` with features, profile, `--no-schema` and manifest path | Covered | The fixture publishes with `AnkusIncludeTests=true`, `Configuration`, `BuildProperties` and `ReuseSchema`: IT ToolCommandBackendTests BackendTestFixtureRejectsInvalidOptions; forwarded properties and configuration reach it: IT ToolCommandTestRunnerTests TestCommandRunsSelectedBackendTests, TestCommandRejectsForwardedConfiguration; schema reuse: IT ToolCommandSchemaReuseTests TestCommandSchemaReusePreservesSqlAndRebuildsBodies |
| get_cargo_test_features, get_cargo_args: read `cargo test` arguments from the process tree | Rust-specific | The testing package carries the build's properties into the test host: IT PostgresTestSelectionTests TestHostSelectionTracksBuildProperties |
| install_extension with `--sudo` under `CARGO_PGRX_TEST_RUNAS` | Different | The fixture never writes into the PostgreSQL installation (`reference/cli.md`, `--runas`). PostgreSQL 18 uses `extension_control_path` and older majors a private copy: IT PostgresTestInstallationTests ScriptDirectoriesStayInsideOwnedTestInstallation, DirectoryAliasStagesRunnableServerAndPreservesSource; IT ToolCommandTestRunnerTests TestCommandRunsSelectedBackendTests (`runas`) |
| PortReservation, get_pg_config: hold a free port until the server starts | Covered | IT ClusterPortHandoffTests PortCollisionRetriesAndPreservesCompetingListener, RepeatedPortCollisionsAreBoundedAndCleanEveryAttempt, CancellationAtHandoffDoesNotStartOrRetryPostgres; explicit ports: RequestedTestPortRunsQueriesAndIsReleased, OccupiedRequestedPortCleansUpWithoutSwitchingPorts; port bases: IT ToolCommandPortTests InitPersistsPortBasesAndPreservesUnselectedSettings |
| maybe_make_pgdata, make_socket_dir, sudo_command | Covered | IT PostgresTestClusterTests ClusterRunsAsAnotherAccount (the other account creates and owns the data, socket and log), FailedStartupCleansDataAndRetainsDiagnostic |
| initdb: C locale flags | Partial | `initdb` gets `C.UTF-8` or `C` by pgrx's rules, but no test asserts the cluster's locale |
| modify_postgresql_conf, postgresql_conf_contents; test pgrx_postgresql_conf_defaults_precede_extension_options | Different | Test clusters use pgrx's `log_min_messages = info` and `log_min_duration_statement = 1000`, a C collation and UTF-8, and supplied settings follow and override them: IT PostgresTestClusterTests ClustersSkipFsyncUnlessConfigured; supplied settings reach the server: IT ToolCommandBackendTests GeneratedBackendTestsDiscoverExecuteAndRollback. Unlike pgrx, they do not set `log_statement = 'all'`; `getting-started/testing.md` documents this and how to enable it |

### Server lifecycle (framework.rs)

| pgrx case | Status | Ankus evidence |
| --- | --- | --- |
| start_pg: `pg_ctl start`, readiness and the startup failure report | Covered | Startup waits through `pg_ctl -w` within a bounded timeout; a failed start reports and keeps the log: IT PostgresTestClusterTests FailedStartupCleansDataAndRetainsDiagnostic; IT PostgresEventLogTests EventDiagnosticsPreserveStartupFailureAndCleanup |
| pipe::UnixFifo, pipe::WindowsNamedPipe | Rust-specific | The server logs to a file, and on Windows the fixture also collects the cluster's Event Log messages: IT PostgresEventLogTests EventDiagnosticsRemainIsolatedAndSurviveShutdown |
| start_pg: live echo of `TMSG:` log lines | Gap | Not implemented; server log lines are not streamed to the test output |
| use_valgrind, valgrind_suppressions_path | Partial | `start`, `run`, `connect`, `regress` and `bench` run the server under Valgrind (IT ToolCommandValgrindTests ValgrindCommandsExecuteNativeExtensionAndPreserveData; IT PostgresValgrindTests ValgrindReportsNativeErrorsAndPreservesDiagnostics), but test fixtures and `ankus test` cannot |
| start_pg: pass `CARGO*` and `RUST*` variables through `sudo` | Rust-specific | Backend tests do not need Cargo's environment; a server run as another account through `sudo` starts and serves queries: IT PostgresTestClusterTests ClusterRunsAsAnotherAccount |
| start_pg shutdown hook: `pg_ctl stop -m fast` at exit, then remove data and sockets | Covered | Disposal stops the server and removes data and sockets, keeping logs: IT PostgresTestClusterTests DisposeStopsOwnedClusterAndPreservesLogs; a timed-out stop can be retried: IT PostgresShutdownRecoveryTests CanceledShutdownCanReclaimStoppedClusterOnRetry; `ankus test` cleans up after a killed or canceled host: IT ToolCommandTestRunnerTests TestCommandPreservesRunnerExitAndCleansAbandonedCluster, TestCommandCancellationStopsCluster; a host that exits without disposing its cluster removes it through the `ProcessExit` handler: IT ToolCommandPortTests PackagedExtensionFixtureUsesSavedTestPortAndCleansUp |
| wait_for_pidfile, get_pid_file | Different | Each cluster's data directory has a unique name, so no earlier server can hold it (`getting-started/testing.md`): IT PostgresTestClusterTests DisposeStopsOwnedClusterAndPreservesLogs |

### Paths, accounts and tools (framework.rs)

| pgrx case | Status | Ankus evidence |
| --- | --- | --- |
| get_pgdata_path, test_invocation_dir_name; test test_pgdata_path_preserves_per_invocation_suffix | Covered | IT ToolCommandTestStorageTests PackagedExtensionFixtureUsesCustomDataDirectory, TestCommandRejectsInvalidDataDirectory; IT ToolCommandTestRunnerTests TestCommandRunsSelectedBackendTests (`custom-data`: data beneath the chosen base, removed afterward, other contents kept) |
| get_socket_dir_path, default_socket_dir_base; test test_socket_dir_is_separate_from_pgdata_parent | Covered | IT PostgresTestClusterTests SocketRootFitsThePlatformLimit, ClusterRunsAsAnotherAccount (socket under `/tmp`, apart from the data) |
| test test_socket_dir_defaults_under_tmp_on_unix | Different | On Linux, sockets stay in the temporary directory when the path fits the socket limit and use `/tmp` otherwise; macOS and another account always use `/tmp` (`reference/cli.md`, `--pgdata`): IT PostgresTestClusterTests SocketRootFitsThePlatformLimit |
| get_pg_dbname, get_pg_user | Different | Fixtures connect to `ankus_tests` as the cluster's own superuser, independent of the operating system account and `--runas` (`reference/cli.md`, `--runas`); `DatabaseName` and `UserName` select others: IT PostgresTestClusterTests ClusterRunsAsAnotherAccount |
| get_runas, requires_runas | Covered | `ankus test --runas`: IT ToolCommandTestRunnerTests TestCommandRunsSelectedBackendTests (`runas`); IT PostgresTestClusterTests ClusterRunsAsAnotherAccount |
| cargo_pgrx, workspace_cargo_pgrx, find_on_path; tests cargo_pgrx_prefers_explicit_override, workspace_cargo_pgrx_runs_from_source_even_with_a_target_binary | Rust-specific | The fixture runs `dotnet publish` from the host's SDK, so there is no separate tool to locate: IT ToolCommandTests TestingPackageRunsInIndependentMSTestProject |

### shutdown.rs, proptest.rs and concurrency

| pgrx case | Status | Ankus evidence |
| --- | --- | --- |
| add_shutdown_hook, run_shutdown_hooks | Different | Test hosts release resources in class or assembly cleanup through `IAsyncDisposable` (`getting-started/testing.md`); the generated fixture disposes in `[ClassCleanup]`: IT ToolCommandBackendTests GeneratedBackendTestsDiscoverExecuteAndRollback |
| PgTestRunner::run | Partial | Seeded property loops run inside backend tests (IT TemporalDatumTests SeededTemporalValuesRoundTripExactly; IT SerializedTypeTests SeededRandomDataSurvivesStorageArraysAndExchange) and backend errors are catchable (IT TryCatchExampleTests TryCatchSampleRecoversFromBackendErrors). Missing: a reusable runner that reports a backend error as a failing input, with shrinking |
| Concurrent execution: one setup under `TEST_MUTEX`, separate sessions, a port and data directory per invocation | Covered | Clusters in one process have separate ports and data: IT PostgresTestClusterTests DisposeStopsOwnedClusterAndPreservesLogs; another live cluster under the same base is unaffected: IT ToolCommandTestRunnerTests TestCommandPreservesRunnerExitAndCleansAbandonedCluster; two concurrent `RunInTransactionAsync` callbacks use separate backends, cannot see each other's uncommitted rows and both roll back: IT PostgresTestClusterTests ConcurrentCallbacksAreIsolatedAndRolledBack |

## Benchmarks

### pgrx-bench/src/

| pgrx case | Status | Ankus evidence |
| --- | --- | --- |
| Bencher::iter, Bencher::iter_batched: one timing loop per benchmark | Covered | RT PgBenchmarkTests BencherRequiresExactlyOneRoutine, BatchedSetupRunsImmediatelyBeforeMeasuredRoutine, BatchedMeasurementIncludesInputPreparation |
| black_box | Covered | RT PgBenchmarkTests BlackBoxDoesNotBoxValueTypes |
| BatchSize, iterations_per_batch | Covered | RT PgBenchmarkTests BatchSizesPreserveStrategiesAndValues, BatchedMeasurementAcceptsIterationCountsBeyondArrayLimits |
| TransactionMode, run_routine, Runtime::with_subtransaction | Covered | RT PgBenchmarkTests TransactionModesUseTheirExactBoundaries, BatchedMeasurementIncludesSubtransactionBoundary, BatchedSetupUsesSelectedTransactionBoundary; IT ToolCommandBenchmarkTests BenchRunsInBackendAndPersistsResultsOutsideMeasurementTransaction (rows written by the measured routine are rolled back) |
| BenchConfig defaults | Covered | RT PgBenchmarkTests AttributeUsesPgrxDefaults; invalid values: GT PgBenchmarkGeneratorTests RejectsInvalidBenchmarkContracts; RT PgBenchmarkTests RunnerRejectsInvalidConfiguration |
| execute_benchmark, Runtime::execute_guarded: setup, measurement and the failure result | Covered | RT PgBenchmarkTests RunnerReturnsNativeAotSafeDescriptorAndSamples, RunnerReturnsExplicitFailurePayload; IT ToolCommandBenchmarkTests BenchRunsInBackendAndPersistsResultsOutsideMeasurementTransaction (`Setup` creates the measured table; a backend `division by zero` becomes a failed result and exit code 1) |
| describe_benchmark | Covered | RT PgBenchmarkTests RunnerReturnsNativeAotSafeDescriptorAndSamples; GT PgBenchmarkGeneratorTests BenchmarkPublicationEmitsConfiguredBackendWrappers, BenchmarkSourceLinesUpdateWithoutRecomposition |
| module_path_has_benches: benchmarks only in `mod benches` | Rust-specific | Benchmarks are compiled only into benchmark publications: GT PgBenchmarkGeneratorTests OrdinaryPublicationExcludesBenchmarks; BT SdkPostgresSelectionTests TestAndBenchmarkPublicationsDefineTheirSymbols; IT ToolCommandBenchmarkTests BenchRunsInBackendAndPersistsResultsOutsideMeasurementTransaction (installation SQL has no `benches` schema) |
| build_criterion: warmup, sampling plan, sample count and measurement time | Covered | RT PgBenchmarkTests WarmupProducesCriterionCalibration, WarmupRejectsInvalidMeasurementsAndOverflow, AutomaticSamplingUsesCriterionLinearPlan, AutomaticSamplingKeepsCriterionBoundaryMode, AutomaticSamplingUsesCriterionFlatPlanForSlowRoutines, MeasurementRetainsEveryPlannedSample, MeasurementPreservesFailuresWithoutRetrying |
| parse_benchmark_output: estimates and samples | Covered | RT PgBenchmarkTests EstimatorsMatchCriterionArithmetic; IT ToolCommandBenchmarkTests BenchRunsInBackendAndPersistsResultsOutsideMeasurementTransaction (ten samples; slope or mean as the primary estimate) |
| collect_artifacts, materialize_baseline_artifacts, find_new_report_dir; tests find_new_report_dir_prefers_criterion_new_directory, materialize_baseline_artifacts_writes_criterion_base_layout, collect_artifacts_includes_change_estimates_when_present | Rust-specific | These handle Criterion's report files. Ankus keeps its own result document with the raw samples and passes it back as the baseline: RT PgBenchmarkTests RunnerComparesPersistedSamplesWithConfiguredResampling; IT ToolCommandBenchmarkTests BenchRunsInBackendAndPersistsResultsOutsideMeasurementTransaction. Criterion's Tukey outlier fences are not kept |
| parse_comparison, criterion_p_value, criterion_summary_from_change_estimate; tests criterion_summary_matches_expected_labels, criterion_p_value_with_rng_detects_large_regression, criterion_p_value_with_rng_is_high_for_identical_samples, parse_comparison_uses_persisted_baseline_artifacts | Covered | RT PgBenchmarkTests ComparisonSummariesFollowSignificanceAndNoise: all four summaries, a significant p-value for a doubled or halved time and a high p-value for identical distributions; RunnerComparesPersistedSamplesWithConfiguredResampling (persisted samples, the configured significance level and 95% intervals), ComparisonRejectsNonFiniteTStatistic |

### cargo-pgrx/src/command/bench.rs

Unless noted, the evidence is IT ToolCommandBenchmarkTests
BenchRunsInBackendAndPersistsResultsOutsideMeasurementTransaction, called the
bench test.

| pgrx case | Status | Ankus evidence |
| --- | --- | --- |
| resolve_args: positional `[pgXX] [benchname]` | Covered | `--pg` selects the server and an ordinal substring selects benchmarks: the bench test (`Success` selects two, `SuccessAddNumeric` one) |
| `--dbname` and the `<extension>_benches` default | Covered | The bench test uses the default database and `--database` |
| `--resetdb` | Partial | The bench test runs with `--resetdb`, but no test shows that it discards retained history |
| refresh_extension, `--cascade` | Partial | Each run drops and recreates the extension (the bench test). Missing: a dependent object that blocks the drop, `--cascade`, and pgrx's hint to rerun with it, which Ankus does not print |
| ensure_persistent_schema, execute_benchmark_query, persist_benchmark_result | Covered | The bench test: measured writes are rolled back, results persist in `ankus_bench` afterward, and a report on an empty database creates no schema. Ankus stores one `jsonb` result per run rather than separate estimate, sample and artifact tables |
| discover_benchmarks, load_benchmark_descriptor | Covered | The bench test, through the `benches` descriptor functions |
| "no benchmarks discovered" error | Covered | IT ToolCommandBenchmarkTests BenchRunsInBackendAndPersistsResultsOutsideMeasurementTransaction: a filter that matches nothing fails with "No benchmarks were discovered in the benches schema." |
| `--list` | Covered | IT ToolCommandBenchmarkTests BenchRunsInBackendAndPersistsResultsOutsideMeasurementTransaction: `--list` names the selected benchmarks with their settings and measures nothing |
| default_group_name, collect_git_metadata | Partial | The default name is a timestamp and short commit, or `GITHUB_SHA`, but no test uses it. Run groups do not record the Git branch, dirty state, describe output, tool versions or command line |
| resolve_compare_group | Partial | IT ToolCommandBenchmarkTests BenchRunsInBackendAndPersistsResultsOutsideMeasurementTransaction covers automatic selection of the latest completed group, a named group and an unknown named group. Missing: the same-configuration filter |
| insert_run_group, snapshot_pg_settings | Partial | Ankus records the configuration, PostgreSQL version, runtime identifier and a name-to-value map of `pg_settings`, but no test checks them; pgrx also keeps each setting's unit, source, boot value and reset value |
| load_backend_pid, wait_before_starting_benchmarks; tests wait_defaults_to_zero, wait_parses_from_cli | Covered | The bench test: the PID is printed and the backend is observed while `--wait 10` holds the run; other runs start without waiting |
| format_wait_duration; test format_wait_duration_uses_singular_and_plural_units | Gap | Not implemented; Ankus prints no waiting message |
| mark_run_group_complete | Partial | Group status drives automatic baseline selection (the bench test), but no test asserts `failed` or `partial` |
| load_missing_benchmarks (`missing_from_current`) | Covered | A compared run lists the baseline group's benchmarks that it did not run, in the console summary and in JSON `missing_from_current`: IT ToolCommandBenchmarkTests BenchRunsInBackendAndPersistsResultsOutsideMeasurementTransaction; PT BenchmarkConsoleFormatTests RunningLineAndSummaryMatchPgrx |
| build_change_summary | Covered | A compared run says when a benchmark is new to the compared group or its baseline did not complete, in place of the change: PT BenchmarkConsoleFormatTests FailuresAndFlatResultsKeepTheirLines; IT ToolCommandBenchmarkTests BenchRunsInBackendAndPersistsResultsOutsideMeasurementTransaction. As in pgrx's console output, a failed current run prints only its error |
| `--json` summary, build_benchmark_summary | Covered | The bench test: the group, comparison group and each result, including a failed one |
| print_running_benchmark, print_completed_benchmark, print_summary and the duration, interval and percent formatters | Covered | Benchmarks print in pgrx's layout: the running line with settings, `time:`, `change:` with its p-value, the summary, `slope:`, `mean:` with std. dev., `median:` with med. abs. dev. and the group summary with ok and failed counts and missing benchmarks, using pgrx's units and precision (PT BenchmarkConsoleFormatTests; IT ToolCommandBenchmarkTests BenchRunsInBackendAndPersistsResultsOutsideMeasurementTransaction) |
| parse_throughput, format_throughput_interval; tests throughput_interval_uses_time_estimate_to_compute_rate, throughput_without_interval_uses_decimal_units_when_requested | Gap | Not implemented. pgrx's `Bencher` cannot set a throughput either, so this output is unreachable from benchmark code |
| `--report`, execute_report; test report_flag_parses_from_cli | Covered | The bench test: `--report --json` returns retained results, and an empty database reports that no history exists |
| validate_report_args; test report_rejects_run_only_flags | Covered | IT ToolCommandBenchmarkTests BenchRunsInBackendAndPersistsResultsOutsideMeasurementTransaction: `--report --list` fails naming the incompatible option. `--json` is accepted with `--report`, as `benchmarks.md` documents |
| load_recent_report_runs, load_report_baselines, load_nondefault_settings_for_groups, build_report_sections, drift_categories_for_run, print_history_report; test report_sections_omit_failed_runs_and_mark_drift | Partial | Ankus lists the ten most recent results (the bench test). Missing: a section per benchmark, deltas from a baseline, drift marking for configuration, version and settings, and omission of failed runs |
| `--cargo`; test cargo_flag_defaults_to_empty | Covered | Repeatable `--property` assignments reach the benchmark build: the bench test (`--property BenchmarkProbe=enabled`); IT ToolCommandPropertyTests ProjectCommandsRejectMalformedGlobalProperties includes `bench` |
| test cargo_flag_is_repeatable (values split on whitespace) | Different | Each `--property` is one literal assignment and is never split (`reference/cli.md`, "Pass MSBuild properties") |
| `--postgresql-conf` | Partial | The parser is shared with `start` (IT ToolCommandClusterTests ClusterCommandsPreserveDataAcrossRestarts), but no benchmark test passes a setting |
| `--debug`, `--profile`, features, `--target`, `--package`, `--manifest-path` | Covered | `--configuration` (default `Release`), `--project` and `--property`: the bench test |
| Build and install before running | Covered | The bench test: PostgreSQL 18 stages the extension through `extension_control_path` without writing to the installation, and `--no-build` reuses the publication |

## cargo-pgrx tests

### cargo_flags_passthrough.rs

| pgrx case | Status | Ankus evidence |
| --- | --- | --- |
| cargo_flag_reaches_cargo_metadata | Covered | `--property` values reach project evaluation and every build step: IT ToolCommandPropertyTests ProjectGlobalPropertiesReachNativePublicationAndQueries (a conditional property selects the name `get` reads, then build, publish, schema, install and package); IT ToolCommandPropertyFlowTests ProjectGlobalPropertiesReachRunConnectAndRegression; malformed values fail before any work: IT ToolCommandPropertyTests ProjectCommandsRejectMalformedGlobalProperties |
| cargo_flag_whitespace_string_is_split | Different | Each `--property` is one literal assignment and is never split (`reference/cli.md`, "Pass MSBuild properties"): IT ToolCommandPropertyFlowTests ProjectGlobalPropertyQueriesPreserveLiteralValues |
| pgrx_build_flags_does_not_reach_cargo_metadata | Different | MSBuild has no build-only channel: properties apply to evaluation and compilation alike (`reference/cli.md`), as the first row's test shows |
| no_flags_does_not_break_cargo_metadata | Covered | Every command test without properties, for example IT ToolCommandTests PublishedInstalledAndPreloadedExtensionExecutesInPostgres |

### cargo_target_directory.rs

| pgrx case | Status | Ankus evidence |
| --- | --- | --- |
| schema_uses_configured_target_directory | Partial | IT ToolCommandPropertyTests ProjectGlobalPropertiesReachNativePublicationAndQueries: with a custom configuration and `BaseIntermediateOutputPath`, `schema` and `schema --skip-build` print the same SQL and the project gets no `obj` directory. Missing: a run from the project directory with a relative override, as in pgrx's third invocation |
| package_uses_configured_target_directory | Covered | The same test packages with the custom configuration and intermediate root; packages include upgrade scripts: IT ToolCommandUpgradeTests InstallAndPackageCopyOnlyDeclaredExtensionFiles; IT ToolCommandPackageTests PackageBuildsConfiguredExtensionAndLoadsInPostgres |
| test_uses_configured_target_directory | Covered | IT ToolCommandTestRunnerTests TestCommandRunsSelectedBackendTests (`forwarded`): a forwarded output root receives the test host's and the fixture publication's build output. `CARGO_TARGET_DIR` precedence is Rust-specific |

### cli_upgrade.rs

| pgrx case | Status | Ankus evidence |
| --- | --- | --- |
| upgrade_help_succeeds | Covered | IT ToolCommandTests InstalledToolProvidesHelp (`upgrade --help` lists `--to`) |
| upgrade_to_rejects_garbage, upgrade_to_rejects_empty | Covered | IT ToolCommandFrameworkUpgradeTests UpgradeInvalidRequestsPreserveAllFiles: `--to not-a-version` and `--to ""` fail and leave every file unchanged |

### install_pg_test_regression.rs

| pgrx case | Status | Ankus evidence |
| --- | --- | --- |
| install_test_extension_handles_mid_stream_schema_sentinel | Rust-specific | The bug was in finding pgrx's sentinel inside a linker section. Ankus embeds one length-delimited schema section and validates it when reading: PT NativeSchemaSectionTests RejectsMissingAndDuplicateSections, RejectsEveryTruncatedPrefix; graph size limits: GT InstallationGraphEncodingTests InstallationGraphEncodingEnforcesExactByteLimit; a test publication installs and runs: IT ToolCommandBackendTests GeneratedBackendTestsDiscoverExecuteAndRollback |
| install_from_virtual_workspace_auto_detects_manifest_and_preserves_rustflags | Partial | A solution's one extension project is selected (IT ToolCommandProjectResolutionTests ExtensionIdentitySelectsEvaluatedSolutionProject, ExtensionIdentitySkipsUnrelatedUnevaluableSolutionProject) and response-file properties apply (DirectoryBuildResponsePropertiesSelectExtensionProject), but these tests run `get extname`; no test builds and installs from a solution directory. The `--no-gc-sections` linker argument is Rust-specific |
| cargo_toml_path_uses_forward_slashes, command_output_path_match_accepts_escaped_windows_paths | Rust-specific | Helpers for writing `Cargo.toml` fixtures and matching Cargo's debug output; no counterpart |

### fixtures/

| pgrx case | Status | Ankus evidence |
| --- | --- | --- |
| package/ and workspace/ with upgrade.rs tests find_package_manifest, find_package_manifest_in_workspace | Partial | `--package` selects one project in a solution (IT ToolCommandFrameworkUpgradeTests UpgradeSelectsProjectAndImportedVersions), and a missing solution project fails (UpgradeInvalidRequestsPreserveAllFiles). Missing: an unknown `--package` name |
| package/ and workspace/ with upgrade.rs tests process_package_manifest, process_workspace_manifest, process_workspace_package_manifest | Covered | IT ToolCommandFrameworkUpgradeTests UpgradePreservesUnrelatedVersionsAndFormatting, UpgradeSelectsProjectAndImportedVersions, UpgradeHonorsSharedImportSelection, UpgradeMixedCentralOverridesRestoreSelectedVersions |
| macos-universal-binary with object_utils.rs and schema.rs tests returns_none_for_valid_binary_without_schema_section, parses_universal_binary_slice, slice_unknown_architecture_returns_none, test_missing_schema_section_errors | Covered | PT NativeSchemaSectionTests SelectsUniversalArchitecture, RejectsMalformedUniversalImage (absent architecture records), RejectsMissingAndDuplicateSections. The tests build Mach-O images instead of storing a binary |

## Remaining gaps and partials

Ranked by importance. Deliberate differences are documented where users meet them.

1. Test fixtures and `ankus test` cannot run the server under Valgrind.
2. The benchmark history report has no per-benchmark sections, baseline deltas, drift marking or failed-run omission, and run groups lack Git, tool and full `pg_settings` metadata.
3. These `bench` paths have no test: `--cascade` (and pgrx's rerun hint), the effect of `--resetdb`, `--postgresql-conf`, the same-configuration comparison filter and `failed` or `partial` group status.
4. No reusable property-test runner reports a backend error as a failing input with shrinking.
5. No CLI test installs from a solution directory, runs `schema` from the project directory with a relative override, or rejects an unknown `upgrade --package` name.
6. Throughput reporting and the waiting message are not implemented; pgrx's throughput output is unreachable from benchmark code.
7. Server log lines marked `TMSG:` are not echoed live to the test output.
