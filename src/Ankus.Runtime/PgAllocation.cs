using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Ankus;

/// <summary>
/// Owns one PostgreSQL palloc-family allocation and validates it before each managed access.
/// </summary>
public sealed unsafe class PgAllocation : IDisposable
{
    private readonly nint _provider;
    private nint _id;
    private nuint _length;

    /// <summary>
    /// Wraps a checked allocation registered by the native provider.
    /// </summary>
    /// <param name="provider">The native extension provider.</param>
    /// <param name="id">The allocation identity.</param>
    /// <param name="length">The allocation byte length.</param>
    internal PgAllocation(nint provider, nint id, nuint length)
    {
        _provider = provider;
        _id = id;
        _length = length;
    }

    /// <summary>
    /// Gets the allocation's byte length.
    /// </summary>
    public nuint Length => _length;

    /// <summary>
    /// Gets the owning context while the allocation remains live.
    /// </summary>
    public PgMemoryContext Context
    {
        get
        {
            EnsureLive();
            NativeMemoryRequest request = new() { _operation = NativeMemoryOperation.Owner, _context = _id };
            Invoke(ref request, out NativeMemoryResult result);
            return PgMemoryContext.FromId(_provider, result._context);
        }
    }

    /// <summary>
    /// Copies bytes from this allocation into managed memory.
    /// </summary>
    /// <param name="destination">The destination span.</param>
    /// <param name="offset">The allocation byte offset.</param>
    public void Read(Span<byte> destination, nuint offset = 0)
    {
        EnsureRange(offset, (nuint)destination.Length);
        fixed (byte* target = destination)
        {
            NativeMemoryRequest request = new()
            {
                _operation = NativeMemoryOperation.Read,
                _context = _id,
                _data = (nint)target,
                _length = (nuint)destination.Length,
                _value = (nint)offset,
            };
            Invoke(ref request);
        }
    }

    /// <summary>
    /// Copies managed bytes into this allocation.
    /// </summary>
    /// <param name="source">The source bytes.</param>
    /// <param name="offset">The allocation byte offset.</param>
    public void Write(ReadOnlySpan<byte> source, nuint offset = 0)
    {
        EnsureRange(offset, (nuint)source.Length);
        fixed (byte* sourcePointer = source)
        {
            NativeMemoryRequest request = new()
            {
                _operation = NativeMemoryOperation.Write,
                _context = _id,
                _data = (nint)sourcePointer,
                _length = (nuint)source.Length,
                _value = (nint)offset,
            };
            Invoke(ref request);
        }
    }

    /// <summary>
    /// Reads one unmanaged value from the allocation.
    /// </summary>
    /// <typeparam name="T">The unmanaged value type.</typeparam>
    /// <param name="offset">The allocation byte offset.</param>
    /// <returns>The copied value.</returns>
    public T Read<T>(nuint offset = 0) where T : unmanaged
    {
        T value = default;
        Read(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1)), offset);
        return value;
    }

    /// <summary>
    /// Writes one unmanaged value to the allocation.
    /// </summary>
    /// <typeparam name="T">The unmanaged value type.</typeparam>
    /// <param name="value">The value to copy.</param>
    /// <param name="offset">The allocation byte offset.</param>
    public void Write<T>(T value, nuint offset = 0) where T : unmanaged
    {
        Write(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)), offset);
    }

    /// <summary>
    /// Clears the allocation or a selected byte range.
    /// </summary>
    /// <param name="offset">The allocation byte offset.</param>
    /// <param name="length">The number of bytes to clear, or zero to clear from the offset through the end.</param>
    public void Clear(nuint offset = 0, nuint length = 0)
    {
        if (length == 0)
        {
            length = _length - offset;
        }

        EnsureRange(offset, length);
        NativeMemoryRequest request = new()
        {
            _operation = NativeMemoryOperation.Clear,
            _context = _id,
            _length = length,
            _value = (nint)offset,
        };
        Invoke(ref request);
    }

    /// <summary>
    /// Resizes this allocation using PostgreSQL's matching context allocator.
    /// </summary>
    /// <param name="length">The new byte length.</param>
    public void Reallocate(nuint length)
    {
        EnsureLive();
        NativeMemoryRequest request = new()
        {
            _operation = NativeMemoryOperation.Reallocate,
            _context = _id,
            _length = length,
        };
        Invoke(ref request, out NativeMemoryResult result);
        _id = result._context;
        _length = result._length;
    }

    /// <summary>
    /// Returns the raw palloc pointer for explicitly unsafe interop.
    /// </summary>
    /// <returns>The native allocation pointer.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void* DangerousGetPointer()
    {
        EnsureLive();
        NativeMemoryRequest request = new() { _operation = NativeMemoryOperation.Read, _context = _id };
        Invoke(ref request, out NativeMemoryResult result);
        return (void*)result._pointer;
    }

    /// <summary>
    /// Releases the allocation immediately. Context reset or deletion makes this operation a no-op.
    /// </summary>
    public void Dispose()
    {
        if (_id == 0)
        {
            return;
        }

        NativeMemoryContext.CheckProvider(_provider);
        NativeMemoryRequest request = new() { _operation = NativeMemoryOperation.Free, _context = _id };
        NativeMemoryContext.Invoke(ref request, out _);
        _id = 0;
        _length = 0;
    }

    private void EnsureLive()
    {
        ObjectDisposedException.ThrowIf(_id == 0, nameof(PgAllocation));

        NativeMemoryContext.CheckProvider(_provider);
    }

    private void EnsureRange(nuint offset, nuint length)
    {
        EnsureLive();
        if (offset > _length || length > _length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "The requested range exceeds the native allocation.");
        }
    }

    private static void Invoke(ref NativeMemoryRequest request)
    {
        try
        {
            NativeMemoryContext.Invoke(ref request, out _);
        }
        catch (PgException exception) when (exception.SqlState == "55000")
        {
            throw new ObjectDisposedException(nameof(PgAllocation), "The PostgreSQL allocation has been reset, freed, or deleted.");
        }
    }

    private static void Invoke(ref NativeMemoryRequest request, out NativeMemoryResult result)
    {
        try
        {
            NativeMemoryContext.Invoke(ref request, out result);
        }
        catch (PgException exception) when (exception.SqlState == "55000")
        {
            throw new ObjectDisposedException(nameof(PgAllocation), "The PostgreSQL allocation has been reset, freed, or deleted.");
        }
    }
}
