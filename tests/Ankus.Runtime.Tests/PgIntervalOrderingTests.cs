namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies PostgreSQL interval ordering without backend access or component normalization.
/// </summary>
[TestClass]
public sealed class PgIntervalOrderingTests
{
    /// <summary>
    /// Finite comparisons use PostgreSQL's duration approximation, including mixed signs and wide intermediate values.
    /// </summary>
    /// <param name="leftMonths">The left calendar months.</param>
    /// <param name="leftDays">The left calendar days.</param>
    /// <param name="leftMicros">The left elapsed microseconds.</param>
    /// <param name="rightMonths">The right calendar months.</param>
    /// <param name="rightDays">The right calendar days.</param>
    /// <param name="rightMicros">The right elapsed microseconds.</param>
    /// <param name="expectedComparison">The independently specified comparison sign.</param>
    [TestMethod]
    [DataRow(0, 0, 0L, 0, 0, 0L, 0)]
    [DataRow(0, 0, -1L, 0, 0, 0L, -1)]
    [DataRow(0, 0, 1L, 0, 0, 0L, 1)]
    [DataRow(1, 0, 0L, 0, 30, 0L, 0)]
    [DataRow(-1, 0, 0L, 0, -30, 0L, 0)]
    [DataRow(0, 1, 0L, 0, 0, 86_400_000_000L, 0)]
    [DataRow(1, -30, -1L, 0, 0, 0L, -1)]
    [DataRow(1, -30, 0L, 0, 0, 0L, 0)]
    [DataRow(1, -30, 1L, 0, 0, 0L, 1)]
    [DataRow(1, -31, 86_400_000_001L, 0, 0, 0L, 1)]
    [DataRow(-1, 31, -86_400_000_001L, 0, 0, 0L, -1)]
    [DataRow(int.MaxValue, int.MaxValue, long.MaxValue, 0, 0, long.MaxValue, 1)]
    [DataRow(int.MinValue, int.MinValue, long.MinValue, 0, 0, long.MinValue, -1)]
    [DataRow(int.MaxValue, int.MinValue, long.MinValue, 0, 0, long.MaxValue, 1)]
    [DataRow(int.MinValue, int.MaxValue, long.MaxValue, 0, 0, long.MinValue, -1)]
    [DataRow(int.MaxValue, 0, long.MinValue, int.MaxValue, 0, long.MaxValue, -1)]
    public void FiniteOrderingUsesPostgresComparisonConvention(int leftMonths, int leftDays, long leftMicros,
        int rightMonths, int rightDays, long rightMicros, int expectedComparison)
    {
        var left = new PgInterval(leftMonths, leftDays, leftMicros);
        var right = new PgInterval(rightMonths, rightDays, rightMicros);
        AssertOrdering(left, right, expectedComparison);
        AssertOrdering(right, left, -expectedComparison);
    }

    /// <summary>
    /// Both explicit infinities bound even finite values containing all signed endpoint components.
    /// </summary>
    [TestMethod]
    public void InfinityOrderingBoundsEveryFiniteRepresentation()
    {
        PgInterval[] finite = [default, new(int.MinValue, int.MinValue, long.MinValue),
            new(int.MaxValue, int.MaxValue, long.MaxValue)];
        foreach (PgInterval value in finite)
        {
            AssertOrdering(PgInterval.NegativeInfinity, value, -1);
            AssertOrdering(value, PgInterval.NegativeInfinity, 1);
            AssertOrdering(PgInterval.PositiveInfinity, value, 1);
            AssertOrdering(value, PgInterval.PositiveInfinity, -1);
        }

        AssertOrdering(PgInterval.NegativeInfinity, PgInterval.PositiveInfinity, -1);
        AssertOrdering(PgInterval.PositiveInfinity, PgInterval.NegativeInfinity, 1);
        AssertOrdering(PgInterval.NegativeInfinity, PgInterval.NegativeInfinity, 0);
        AssertOrdering(PgInterval.PositiveInfinity, PgInterval.PositiveInfinity, 0);
    }

    /// <summary>
    /// The default generic comparer sorts finite, mixed-sign and infinite intervals without backend calls.
    /// </summary>
    [TestMethod]
    public void IntervalsSortOutsideTheBackend()
    {
        PgInterval[] values = [PgInterval.PositiveInfinity, new(1, -30, 1), new(1, -30, -1),
            new(int.MaxValue, int.MaxValue, long.MaxValue), default, PgInterval.NegativeInfinity,
            new(int.MinValue, int.MinValue, long.MinValue)];
        Array.Sort(values);
        Assert.AreSequenceEqual([PgInterval.NegativeInfinity, new(int.MinValue, int.MinValue, long.MinValue),
            new(1, -30, -1), default, new(1, -30, 1), new(int.MaxValue, int.MaxValue, long.MaxValue),
            PgInterval.PositiveInfinity], values);
    }

    /// <summary>
    /// Equivalent intervals share equality and collection membership while retaining independent stored components.
    /// </summary>
    [TestMethod]
    public void ComparisonEqualityPreservesComponentsAndCollectionMembership()
    {
        var month = new PgInterval(1, 0, 0);
        var days = new PgInterval(0, 30, 0);
        var elapsed = new PgInterval(0, 0, 2_592_000_000_000);
        AssertOrdering(month, days, 0);
        AssertOrdering(days, elapsed, 0);
        Assert.AreEqual(month, days);
        Assert.AreEqual(month, elapsed);
        Assert.AreEqual(days, elapsed);
        Assert.AreEqual((1, 0, 0L), (month.Months, month.Days, month.Microseconds));
        Assert.AreEqual((0, 30, 0L), (days.Months, days.Days, days.Microseconds));
        Assert.AreEqual((0, 0, 2_592_000_000_000L), (elapsed.Months, elapsed.Days, elapsed.Microseconds));
        HashSet<PgInterval> identities = [month, days, elapsed];
        SortedSet<PgInterval> ordered = [month, days, elapsed];
        Assert.HasCount(1, identities);
        Assert.HasCount(1, ordered);
        Assert.Contains(elapsed, identities);
        Assert.Contains(elapsed, ordered);
        Assert.DoesNotContain(new PgInterval(1, 0, 1), identities);
        Assert.DoesNotContain(new PgInterval(1, 0, 1), ordered);
    }

    /// <summary>
    /// Checks comparison and every ordering operator against an independently specified sign.
    /// </summary>
    /// <param name="left">The left interval.</param>
    /// <param name="right">The right interval.</param>
    /// <param name="expectedComparison">The expected comparison sign.</param>
    private static void AssertOrdering(PgInterval left, PgInterval right, int expectedComparison)
    {
        Assert.AreEqual(expectedComparison, left.CompareTo(right));
        bool[] comparisons = [left < right, left > right, left <= right, left >= right];
        Assert.AreSequenceEqual([expectedComparison < 0, expectedComparison > 0,
            expectedComparison <= 0, expectedComparison >= 0], comparisons);
        Assert.AreEqual(expectedComparison == 0, left.Equals(right));
        Assert.AreEqual(expectedComparison == 0, left.Equals((object)right));
        Assert.AreEqual(expectedComparison == 0, left == right);
        Assert.AreEqual(expectedComparison != 0, left != right);
        Assert.IsFalse(left.Equals(null));
        Assert.IsFalse(left.Equals("interval"));
        HashSet<PgInterval> hashed = [left, right];
        SortedSet<PgInterval> ordered = [left, right];
        Assert.HasCount(expectedComparison == 0 ? 1 : 2, hashed);
        Assert.HasCount(expectedComparison == 0 ? 1 : 2, ordered);
        if (expectedComparison == 0)
        {
            Assert.AreEqual(left.GetHashCode(), right.GetHashCode());
        }
    }
}
