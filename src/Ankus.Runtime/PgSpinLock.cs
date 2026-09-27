namespace Ankus;

/// <summary>
/// Owns stable local storage for a value protected by a PostgreSQL spinlock.
/// </summary>
/// <typeparam name="T">The unmanaged protected value.</typeparam>
/// <remarks>
/// This local owner pins its storage for its managed lifetime. Use PgSpinLockValue inside
/// PgShared for cross-process storage. Construction and access require the owning backend's
/// native callback. Prefer PgLwLock for work longer than a few instructions.
/// </remarks>
public sealed class PgSpinLock<T> where T : unmanaged
{
    private readonly PgSpinLockValue<T>[] _storage;
    private readonly nint _provider;

    /// <summary>
    /// Creates stable local storage and initializes its selected-header native spinlock.
    /// </summary>
    /// <param name="value">The initial protected value.</param>
    public PgSpinLock(T value)
    {
        NativeSpinLockLease.CheckBackendAccess();
        _provider = NativeMemoryContext.Provider;
        _storage = GC.AllocateArray<PgSpinLockValue<T>>(1, pinned: true);
        _storage[0] = new(value);
    }

    /// <summary>
    /// Gets an instantaneous lock-state observation on PostgreSQL versions before 19.
    /// </summary>
    public bool IsLocked
    {
        get
        {
            Validate();
            return _storage[0].Query(_storage);
        }
    }

    /// <summary>
    /// Acquires exclusive access until disposal or callback exit.
    /// </summary>
    /// <returns>The checked guard, which keeps this owner's storage alive.</returns>
    public PgSpinLockGuard<T> Lock()
    {
        Validate();
        return _storage[0].Acquire(_storage);
    }

    /// <summary>
    /// Rejects access outside the owner's active backend capability.
    /// </summary>
    private void Validate()
    {
        NativeMemoryContext.CheckProvider(_provider);
    }
}

/// <summary>
/// Holds one PostgreSQL spinlock acquisition with checked copied value access.
/// </summary>
/// <typeparam name="T">The unmanaged protected value.</typeparam>
public sealed class PgSpinLockGuard<T> : IDisposable where T : unmanaged
{
    private readonly NativeSpinLockLease _lease;

    /// <summary>
    /// Creates the managed guard before its native acquisition begins.
    /// </summary>
    internal PgSpinLockGuard(NativeSpinLockLease lease) => _lease = lease;

    /// <summary>
    /// Gets or immediately replaces the protected value while this guard owns the lock.
    /// </summary>
    public T Value
    {
        get => _lease.Read<T>();
        set => _lease.Write(value);
    }

    /// <summary>
    /// Releases the acquisition once; aliases and expired guards cannot release a later owner.
    /// </summary>
    public void Dispose() => _lease.Dispose();
}
