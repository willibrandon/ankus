---
title: Memory contexts
description: Allocate PostgreSQL-owned native memory with checked context and allocation lifetimes.
---

`PgMemoryContext` represents a PostgreSQL memory context. Use `Create` to own a
child context and `PgAllocation` to copy unmanaged values or byte buffers into
its native storage:

```csharp
[PgFunction]
public static int NativeValue(int value)
{
    using PgMemoryContext context = PgMemoryContext.Create("native value");
    using PgAllocation allocation = context.AllocateZeroed(sizeof(int));
    allocation.Write(value);
    return allocation.Read<int>();
}
```

These APIs require a synchronous callback on PostgreSQL's backend thread. They
are available independently of SPI, including initialization and configuration
hooks. They do not enable database queries in callback phases where PostgreSQL
prohibits queries. Do not call them from a worker thread or after `await`.

## Context ownership

`Current` resolves PostgreSQL's current context at the point of the call.
`Get(PgMemoryContextKind)` resolves predefined contexts such as `Top`,
`TopTransaction`, and `CurTransaction`; it returns null when the selected context
does not exist in the current phase. `Parent` returns a borrowed parent handle.

`Create(name, parent, options)` creates an AllocSet child without changing the current
context. Omit `parent` to use the current context. Dispose an owned context to
delete it and its descendants. Disposing a borrowed handle leaves the native
context intact. Contexts have no finalizers: the garbage collector must never
call PostgreSQL from its finalizer thread. PostgreSQL still reclaims a context
when its parent is deleted, even if a managed handle remains reachable.

`PgMemoryContextOptions` supplies the minimum retained context size, initial block
size, and maximum block size. Its `Default`, `Small`, and `StartSmall` presets match
PostgreSQL's AllocSet presets. Sizes must satisfy the selected server's native
alignment and range limits; they need not be powers of two. Invalid sizes throw
before reaching PostgreSQL's allocator assertions.

Use `Run` to select a context temporarily:

```csharp
using PgMemoryContext context = PgMemoryContext.Create("temporary work");
int result = context.Run(() =>
{
    using PgAllocation value = PgMemoryContext.Current.Allocate(sizeof(int));
    value.Write(42);
    return value.Read<int>();
});
```

`Run` restores the previous context on ordinary return and managed exceptions.
It supports nested calls. Deleting the current context or an ancestor is
rejected; after leaving the scope, disposal can be retried. Native callback
storage and its ancestors cannot be reset while the callback is active.
PostgreSQL's infrastructure contexts, including its transaction, portal, cache,
message and error contexts, cannot be reset or deleted through these checked
APIs. Create a child context to own storage that your extension can reclaim.

PostgreSQL error recovery resets `ErrorContext` and deletes its children. If a
guarded operation fails while one of those children is current, Ankus resumes in
the surviving `ErrorContext`; the child and its allocations become stale. The
enclosing `Run` restores its original caller when that caller remains alive.

Handles may be reused in a later synchronous callback from the same extension
and backend while their native owner remains alive. A child of `TopTransaction`
survives multiple SQL calls in that transaction. A child of `CurTransaction`
expires when its subtransaction is rolled back. `IsAlive` checks native context
identity; transaction cleanup and reuse of a native address cannot revive an old
handle. Borrowed contexts reset implicitly by PostgreSQL must be resolved again.

`RunTransient` combines creation, selection, restoration, and deletion:

```csharp
int result = PgMemoryContext.RunTransient("temporary values", context =>
{
    using PgAllocation values = context.CopyFrom<int>([10, 20, 30]);
    return values.Read<int>(sizeof(int));
}, options: PgMemoryContextOptions.Small);
```

Return copied managed data. Checked handles that escape become stale after
successful deletion. The helper attempts deletion after both normal return and
exceptions, preserving callback, restoration, and deletion failures together.
If a cleanup callback throws, older callbacks and the context can remain pending;
the parent still owns that native context until cleanup is retried.

## Reset and allocation lifetime

| Operation | Selected context | Descendant contexts |
| --- | --- | --- |
| `Reset()` | Retained; allocations invalidated | Deleted |
| `ResetOnly()` | Retained; allocations invalidated | Unchanged |
| `ResetChildren()` | Unchanged, including allocations | Retained; allocations invalidated |
| Owned `Dispose()` | Deleted | Deleted |

An affected allocation rejects later reads, writes, owner lookup, and resizing
with `ObjectDisposedException`. Disposing an already reclaimed allocation or
owned context is harmless. Newly allocated bytes use a new identity. Context
names remain readable across explicit resets.

`Allocate` returns uninitialized storage; write it before reading. `AllocateZeroed`
clears every byte. `TryAllocate` uses PostgreSQL's no-OOM allocation flag and
returns null when that allocator returns null. Invalid sizes and other native
errors still throw `PgException`. Allocation, resize, and free use PostgreSQL's
matching allocator, and errors are caught in native code before managed execution
resumes.

`Allocate<T>(count)`, `AllocateZeroed<T>(count)`, and `TryAllocate<T>(count)`
check multiplication by `sizeof(T)` before native access. `CopyFrom` copies a byte
span or an unmanaged value span into independent native storage. These APIs copy
unmanaged bytes: they do not infer SQL types or guarantee that an arbitrary .NET
struct matches a PostgreSQL C layout. Allocation lengths and access offsets
remain measured in bytes.

Use `PgAllocationOptions.Zeroed` to clear initial storage and `Huge` to permit
PostgreSQL's larger allocation size limit. Huge allocations still depend on
available memory. The `alignment` argument accepts zero for native default
alignment, or a power of two below 128 MiB. Alignment above the server's native
default requires PostgreSQL 16 or later. Aligned `TryAllocate` and `TryReallocate`
require PostgreSQL 16.15, 17.11, 18.6, or 19 beta 3 or later because earlier releases have
unsafe native no-OOM handling. PostgreSQL 19 development snapshots are also
rejected for these no-OOM requests because their version cannot establish the fix.
Unsupported requests throw a feature error.

`AllocateUtf8String` copies strict UTF-8 bytes followed by one zero byte. It rejects
embedded NUL and malformed UTF-16. This is raw UTF-8 copying, including in a
LATIN1 database; it does not convert to the database encoding.

`Read` and `Write` copy spans or unmanaged values after checking the live chunk
and its bounds. `Reallocate` preserves the existing prefix when it succeeds;
an allocation error leaves the old chunk usable. `Clear` clears a selected range;
omitting its length clears through the end. Zero-length allocations and empty
copies are supported. `Context` queries the chunk's native owner.

Resizing preserves the original huge policy and alignment. Pass
`zeroNewMemory: true` to clear only the newly added tail; shrinking is still
allowed. `TryReallocate` returns false only when the native allocator returns
null, preserving the old storage, length, and contents. Invalid sizes and other
native failures still throw. `Options` and `Alignment` describe the original
allocation policy; initial zeroing does not automatically zero future growth.

`GetAllocatedBytes()` includes descendant context storage and native allocator
overhead. `IsEmpty` returns PostgreSQL's native emptiness result. Registering the
invalidation callback marks a context nonempty, including after an explicit reset,
so this property does not establish whether all user allocations were freed.
These statistics describe native contexts, not the managed heap.

## Cleanup callbacks

`RegisterResetCallback(Action)` connects managed cleanup to PostgreSQL's native
context lifetime. The returned `PgMemoryCallback` remains pending until the
context resets or is deleted, or until you cancel the registration:

```csharp
using PgMemoryContext context = PgMemoryContext.Create("context cleanup");
var events = new List<string>();
PgMemoryCallback first = context.RegisterResetCallback(() => events.Add("first"));
PgMemoryCallback second = context.RegisterResetCallback(() => events.Add("second"));

context.Reset(); // events contains "second", then "first"
bool pending = first.IsPending || second.IsPending; // false
```

Callbacks run once in reverse registration order within each context. PostgreSQL
also invokes callbacks registered by a callback during the same drain. The action
is rooted even if application code drops its registration object. Its root is
released and `IsPending` becomes false before user code runs, so self-disposal
does not cancel or invoke the action again. `Dispose()` cancels a pending action
without running it; repeated disposal is harmless. Cancellation releases managed
references immediately, while a small native cancellation record remains until
the context next resets or is deleted. Registrations have no finalizer.

An exception consumes that callback and stops the native drain. Older callbacks
remain pending; retrying reset or deletion invokes them. Already deleted child
contexts stay deleted. The diagnostic becomes a PostgreSQL error only after the
managed callback returns. An explicit managed `Reset()` or owned `Dispose()`
receives a `PgException`; implicit PostgreSQL cleanup can report the error to the
SQL caller, including during statement or transaction completion. Write cleanup
actions so they can release their own resources without throwing. If cleanup
throws while PostgreSQL is already handling a SQL error, the server reports the
cleanup error too. Npgsql reports the last error it receives before the backend
becomes ready; the server log retains both diagnostics.

Cleanup permits checked native allocation access and disposal of retained SPI
plans and cursors when the registration was created with backend access. Queries,
logging, configuration reads, and aggregate operations are unavailable. The resetting context's payload remains readable
until its invalidation callback runs. Destructive operations on the active owner
or its ancestors, switching into the context being reclaimed, and child creation
within the active cleanup tree are rejected. Independent contexts can still be
used and reset. Iterator and aggregate cleanup has the same native owner
protection, including during transaction abort.

A callback owned by `ErrorContext` or one of its children runs inside the error
handler's cleanup phase. It can release managed resources, but guarded memory
operations and SPI resource disposal return SQLSTATE `55006` in that phase.
Those operations could otherwise consume PostgreSQL's active error stack or
recursively reclaim the callback's own storage. A failed disposal leaves the
resource available for a later allowed phase. Choose an ordinary owned context
for cleanup that needs native memory or SPI resources. PostgreSQL 19 also resets
`ErrorContext` after an emitted outermost notice or warning; earlier supported
versions reset it during error recovery.

Unsafe interop can use `DangerousGetPointer()`. The pointer is valid only until
free, resize, or context cleanup. The caller must enforce its lifetime and
alignment; checked managed operations do not make a retained raw pointer safe.

`DangerousDetach()` consumes the checked owner and transfers its raw pointer
without freeing the allocation. PostgreSQL still owns the storage through its
context. A recipient can take exclusive individual ownership with
`context.DangerousAdopt(pointer, length, huge, alignment)`. Adoption checks the
native context and rejects a pointer that already has a checked owner. Failed
adoption leaves ownership with the caller.

These are unsafe interoperability operations: the caller must guarantee a live
`palloc`-family pointer compatible with `pfree`, the accessible byte length,
original alignment and size policy, and exclusive ownership. A context lookup
cannot validate an arbitrary pointer. Do not free or resize a raw pointer while
a checked owner remains live. Context-only allocators such as PostgreSQL's Bump
context do not provide the individual-free contract required here.
