using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Ankus;

/// <summary>
/// Represents a checked PostgreSQL memory context accessed from synchronous backend callbacks.
/// </summary>
public sealed unsafe class PgMemoryContext : IDisposable
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
    /// <returns>The new owned context.</returns>
    public static PgMemoryContext Create(string name, PgMemoryContext? parent = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (name.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("A memory context name cannot contain a zero character.", nameof(name));
        }

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
            };
            NativeMemoryContext.Invoke(ref request, out NativeMemoryResult result);
            return new PgMemoryContext(provider, result._context, owned: true);
        }
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
    /// Allocates uninitialized bytes owned by this context.
    /// </summary>
    /// <param name="length">The number of bytes.</param>
    /// <returns>The context-owned allocation.</returns>
    public PgAllocation Allocate(nuint length) => AllocateCore(length, zeroed: false);

    /// <summary>
    /// Allocates zeroed bytes owned by this context.
    /// </summary>
    /// <param name="length">The number of bytes.</param>
    /// <returns>The context-owned allocation.</returns>
    public PgAllocation AllocateZeroed(nuint length) => AllocateCore(length, zeroed: true);

    /// <summary>
    /// Attempts a no-OOM allocation and returns null only for allocator exhaustion.
    /// </summary>
    /// <param name="length">The number of bytes.</param>
    /// <param name="zeroed">Whether to clear the allocated bytes.</param>
    /// <returns>The allocation, or null when PostgreSQL reports out of memory.</returns>
    public PgAllocation? TryAllocate(nuint length, bool zeroed = false)
    {
        EnsureAlive();
        NativeMemoryRequest request = new()
        {
            _operation = NativeMemoryOperation.Allocate,
            _context = _id,
            _length = length,
            _flags = (zeroed ? 1 : 0) | 2,
        };
        NativeMemoryContext.Invoke(ref request, out NativeMemoryResult result);
        if (result._pointer == 0)
        {
            return null;
        }

        return new PgAllocation(_provider, result._pointer, result._length);
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
    /// PostgreSQL marks contexts with registered invalidation callbacks as nonempty, including after an explicit reset.
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

    private PgAllocation AllocateCore(nuint length, bool zeroed)
    {
        EnsureAlive();
        NativeMemoryRequest request = new()
        {
            _operation = NativeMemoryOperation.Allocate,
            _context = _id,
            _length = length,
            _flags = zeroed ? 1 : 0,
        };
        NativeMemoryContext.Invoke(ref request, out NativeMemoryResult result);
        return new PgAllocation(_provider, result._pointer, result._length);
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
