namespace Ankus;

/// <summary>
/// Selects a guarded StringInfo operation within the native memory capability.
/// </summary>
internal enum NativeStringInfoOperation
{
    /// <summary>
    /// Acquires and initializes an owned native buffer.
    /// </summary>
    Create = 1,
    /// <summary>
    /// Validates lifetime and reads native buffer metadata.
    /// </summary>
    Inspect = 2,
    /// <summary>
    /// Appends bytes with native growth and termination.
    /// </summary>
    Append = 3,
    /// <summary>
    /// Copies an existing payload range into managed storage.
    /// </summary>
    Read = 4,
    /// <summary>
    /// Replaces a payload range without changing its length.
    /// </summary>
    Write = 5,
    /// <summary>
    /// Clears length and cursor without releasing capacity.
    /// </summary>
    Reset = 6,
    /// <summary>
    /// Reserves additional bytes beyond the current payload length.
    /// </summary>
    Enlarge = 7,
    /// <summary>
    /// Reserves an absolute minimum payload capacity.
    /// </summary>
    EnsureCapacity = 8,
    /// <summary>
    /// Releases both individually owned native allocations.
    /// </summary>
    Dispose = 9,
    /// <summary>
    /// Transfers the native struct and data without freeing either.
    /// </summary>
    Detach = 10,
    /// <summary>
    /// Transfers data and releases an owned native struct.
    /// </summary>
    DetachData = 11,
    /// <summary>
    /// Validates C-string contents before transferring data.
    /// </summary>
    DetachCString = 12,
}
