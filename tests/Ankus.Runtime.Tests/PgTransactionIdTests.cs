namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies PostgreSQL transaction ID identity, epoch expansion, and detached datum transport.
/// </summary>
[TestClass]
public sealed class PgTransactionIdTests
{
    /// <summary>
    /// Keeps PostgreSQL's special transaction IDs outside epoch expansion.
    /// </summary>
    /// <param name="raw">The special transaction ID.</param>
    [TestMethod]
    [DataRow(0U)]
    [DataRow(1U)]
    [DataRow(2U)]
    public void SpecialTransactionIdsRemainUnexpanded(uint raw)
    {
        var value = new PgTransactionId(raw);
        Assert.AreEqual(raw, PgTransactionId.Expand(value, (17UL << 32) | 42));
        Assert.AreEqual(raw != 0, value.IsValid);
        Assert.IsFalse(value.IsNormal);
        Assert.AreEqual(raw.ToString(System.Globalization.CultureInfo.InvariantCulture), value.ToString());
    }

    /// <summary>
    /// Chooses the current, previous, or next epoch using PostgreSQL's wrap-aware ordering.
    /// </summary>
    /// <param name="raw">The 32-bit transaction ID.</param>
    /// <param name="nextRaw">The next transaction ID in the reference epoch.</param>
    /// <param name="expectedEpoch">The expected epoch.</param>
    [TestMethod]
    [DataRow(100U, 200U, 7U)]
    [DataRow(uint.MaxValue - 4, 10U, 6U)]
    [DataRow(5U, uint.MaxValue - 10, 8U)]
    public void NormalTransactionIdsSelectTheNearestEpoch(uint raw, uint nextRaw, uint expectedEpoch)
    {
        ulong expanded = PgTransactionId.Expand(new PgTransactionId(raw), (7UL << 32) | nextRaw);
        Assert.AreEqual(((ulong)expectedEpoch << 32) | raw, expanded);
        Assert.IsTrue(new PgTransactionId(raw).IsNormal);
    }

    /// <summary>
    /// Keeps xid distinct from oid in scalar and array SPI identities and maps invalid xid to SQL NULL.
    /// </summary>
    [TestMethod]
    public void SpiTransportPreservesXidIdentityAndNullSemantics()
    {
        var value = new PgTransactionId(uint.MaxValue);
        Assert.AreEqual(28U, SpiParameter.Create(value).TypeOid);
        Assert.AreEqual(value, SpiType.FromNative(SpiType.ToNative(value), 28));
        Assert.AreEqual(1011U, SpiParameter.Create(new PgTransactionId?[] { value, null }).TypeOid);

        NativeValue invalid = SpiType.ToNative(PgTransactionId.Invalid);
        Assert.AreEqual(1, invalid.IsNull);

        NativeValue array = NativeValue.FromArray(new PgArray<PgTransactionId?>([value, null]));
        try
        {
            Assert.AreSequenceEqual([value, null], array.ReadArray<PgTransactionId?>());
        }
        finally
        {
            array.Release();
        }
    }

    /// <summary>
    /// Exposes callback-only subtransaction IDs as typed numeric values.
    /// </summary>
    [TestMethod]
    public void SubtransactionIdsExposeTypedSpecialValues()
    {
        Assert.AreEqual(0U, PgSubtransactionId.Invalid.Value);
        Assert.AreEqual(1U, PgSubtransactionId.Top.Value);
        Assert.IsFalse(PgSubtransactionId.Invalid.IsValid);
        Assert.IsTrue(PgSubtransactionId.Top.IsValid);
        Assert.AreEqual("4294967295", new PgSubtransactionId(uint.MaxValue).ToString());
    }
}
