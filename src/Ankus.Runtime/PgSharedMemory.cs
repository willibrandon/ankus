using System.Runtime.CompilerServices;

namespace Ankus;

/// <summary>
/// Registers unmanaged values in PostgreSQL's named shared memory during shared preload.
/// </summary>
/// <remarks>
/// Register static descriptors from a <see cref="PgModuleLoadAttribute"/> method. Initialization
/// runs when PostgreSQL creates shared memory, not while the descriptor is registered. Existing
/// storage is attached without running its initializer or overwriting its contents.
/// </remarks>
public static class PgSharedMemory
{
    /// <summary>
    /// Registers a zero-initialized immutable shared value or aggregate of atomic values.
    /// </summary>
    /// <typeparam name="T">The unmanaged aggregate type.</typeparam>
    /// <param name="storage">The static named shared descriptor.</param>
    public static void Initialize<T>(PgShared<T> storage) where T : unmanaged
        => Initialize(storage, static () => default);

    /// <summary>
    /// Registers a shared aggregate whose factory runs only when PostgreSQL creates its storage.
    /// </summary>
    /// <typeparam name="T">The unmanaged aggregate type.</typeparam>
    /// <param name="storage">The static named shared descriptor.</param>
    /// <param name="initializer">The factory run at shared-memory startup.</param>
    public static unsafe void Initialize<T>(PgShared<T> storage, Func<T> initializer) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(initializer);
        storage.Register((destination, length) =>
        {
            if (destination == 0 || length != (nuint)sizeof(T))
            {
                throw new InvalidOperationException("The PostgreSQL shared initializer has invalid value storage.");
            }

            T value = initializer();
            Unsafe.WriteUnaligned((void*)destination, value);
        });
    }

    /// <summary>
    /// Registers a zero-initialized atomic scalar.
    /// </summary>
    /// <typeparam name="T">The supported unmanaged scalar type.</typeparam>
    /// <param name="storage">The static named atomic descriptor.</param>
    public static void Initialize<T>(PgAtomic<T> storage) where T : unmanaged
        => Initialize(storage, static () => default);

    /// <summary>
    /// Registers an atomic scalar whose factory runs only when PostgreSQL creates its shared storage.
    /// </summary>
    /// <typeparam name="T">The supported unmanaged scalar type.</typeparam>
    /// <param name="storage">The static named atomic descriptor.</param>
    /// <param name="initializer">The factory run at shared-memory startup.</param>
    public static unsafe void Initialize<T>(PgAtomic<T> storage, Func<T> initializer) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(initializer);
        storage.Register((destination, length) =>
        {
            if (destination == 0 || length != (nuint)sizeof(T))
            {
                throw new InvalidOperationException("The PostgreSQL atomic initializer has invalid value storage.");
            }

            T value = initializer();
            Unsafe.WriteUnaligned((void*)destination, value);
        });
    }

    /// <summary>
    /// Registers a reader/writer lock with a zero-initialized value.
    /// </summary>
    /// <typeparam name="T">The unmanaged value shared by PostgreSQL processes.</typeparam>
    /// <param name="storage">The static named descriptor.</param>
    public static void Initialize<T>(PgLwLock<T> storage) where T : unmanaged
        => Initialize(storage, static () => default);

    /// <summary>
    /// Registers a reader/writer lock whose value is constructed at shared-memory startup.
    /// </summary>
    /// <typeparam name="T">The unmanaged value shared by PostgreSQL processes.</typeparam>
    /// <param name="storage">The static named descriptor.</param>
    /// <param name="initializer">The factory used only when creating the shared value.</param>
    /// <remarks>
    /// The value must contain no managed references or process-local addresses. The unmanaged
    /// constraint cannot establish that an embedded native address is valid in another process.
    /// Registration is idempotent for the same descriptor; another descriptor with the same name
    /// in one extension is rejected. Shared values and their locks live until PostgreSQL shuts down or recreates
    /// its shared memory after a backend crash; a replacement segment runs the initializer again.
    /// </remarks>
    public static unsafe void Initialize<T>(PgLwLock<T> storage, Func<T> initializer) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(initializer);
        storage.Register((destination, length) =>
        {
            if (destination == 0 || length != (nuint)sizeof(T))
            {
                throw new InvalidOperationException("The PostgreSQL shared-memory initializer has invalid value storage.");
            }

            T value = initializer();
            Unsafe.WriteUnaligned((void*)destination, value);
        });
    }
}
