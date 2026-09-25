---
title: Tuple locations
description: Preserve PostgreSQL tid values and work with checked native ItemPointerData storage.
---

`PgItemPointer` represents PostgreSQL's `tid` type, including the `ctid` system
column. It stores an unsigned 32-bit block number and an unsigned 16-bit offset.
It is an immutable managed value; constructing or comparing one needs no backend.

```csharp
[PgFunction]
public static PgItemPointer? EchoLocation(PgItemPointer? value) => value;
```

```sql
SELECT echo_location('(42,3)'::tid); -- (42,3)
SELECT echo_location('(0,0)'::tid);  -- (0,0), a present invalid value
SELECT echo_location(NULL::tid);    -- NULL
```

A tuple's physical location can change when PostgreSQL moves or rewrites a row.
Use a logical key when you need a permanent row identifier.

## Raw fields and validity

`BlockNumber` and `OffsetNumber` always expose the exact raw fields, including
PostgreSQL's special values. `IsValid` checks only whether the offset is nonzero,
matching the native item-pointer check. It does not prove that a row exists at
that location or that its offset fits a particular page.

`GetBlockNumber()` and `GetOffsetNumber()` require a nonzero offset and otherwise
throw `InvalidOperationException`. `PgItemPointer.Invalid` is `(4294967295,0)`;
the default value `(0,0)` is also invalid. Both remain non-NULL SQL values.
Use `PgItemPointer?` to represent SQL NULL.

`MovedPartitions` and `IndicatesMovedPartitions` expose PostgreSQL's
`(4294967295,65533)` marker. Other special offsets, including speculative
insertion tokens, retain their bits through ordinary construction and conversion.
Use record deconstruction or `with` expressions to read or replace fields:

```csharp
PgItemPointer location = new(42, 3);
(uint block, ushort offset) = location;
PgItemPointer nextOffset = location with { OffsetNumber = 4 };
```

Equality and ordering compare the unsigned block, then the unsigned offset,
including invalid values. `ToString()` produces invariant `(block,offset)` text.
`Increment()` and `Decrement()` follow the complete raw field range, carrying
between offset and block and saturating at its endpoints. They can produce an
offset of zero; they do not enumerate actual rows.

## Two packed representations

pgrx's helper and PostgreSQL's index helper use different encodings:

| API | Encoding | Invalid offset zero |
| --- | --- | --- |
| `ToUInt64()` | Block shifted by 32 bits, OR offset; bits 16–31 are zero | Preserved |
| `ToIndexKey()` | Block shifted by 16 bits, OR offset; a nonnegative 48-bit `long` | Rejected |

`FromUInt64()` rejects nonzero bits in the unused middle region.
`FromIndexKey()` rejects negative values and values above 48 bits. Both decoders
preserve offset zero. The explicitly named `FromUInt64Truncating()` and
`FromIndexKeyTruncating()` reproduce the raw reference helpers when discarding
unused bits is intentional.

These encodings are not the in-memory C struct layout or PostgreSQL's six-byte
network representation. Ankus uses the selected server headers to convert native
fields; no managed struct packing is assumed.

## SQL, arrays and copied values

The scalar mapping also works in [arrays](/arrays/), SPI parameters and results,
composite fields, sets, [function calls](/calling-functions/), and supported
[`PgDatum.Read<T>()`](/raw-values/) conversions. `tid` and `tid[]` keep their
native OIDs, 27 and 1010. `PgArray<PgItemPointer?>` retains dimensions, lower
bounds and NULL cells. A required `PgItemPointer[]` rejects NULL elements;
an ordinary vector also rejects a shape it cannot represent without losing bounds.

Managed values are independent copies. They remain usable after the native
input's callback or memory context ends. Raw `PgDatum` values still follow their
own checked native lifetimes and exact type identity.

## Native storage and ownership

Use `PgNativeItemPointer` when a native API needs an actual `ItemPointerData`:

```csharp
using PgMemoryContext context = PgMemoryContext.Create("tuple location");
using PgNativeItemPointer native = PgNativeItemPointer.Create(new(42, 3), context);
using PgNativeItemPointer borrowed = native.Borrow();
borrowed.Value = new(42, 4);
PgItemPointer copy = native.Value;
```

`Create` allocates in the specified context, or the current context when omitted.
`Value` copies the two fields in either direction. `LifetimeContext` returns the
checked owner. `CloneInto(context)` returns another individually disposable owner
with independent native storage. Native errors are caught below managed frames
and transported as `PgException`.

Disposing an owner frees its allocation and invalidates its checked borrows.
Disposing a borrowed view closes that wrapper. Native context reset, deletion,
and transaction cleanup invalidate affected access with `ObjectDisposedException`.
Failed native free retains ownership for retry. Dispose on the backend thread;
there is no finalizer that calls PostgreSQL. Individually owned creation rejects
Bump contexts; Slab allocation must satisfy the allocator's fixed chunk size.

For unsafe native integration, `DangerousGetPointer()` returns a checked address.
`DangerousDetach()` closes the wrapper. Detaching an owner transfers its release
obligation to the caller or native context and invalidates its checked borrows.
Detaching a borrowed view grants no release rights.

`DangerousBorrow(address, lifetimeContext)` accepts an initialized native
`ItemPointerData`, including stack or interior storage. A null address returns
null without backend access. The context is a reset-sensitive lifetime anchor;
it need not own the bytes. The caller must guarantee the selected server's layout,
accessible storage, and any shorter lifetime such as stack return or external
free. Use `Borrow()` for views of a checked owner so that individual disposal
is also observed. Do not use `PgNativeBox<PgItemPointer>` as a native tid layout:
that generic API copies managed representation bytes.
