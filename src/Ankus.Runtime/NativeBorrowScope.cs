namespace Ankus;

/// <summary>
/// Owns callback input lifetimes and releases callback-scoped shared-memory lock leases.
/// </summary>
internal sealed class NativeBorrowScope(nint provider, int depth, NativeBorrowScope? parent)
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private bool _alive = true;
    private List<NativeSharedMemoryLease>? _locks;

    /// <summary>
    /// Gets the nesting depth that owns this unique lease.
    /// </summary>
    internal int Depth { get; } = depth;

    /// <summary>
    /// Gets the still-active enclosing lease, if one was captured.
    /// </summary>
    internal NativeBorrowScope? Parent { get; } = parent;

    /// <summary>
    /// Releases remaining lock leases before invalidating the callback's borrowed views.
    /// </summary>
    internal void Expire()
    {
        if (_locks is not null)
        {
            for (int index = _locks.Count - 1; index >= 0; index--)
            {
                _locks[index].Dispose();
            }

            _locks = null;
        }

        _alive = false;
    }

    /// <summary>
    /// Retains a lock lease before acquisition so callback exit releases forgotten guards.
    /// </summary>
    /// <param name="lease">The not-yet-acquired lease.</param>
    internal void Register(NativeSharedMemoryLease lease) => (_locks ??= []).Add(lease);

    /// <summary>
    /// Stops retaining a released or unacquired lease while its callback remains alive.
    /// </summary>
    /// <param name="lease">The lease whose native ownership has ended.</param>
    internal void Unregister(NativeSharedMemoryLease lease) => _locks?.Remove(lease);

    /// <summary>
    /// Rejects expired, foreign-thread or foreign-provider access before touching input storage.
    /// </summary>
    internal void Validate()
    {
        ObjectDisposedException.ThrowIf(!_alive, this);
        if (_thread != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("A PostgreSQL input view must remain on its originating backend thread.");
        }

        NativeMemoryContext.CheckProvider(provider);
    }
}
