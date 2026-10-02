namespace Ankus.TestExtension;

/// <summary>
/// Exposes managed interval ordering to PostgreSQL's independent native comparison oracle.
/// </summary>
public static class IntervalOrderingFunctions
{
    /// <summary>
    /// Returns managed ordering, equality and equivalent-value hash results without native temporal calls.
    /// </summary>
    /// <param name="left">The left native interval.</param>
    /// <param name="right">The right native interval.</param>
    /// <returns>The comparison sign, relational flags, equality flags and equal-value hash agreement.</returns>
    [PgFunction]
    public static int[] IntervalOrdering(PgInterval left, PgInterval right)
        => [left.CompareTo(right), left < right ? 1 : 0, left > right ? 1 : 0,
            left <= right ? 1 : 0, left >= right ? 1 : 0, left == right ? 1 : 0, left != right ? 1 : 0,
            left != right || left.GetHashCode() == right.GetHashCode() ? 1 : 0];
}
