namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies selection's backend access boundary and explicit native snapshot modes.
/// </summary>
[TestClass]
public sealed class SpiSelectionTests
{
    /// <summary>
    /// Each explicit or transaction-aware choice has a distinct native request mode without changing the request layout.
    /// </summary>
    /// <param name="readOnly">The managed choice.</param>
    /// <param name="expected">The native request mode.</param>
    [TestMethod]
    [DataRow(false, (byte)0)]
    [DataRow(true, (byte)1)]
    [DataRow(null, (byte)2)]
    public void SnapshotModesPreserveExplicitChoices(bool? readOnly, byte expected)
        => Assert.AreEqual(expected, NativeSpiRequest.GetReadMode(readOnly));

    /// <summary>
    /// Selection APIs reject calls outside a PostgreSQL callback before dispatching a native request.
    /// </summary>
    [TestMethod]
    public void SelectionRequiresAnActiveBackend()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => Spi.Select("SELECT 42"));
        Assert.ThrowsExactly<InvalidOperationException>(() => Spi.Select("SELECT 42", 1));
        Assert.ThrowsExactly<InvalidOperationException>(() => Spi.SelectRaw("SELECT 42"));
        Assert.ThrowsExactly<InvalidOperationException>(() => Spi.SelectRaw("SELECT 42", 1));
    }
}
