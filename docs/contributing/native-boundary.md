# Native datum boundary

Ankus generates both sides of the PostgreSQL/Native AOT boundary. The C wrapper
is compiled against the selected server's headers and linked into the extension's
native library. Extension authors write attributed C# methods.

## Declaration metadata

`FunctionDeclaration` resolves parameter names/defaults, the nearest containing
`PgSchema`, function-level overrides, and PostgreSQL execution options from
Roslyn symbols. It never loads or executes the extension assembly. Identifiers
are quoted, UTF-8 length is checked, and generated string constants use escape
literals independent of `standard_conforming_strings`. Negative numeric
constants are parenthesized before casts so unary negation does not follow a
narrowing conversion.

Schema creation precedes function DDL. Fixed schemas set `Ankus.Relocatable` to
false in assembly metadata; `ExtensionManifest` reads that with `PEReader`, and
`ExtensionPackage` writes the corresponding control-file flag. A schema-only
extension still emits module magic and a manifest, without managed dispatchers
or their native call dependencies.

`SqlGraph` unifies generated schemas/functions and assembly `PgSql`/`PgSqlFile`
blocks. Schema-to-function edges are automatic; explicit `Id`, `Requires`,
and `Before` references resolve before deterministic topological sorting.
Bootstrap/final nodes gain edges to every other node. Duplicate/missing IDs,
contradictory ordering and cycles fail generation rather than emitting a partial
script. Schema aliases share a single creation node.

SQL files flow through Roslyn's `AdditionalTextsProvider`, including their
content, so file-only edits invalidate incremental generation. Paths resolve
against the SDK's compiler-visible `MSBuildProjectDirectory`; no untracked disk
reads or runtime assembly execution are used. Custom SQL is emitted verbatim
with a final newline to terminate trailing line comments. PostgreSQL validates
and executes it transactionally during installation.

Network values use PostgreSQL's binary send/receive functions behind the native
guard. The wire family byte is normalized to 4/6; the native bridge maps it to
the selected PostgreSQL headers' `PGSQL_AF_INET`/`PGSQL_AF_INET6` values. Remaining
bytes carry the prefix, inet/cidr marker, address length and network-order address.
No native structure layouts or platform socket-family constants enter managed
storage. The receive path validates framing and lets PostgreSQL enforce prefix
and cidr host-bit rules. Send buffers belong to the same per-call/per-operation
ownership tracking as other buffered datums.

`PgInet` owns numeric address bits rather than a mutable `IPAddress` reference.
`PgCidr` enforces zero host bits, and both share the existing statically closed
array and SPI conversions. Network parsing uses an allowlisted scalar family
inside the subtransaction; mask, comparison and formatting operations run on
detached managed values.

Geometric transport uses PostgreSQL's network-order binary protocols rather than
managed copies of native structs. Fixed shapes carry doubles; path/polygon frames
carry a vertex count and, for paths, a closure byte. Both sides validate framing
before allocating point arrays. Variable-length inputs are explicitly detoasted
and tracked before the send routine runs, so PostgreSQL send functions cannot
leave hidden detoast copies alive for the rest of a large SPI operation.

The native writer delegates nonempty shapes to the matching receive function,
including line/circle validation. pgrx also permits empty owned paths/polygons,
which PostgreSQL's receive functions reject. For exactly zero vertices, the
bridge creates a zeroed header using `offsetof(PATH, p)` or `offsetof(POLYGON, p)`
from the selected server headers. Empty polygon bounds remain zero. The bridge
does not rely on a managed native-header layout.

Range transport carries a built-in range OID and flags, followed by up to two
scalar transport records. The outer `auxiliary1 = -2` marker distinguishes it
from arrays. Bounds are length-delimited and pointer-free, including numeric text
payloads; absent bounds and empty ranges have no bound records. Native readers
detoast the range before `range_deserialize`, and native writers use the selected
server's type cache and `make_range` for validation and canonicalization. Numeric
bound payloads are explicitly terminated before calling scalar input routines.

Range operations use the scalar dispatcher's initialized `FmgrInfo`, which
PostgreSQL needs for its per-call type-cache pointer. Parsing and formatting use
OID-based input/output invocation for the same reason. Neither path lets a
PostgreSQL error unwind through a managed frame.

Named, defaulted, variadic and security-definer calls use the same native ABI.
PostgreSQL resolves defaults, assembles variadic arrays, applies function-local
settings and privileges, and suppresses STRICT calls before dispatch. Required
parameters retain the native NULL guard even in mixed nullable signatures.

## Call sequence

### Library initialization

`[PgInitialize]` generates a native `_PG_init` and a separate managed dispatcher.
The native entry first rejects the forking postmaster before any reverse P/Invoke
can initialize Native AOT's runtime threads. Backend and standalone process
initialization are permitted. The wrapper supplies the guarded SPI callback only
when `IsTransactionState()` is true; otherwise the managed scope has no backend
entry point. Nested scopes restore the enclosing binding and callback depth.
Session preload has a transaction before it has a portal or active snapshot.
The initializer pushes a transaction snapshot only when none exists, and pops
only its own snapshot after managed return or native failure. Existing caller
snapshots remain untouched. This permits guarded startup SPI and leaves
PostgreSQL's initial transaction with its original snapshot stack.

PostgreSQL's loader records a library only after `_PG_init` returns successfully.
A native three-state guard detects recursive loading before it can recurse
through the same managed initializer. Failure resets the state so a later load
can retry; success retains the initialized state for the process lifetime.
Diagnostics use the ordinary owned error transport. Managed exceptions return to
native code before error reporting, so `finally` runs normally. SQL mutations
follow PostgreSQL rollback, while managed static mutations survive failed attempts.

Initialization-only assemblies emit module magic, native dependencies, exports,
and a manifest even when their installation SQL has no function declarations.
The initialization callback itself never creates a SQL function.

### SQL function dispatch

1. PostgreSQL invokes the exported C wrapper. The wrapper checks argument count
   and required argument nullability before allocating or invoking managed code.
2. Native argument conversion uses PostgreSQL macros, detoasting, and server-to-UTF-8
   conversion. These operations can raise PostgreSQL ERROR before entering the
   current managed callback. Recursive SPI calls catch these errors in the
   enclosing native SPI guard.
3. The wrapper invokes an assembly/method-specific Cdecl managed callback. The
   callback copies borrowed text and binary buffers into managed objects, invokes
   the user's method, and converts its result to the transport structure.
4. The callback catches managed exceptions, fills the caller-owned error transport
   with owned diagnostics and a bounded fallback, and returns status. No exception crosses the ABI.
5. After managed dispatch returns, the wrapper frees temporary input buffers. If
   the callback failed, the wrapper raises PostgreSQL ERROR. `PgException` supplies
   SQLSTATE, message, detail, and hint; other exceptions use SQLSTATE `38000`.
6. The wrapper converts successful results into PostgreSQL datums. A native
   `PG_TRY`/`PG_FINALLY` block invokes the result buffer's release callback on both
   success and PostgreSQL ERROR, including output encoding and allocation errors.

The `PG_FINALLY` release callback only frees Native AOT-owned native memory. It
does not call PostgreSQL or execute extension user code.

## Ownership

| Value | Owner and lifetime |
|---|---|
| Original input datum | PostgreSQL; borrowed for this invocation |
| Detoasted or encoding-converted input | PostgreSQL allocation; explicitly freed after dispatch when a new allocation was returned |
| Managed input string/array | Managed copy; independent of the PostgreSQL input buffer |
| Managed output string/array | Managed object copied into a native transport buffer |
| Native output buffer | `NativeMemory.Alloc`; released through a callback into its allocating runtime |
| Final output datum | PostgreSQL's current memory context |

On input conversion failure, PostgreSQL's error cleanup owns native input
allocations. Output allocations owned by Native AOT require the explicit release
callback: PostgreSQL's memory contexts cannot reclaim them. Keeping allocation
and release in the same runtime also avoids crossing C runtime heaps on Windows.

## Representation and encoding

`NativeValue` and generated `AnkusValue` use matching sequential fields: 64-bit
integral payload, two 32-bit auxiliary fields, a 32-bit interval-infinity
discriminator, data pointer, 32-bit length, one-byte null flag, and release
function pointer. The integral payload carries integers, booleans, OIDs, or exact
IEEE-754 bits. A `float` is not widened to a `double`, preserving NaN payloads and
signed zero.

Text buffers are length-delimited UTF-8. PostgreSQL handles conversion to and from
the database's encoding. Managed conversion rejects unpaired UTF-16 surrogates;
PostgreSQL text results reject embedded zero characters. Binary buffers preserve
all byte values. NULL and an empty buffer are distinct values.

`pg_detoast_datum_packed`, `VARDATA_ANY`, and `VARSIZE_ANY_EXHDR` handle one-byte,
four-byte, compressed, and external varlena forms. Managed code never assumes the
input datum is aligned or has a particular header size.

UUID buffers contain exactly sixteen network-order bytes. Managed `Guid` conversion
uses the explicit `bigEndian` overloads in both directions. PostgreSQL allocates
the final `pg_uuid_t` in its current memory context.

JSON uses its original text; JSONB is detoasted with alignment preserved and
serialized by native `jsonb_out`. Both travel as UTF-8 text in owned or borrowed
buffers. JSONB serialization and encoding buffers are released alongside detoasted
inputs. Managed wrappers validate and retain the text without numeric coercion.
Native `json_in` / `jsonb_in` construct results and SPI parameter datums, enforcing
the server's syntax, numeric, Unicode, and encoding restrictions. Result-conversion
errors occur after managed dispatch has returned; parameter-conversion errors
remain inside the SPI guard. Both paths release allocator-matched transport buffers.

### Arrays

The outer transport marks an array with `auxiliary1 = -1`. Its single contiguous
buffer contains no pointers: big-endian 32-bit rank, count, and scalar element OID;
one length/lower-bound pair per dimension; then an element header and payload for
each row-major value. Each 28-byte element header contains the integral bits,
auxiliary fields, infinity discriminator, NULL flag, and payload length.

`DatumGetArrayTypeP` handles array detoasting, and `deconstruct_array` uses the
server's element length, alignment, and by-value metadata. Domain elements resolve
to their base type for scalar conversion. Each element reuses the scalar reader,
then releases its PostgreSQL-owned temporary buffers. SPI copies the complete
array transport into one malloc-owned cell buffer, preserving existing result
cleanup even if a later cell fails.

Managed decoding validates shape, bounds, count, lengths, and NULL flags before
creating the requested typed storage. No object-array intermediate or runtime
generic construction is needed. Native output validates the envelope and uses
`construct_md_array` after scalar conversion. Text/binary payloads stay
length-delimited; only JSON/JSONB/numeric input routines receive a terminated
copy. Using a C-string copy for binary elements would truncate embedded zero bytes.

All PostgreSQL allocation and element conversion stays in native frames. SPI
operation contexts reclaim partial arrays on failure. Generated return wrappers
release the one managed transport buffer in `PG_FINALLY`, including failures in
later elements. No per-element malloc pointers need to be recovered across longjmp.

C# vectors accept only empty or one-dimensional, lower-bound-one input. `PgArray<T>`
preserves all dimensions. `params T[]` changes the SQL declaration to `VARIADIC`;
PostgreSQL still supplies one array datum to the same generated wrapper.

## Managed-to-PostgreSQL calls

`Spi.Execute` enters a native function pointer supplied by the generated wrapper.
The binding is thread-local and scoped to that callback. Recursive dispatch saves
and restores the enclosing binding. Calls on worker threads or outside a backend
callback fail in managed code before entering PostgreSQL.

The native SPI guard starts an internal subtransaction and runs the command with
`SPI_connect`, `SPI_execute` or `SPI_execute_with_args`, and `SPI_finish`. Typed
parameters are converted under this guard. On success, it releases the
subtransaction and returns the final statement's processed-row count and any
requested results. Changes remain part of the calling transaction.

Result metadata and cells are copied into native-owned buffers before SPI
disconnects. The runtime materializes managed `SpiResult`, `SpiRow`, and `SpiColumn`
objects, then releases all native result allocations in a managed `finally` block
through their allocator-specific callback. The callback uses only native `free`;
it does not enter PostgreSQL. A native conversion failure releases partially copied
results and rolls back the command. Managed parameter buffers are also released
in `finally` on success, native failure, or managed conversion failure.

Scalar execution copies only the first cell but lets PostgreSQL execute the full
command. Explicit query row limits are passed to SPI separately, so reading a
scalar does not truncate the effects of a command with `RETURNING`.

The SPI entry point accepts a sequential request structure containing the operation,
borrowed command/parameters, execution options, and any owned plan handle. Statement
preparation uses `SPI_prepare` followed by `SPI_keepplan` under the same native guard.
The managed owner is allocated before native preparation, and the kept plan is freed
if the native operation subsequently fails. Execution uses `SPI_execute_plan` and
shares the existing parameter conversion and result-copy path.

`SpiPreparedStatement.Dispose` calls guarded `SPI_freeplan` on the backend thread.
The request clears its handle when ownership is consumed, and the managed owner
observes that update even if a later operation fails. An active-execution count
prevents reentrant disposal while permitting recursive execution. No PostgreSQL
cleanup runs on the finalizer thread. Saved plans have explicit managed disposal;
PostgreSQL retains them in its cache memory context across transaction boundaries.

`Spi.Connect` opens a scoped native SPI connection and closes it in a managed
`finally` block. Native opening retains an internal subtransaction for the scope
so even a partially completed `SPI_connect` can be recovered by rollback. Each
operation within the connection gets a nested subtransaction. Closing releases
the connection and scope subtransaction, restoring the original memory context,
resource owner, and nesting level. Successful commands remain in the enclosing
transaction when the callback exits.

Native session identities are registered in the connection's procedure context;
a memory-context callback removes the registration on cleanup. Managed ownership
checks require the same dispatcher depth and innermost active session, preventing
reentrant code from operating on the wrong native SPI stack frame. Native
operations also validate the session identity before using a plan pointer.

Session-bound plans use `SPI_keepplan` to participate in PostgreSQL's invalidation
list. A native session-owned list frees these plans when its procedure context is
deleted. This differs from unsaved SPI plans, which do not receive relation
invalidation notifications. `Keep()` detaches a plan from session cleanup;
explicit disposal removes a scoped plan from the list before freeing it. A
partially prepared or retained plan is freed by native error recovery. Result
tuple tables are released immediately after copying, so a long-lived session
does not accumulate already materialized batches.

Scoped operations allocate temporary parameters and conversion buffers in an
`Ankus SPI operation` memory context. Success deletes that context before releasing
the operation's subtransaction; failure reclaims it during rollback. This is
necessary because PostgreSQL keeps nonempty `CurTransactionContext` children
after subtransaction commit. Long-lived plans, portal state, and session cleanup
registrations live in their respective owning contexts.

Quoting helpers share the error guard but invoke `quote_identifier`,
`quote_qualified_identifier`, or `quote_literal_cstr` directly, without opening a
SPI connection. Their input conversion and quoted output use the disposable
operation context; the returned UTF-8 fragment is copied into allocator-matched
native result storage before the context is deleted.

EXPLAIN uses the same typed parameter and result conversion as queries. Native
`pg_parse_query` verifies that the prefixed `EXPLAIN (FORMAT JSON)` command has one
statement before SPI executes it. The result is an independently owned `PgJson`.

Managed `SpiRow` edits replace local values and lazily allocated per-cell type OIDs.
The original query's shared column metadata remains available separately. Row
access and mutation use owned managed data and require no PostgreSQL calls.

Nonterminal `PgLog` reports use the same native guard and disposable operation
context. `ThrowErrorData` applies PostgreSQL's routing and context callbacks;
encoding failures or interrupts return to managed code as `PgException`.
`IsEnabled` uses `message_level_is_interesting` on PostgreSQL 14 and newer, with
the corresponding routing checks for PostgreSQL 13. Managed severity values map
to header constants rather than relying on version-specific numbers.

ERROR is a managed `PgException`. FATAL and PANIC use an internal exception that
carries severity and diagnostics to the generated dispatcher. After managed
unwinding, the native wrapper reports the requested terminal level. Tests use
dedicated clusters to verify connection termination and crash recovery.

Cursor operations share the native guard and result-copy path. Opening uses
`SPI_cursor_open_with_args` or `SPI_cursor_open` for prepared plans. Managed cursor
objects carry monotonically assigned identities and copied names rather than
native portal pointers. A native registry entry lives inside the portal's memory
context. Its `MemoryContextRegisterResetCallback` callback unlinks the entry before
the context is reclaimed, including on SQL CLOSE, transaction end, and rollback.
Lookup validates the identity before accessing the portal. Recreating a portal with
the same name or address cannot revive a stale identity.

Fetch direction and count are passed to `SPI_cursor_fetch`; returned cells are copied
before disconnecting SPI. Disposal uses `SPI_cursor_close` when the identity is live
and is harmless when PostgreSQL has already removed the portal. Detach transfers
ownership to the caller retaining the portal name. Managed reentrant operations on
a currently fetching cursor are rejected before entering PostgreSQL. Registry cleanup
is entirely native and does not retain managed objects or invoke managed callbacks.

On PostgreSQL ERROR, native `PG_CATCH` copies diagnostics outside the failing
subtransaction, flushes the error state, rolls back to the caller's transaction
nesting level, and restores the memory context and resource owner. It returns
owned UTF-8 diagnostics to managed code, which throws `PgException`. Managed
`catch` and `finally` blocks therefore execute normally. An extension can catch the
exception and issue another SPI call, or let its generated dispatcher return the
error to PostgreSQL.

Recovery itself has a native guard. An unrecoverable error during recovery
terminates the backend with FATAL rather than jumping across managed frames.

The error transport contains SQLSTATE, query positions, source line, routing flags,
and fourteen optional `NativeValue` string slots: message, detail, hint, context,
schema, table, column, data type, constraint, internal query, file, routine,
server-only detail, and backtrace. Absent and empty strings remain distinct.
Native capture allocates buffers with `malloc`; managed reporting uses
`NativeMemory.Alloc`. Every slot carries its originating allocator's release
callback. Managed capture releases the transport in `finally`, including when
exception construction fails. Native reporting copies all fields into PostgreSQL
memory before releasing the transport, with a native `PG_FINALLY` handling
server-encoding conversion failures.

Diagnostic strings have no fixed transport truncation limit. A separate 2048-byte
UTF-8 primary-message buffer is reserved for allocation/encoding failures;
truncation of that fallback preserves complete characters. A managed exception's
`DiagnosticsIncomplete` property identifies incomplete capture. Native copied
`ErrorData` lives in a temporary context deleted after transport, so repeated
caught errors do not accumulate source-location copies in the caller's context.

New managed errors use `ThrowErrorData` after the dispatcher has returned. Errors
originally captured from PostgreSQL retain their reporting flags and use
`ReThrowError`: PostgreSQL's context callbacks already ran for these errors, so
rerunning them would duplicate procedural and SQL frames. File/routine strings
are copied into `ErrorContext` because PostgreSQL treats source-location pointers
as constants. `DetailLog` stays separate from client-visible `Detail`.

## Implementation files

- `src/Ankus.Generators/PgFunctionEmitter.cs`: generated managed and native entry points.
- `src/Ankus.Generators/NativeBridge.cs`: native transport and varlena conversion.
- `src/Ankus.Generators/GuardedBackend.cs`: native SPI and error-recovery guards.
- `src/Ankus.Generators/NativeSpiBridge.cs`: typed SPI parameter and result conversion.
- `src/Ankus.Generators/NativeArrayBridge.cs`: detoasting, shape validation, and contiguous array transport.
- `src/Ankus.Runtime/NativeArray.cs`: managed array encoding and typed materialization.
- `src/Ankus.Generators/NativeCursorBridge.cs`: portal identities and memory-context invalidation.
- `src/Ankus.Generators/NativeSessionBridge.cs`: scoped connections and session-owned plan cleanup.
- `src/Ankus.Generators/NativeErrorBridge.cs`: diagnostic ownership, capture, and native error reconstruction.
- `src/Ankus.Runtime/NativeValue.cs`: managed transport and output-buffer ownership.
- `src/Ankus.Runtime/NativeBackend.cs`: thread-local backend bindings.
- `src/Ankus.Runtime/SpiPreparedStatement.cs`: retained-plan ownership and execution.
- `src/Ankus.Runtime/SpiCursor.cs`: batched fetching and cursor ownership.
- `src/Ankus.Runtime/SpiSession.cs`: scoped query, preparation, and cursor APIs.
- `src/Ankus.Runtime/PgException.cs`: managed PostgreSQL diagnostics.
- `tests/Ankus.IntegrationTests/DatumConversionTests.cs`: backend conversion tests.
- `tests/Ankus.IntegrationTests/SpiTests.cs`: transaction, reentrancy, and error-unwinding tests.
- `tests/Ankus.IntegrationTests/SpiQueryTests.cs`: parameter, metadata, and result-lifetime tests.
- `tests/Ankus.IntegrationTests/SpiPreparedTests.cs`: retained plans, invalidation, and native cleanup tests.
- `tests/Ankus.IntegrationTests/SpiCursorTests.cs`: cursor batching, portal lifetime, and error cleanup tests.
- `tests/Ankus.IntegrationTests/SpiSessionTests.cs`: nested connections, scoped/retained plans, and failure cleanup.
- `tests/Ankus.IntegrationTests/PgDiagnosticTests.cs`: native/managed diagnostics and encoding/rethrow tests.
- `tests/Ankus.Runtime.Tests/NativeDiagnosticTests.cs`: optional values, emergency fallback, and transport cleanup.

## Enum datums

Generated enum module initializers register closed scalar/nullable/array
conversions without reflecting over members. Managed values carry C# enum
identity; native transport carries exact UTF-8 labels. PostgreSQL `enum_in` and
`enum_out` perform label/OID conversion, including visibility checks on newly
added labels. C# numeric values are never interpreted as stored enum OIDs.

The native dispatcher saves and restores the current function OID around each
managed callback. Guarded enum resolution finds fixed schema names or the owning
extension's current catalog namespace, with a function-namespace fallback for
functions installed outside an extension. Recursive calls restore the enclosing
function identity. Lookups are not cached across queries or DDL; materializing
one immutable SPI result may reuse its resolved mapping per column.

Catalog inspection copies labels, type/value OIDs and sort positions before
releasing native storage. SPI results, array elements and function results share
the enum converters. Managed array conversion checks exact CLR enum identity
before primitive-array patterns, because the CLR permits casts between some enum
arrays and arrays of their underlying integer type.

Generated installation SQL is UTF-8. Control files declare `encoding = 'UTF8'`
so PostgreSQL transcodes labels, names and custom SQL for the target database.

## Set-returning functions

Generated enumerable callbacks initialize a typed managed iterator, advance it,
and dispose it through a GCHandle. PostgreSQL owns the native state in its
multi-call context. An expression-context shutdown callback disposes the iterator
on normal early termination; a memory-context reset callback covers abort paths
where PostgreSQL deliberately skips expression callbacks. The handle is cleared
and released before invoking user disposal, preventing repeat cleanup after an error.

Every callback saves and restores the active function OID and managed backend
binding. Early expression shutdown supplies an active snapshot when the executor
has already removed it. Abort cleanup denies queries but permits release of owned
SPI plans and cursors through the native guard without starting a subtransaction.
The existing nested recovery guard also covers failures during that cleanup.
Cursor release during rollback marks a stable registry entry for closure. Native
transaction callbacks drain it after PostgreSQL's portal scan; a transaction-memory
reset callback covers cleanup queued during the later portal deletion scan.
Already-deleted portals invalidate their entries. Surviving parent-transaction
cursors are closed without executor callbacks against failed transaction state.
Active or pinned portals remain queued until safe cleanup. No cursor hash entry
is removed from inside PostgreSQL's abort scan.

Managed rows use the same conversion expressions as scalar functions. Native
TABLE conversion selects each tuple descriptor attribute's OID, preserving enum
and array identity independently of the overall RECORD return type. Output buffers
are released in native `PG_FINALLY` blocks on successful conversion and errors.
One-column TABLE results use the scalar datum ABI, as PostgreSQL requires.

Value-per-call execution returns a single row and leaves the iterator rooted for
the next call. Materialization writes to a PostgreSQL tuple store owned by the
query context, resetting temporary conversion storage between rows and allowing
`work_mem`-controlled spill. Interrupt checks occur before each iterator advance.

## Trigger callbacks

Trigger entry points validate `CALLED_AS_TRIGGER` and expose twelve `AnkusValue`
slots: event bits, relation OID, trigger OID, trigger/table/schema names, optional
OLD/NEW transition aliases, text arguments, optional OLD/NEW tuples, and the
relation descriptor. The managed reader copies and validates every value.
PostgreSQL's trigger structures remain native and are compiled against the
selected server headers.

The return ABI is a `HeapTuple` pointer with `fcinfo->isnull = false`, including
a NULL pointer that skips a row. It is distinct from a composite datum's
`HeapTupleHeader`. INSERT/UPDATE replacements validate the relation's physical
layout and apply field conversions before `heap_modify_tuple` preserves tuple
identification. The resulting tuple is copied into the caller's memory context.
AFTER results and nonnull DELETE payloads are ignored before serialization;
BEFORE statement triggers cannot return a tuple.

Undefined generated fields carry tuple attribute flag 8 and a NULL transport
payload. Managed access rejects these fields rather than exposing SQL NULL.
Trigger reconstruction preserves PostgreSQL's generated slots, while ordinary
composite reconstruction rejects unavailable attributes. Stored NEW generated
columns are recomputed by PostgreSQL after BEFORE callbacks.

A native trigger scope owns temporary input and transition query-environment
storage. Each new SPI connection registers the active `TriggerData` once, with
the callback context as the allocation context. This matters because SPI cursors
retain the query environment after `SPI_finish`. Such portals receive the
trigger scope's identity and close before that scope ends, including detached
portals. The active trigger context remains bound during iterator disposal so
its `finally` blocks can still query transition tables. A native `PG_FINALLY`
restores the enclosing scope and function identity on success or failure.

If portal cleanup raises, the callback context remains owned by its caller until
PostgreSQL abort cleanup has released the surviving portals. Deleting it
unconditionally on that path would invalidate their borrowed query environments.
Mutable result/error headers live in the callback memory context, so cleanup
can read them safely after `longjmp`; their pointers are assigned before
`PG_TRY`. Managed buffers retain their allocator-matched cleanup. Retained
plans keep the plan, not a transition table: execution uses the current SPI
query environment, and PostgreSQL's original error is transported when a required
named tuplestore is absent.

## Event trigger callbacks

Event entry points validate `CALLED_AS_EVENT_TRIGGER` and zero SQL arguments
before reading `EventTriggerData`. The native header owns the struct layout and
`CommandTag` enum; `GetCommandTagName` supplies the command text. Two borrowed
UTF-8 text slots carry the event name and command tag. The managed entry copies
them into a validated context, invokes the void callback, then restores the
enclosing managed event and backend scopes. Login events need no parse tree.

Each callback owns a temporary native memory context, including the mutable
result/error headers that cleanup reads after `longjmp`. The wrapper saves and
clears the current row-trigger scope so event SPI cannot inherit an outer row
trigger's transition query environment. Native `PG_FINALLY` restores that scope,
the function OID, and the caller's memory context on every path, then releases
managed result/error buffers and native temporary storage. The result is Datum
zero with `fcinfo->isnull = false`. Protocol errors use PostgreSQL's `39P03`.

Typed DDL/drop/rewrite helpers execute explicit projections of PostgreSQL's
metadata functions through guarded SPI. They copy nullable identities and
address arrays into immutable snapshots; the opaque `pg_ddl_command` column is
never transported. Thread-local context identity and event-phase checks prevent
old or suspended contexts from reading a newer native invocation. Snapshot
property access needs no live backend. As with row triggers, PostgreSQL ERROR
can be raised only after managed frames have returned to the native boundary.

## Aggregate callbacks and state ownership

Generated aggregate support functions use `AggCheckCallContext` before reading
arguments. Each call resolves the current aggregate or window memory context;
ownership is never cached under `fn_extra`, because grouping sets can interleave
states at one call site. SQL datum states use the existing owned conversions and
PostgreSQL's executor copies. Managed `PgAggregateState<T>` values use SQL
`internal` and an opaque checked managed root ID.

Native state headers live in their owner's context and register a
`MemoryContextRegisterResetCallback`. A backend-local address registry validates
headers before dereferencing them, so a foreign `internal` pointer cannot be
interpreted as an Ankus header. Reset removes the registry entry, invalidates the
managed root, and invokes optional payload disposal exactly once. Reset covers
ordinary group teardown, rescans, moving-window restarts, and abort. Finalization
does not own cleanup. Cleanup suspends the aggregate scope and permits only
owned plan/cursor release through the existing restricted backend boundary;
managed cleanup exceptions become warnings after the callback returns.

Deserialize wrappers omit PostgreSQL's mandatory SQL `internal` dummy from
managed arguments. PostgreSQL passes a zero pointer with SQL nullness set false
for that dummy. Deserialized state is registered in the caller's temporary
context; combine must copy its values into a fresh destination state when
necessary. Both runtime and native return validation reject cross-owner reuse.

Callback metadata copies aggregate/window kind, shared-state status, collation,
an optional aggregate OID, and sort keys derived from `AggGetAggref`. Window
contexts have no aggregate parse node. Native `SortSupport` performs comparisons
using the actual ordering operator, collation and NULL placement. Comparison
calls run in a guarded subtransaction so a custom operator error can be caught
by managed code after native recovery. Successful subtransaction commit also
changes PostgreSQL's current memory context, so comparison restores the saved
caller context and resource owner before returning to managed code. State registration has its own native
error guard and cannot unwind through a managed allocator call.

Callback result/error headers and mutable conversion buffers are heap-backed
before `PG_TRY`. The wrapper restores the prior function and aggregate scopes in
`PG_FINALLY`, releases transport buffers and deletes only callback-temporary
storage. SQL results are constructed in the caller's memory context before that
temporary context is deleted. Registered managed states retain their separate
aggregate or deserialize owner until its reset callback runs.

## Memory-context capability

Every generated managed callback also receives a synchronous memory-operation
envelope. Its provider token identifies the extension's native registry rather
than a callback stack address. Thread-local managed binding selects the current
envelope and restores the enclosing envelope on return. Checked context and
allocation identities can therefore outlive a callback when PostgreSQL retains
their owner, without keeping a pointer to an expired native stack frame.

The memory bridge invokes PostgreSQL directly under a native error guard. It
does not allocate in an SPI subtransaction or an operation work context. On error
it copies owned diagnostics into a temporary child of `TopMemoryContext`, resets
PostgreSQL's error state, and restores the context selected at operation entry.
If that context is an `ErrorContext` descendant, flushing deletes it and recovery
selects the surviving `ErrorContext` instead of restoring a freed pointer.
The diagnostic owner must remain outside `ErrorContext`, even when that context
is current: `FlushErrorState` resets it. The memory and SPI guards also restore
the entry values of `InterruptHoldoffCount` and `QueryCancelHoldoffCount`, which
PostgreSQL ERROR clears. Abort cleanup can catch native errors inside an existing
interrupt holdoff section. A caught allocation error leaves ordinary selected
contexts and their original chunks intact; storage under `ErrorContext` follows
PostgreSQL's error-flush lifetime.

Validation metadata and owned context identifiers use the extension's C runtime
allocator. PostgreSQL owns the actual chunks. Context reset callbacks remove
allocation records before native storage can be reused; monotonically increasing
identities prevent a recycled address from reviving a stale handle. Explicit
resets retain the selected registry entries and re-register one-shot invalidators.
Implicit native cleanup invalidates those entries. An explicitly owned context's
name remains outside its resettable storage and is released with its registry
entry. Name transport converts between UTF-8 and server encoding and copies into
a pinned managed destination before releasing conversion storage.

Allocation records retain their exact requested byte length, huge size policy,
and alignment. Aligned pointers use the selected PostgreSQL headers' redirect
chunk allocator (PG16+), so native owner lookup and `pfree` remain compatible.
Checked payload and padding arithmetic precedes the native call. Aligned no-OOM
calls require the upstream fixes in 16.15, 17.11, 18.6, or 19 beta 3. Because
PostgreSQL 19 prereleases share a numeric version, beta labels are checked too;
ambiguous development snapshots are rejected for aligned no-OOM calls.
Aligned or no-OOM resize allocates a replacement, copies only the known prefix,
then frees the original after success. Ordinary throwing resize uses `repalloc`
or `repalloc_huge`. Tail clearing starts at the old requested byte length.

Detach removes only the allocation registry entry. Adoption requires caller-proven
live native pointer provenance and exclusive ownership, checks the actual owner,
and registers a new identity without freeing the pointer on failure. AllocSet
creation validates supplied block sizes against target-header alignment and
version-specific chunk-offset limits before entering assert-only native checks.

Typed boxes and tracked borrowed views share the existing checked allocation
identity. Transferring an individual owner to context ownership retains that
identity; raw detach consumes it. Raw borrowed addresses use a separate guarded
copy operation with the supplied context identity and captured reset generation.
They require no allocation-header lookup, release rights, or per-view native
record. Explicit reset advances the generation; implicit cleanup removes the
context identity. Generation exhaustion permanently retires new raw borrows
without wrapping back to a live generation. The unsafe caller still proves
address validity and any external lifetime shorter than the context's lifetime.

Deletion checks current-context ancestry. A native protection stack retains
explicit reset/delete roots and the actual iterator/aggregate state owners across
managed callbacks. It prevents overlapping destructive operations, child creation
in teardown trees, and switching into a context being reclaimed. Each entry is
removed in `PG_FINALLY`; nested independent resets retain only their own registry
entries. PostgreSQL infrastructure contexts are protected separately because
their transaction and backend metadata can outlive the active executor tree.
Managed finalizers never invoke PostgreSQL. Context and allocation disposal are
deterministic and native cleanup makes later disposal harmless.

`PgMemoryCallback` registers a native one-shot record with a monotonic managed
root ID. Both registries remove that ID before entering user code; cancellation
releases the action immediately and leaves an inert native record until reset.
This protocol also supports PostgreSQL 13–18, which lack the unregister function
added in PostgreSQL 19. Native records are independent of context registry node
lifetime. A managed exception is transported back to native code and raised as
ERROR, preserving PostgreSQL's pending older callbacks and retry behavior.
The managed dispatcher masks SQL/logging and inherited GUC/aggregate capabilities, permits owned plan/cursor release
through the binding captured at registration, and restores enclosing capabilities.
It also captures a transaction context only when that context is an ancestor of
the callback owner. That ancestor stays alive through the callback and receives
deferred cursor cleanup. During late subtransaction deletion PostgreSQL has
already changed `CurTransactionContext` to the surviving parent; using that
global would postpone an adopted parent cursor's close until the outer
transaction ends. Native portal scans still require deferred closing.

If abort cleanup itself raises ERROR, PostgreSQL reports that error after the
primary SQL error and retries the remaining cleanup. Npgsql retains the last
ErrorResponse before ReadyForQuery. Backend tests assert both ordered server-log
diagnostics and the final client exception, followed by same-session recovery.

ErrorContext-owned callbacks inherit a separate error-handler cleanup flag.
Both native guards return a direct `55006` (object in use) diagnostic before entering PostgreSQL
while that flag is set. PostgreSQL's private error-stack and recursion counters
cannot be saved through its public API: swapping the `ErrorContext` pointer would
not isolate them. A nested caught error could otherwise flush an outer reporter's
state or reclaim an owner still referenced by the reset stack. This matters for
ordinary error flushing on every supported version and normal outermost reporting
on PostgreSQL 19. The flag is restored in `PG_FINALLY`; managed callback failures
still become native ERROR only after the managed frame has returned.

## Configuration hook capabilities

GUC callbacks receive separate native pointers for typed setting reads, logging,
and transaction-bound operations. Managed thread-local scopes restore all three
bindings in reverse order, including on exception. Assign and show receive no SQL
executor. Checks receive one only when PostgreSQL has a valid transaction.

The logging callback supports threshold checks and nonterminal reports. It borrows
the managed diagnostic buffers for the duration of the call and returns separately
owned errors. Native `PG_TRY` catches conversion and reporting errors below managed
frames; a temporary context owns converted fields and is deleted on success or
failure. Cached database converters allow logging during abort, file reload, and
parameter reporting without catalog lookup, SPI, or a subtransaction.

ERROR, FATAL, and PANIC first unwind managed code. Ordinary hook errors follow
PostgreSQL's phase constraints: check rejection, assign FATAL, transactional show
ERROR, and nontransactional show FATAL. Explicit FATAL/PANIC requests keep their
severity, including if converting their diagnostic text fails. A backend which
cannot copy an error safely terminates rather than returning through managed code
with an active PostgreSQL error stack.

Error copies retain their source and translation metadata before the native error
stack is reset. PostgreSQL 13–16 leave five source/translation pointers borrowed
in `CopyErrorData`; the bridge copies them explicitly. PostgreSQL 17+ copies those
strings itself. Paired cleanup frees the owned copies on all supported versions,
including fields that PostgreSQL's `FreeErrorData` treats as constant.

Assembly prefix declarations run in native initialization after definitions and
before managed initialization. PostgreSQL 15+ uses `MarkGUCPrefixReserved`;
13–14 use `EmitWarningsOnPlaceholders`. Prefix-only libraries need neither a
managed callback nor the SQL execution bridge. Prefix conversion uses the database
encoding for backend loading and restricts shared-preload text to ASCII.
