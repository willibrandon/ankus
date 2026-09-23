using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Ankus;

/// <summary>
/// Represents a checked PostgreSQL memory context accessed from synchronous backend callbacks.
/// </summary>
/// <remarks>
/// A function, operator, or cast can declare this type as a by-value parameter to receive
/// a borrowed context without adding a SQL argument. Scalar functions receive the current
/// context; set factories receive their multi-call context. The handle keeps that identity
/// across ambient context switches and becomes stale when PostgreSQL reclaims its owner.
/// Nullable annotations and optional managed defaults do not make the injected context null.
/// </remarks>
public sealed unsafe partial class PgMemoryContext : IDisposable
{
    private static readonly UTF8Encoding s_utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly nint _provider;
    private readonly nint _id;
    private readonly bool _owned;
    private bool _disposed;

    private PgMemoryContext(nint provider, nint id, bool owned)
    {
        _provider = provider;
        _id = id;
        _owned = owned;
    }

    /// <summary>
    /// Gets the callback's current PostgreSQL memory context.
    /// </summary>
    /// <remarks>
    /// Each lookup resolves the ambient context at that moment. A previously returned or
    /// injected handle continues to represent its original context after a context switch.
    /// </remarks>
    public static PgMemoryContext Current => Resolve(NativeMemoryOperation.Current, 0, owned: false)
        ?? throw new InvalidOperationException("PostgreSQL did not expose a current memory context.");

    /// <summary>
    /// Resolves a predefined PostgreSQL memory context, or null when that context is not active.
    /// </summary>
    /// <param name="kind">The predefined context to resolve.</param>
    /// <returns>A borrowed context, or null when PostgreSQL has no such context in this phase.</returns>
    public static PgMemoryContext? Get(PgMemoryContextKind kind)
    {
        ArgumentOutOfRangeException.ThrowIfNegative((int)kind, nameof(kind));
        ArgumentOutOfRangeException.ThrowIfGreaterThan((int)kind, (int)PgMemoryContextKind.CurTransaction, nameof(kind));
        return Resolve(NativeMemoryOperation.Predefined, (nint)kind, owned: false);
    }

    /// <summary>
    /// Creates an owned AllocSet child of the current context.
    /// </summary>
    /// <param name="name">The identifier shown by PostgreSQL memory statistics.</param>
    /// <param name="parent">The parent context, or null for the callback's current context.</param>
    /// <param name="options">The AllocSet block sizes, or null for PostgreSQL's default preset.</param>
    /// <returns>The new owned context.</returns>
    public static PgMemoryContext Create(string name, PgMemoryContext? parent = null, PgMemoryContextOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (name.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("A memory context name cannot contain a zero character.", nameof(name));
        }

        options?.Validate();
        NativeMemoryContextSizes sizes = new()
        {
            _minimumContextSize = options?.MinimumContextSize ?? 0,
            _initialBlockSize = options?.InitialBlockSize ?? 0,
            _maximumBlockSize = options?.MaximumBlockSize ?? 0,
        };
        nint provider = NativeMemoryContext.Provider;
        nint parentId = parent is null ? 0 : parent.GetId();
        byte[] utf8 = s_utf8.GetBytes(name + '\0');
        fixed (byte* text = utf8)
        {
            NativeMemoryRequest request = new()
            {
                _operation = NativeMemoryOperation.Create,
                _context = parentId,
                _data = (nint)text,
                _length = (nuint)(utf8.Length - 1),
                _pointer = options is null ? 0 : (nint)(&sizes),
            };
            NativeMemoryContext.Invoke(ref request, out NativeMemoryResult result);
            return new PgMemoryContext(provider, result._context, owned: true);
        }
    }

    /// <summary>
    /// Runs synchronous work in a new child context and attempts its deletion after restoring the caller.
    /// </summary>
    /// <typeparam name="TResult">The managed result type.</typeparam>
    /// <param name="name">The transient context's native identifier.</param>
    /// <param name="func">The work performed with the transient context current.</param>
    /// <param name="parent">The parent context, or null for the caller's current context.</param>
    /// <param name="options">The AllocSet block sizes, or null for PostgreSQL's default preset.</param>
    /// <returns>The callback's result.</returns>
    /// <remarks>
    /// Return copied managed values rather than native pointers. Escaped checked handles become stale
    /// after successful deletion. Callback, restoration, and deletion failures are preserved together.
    /// If native cleanup fails, the context remains owned by its parent until cleanup is retried.
    /// </remarks>
    public static TResult RunTransient<TResult>(
        string name, Func<PgMemoryContext, TResult> func, PgMemoryContext? parent = null, PgMemoryContextOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(func);
        PgMemoryContext context = Create(name, parent, options);
        TResult result = default!;
        Exception? primary = null;
        try
        {
            result = context.Run(() => func(context));
        }
        catch (Exception exception)
        {
            primary = exception;
        }

        try
        {
            context.Dispose();
        }
        catch (Exception cleanupError) when (primary is not null)
        {
            throw new AggregateException("Transient PostgreSQL memory work and context deletion failed.", primary, cleanupError);
        }

        if (primary is not null)
        {
            ExceptionDispatchInfo.Capture(primary).Throw();
        }

        return result;
    }

    /// <summary>
    /// Runs synchronous work in a new child context and attempts its deletion on every exit path.
    /// </summary>
    /// <param name="name">The transient context's native identifier.</param>
    /// <param name="action">The work performed with the transient context current.</param>
    /// <param name="parent">The parent context, or null for the caller's current context.</param>
    /// <param name="options">The AllocSet block sizes, or null for PostgreSQL's default preset.</param>
    public static void RunTransient(
        string name, Action<PgMemoryContext> action, PgMemoryContext? parent = null, PgMemoryContextOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        RunTransient(name, context =>
        {
            action(context);
            return true;
        }, parent, options);
    }

    /// <summary>
    /// Gets the stable native identity used to validate this handle.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public nint Id => _id;

    /// <summary>
    /// Gets whether this handle still refers to a live native context.
    /// </summary>
    public bool IsAlive
    {
        get
        {
            if (_disposed)
            {
                return false;
            }

            NativeMemoryContext.CheckProvider(_provider);
            NativeMemoryRequest request = new() { _operation = NativeMemoryOperation.Name, _context = _id };
            try
            {
                NativeMemoryContext.Invoke(ref request, out _);
                return true;
            }
            catch (PgException exception) when (exception.SqlState == "55000")
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Gets the native context identifier, copied before the next reset can invalidate it.
    /// </summary>
    public string Name => ReadText(NativeMemoryOperation.Name);

    /// <summary>
    /// Gets the live parent context, or null for a top-level context.
    /// </summary>
    public PgMemoryContext? Parent
    {
        get
        {
            EnsureAlive();
            NativeMemoryRequest request = new() { _operation = NativeMemoryOperation.Parent, _context = _id };
            NativeMemoryContext.Invoke(ref request, out NativeMemoryResult result);
            return result._context == 0 ? null : new PgMemoryContext(_provider, result._context, owned: false);
        }
    }

    /// <summary>
    /// Resets this context, invalidates its existing allocations, and deletes its descendant contexts.
    /// </summary>
    public void Reset() => InvokeContext(NativeMemoryOperation.Reset);

    /// <summary>
    /// Resets this context without deleting its child contexts.
    /// </summary>
    public void ResetOnly() => InvokeContext(NativeMemoryOperation.ResetOnly);

    /// <summary>
    /// Resets all child contexts while retaining this context and its direct allocations.
    /// </summary>
    public void ResetChildren() => InvokeContext(NativeMemoryOperation.ResetChildren);

    /// <summary>
    /// Registers a one-shot callback before this context's next reset or deletion.
    /// </summary>
    /// <param name="callback">The synchronous cleanup action.</param>
    /// <returns>A registration whose disposal cancels the pending callback.</returns>
    /// <remarks>
    /// Callbacks run in reverse registration order, including callbacks registered during cleanup.
    /// SQL, logging, configuration reads, and aggregate operations are unavailable during cleanup.
    /// Errors propagate after managed frames unwind;
    /// callbacks not yet invoked remain pending for a subsequent reset or deletion.
    /// Cleanup owned by ErrorContext or its descendants cannot perform guarded native memory or SPI operations.
    /// </remarks>
    public PgMemoryCallback RegisterResetCallback(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        EnsureAlive();
        return PgMemoryCallback.Register(_provider, _id, callback);
    }

    /// <summary>
    /// Executes a synchronous callback with this context current and restores the previous context on every exit path.
    /// </summary>
    /// <param name="action">The callback that runs with this context current.</param>
    public void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Run(static callback =>
        {
            callback();
            return 0;
        }, action);
    }

    /// <summary>
    /// Executes a synchronous callback with this context current and restores the previous context on every exit path.
    /// </summary>
    /// <typeparam name="TResult">The callback result type.</typeparam>
    /// <param name="func">The callback that runs with this context current.</param>
    /// <returns>The callback result.</returns>
    public TResult Run<TResult>(Func<TResult> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return Run(static callback => callback(), func);
    }

    /// <summary>
    /// Allocates bytes owned by this context with checked access through the returned handle.
    /// </summary>
    /// <param name="length">The number of bytes.</param>
    /// <param name="options">The initialization and native size policies.</param>
    /// <param name="alignment">A power of two below 128 MiB, or zero for PostgreSQL's default alignment.</param>
    /// <returns>The checked allocation.</returns>
    /// <remarks>
    /// Native allocator restrictions apply. Slab requires its configured chunk size. Bump permits
    /// allocation but reclaims storage only through context reset or deletion, rejecting individual
    /// disposal and resizing. Prefer context-owned values when individual release is unavailable.
    /// </remarks>
    public PgAllocation Allocate(nuint length, PgAllocationOptions options = PgAllocationOptions.None, nuint alignment = 0)
    {
        NativeMemoryResult result = AllocateCore(length, options, alignment, noOutOfMemory: false);
        return new PgAllocation(_provider, result._pointer, result._length, options, alignment);
    }

    /// <summary>
    /// Allocates zeroed bytes owned by this context.
    /// </summary>
    /// <param name="length">The number of bytes.</param>
    /// <param name="options">Additional native allocation policies.</param>
    /// <param name="alignment">A power of two below 128 MiB, or zero for PostgreSQL's default alignment.</param>
    /// <returns>The checked zeroed allocation.</returns>
    public PgAllocation AllocateZeroed(nuint length, PgAllocationOptions options = PgAllocationOptions.None, nuint alignment = 0)
        => Allocate(length, options | PgAllocationOptions.Zeroed, alignment);

    /// <summary>
    /// Attempts a no-OOM allocation and returns null only for allocator exhaustion.
    /// </summary>
    /// <param name="length">The number of bytes.</param>
    /// <param name="zeroed">Whether to clear the allocated bytes.</param>
    /// <returns>The allocation, or null when PostgreSQL reports out of memory.</returns>
    public PgAllocation? TryAllocate(nuint length, bool zeroed = false)
        => TryAllocate(length, zeroed ? PgAllocationOptions.Zeroed : PgAllocationOptions.None);

    /// <summary>
    /// Attempts an allocation with explicit native policies without raising an out-of-memory error.
    /// </summary>
    /// <param name="length">The number of bytes.</param>
    /// <param name="options">The initialization and native size policies.</param>
    /// <param name="alignment">A power of two below 128 MiB, or zero for PostgreSQL's default alignment.</param>
    /// <returns>The allocation, or null for allocator exhaustion; other native errors still throw.</returns>
    public PgAllocation? TryAllocate(nuint length, PgAllocationOptions options, nuint alignment = 0)
    {
        NativeMemoryResult result = AllocateCore(length, options, alignment, noOutOfMemory: true);
        return result._pointer == 0 ? null : new PgAllocation(_provider, result._pointer, result._length, options, alignment);
    }

    /// <summary>
    /// Allocates enough native bytes for a checked number of unmanaged values.
    /// </summary>
    /// <typeparam name="T">The unmanaged value type; no catalog or C struct layout is inferred.</typeparam>
    /// <param name="count">The number of values.</param>
    /// <param name="options">The initialization and native size policies.</param>
    /// <param name="alignment">A power of two below 128 MiB, or zero for PostgreSQL's default alignment.</param>
    /// <returns>The byte-addressed checked allocation.</returns>
    public PgAllocation Allocate<T>(nuint count = 1, PgAllocationOptions options = PgAllocationOptions.None, nuint alignment = 0)
        where T : unmanaged => Allocate(checked(count * (nuint)sizeof(T)), options, alignment);

    /// <summary>
    /// Allocates zeroed native bytes for a checked number of unmanaged values.
    /// </summary>
    /// <typeparam name="T">The unmanaged value type.</typeparam>
    /// <param name="count">The number of values.</param>
    /// <param name="options">Additional native allocation policies.</param>
    /// <param name="alignment">A power of two below 128 MiB, or zero for PostgreSQL's default alignment.</param>
    /// <returns>The byte-addressed checked zeroed allocation.</returns>
    public PgAllocation AllocateZeroed<T>(nuint count = 1, PgAllocationOptions options = PgAllocationOptions.None, nuint alignment = 0)
        where T : unmanaged => AllocateZeroed(checked(count * (nuint)sizeof(T)), options, alignment);

    /// <summary>
    /// Attempts native allocation for a checked number of unmanaged values.
    /// </summary>
    /// <typeparam name="T">The unmanaged value type.</typeparam>
    /// <param name="count">The number of values.</param>
    /// <param name="options">The initialization and native size policies.</param>
    /// <param name="alignment">A power of two below 128 MiB, or zero for PostgreSQL's default alignment.</param>
    /// <returns>The checked allocation, or null for allocator exhaustion.</returns>
    public PgAllocation? TryAllocate<T>(nuint count = 1, PgAllocationOptions options = PgAllocationOptions.None, nuint alignment = 0)
        where T : unmanaged => TryAllocate(checked(count * (nuint)sizeof(T)), options, alignment);

    /// <summary>
    /// Copies managed bytes into a distinct native allocation.
    /// </summary>
    /// <param name="source">The bytes to copy.</param>
    /// <param name="options">The initialization and native size policies.</param>
    /// <param name="alignment">A power of two below 128 MiB, or zero for PostgreSQL's default alignment.</param>
    /// <returns>The independently owned checked allocation.</returns>
    public PgAllocation CopyFrom(ReadOnlySpan<byte> source, PgAllocationOptions options = PgAllocationOptions.None, nuint alignment = 0)
    {
        PgAllocation allocation = Allocate((nuint)source.Length, options, alignment);
        try
        {
            allocation.Write(source);
            return allocation;
        }
        catch (Exception primary)
        {
            try
            {
                allocation.Dispose();
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException("Copying PostgreSQL allocation data and releasing its storage failed.", primary, cleanupError);
            }

            throw;
        }
    }

    /// <summary>
    /// Copies the exact unmanaged representation of managed values into distinct native storage.
    /// </summary>
    /// <typeparam name="T">The unmanaged value type; its bytes are copied without SQL conversion.</typeparam>
    /// <param name="source">The values to copy.</param>
    /// <param name="options">The initialization and native size policies.</param>
    /// <param name="alignment">A power of two below 128 MiB, or zero for PostgreSQL's default alignment.</param>
    /// <returns>The independently owned byte-addressed allocation.</returns>
    public PgAllocation CopyFrom<T>(ReadOnlySpan<T> source, PgAllocationOptions options = PgAllocationOptions.None, nuint alignment = 0)
        where T : unmanaged => CopyFrom(MemoryMarshal.AsBytes(source), options, alignment);

    /// <summary>
    /// Copies strict UTF-8 text and one terminating zero into a distinct native allocation.
    /// </summary>
    /// <param name="value">Text without embedded zero characters or malformed UTF-16.</param>
    /// <returns>The allocation containing UTF-8 bytes and their terminating zero.</returns>
    /// <remarks>
    /// This copies UTF-8 without conversion to PostgreSQL's database encoding.
    /// </remarks>
    public PgAllocation AllocateUtf8String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("A native UTF-8 string cannot contain a zero character.", nameof(value));
        }

        return CopyFrom(s_utf8.GetBytes(value + '\0'));
    }

    /// <summary>
    /// Takes exclusive individual ownership of a live native palloc-family allocation in this context.
    /// </summary>
    /// <param name="address">The live pointer, compatible with PostgreSQL's native pfree operation.</param>
    /// <param name="length">The accessible allocation byte length supplied by the caller.</param>
    /// <param name="huge">Whether the allocation uses PostgreSQL's huge size policy.</param>
    /// <param name="alignment">The original explicit alignment, or zero for default alignment.</param>
    /// <returns>A checked owner that releases the native allocation on disposal.</returns>
    /// <remarks>
    /// The caller must guarantee valid pointer provenance, size, alignment, and exclusive ownership.
    /// Do not free or resize the pointer externally while this handle owns it. Failed adoption leaves
    /// ownership with the caller. A null pointer is rejected before native access.
    /// Bump contexts do not expose chunk ownership headers and reject adoption; use an explicitly
    /// anchored raw borrow for existing Bump storage.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public PgAllocation DangerousAdopt(void* address, nuint length, bool huge = false, nuint alignment = 0)
    {
        if (address is null)
        {
            throw new ArgumentNullException(nameof(address));
        }

        PgAllocationOptions options = huge ? PgAllocationOptions.Huge : PgAllocationOptions.None;
        ValidateAllocation(options, alignment);
        EnsureAlive();
        NativeMemoryRequest request = new()
        {
            _operation = NativeMemoryOperation.Adopt,
            _context = _id,
            _pointer = (nint)address,
            _length = length,
            _flags = (int)options,
            _alignment = alignment,
        };
        NativeMemoryContext.Invoke(ref request, out NativeMemoryResult result);
        return new PgAllocation(_provider, result._pointer, result._length, options, alignment);
    }

    /// <summary>
    /// Gets the context's native allocation statistics.
    /// </summary>
    /// <returns>The bytes accounted to this context and its descendants.</returns>
    public nuint GetAllocatedBytes()
    {
        EnsureAlive();
        NativeMemoryRequest request = new() { _operation = NativeMemoryOperation.Statistics, _context = _id };
        NativeMemoryContext.Invoke(ref request, out NativeMemoryResult result);
        return result._length;
    }

    /// <summary>
    /// Gets whether PostgreSQL reports this context as empty.
    /// </summary>
    /// <remarks>
    /// PostgreSQL marks AllocSet contexts with registered invalidation callbacks as nonempty,
    /// including after an explicit reset. Slab, Generation, and Bump use allocator-specific
    /// block or live-chunk accounting; this is not a portable count of user allocations.
    /// </remarks>
    public bool IsEmpty
    {
        get
        {
            EnsureAlive();
            NativeMemoryRequest request = new() { _operation = NativeMemoryOperation.IsEmpty, _context = _id };
            NativeMemoryContext.Invoke(ref request, out NativeMemoryResult result);
            return result._value != 0;
        }
    }

    /// <summary>
    /// Disposes an owned context. Borrowed contexts remain owned by PostgreSQL.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_owned)
        {
            NativeMemoryContext.CheckProvider(_provider);
            NativeMemoryRequest request = new() { _operation = NativeMemoryOperation.Delete, _context = _id };
            NativeMemoryContext.Invoke(ref request, out _);
        }

        _disposed = true;
    }

    private static PgMemoryContext? Resolve(NativeMemoryOperation operation, nint value, bool owned)
    {
        NativeMemoryRequest request = new() { _operation = operation, _value = value };
        NativeMemoryContext.Invoke(ref request, out NativeMemoryResult result);
        return result._context == 0 ? null : new PgMemoryContext(NativeMemoryContext.Provider, result._context, owned);
    }

    /// <summary>
    /// Creates a borrowed handle for an identity validated by the native registry.
    /// </summary>
    /// <param name="provider">The native extension provider.</param>
    /// <param name="id">The live context identity.</param>
    /// <returns>The borrowed handle.</returns>
    internal static PgMemoryContext FromId(nint provider, nint id) => new(provider, id, owned: false);

    private nint GetId()
    {
        EnsureAlive();
        return _id;
    }

    private void EnsureAlive()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(PgMemoryContext));

        NativeMemoryContext.CheckProvider(_provider);
        NativeMemoryRequest request = new() { _operation = NativeMemoryOperation.Name, _context = _id };
        try
        {
            NativeMemoryContext.Invoke(ref request, out _);
        }
        catch (PgException exception) when (exception.SqlState == "55000")
        {
            throw new ObjectDisposedException(nameof(PgMemoryContext), "The PostgreSQL memory context has been reset or deleted.");
        }
    }

    private void InvokeContext(NativeMemoryOperation operation)
    {
        EnsureAlive();
        NativeMemoryRequest request = new() { _operation = operation, _context = _id };
        NativeMemoryContext.Invoke(ref request, out _);
    }

    private string ReadText(NativeMemoryOperation operation)
    {
        EnsureAlive();
        NativeMemoryRequest request = new() { _operation = operation, _context = _id };
        NativeMemoryContext.Invoke(ref request, out NativeMemoryResult result);
        byte[] bytes = new byte[checked((int)result._length)];
        fixed (byte* buffer = bytes)
        {
            request._data = (nint)buffer;
            request._length = (nuint)bytes.Length;
            NativeMemoryContext.Invoke(ref request, out _);
        }

        return s_utf8.GetString(bytes);
    }

    private NativeMemoryResult AllocateCore(nuint length, PgAllocationOptions options, nuint alignment, bool noOutOfMemory)
    {
        ValidateAllocation(options, alignment);
        EnsureAlive();
        NativeMemoryRequest request = new()
        {
            _operation = NativeMemoryOperation.Allocate,
            _context = _id,
            _length = length,
            _flags = (int)options | (noOutOfMemory ? 2 : 0),
            _alignment = alignment,
        };
        NativeMemoryContext.Invoke(ref request, out NativeMemoryResult result);
        return result;
    }

    private static void ValidateAllocation(PgAllocationOptions options, nuint alignment)
    {
        if ((options & ~(PgAllocationOptions.Zeroed | PgAllocationOptions.Huge)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options, "Unknown PostgreSQL allocation options.");
        }

        if (alignment >= 128 * 1024 * 1024 || (alignment != 0 && (alignment & (alignment - 1)) != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Alignment must be zero or a power of two below 128 MiB.");
        }
    }

    private TResult Run<TCallback, TResult>(Func<TCallback, TResult> callback, TCallback callbackValue)
    {
        EnsureAlive();
        NativeMemoryRequest request = new() { _operation = NativeMemoryOperation.Switch, _context = _id };
        NativeMemoryContext.Invoke(ref request, out NativeMemoryResult result);
        TResult value = default!;
        Exception? primary = null;
        try
        {
            value = callback(callbackValue);
        }
        catch (Exception exception)
        {
            primary = exception;
        }

        Exception? restoreError = null;
        NativeMemoryRequest restore = new() { _operation = NativeMemoryOperation.Switch, _context = result._context };
        try
        {
            NativeMemoryContext.Invoke(ref restore, out _);
        }
        catch (Exception exception)
        {
            restoreError = exception;
        }

        if (primary is not null)
        {
            if (restoreError is not null)
            {
                throw new AggregateException("Restoring PostgreSQL's previous memory context failed.", primary, restoreError);
            }

            ExceptionDispatchInfo.Capture(primary).Throw();
        }

        if (restoreError is not null)
        {
            throw restoreError;
        }

        return value;
    }
}
