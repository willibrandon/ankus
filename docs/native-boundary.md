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
`SPI_connect`, `SPI_execute`, and `SPI_finish`. On success, it releases the
subtransaction and returns the final statement's processed-row count. Changes
remain part of the calling transaction.

On PostgreSQL ERROR, native `PG_CATCH` copies diagnostics outside the failing
subtransaction, flushes the error state, rolls back to the caller's transaction
nesting level, and restores the memory context and resource owner. It returns
bounded UTF-8 diagnostics to managed code, which throws `PgException`. Managed
`catch` and `finally` blocks therefore execute normally. An extension can catch the
exception and issue another SPI call, or let its generated dispatcher return the
error to PostgreSQL.

Recovery itself has a native guard. An unrecoverable error during recovery
terminates the backend with FATAL rather than jumping across managed frames.

The diagnostic transport reserves 2048 bytes each for the message and detail,
and 1024 bytes for the hint, including terminators. Truncation preserves complete
UTF-8 characters. Native reporting converts these fields to the server encoding.

## Implementation files

- `src/Ankus.Generators/PgFunctionEmitter.cs`: generated managed and native entry points.
- `src/Ankus.Generators/NativeBridge.cs`: native transport and varlena conversion.
- `src/Ankus.Generators/GuardedBackend.cs`: native SPI and error-recovery guards.
- `src/Ankus.Runtime/NativeValue.cs`: managed transport and output-buffer ownership.
- `src/Ankus.Runtime/NativeBackend.cs`: thread-local backend bindings.
- `tests/Ankus.IntegrationTests/DatumConversionTests.cs`: backend conversion tests.
- `tests/Ankus.IntegrationTests/SpiTests.cs`: transaction, reentrancy, and error-unwinding tests.
