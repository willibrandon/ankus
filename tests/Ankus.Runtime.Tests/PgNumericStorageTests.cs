using System.Buffers.Binary;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Tests portable numeric protocol vectors and detached ownership independently of private PostgreSQL layouts.
/// </summary>
[TestClass]
public sealed class PgNumericStorageTests
{
    /// <summary>
    /// Compares base-10000 encoding and formatting with independently specified PostgreSQL binary vectors.
    /// </summary>
    /// <param name="text">Canonical PostgreSQL output.</param>
    /// <param name="hex">Network-order count, weight, sign, scale and digits.</param>
    [TestMethod]
    [DataRow("0", "0000000000000000")]
    [DataRow("0.0000", "0000000000000004")]
    [DataRow("1", "00010000000000000001")]
    [DataRow("-42", "0001000040000000002A")]
    [DataRow("9999", "0001000000000000270F")]
    [DataRow("10000", "00010001000000000001")]
    [DataRow("100000000", "00010002000000000001")]
    [DataRow("123.4500", "0002000000000004007B1194")]
    [DataRow("-123.4500", "0002000040000004007B1194")]
    [DataRow("12345.67890", "0003000100000005000109291A85")]
    [DataRow("1.00001", "00030000000000050001000003E8")]
    [DataRow("0.1", "0001FFFF0000000103E8")]
    [DataRow("0.0001", "0001FFFF000000040001")]
    [DataRow("0.00001", "0001FFFE0000000503E8")]
    [DataRow("0.00000001", "0001FFFE000000080001")]
    [DataRow("-0.00001", "0001FFFE4000000503E8")]
    [DataRow("NaN", "00000000C0000000")]
    [DataRow("Infinity", "00000000D0000000")]
    [DataRow("-Infinity", "00000000F0000000")]
    public void ProtocolVectorsPreserveExactValueAndDisplayScale(string text, string hex)
    {
        byte[] expected = Convert.FromHexString(hex);
        PgNumericStorage encoded = PgNumericStorage.FromCanonicalText(text);
        Assert.AreEqual(0, encoded.RawLength);
        Assert.AreSequenceEqual(expected, encoded.Buffer.ToArray());
        Assert.AreEqual(text, encoded.Text);
        PgNumericStorage decoded = PgNumericStorage.Copy(expected, 0);
        Assert.AreEqual(text, decoded.Text);
    }

    /// <summary>
    /// Validates complete copying of opaque datum and wire bytes before the borrowed buffer expires.
    /// </summary>
    [TestMethod]
    public void NativePayloadIsCopiedAndNeverInterpretedForFormatting()
    {
        byte[] borrowed = Convert.FromHexString("A1B2C3D40002000000000004007B1194");
        PgNumericStorage storage = PgNumericStorage.Copy(borrowed, 4);
        Array.Fill(borrowed, (byte)0);
        Assert.AreEqual(4, storage.RawLength);
        Assert.AreSequenceEqual(Convert.FromHexString("A1B2C3D4"), storage.Buffer[..4].ToArray());
        Assert.AreSequenceEqual(Convert.FromHexString("0002000000000004007B1194"), storage.Wire.ToArray());
        Assert.AreEqual("123.4500", storage.Text);
    }

    /// <summary>
    /// Covers both full-range boundaries without collapsing display scale or base-10000 weight gaps.
    /// </summary>
    [TestMethod]
    public void MaximumIntegerAndFractionalWidthsRemainExact()
    {
        string integer = new('9', 131072);
        PgNumericStorage maximum = PgNumericStorage.FromCanonicalText(integer);
        Assert.AreEqual(integer, maximum.Text);
        Assert.AreEqual(32768, BinaryPrimitives.ReadUInt16BigEndian(maximum.Wire));
        Assert.AreEqual(32767, BinaryPrimitives.ReadInt16BigEndian(maximum.Wire[2..]));
        string fractional = "0." + new string('0', 16382) + "1";
        PgNumericStorage minimum = PgNumericStorage.FromCanonicalText(fractional);
        Assert.AreEqual(fractional, minimum.Text);
        Assert.AreEqual(16383, BinaryPrimitives.ReadUInt16BigEndian(minimum.Wire[6..]));
        Assert.AreEqual(1, BinaryPrimitives.ReadUInt16BigEndian(minimum.Wire));
        Assert.ThrowsExactly<ArgumentException>(() => PgNumericStorage.FromCanonicalText(integer + "9"));
        Assert.ThrowsExactly<ArgumentException>(() => PgNumericStorage.FromCanonicalText(fractional + "0"));
        string combined = integer + fractional[1..];
        PgNumericStorage full = PgNumericStorage.FromCanonicalText(combined);
        Assert.AreEqual(combined, full.Text);
        Assert.AreEqual(36864, BinaryPrimitives.ReadUInt16BigEndian(full.Wire));
    }

    /// <summary>
    /// Rejects malformed, unsupported or lossy binary values before exposing them to managed conversions.
    /// </summary>
    /// <param name="hex">The malformed protocol vector.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("00000000000000")]
    [DataRow("0001000000000000")]
    [DataRow("00000000000000000000")]
    [DataRow("0000000020000000")]
    [DataRow("0000000000004000")]
    [DataRow("00010000000000002710")]
    [DataRow("0001000000000000FFFF")]
    [DataRow("0001FFFF000000010001")]
    [DataRow("0001FFFE000000040001")]
    [DataRow("0001FFFF000000020001")]
    [DataRow("0001FFFF000000030001")]
    public void InvalidProtocolVectorsAreRejected(string hex)
        => Assert.ThrowsExactly<InvalidOperationException>(() => PgNumericStorage.Copy(Convert.FromHexString(hex), 0));

    /// <summary>
    /// Treats special-value weight, scale and valid legacy digits as immaterial, matching numeric_recv.
    /// </summary>
    /// <param name="hex">A valid special numeric binary representation.</param>
    /// <param name="expected">The value determined solely by its special sign.</param>
    [TestMethod]
    [DataRow("00000000D0000020", "Infinity")]
    [DataRow("00000000F0000020", "-Infinity")]
    [DataRow("00010000C00000000001", "NaN")]
    [DataRow("00000000C0000001", "NaN")]
    [DataRow("00000001C0000000", "NaN")]
    public void SpecialProtocolMetadataDoesNotChangeValue(string hex, string expected)
    {
        PgNumericStorage storage = PgNumericStorage.Copy(Convert.FromHexString(hex), 0);
        Assert.AreEqual(expected, storage.Text);
        Assert.IsNull(storage.Scale);
        Assert.AreEqual(PgNumeric.FromCanonicalText(expected).Sign, storage.Sign);
    }

    /// <summary>
    /// Rejects invalid separation of the opaque datum from its portable representation.
    /// </summary>
    /// <param name="rawLength">The invalid native payload length.</param>
    [TestMethod]
    [DataRow(-1)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(9)]
    public void InvalidNativePayloadLengthsAreRejected(int rawLength)
        => Assert.ThrowsExactly<InvalidOperationException>(() => PgNumericStorage.Copy(new byte[8], rawLength));

    /// <summary>
    /// Formats redundant binary zero groups according to value rather than their transport padding.
    /// </summary>
    /// <param name="hex">The valid portable representation with redundant zeros.</param>
    /// <param name="expected">The canonical output.</param>
    [TestMethod]
    [DataRow("00010000400000000000", "0")]
    [DataRow("00030001000000020000007B0000", "123.00")]
    [DataRow("0002FFFF4000000400010000", "-0.0001")]
    public void RedundantBinaryZerosDoNotChangeValue(string hex, string expected)
        => Assert.AreEqual(expected, PgNumericStorage.Copy(Convert.FromHexString(hex), 0).Text);

    /// <summary>
    /// Rejects noncanonical text instead of accidentally treating it as numeric_send digits.
    /// </summary>
    /// <param name="text">The unsupported managed-only representation.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("-")]
    [DataRow("+1")]
    [DataRow(".1")]
    [DataRow("1.")]
    [DataRow("1..2")]
    [DataRow("1e2")]
    [DataRow("1 2")]
    public void ManagedEncodingRejectsNoncanonicalText(string text)
        => Assert.ThrowsExactly<ArgumentException>(() => PgNumericStorage.FromCanonicalText(text));

    /// <summary>
    /// Tests the public generated-code transport and verifies copied values survive allocator release.
    /// </summary>
    /// <param name="text">The canonical managed-origin numeric.</param>
    [TestMethod]
    [DataRow("0")]
    [DataRow("-123.4500")]
    [DataRow("NaN")]
    [DataRow("Infinity")]
    [DataRow("-Infinity")]
    public void NumericTransportRetainsOwnedBinaryAfterRelease(string text)
    {
        NativeValue transport = NativeValue.FromNumeric(PgNumeric.FromCanonicalText(text));
        PgNumeric detached;
        try
        {
            Assert.AreSequenceEqual(PgNumericStorage.FromCanonicalText(text).Wire.ToArray(), transport.ReadBytes());
            Assert.AreEqual(0L, transport.Integral);
            detached = transport.ReadNumeric();
        }
        finally
        {
            transport.Release();
        }

        Assert.AreEqual(text, detached.Text);
        Assert.AreEqual(PgNumeric.FromCanonicalText(text), detached);
    }

    /// <summary>
    /// Rejects text, unrelated bytes and NULL rather than interpreting them as binary numeric payloads.
    /// </summary>
    [TestMethod]
    public void NumericTransportRequiresItsExactDiscriminator()
    {
        NativeValue text = NativeValue.FromString("1.2300");
        NativeValue binary = NativeValue.FromBytes(Convert.FromHexString("00010000000000000001"));
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => text.ReadNumeric());
            Assert.ThrowsExactly<InvalidOperationException>(() => binary.ReadNumeric());
            Assert.ThrowsExactly<InvalidOperationException>(() => default(NativeValue).ReadNumeric());
        }
        finally
        {
            text.Release();
            binary.Release();
        }
    }
}
