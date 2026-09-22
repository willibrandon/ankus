# Native datum boundary

Ankus generates both sides of the PostgreSQL/Native AOT boundary. The C wrapper
is compiled against the selected server's headers and linked into the extension's
native library. Extension authors write attributed C# methods.

## Call sequence

1. PostgreSQL invokes the exported C wrapper. The wrapper checks argument count
   and required argument nullability before allocating or invoking managed code.
2. Native argument conversion uses PostgreSQL macros, detoasting, and server-to-UTF-8
   conversion. These operations can raise PostgreSQL ERROR before entering the
   current managed callback. Recursive SPI calls catch these errors in the
   enclosing native SPI guard.
3. The wrapper invokes an assembly/method-specific Cdecl managed callback. The
   callback copies borrowed text and binary buffers into managed objects, invokes
   the user's method, and converts its result to the transport structure.
4. The callback catches managed exceptions, writes a bounded diagnostic into the
   caller-owned error buffer, and returns status. No exception crosses the ABI.
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
integral payload, data pointer, 32-bit length, one-byte null flag, and release
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
