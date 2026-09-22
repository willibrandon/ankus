namespace Ankus;

/// <summary>
/// Lets the nongeneric root registry validate and release typed managed aggregate state without reflection.
/// </summary>
internal interface IAggregateState
{
    /// <summary>
    /// Gets the owning native memory context, or zero before attachment.
    /// </summary>
    nint Owner { get; }

    /// <summary>
    /// Gets the adopted native state header, or zero before registration completes.
    /// </summary>
    nint Pointer { get; }

    /// <summary>
    /// Rejects invalidated state and attached state accessed from another thread.
    /// </summary>
    void CheckAccess();

    /// <summary>
    /// Binds a rooted state to its native owner and current thread before native registration.
    /// </summary>
    void BeginAttachment(nint owner);

    /// <summary>
    /// Retains the native header returned after successful adoption.
    /// </summary>
    void CompleteAttachment(nint pointer);

    /// <summary>
    /// Invalidates state and clears its payload before invoking any user disposal callback.
    /// </summary>
    void Release();
}
