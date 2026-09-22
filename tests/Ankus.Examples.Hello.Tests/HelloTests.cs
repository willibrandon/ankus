namespace Ankus.Examples.Hello.Tests;

/// <summary>
/// Verifies that extension authors can test their ordinary managed functions directly with MSTest.
/// </summary>
[TestClass]
public sealed class HelloTests
{
    /// <summary>
    /// Verifies signed integer addition through the sample's public .NET API.
    /// </summary>
    /// <param name="left">The first operand.</param>
    /// <param name="right">The second operand.</param>
    /// <param name="expected">The expected sum.</param>
    [TestMethod]
    [DataRow(40, 2, 42)]
    [DataRow(-20, 5, -15)]
    [DataRow(0, 0, 0)]
    public void AddReturnsSum(int left, int right, int expected)
    {
        Assert.AreEqual(expected, Hello.Add(left, right));
    }

    /// <summary>
    /// Verifies the sample's checked arithmetic at both signed integer boundaries.
    /// </summary>
    /// <param name="left">The first operand.</param>
    /// <param name="right">The overflowing second operand.</param>
    [TestMethod]
    [DataRow(int.MaxValue, 1)]
    [DataRow(int.MinValue, -1)]
    public void AddRejectsOverflow(int left, int right)
    {
        Assert.ThrowsExactly<OverflowException>(() => Hello.Add(left, right));
    }
}
