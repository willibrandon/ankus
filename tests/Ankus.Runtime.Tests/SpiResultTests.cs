namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies first-row selection and exact type, NULL, and column-count contracts.
/// </summary>
[TestClass]
public sealed class SpiResultTests
{
    /// <summary>
    /// Verifies each ordinal selects the first row and retains its exact managed value.
    /// </summary>
    [TestMethod]
    public void FirstValuesPreserveOrderAndTypes()
    {
        SpiColumn[] columns = [new("number", 23), new("text", 25), new("bytes", 17)];
        byte[] bytes = [0, 255, 0];
        var result = new SpiResult(columns,
            [new SpiRow([0, "", bytes], columns), new SpiRow([99, "later", null], columns)], 2);

        Assert.AreEqual(0, result.GetFirstValue<int>(0));
        Assert.AreEqual("", result.GetFirstValue<string>(1));
        Assert.AreSequenceEqual(bytes, result.GetFirstValue<byte[]>(2));
        Assert.ThrowsExactly<InvalidCastException>(() => result.GetFirstValue<long>(0));
        Assert.ThrowsExactly<InvalidCastException>(() => result.GetFirstValue<int>(1));
    }

    /// <summary>
    /// Verifies empty results do not invent values for any requested column.
    /// </summary>
    /// <param name="ordinal">The requested first-row ordinal.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void EmptyResultsRequireNullableTypes(int ordinal)
    {
        var result = new SpiResult([], [], 0);
        Assert.IsNull(result.GetFirstValue<int?>(ordinal));
        Assert.IsNull(result.GetFirstValue<string?>(ordinal));
        Assert.ThrowsExactly<InvalidOperationException>(() => result.GetFirstValue<int>(ordinal));
    }

    /// <summary>
    /// Verifies a present SQL NULL is distinct from a missing column in a returned row.
    /// </summary>
    [TestMethod]
    public void NullCellsAndMissingColumnsHaveDifferentContracts()
    {
        SpiColumn[] columns = [new("number", 23), new("text", 25)];
        var result = new SpiResult(columns, [new SpiRow([null, null], columns)], 1);

        Assert.IsNull(result.GetFirstValue<int?>(0));
        Assert.IsNull(result.GetFirstValue<string?>(1));
        InvalidOperationException nullError = Assert.ThrowsExactly<InvalidOperationException>(() => result.GetFirstValue<int>(0));
        Assert.Contains("SQL NULL", nullError.Message);
        InvalidOperationException missingError = Assert.ThrowsExactly<InvalidOperationException>(() => result.GetFirstValue<int?>(2));
        Assert.AreEqual("The SPI result has 2 columns; column 3 was requested.", missingError.Message);
    }

    /// <summary>
    /// Verifies a returned row without any columns does not count as an empty result.
    /// </summary>
    [TestMethod]
    public void ZeroColumnRowsRejectFirstValue()
    {
        var result = new SpiResult([], [new SpiRow([], [])], 1);
        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() => result.GetFirstValue<int?>(0));
        Assert.AreEqual("The SPI result has 0 columns; column 1 was requested.", error.Message);
    }
}
