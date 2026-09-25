namespace Ankus.Examples.Ranges;

/// <summary>
/// Ports pgrx's range example with detached bounds and native PostgreSQL canonicalization.
/// </summary>
public static class RangeFunctions
{
    /// <summary>
    /// Includes the lower integer and excludes the upper integer.
    /// </summary>
    /// <param name="start">The included lower bound.</param>
    /// <param name="end">The excluded upper bound.</param>
    /// <returns>The requested integer range, canonicalized when returned to PostgreSQL.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static PgRange<int> Range(int start, int end) => PgRange.Create(start, end);

    /// <summary>
    /// Includes the lower integer and leaves the upper end unbounded.
    /// </summary>
    /// <param name="start">The included lower bound.</param>
    /// <returns>The range beginning at the supplied integer.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static PgRange<int> RangeFrom(int start) => new(start, null);

    /// <summary>
    /// Constructs a range without either finite bound.
    /// </summary>
    /// <returns>The unbounded integer range.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static PgRange<int> RangeFull() => PgRange.Unbounded<int>();

    /// <summary>
    /// Includes both integer ends before PostgreSQL selects its discrete canonical representation.
    /// </summary>
    /// <param name="start">The included lower bound.</param>
    /// <param name="end">The included upper bound.</param>
    /// <returns>The inclusive integer range.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static PgRange<int> RangeInclusive(int start, int end) => PgRange.Create(start, end, upperInclusive: true);

    /// <summary>
    /// Leaves the lower end unbounded and excludes the upper integer.
    /// </summary>
    /// <param name="end">The excluded upper bound.</param>
    /// <returns>The range ending before the supplied integer.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static PgRange<int> RangeTo(int end) => new(null, end);

    /// <summary>
    /// Leaves the lower end unbounded and includes the upper integer.
    /// </summary>
    /// <param name="end">The included upper bound.</param>
    /// <returns>The range ending at the supplied integer.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static PgRange<int> RangeToInclusive(int end) => new(null, end, upperInclusive: true);

    /// <summary>
    /// Constructs a present empty range, distinct from SQL NULL and from an unbounded range.
    /// </summary>
    /// <returns>The empty integer range.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static PgRange<int> Empty() => PgRange.Empty<int>();

    /// <summary>
    /// Constructs the same unbounded range as RangeFull through the named infinity example.
    /// </summary>
    /// <returns>The unbounded integer range.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static PgRange<int> Infinite() => PgRange.Unbounded<int>();

    /// <summary>
    /// Compares stored bounds against the requested lower-inclusive and upper-exclusive representation.
    /// </summary>
    /// <param name="value">The range received from PostgreSQL.</param>
    /// <param name="start">The expected included lower bound.</param>
    /// <param name="end">The expected excluded upper bound.</param>
    /// <returns>Whether the detached representations are equal.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static bool AssertRange(PgRange<int> value, int start, int end) => value.Equals(PgRange.Create(start, end));
}
