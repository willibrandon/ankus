namespace Ankus.Examples.Operators;

/// <summary>
/// Stores text with generated equality, B-tree ordering and hash operators, families and classes.
/// </summary>
/// <param name="Value">The stored text, compared in Unicode scalar order like a Rust string.</param>
[PgType]
[PgEquality]
[PgOrdering]
[PgHashing]
public sealed record Thing(string Value) : IComparable<Thing>, IPgHashable
{
    /// <summary>
    /// Orders values by Unicode scalar value, which is also the order of their UTF-8 bytes.
    /// </summary>
    /// <param name="other">The value to compare, or null.</param>
    /// <returns>A negative, zero or positive result; zero exactly when the record values are equal.</returns>
    public int CompareTo(Thing? other) => other is null ? 1 : CompareScalars(Value, other.Value);

    /// <summary>
    /// Returns the stable SeaHash of the UTF-8 text for PostgreSQL hash indexes.
    /// </summary>
    /// <returns>The stable signed 32-bit hash.</returns>
    public int GetPostgresHashCode() => PgHash.Compute(Value);

    /// <summary>
    /// Compares values with null before present values.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether the left value sorts before the right value.</returns>
    public static bool operator <(Thing? left, Thing? right)
    {
        return Comparer<Thing>.Default.Compare(left, right) < 0;
    }

    /// <summary>
    /// Compares values with null before present values.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether the left value sorts after the right value.</returns>
    public static bool operator >(Thing? left, Thing? right)
    {
        return Comparer<Thing>.Default.Compare(left, right) > 0;
    }

    /// <summary>
    /// Compares values with null before present values.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether the left value sorts before or equals the right value.</returns>
    public static bool operator <=(Thing? left, Thing? right)
    {
        return Comparer<Thing>.Default.Compare(left, right) <= 0;
    }

    /// <summary>
    /// Compares values with null before present values.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether the left value sorts after or equals the right value.</returns>
    public static bool operator >=(Thing? left, Thing? right)
    {
        return Comparer<Thing>.Default.Compare(left, right) >= 0;
    }

    /// <summary>
    /// Compares UTF-16 text in Unicode scalar order instead of ordinal UTF-16 code-unit order.
    /// </summary>
    /// <remarks>
    /// Ordinal comparison sorts surrogate pairs (U+10000 and above) before U+E000 through U+FFFF.
    /// Moving surrogate code units after that range restores scalar order while keeping zero for exactly equal text.
    /// </remarks>
    private static int CompareScalars(string left, string right)
    {
        int length = Math.Min(left.Length, right.Length);
        for (int index = 0; index < length; index++)
        {
            if (left[index] != right[index])
            {
                return ScalarOrderKey(left[index]) - ScalarOrderKey(right[index]);
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    /// <summary>
    /// Maps a UTF-16 code unit to a key whose order matches the scalar values it encodes.
    /// </summary>
    private static int ScalarOrderKey(char value) => value switch
    {
        >= '' => value - 0x800,
        >= '\uD800' => value + 0x2000,
        _ => value,
    };
}
