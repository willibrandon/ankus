namespace Ankus;

/// <summary>
/// Invalidates callback input views without invoking PostgreSQL while managed frames unwind.
/// </summary>
internal sealed class NativeBorrowScope(nint provider, int depth, NativeBorrowScope? parent)
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private bool _alive = true;

    /// <summary>
    /// Gets the nesting depth that owns this unique lease.
    /// </summary>
    internal int Depth { get; } = depth;

    /// <summary>
    /// Gets the still-active enclosing lease, if one was captured.
    /// </summary>
    internal NativeBorrowScope? Parent { get; } = parent;

    /// <summary>
    /// Ends the lease without native calls or allocation.
    /// </summary>
    internal void Expire() => _alive = false;

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
