namespace Ankus;

/// <summary>
/// Tracks one callback-owned lock acquisition without exposing a shared-memory address.
/// </summary>
internal sealed unsafe class NativeSharedMemoryLease(NativeBorrowScope scope, nint storage) : IDisposable
{
    private readonly int _process = Environment.ProcessId;
    private nint _token;

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
        T value = default;
        Copy(3, (nint)(&value), (nuint)sizeof(T));
        return value;
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
