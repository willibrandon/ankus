using System.Text;

namespace Ankus.Examples.MinimalRuntime;

/// <summary>
/// A text value with generated equality, ordering and hashing, like pgrx's <c>Thing</c> with
/// <c>PostgresEq</c>, <c>PostgresOrd</c> and <c>PostgresHash</c>.
/// </summary>
/// <param name="Value">The text.</param>
[PgType]
[PgEquality]
[PgOrdering]
[PgHashing]
public sealed record Thing(string Value) : IComparable<Thing>, IPgHashable
{
    /// <summary>
    /// Orders values by their UTF-8 bytes, which is Rust's <c>String</c> ordering.
    /// </summary>
    /// <param name="other">The value to compare, or null.</param>
    /// <returns>A negative, zero or positive result.</returns>
    public int CompareTo(Thing? other)
        => other is null ? 1 : Encoding.UTF8.GetBytes(Value).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(other.Value));

    /// <summary>
    /// Returns a stable hash of the UTF-8 text for PostgreSQL hash indexes.
    /// </summary>
    /// <returns>The stable signed 32-bit hash.</returns>
    public int GetPostgresHashCode() => PgHash.Compute(Value);

    /// <summary>
    /// Compares values with null first.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether the left value sorts first.</returns>
    public static bool operator <(Thing? left, Thing? right) => Comparer<Thing>.Default.Compare(left, right) < 0;

    /// <summary>
    /// Compares values with null first.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether the left value sorts last.</returns>
    public static bool operator >(Thing? left, Thing? right) => Comparer<Thing>.Default.Compare(left, right) > 0;

    /// <summary>
    /// Compares values with null first.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether the left value sorts first or equal.</returns>
    public static bool operator <=(Thing? left, Thing? right) => Comparer<Thing>.Default.Compare(left, right) <= 0;

    /// <summary>
    /// Compares values with null first.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether the left value sorts last or equal.</returns>
    public static bool operator >=(Thing? left, Thing? right) => Comparer<Thing>.Default.Compare(left, right) >= 0;
}
