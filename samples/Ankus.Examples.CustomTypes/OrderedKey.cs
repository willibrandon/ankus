namespace Ankus.Examples.CustomTypes;

/// <summary>
/// Stores an ASCII case-insensitive key with generated equality, ordering and stable index hashing.
/// </summary>
/// <param name="Value">The original spelling preserved in storage.</param>
[PgType]
[PgEquality]
[PgOrdering]
[PgHashing]
public sealed record OrderedKey(string Value) : IComparable<OrderedKey>, IPgHashable
{
    /// <summary>
    /// Compares keys using their ASCII uppercase equality key.
    /// </summary>
    /// <param name="other">The key to compare, or null.</param>
    /// <returns>A negative, zero or positive result according to ordinal order.</returns>
    public int CompareTo(OrderedKey? other) => other is null ? 1 : string.CompareOrdinal(Normalize(Value), Normalize(other.Value));

    /// <summary>
    /// Compares normalized values independently of the original spelling.
    /// </summary>
    /// <param name="other">The other key.</param>
    /// <returns>Whether both normalized values are equal.</returns>
    public bool Equals(OrderedKey? other) => other is not null && string.Equals(Normalize(Value), Normalize(other.Value), StringComparison.Ordinal);

    /// <summary>
    /// Returns the stable hash of the normalized UTF-8 key for PostgreSQL indexes.
    /// </summary>
    /// <returns>The stable signed 32-bit hash.</returns>
    public int GetPostgresHashCode() => PgHash.Compute(Normalize(Value));

    /// <summary>
    /// Returns the same equality-compatible hash for managed collections.
    /// </summary>
    /// <returns>The normalized key hash.</returns>
    public override int GetHashCode() => GetPostgresHashCode();

    /// <summary>
    /// Compares keys with null before present values.
    /// </summary>
    /// <param name="left">The left key.</param>
    /// <param name="right">The right key.</param>
    /// <returns>Whether the left key sorts before the right key.</returns>
    public static bool operator <(OrderedKey? left, OrderedKey? right) => Comparer<OrderedKey>.Default.Compare(left, right) < 0;

    /// <summary>
    /// Compares keys with null before present values.
    /// </summary>
    /// <param name="left">The left key.</param>
    /// <param name="right">The right key.</param>
    /// <returns>Whether the left key sorts after the right key.</returns>
    public static bool operator >(OrderedKey? left, OrderedKey? right) => Comparer<OrderedKey>.Default.Compare(left, right) > 0;

    /// <summary>
    /// Compares keys with null before present values.
    /// </summary>
    /// <param name="left">The left key.</param>
    /// <param name="right">The right key.</param>
    /// <returns>Whether the left key sorts before or equals the right key.</returns>
    public static bool operator <=(OrderedKey? left, OrderedKey? right) => Comparer<OrderedKey>.Default.Compare(left, right) <= 0;

    /// <summary>
    /// Compares keys with null before present values.
    /// </summary>
    /// <param name="left">The left key.</param>
    /// <param name="right">The right key.</param>
    /// <returns>Whether the left key sorts after or equals the right key.</returns>
    public static bool operator >=(OrderedKey? left, OrderedKey? right) => Comparer<OrderedKey>.Default.Compare(left, right) >= 0;

    /// <summary>
    /// Folds only ASCII letters so the persisted equality key does not depend on changing Unicode tables.
    /// </summary>
    private static string Normalize(string value) => string.Create(value.Length, value, static (destination, original) =>
    {
        for (int index = 0; index < original.Length; index++)
        {
            char character = original[index];
            destination[index] = character is >= 'a' and <= 'z' ? (char)(character - ('a' - 'A')) : character;
        }
    });
}
