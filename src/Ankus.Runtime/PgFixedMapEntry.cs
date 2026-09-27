namespace Ankus;

/// <summary>
/// Stores one unmanaged key, value and process-stable hash in a fixed dictionary's dense buffer.
/// </summary>
/// <typeparam name="TKey">The unmanaged key.</typeparam>
/// <typeparam name="TValue">The unmanaged value.</typeparam>
public readonly struct PgFixedMapEntry<TKey, TValue> where TKey : unmanaged where TValue : unmanaged
{
    /// <summary>
    /// Creates an entry after the dictionary has calculated its key hash.
    /// </summary>
    internal PgFixedMapEntry(TKey key, TValue value, uint hash)
    {
        Key = key;
        Value = value;
        Hash = hash;
    }

    /// <summary>
    /// Gets the original inserted key, preserved when an equivalent key replaces its value.
    /// </summary>
    public TKey Key { get; }

    /// <summary>
    /// Gets the entry's current value.
    /// </summary>
    public TValue Value { get; }

    /// <summary>
    /// Gets the cached process-independent hash used to repair probe chains after removal.
    /// </summary>
    internal uint Hash { get; }
}
