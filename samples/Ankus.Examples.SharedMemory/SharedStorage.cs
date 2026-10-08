using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Ankus.Examples.SharedMemory;

/// <summary>
/// Holds 400 inline <see cref="Pgtest"/> values, matching the capacity of pgrx's heapless collections.
/// </summary>
[InlineArray(400)]
public struct PgtestBuffer
{
    private Pgtest _element;
}

/// <summary>
/// Stores a bounded list in shared memory, like pgrx's <c>heapless::Vec&lt;Pgtest, 400&gt;</c>.
/// </summary>
public struct PgtestVec
{
    private PgtestBuffer _items;
    private int _count;

    /// <summary>
    /// Borrows the list operations over this value's inline storage.
    /// </summary>
    /// <returns>A view that updates this storage directly.</returns>
    [UnscopedRef]
    public PgFixedList<Pgtest> Items() => new(_items, ref _count);
}

/// <summary>
/// Stores a bounded double-ended queue in shared memory, like pgrx's <c>heapless::Deque&lt;Pgtest, 400&gt;</c>.
/// </summary>
public struct PgtestDeque
{
    private PgtestBuffer _items;
    private int _count;
    private int _head;

    /// <summary>
    /// Borrows the deque operations over this value's inline storage.
    /// </summary>
    /// <returns>A view that updates this storage directly.</returns>
    [UnscopedRef]
    public PgFixedDeque<Pgtest> Items() => new(_items, ref _count, ref _head);
}

/// <summary>
/// Holds four inline map entries, matching the capacity of pgrx's <c>FnvIndexMap&lt;i32, i32, 4&gt;</c>.
/// </summary>
[InlineArray(4)]
public struct HashEntries
{
    private PgFixedMapEntry<int, int> _element;
}

/// <summary>
/// Holds the hash index for four inline map entries.
/// </summary>
[InlineArray(4)]
public struct HashIndices
{
    private int _element;
}

/// <summary>
/// Stores a bounded insertion-ordered map in shared memory.
/// </summary>
public struct PgtestHash
{
    private HashEntries _entries;
    private HashIndices _indices;
    private int _count;

    /// <summary>
    /// Borrows the map operations over this value's inline storage.
    /// </summary>
    /// <returns>A view that updates this storage directly.</returns>
    [UnscopedRef]
    public PgFixedMap<int, int> Items() => new(_entries, _indices, ref _count);
}
