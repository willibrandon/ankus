using System.Runtime.CompilerServices;

namespace Ankus;

/// <summary>
/// Describes an unmanaged shared value protected by a PostgreSQL lightweight reader/writer lock.
/// </summary>
/// <typeparam name="T">The unmanaged value copied to and from shared storage.</typeparam>
/// <remarks>
/// Keep this descriptor in a static field and register it with <see cref="PgSharedMemory"/>.
/// Each guard belongs to the acquiring callback and backend thread. Recursive acquisition is
/// rejected. Use a using declaration to release a guard promptly; callback exit also releases it.
/// Native addresses embedded in T must be valid across processes, including Windows workers.
/// </remarks>
public sealed class PgLwLock<T> where T : unmanaged
{
    private readonly NativeSharedMemoryRegistration _registration;

    /// <summary>
    /// Creates an unregistered descriptor with a cluster-wide shared-memory name.
    /// </summary>
    /// <param name="name">A nonempty, exact UTF-8 name without a zero byte.</param>
    public PgLwLock(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (name.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("A PostgreSQL shared-memory name cannot contain a zero byte.", nameof(name));
        }

        Name = name;
        _registration = new NativeSharedMemoryRegistration(name, typeof(T).AssemblyQualifiedName!, Unsafe.SizeOf<T>());
    }

    /// <summary>
    /// Gets the exact name used by PostgreSQL's shared-memory index and named lock tranche.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Acquires shared read access, waiting for an exclusive writer to release the lock.
    /// </summary>
    /// <returns>A callback-scoped shared guard.</returns>
    public PgLwLockShareGuard<T> Share() => new(_registration.Acquire(exclusive: false));

    /// <summary>
    /// Acquires exclusive read/write access, waiting for all other holders to release the lock.
    /// </summary>
    /// <returns>A callback-scoped exclusive guard.</returns>
    public PgLwLockExclusiveGuard<T> Exclusive() => new(_registration.Acquire(exclusive: true));

    /// <summary>
    /// Registers the exact value layout and its process-local initializer.
    /// </summary>
    /// <param name="initializer">Writes the initial value after validating the native destination.</param>
    internal void Register(Action<nint, nuint> initializer) => _registration.Register(initializer);
}

/// <summary>
/// Holds shared read access to a PostgreSQL shared-memory value until disposal or callback exit.
/// </summary>
/// <typeparam name="T">The unmanaged shared value.</typeparam>
public sealed class PgLwLockShareGuard<T> : IDisposable where T : unmanaged
{
    private readonly NativeSharedMemoryLease _lease;

    /// <summary>
    /// Owns one acquired native lease.
    /// </summary>
    /// <param name="lease">The acquired shared lease.</param>
    internal PgLwLockShareGuard(NativeSharedMemoryLease lease) => _lease = lease;

    /// <summary>
    /// Gets a copy of the shared value while this guard holds the lock.
    /// </summary>
    public T Value => _lease.Read<T>();

    /// <summary>
    /// Releases this guard once without modifying the shared value.
    /// </summary>
    public void Dispose() => _lease.Dispose();
}

/// <summary>
/// Holds exclusive read/write access to a PostgreSQL shared-memory value until disposal or callback exit.
/// </summary>
/// <typeparam name="T">The unmanaged shared value.</typeparam>
public sealed class PgLwLockExclusiveGuard<T> : IDisposable where T : unmanaged
{
    private readonly NativeSharedMemoryLease _lease;

    /// <summary>
    /// Owns one acquired native lease.
    /// </summary>
    /// <param name="lease">The acquired exclusive lease.</param>
    internal PgLwLockExclusiveGuard(NativeSharedMemoryLease lease) => _lease = lease;

    /// <summary>
    /// Gets or sets a copy of the shared value while this guard holds the lock.
    /// </summary>
    /// <remarks>
    /// Writes take effect immediately and are not rolled back by a transaction abort or exception.
    /// Read the value, modify the copy, then assign it back to publish a complete update.
    /// </remarks>
    public T Value
    {
        get => _lease.Read<T>();
        set => _lease.Write(value);
    }

    /// <summary>
    /// Releases this guard once, preserving any value already written.
    /// </summary>
    public void Dispose() => _lease.Dispose();
}
