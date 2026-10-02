namespace Ankus.TestExtension;

/// <summary>
/// Exposes managed interval ordering to PostgreSQL's independent native comparison oracle.
/// </summary>
public static class IntervalOrderingFunctions
{
    /// <summary>
    /// Returns the managed comparison sign and four relational operator results without native temporal calls.
    /// </summary>
    /// <param name="left">The left native interval.</param>
    /// <param name="right">The right native interval.</param>
    /// <returns>The comparison sign followed by less, greater, less-or-equal and greater-or-equal flags.</returns>
    [PgFunction]
    public static int[] IntervalOrdering(PgInterval left, PgInterval right)
        => [left.CompareTo(right), left < right ? 1 : 0, left > right ? 1 : 0,
            left <= right ? 1 : 0, left >= right ? 1 : 0];
}
