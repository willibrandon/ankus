namespace Ankus.Runtime.Tests;

/// <summary>
/// Checks flat array limits before allocation, as pgrx's <c>error_cases</c> does for <c>FlatArray::new_zeroed_in</c>.
/// </summary>
[TestClass]
public sealed class PgFlatArrayTests
{
    /// <summary>
    /// Too many elements, a zero-length dimension, too many bytes and too many dimensions fail without allocating.
    /// </summary>
    [TestMethod]
    public void LimitsAreCheckedBeforeAllocation()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        PgMemoryContext context = PgMemoryContext.Current;
        Assert.Contains("134217727 elements", Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => context.CreateFlatArray<int>([int.MaxValue])).Message);
        Assert.Contains("134217727 elements", Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => context.CreateFlatArray<int>([65536, 65536])).Message);
        Assert.Contains("must be positive", Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => context.CreateFlatArray<int>([0])).Message);
        Assert.Contains("allocation limit", Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => context.CreateFlatArray<long>([(0x3FFF_FFFF / 8) - 1])).Message);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => context.CreateFlatArray<int>([1, 1, 1, 1, 1, 1, 1]));
        Assert.ThrowsExactly<ArgumentException>(() => context.CreateFlatArray<int>([2, 2], [1]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => context.CreateFlatArray<int>([2], [int.MaxValue]));
        Assert.ThrowsExactly<NotSupportedException>(() => context.CreateFlatArray<Guid>([1]));
        Assert.IsEmpty(fixture.Requests.Where(static request => request._operation == NativeMemoryOperation.Allocate));
    }
}
