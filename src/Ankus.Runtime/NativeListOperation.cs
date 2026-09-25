namespace Ankus;

/// <summary>
/// Selects a checked PostgreSQL list operation.
/// </summary>
internal enum NativeListOperation
{
    /// <summary>
    /// Creates a context-bound owned list.
    /// </summary>
    Create = 1,
    /// <summary>
    /// Registers an exclusive borrowed list after checking its tag.
    /// </summary>
    Borrow = 2,
    /// <summary>
    /// Reads current native metadata.
    /// </summary>
    Inspect = 3,
    /// <summary>
    /// Copies exact native cells into a wire buffer.
    /// </summary>
    Read = 4,
    /// <summary>
    /// Replaces existing cells from a wire buffer.
    /// </summary>
    Write = 5,
    /// <summary>
    /// Appends cells, allocating only when required.
    /// </summary>
    Add = 6,
    /// <summary>
    /// Appends one cell only when capacity already exists.
    /// </summary>
    TryAdd = 7,
    /// <summary>
    /// Reserves additional cells on a nonempty list.
    /// </summary>
    Reserve = 8,
    /// <summary>
    /// Inserts one cell at a checked index.
    /// </summary>
    Insert = 9,
    /// <summary>
    /// Removes a checked range without returning its cells.
    /// </summary>
    Remove = 10,
    /// <summary>
    /// Releases all cells and restores NIL.
    /// </summary>
    Clear = 11,
    /// <summary>
    /// Copies and removes a checked range atomically.
    /// </summary>
    Drain = 12,
    /// <summary>
    /// Releases owned storage and unregisters the wrapper.
    /// </summary>
    Dispose = 13,
    /// <summary>
    /// Transfers native storage without releasing it.
    /// </summary>
    Detach = 14,
}
