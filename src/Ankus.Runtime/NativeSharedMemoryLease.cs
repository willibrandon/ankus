using System.Runtime.CompilerServices;

namespace Ankus;

/// <summary>
/// Tracks one callback-owned lock acquisition without exposing a shared-memory address.
/// </summary>
internal sealed unsafe class NativeSharedMemoryLease(NativeBorrowScope scope, nint storage) : IDisposable
{
    [ThreadStatic]
    private static int s_readers;

    private readonly int _process = Environment.ProcessId;
    private nint _token;
    private nint _readAddress;
    private nuint _readLength;
    private int _readDepth;

    /// <summary>
    /// Rejects backend entry that could revoke a lock while an original reference is borrowed.
    /// </summary>
    internal static void CheckBackendAccess()
    {
        if (s_readers != 0)
        {
            throw new InvalidOperationException("Finish scoped lightweight-lock reads before calling backend APIs.");
        }
    }

    /// <summary>
    /// Publishes the native acquisition identity after the guarded acquire succeeds.
    /// </summary>
    /// <param name="token">The nonzero native lease identity.</param>
    internal void Acquired(nint token)
    {
        if (token == 0)
        {
            throw new InvalidOperationException("PostgreSQL did not return a shared-memory lock lease.");
        }

        _token = token;
    }

    /// <summary>
    /// Copies a complete shared value after validating the active native lease.
    /// </summary>
    /// <typeparam name="T">The registered unmanaged representation.</typeparam>
    /// <returns>The exact copied value.</returns>
    internal T Read<T>() where T : unmanaged
    {
        if (_readDepth != 0)
        {
            Validate();
            ValidateReadLength<T>();
            return Unsafe.ReadUnaligned<T>((void*)_readAddress);
        }

        T value = default;
        Copy(3, (nint)(&value), (nuint)sizeof(T));
        return value;
    }

    /// <summary>
    /// Borrows original protected storage without permitting native lock retirement during the reader.
    /// </summary>
    internal TResult Read<T, TResult>(PgSharedReader<T, TResult> reader) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(reader);
        Validate();
        if (_readDepth == 0)
        {
            NativeMemoryRequest request = new()
            {
                _operation = NativeMemoryOperation.SharedMemory,
                _flags = 6,
                _context = storage,
                _other = _token,
                _length = (nuint)sizeof(T),
            };
            NativeMemoryContext.Invoke(ref request, out NativeMemoryResult result);
            if (result._data == 0 || (nuint)result._data % sizeof(long) != 0 || result._length != (nuint)sizeof(T))
            {
                throw new InvalidOperationException("PostgreSQL returned invalid lightweight-lock read storage.");
            }

            _readAddress = result._data;
            _readLength = result._length;
        }
        else
        {
            ValidateReadLength<T>();
        }

        int depth = checked(_readDepth + 1);
        int readers = checked(s_readers + 1);
        _readDepth = depth;
        s_readers = readers;
        NativeSharedReadScope frame = default;
        NativeSharedReadScope.Push(&frame, NativeMemoryContext.Provider, _readAddress, _readLength);
        try
        {
            return reader(in Unsafe.AsRef<T>((void*)_readAddress));
        }
        finally
        {
            try
            {
                NativeSharedReadScope.Pop(&frame);
            }
            finally
            {
                _readDepth--;
                s_readers--;
                if (_readDepth == 0)
                {
                    _readAddress = 0;
                    _readLength = 0;
                }
            }
        }
    }

    /// <summary>
    /// Publishes a complete value while holding an exclusive native lease.
    /// </summary>
    /// <typeparam name="T">The registered unmanaged representation.</typeparam>
    /// <param name="value">The value to publish immediately.</param>
    internal void Write<T>(T value) where T : unmanaged => Copy(4, (nint)(&value), (nuint)sizeof(T));

    /// <summary>
    /// Releases a live lease once, including a lease whose native lock was released during error cleanup.
    /// </summary>
    public void Dispose()
    {
        if (_token == 0)
        {
            return;
        }

        Validate();
        CheckNoReaders();
        NativeMemoryRequest request = new()
        {
            _operation = NativeMemoryOperation.SharedMemory,
            _flags = 5,
            _context = storage,
            _other = _token,
        };
        NativeMemoryContext.Invoke(ref request, out _);
        _token = 0;
        scope.Unregister(this);
    }

    private void Copy(int operation, nint data, nuint length)
    {
        Validate();
        if (operation == 4)
        {
            CheckNoReaders();
        }

        NativeMemoryRequest request = new()
        {
            _operation = NativeMemoryOperation.SharedMemory,
            _flags = operation,
            _context = storage,
            _other = _token,
            _data = data,
            _length = length,
        };
        NativeMemoryContext.Invoke(ref request, out _);
    }

    /// <summary>
    /// Preserves original storage until every scoped reader returns.
    /// </summary>
    private void CheckNoReaders()
    {
        if (_readDepth != 0)
        {
            throw new InvalidOperationException("A PostgreSQL lightweight-lock guard cannot replace or release its value during a scoped read.");
        }
    }

    /// <summary>
    /// Keeps repeated typed accesses within the exact originally admitted representation.
    /// </summary>
    private void ValidateReadLength<T>() where T : unmanaged
    {
        if (_readLength != (nuint)sizeof(T))
        {
            throw new InvalidOperationException("The lightweight-lock reader value size does not match its admission.");
        }
    }

    private void Validate()
    {
        ObjectDisposedException.ThrowIf(_token == 0, this);
        scope.Validate();
        if (_process != Environment.ProcessId)
        {
            throw new InvalidOperationException("A PostgreSQL shared-memory lock guard must remain in its acquiring backend process.");
        }
    }
}
