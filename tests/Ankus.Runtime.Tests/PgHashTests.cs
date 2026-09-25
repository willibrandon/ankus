using System.Text;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Pins the public hash contract to literals obtained from both unmodified SeaHash 4.1.0 implementations.
/// </summary>
/// <remarks>
/// Reference: https://docs.rs/crate/seahash/4.1.0/source/src/reference.rs and helper.rs.
/// Vectors use pgrx's four frozen seeds and take the signed low 32 bits of the finished hash.
/// Byte-sequence fixtures contain byte(index modulo 256), without a Rust Hash type-specific feed.
/// </remarks>
[TestClass]
public sealed class PgHashTests
{
    /// <summary>
    /// Verifies every short tail, lane transition, complete block and larger repeated-byte range against independent literals.
    /// </summary>
    /// <param name="length">The number of ascending bytes, wrapping after 255.</param>
    /// <param name="expected">The independently computed signed low 32 bits.</param>
    [TestMethod]
    [DataRow(0, 628087993)]
    [DataRow(1, 42649085)]
    [DataRow(2, 1068648851)]
    [DataRow(3, 829891988)]
    [DataRow(4, 1768380920)]
    [DataRow(5, -970883977)]
    [DataRow(6, -42448081)]
    [DataRow(7, 698895325)]
    [DataRow(8, 382277149)]
    [DataRow(9, -459157994)]
    [DataRow(15, -2092589225)]
    [DataRow(16, -338059607)]
    [DataRow(17, 144857830)]
    [DataRow(23, -1367095003)]
    [DataRow(24, -1639928012)]
    [DataRow(25, -588764241)]
    [DataRow(31, 1562922755)]
    [DataRow(32, 407511119)]
    [DataRow(33, 1463953940)]
    [DataRow(63, 990292032)]
    [DataRow(64, -1667179664)]
    [DataRow(65, 1941393660)]
    [DataRow(255, 2076268672)]
    [DataRow(256, -496314881)]
    [DataRow(257, 6990154)]
    [DataRow(1024, -834075834)]
    [DataRow(4097, -1596753619)]
    public void ByteSequencesMatchSeaHashFourReferenceVectors(int length, int expected)
    {
        byte[] bytes = new byte[length];
        for (int index = 0; index < bytes.Length; index++)
        {
            bytes[index] = unchecked((byte)index);
        }

        byte[] original = [.. bytes];
        byte[] enclosed = [0xFF, .. bytes, 0xEE];
        byte[] unchanged = [0xFF, .. bytes, 0xEE];
        Assert.AreEqual(expected, PgHash.Compute(bytes));
        Assert.AreEqual(expected, PgHash.Compute(enclosed.AsSpan(1, length)));
        Assert.AreSequenceEqual(original, bytes);
        Assert.AreSequenceEqual(unchanged, enclosed);
    }

    /// <summary>
    /// Distinguishes original zero-byte lengths even when padded input words would otherwise be identical.
    /// </summary>
    /// <param name="length">The original number of zero bytes.</param>
    /// <param name="expected">The independent reference hash.</param>
    [TestMethod]
    [DataRow(1, 42649085)]
    [DataRow(2, 58130881)]
    [DataRow(7, -553953610)]
    [DataRow(8, -1536329377)]
    [DataRow(9, -165198700)]
    [DataRow(16, 1887564375)]
    [DataRow(32, -1638458130)]
    [DataRow(33, 1118891690)]
    public void ZeroBytesRetainTheirOriginalLength(int length, int expected)
    {
        Assert.AreEqual(expected, PgHash.Compute(new byte[length]));
    }

    /// <summary>
    /// Retains nonzero tail bits and distinguishes an explicit trailing zero from internal padding.
    /// </summary>
    /// <param name="hex">The complete independent byte fixture.</param>
    /// <param name="expected">The independent reference hash.</param>
    [TestMethod]
    [DataRow("01", 1029305192)]
    [DataRow("0100", -754362522)]
    [DataRow("FFFFFFFFFFFFFFFFFF", 1606253400)]
    public void ExplicitByteFixturesRetainEveryBit(string hex, int expected)
    {
        Assert.AreEqual(expected, PgHash.Compute(Convert.FromHexString(hex)));
    }

    /// <summary>
    /// Uses strict UTF-8 with no added BOM, retaining embedded zero and normalization distinctions.
    /// </summary>
    /// <param name="value">The exact text, without implicit normalization.</param>
    /// <param name="utf8">The independently specified UTF-8 bytes.</param>
    /// <param name="expected">The independent reference hash of those bytes.</param>
    [TestMethod]
    [DataRow("", "", 628087993)]
    [DataRow("hello", "68656C6C6F", 494167945)]
    [DataRow("a\0b", "610062", -855795698)]
    [DataRow("héllo 😀", "68C3A96C6C6F20F09F9880", 1050026984)]
    [DataRow("漢字", "E6BCA2E5AD97", 2051374144)]
    [DataRow("\uFEFFhello", "EFBBBF68656C6C6F", -1256757024)]
    [DataRow("\0", "00", 42649085)]
    [DataRow("é", "C3A9", 853363103)]
    [DataRow("e\u0301", "65CC81", -2072221039)]
    [DataRow("\U00010000", "F0908080", 1643535738)]
    [DataRow("\U0010FFFF", "F48FBFBF", -785625998)]
    [DataRow("to be or not to be", "746F206265206F72206E6F7420746F206265", 1867179381)]
    public void TextUsesExactUtf8ReferenceVectors(string value, string utf8, int expected)
    {
        Assert.AreEqual(expected, PgHash.Compute(value));
        Assert.AreEqual(expected, PgHash.Compute(Convert.FromHexString(utf8)));
    }

    /// <summary>
    /// Hashes a larger multibyte string completely, including its final supplementary character.
    /// </summary>
    [TestMethod]
    public void LargeUnicodeTextUsesCompleteUtf8Payload()
    {
        string value = new string('é', 1024) + "😀";
        Assert.AreEqual(-1497382656, PgHash.Compute(value));
    }

    /// <summary>
    /// Rejects isolated or repeated surrogates at the input boundaries and between otherwise valid text.
    /// </summary>
    /// <param name="codeUnit">The unpaired high or low surrogate code unit.</param>
    [TestMethod]
    [DataRow(0xD800)]
    [DataRow(0xDBFF)]
    [DataRow(0xDC00)]
    [DataRow(0xDFFF)]
    public void UnpairedUtf16SurrogatesAreRejected(int codeUnit)
    {
        string invalid = new((char)codeUnit, 1);
        Assert.ThrowsExactly<EncoderFallbackException>(() => PgHash.Compute(invalid));
        Assert.ThrowsExactly<EncoderFallbackException>(() => PgHash.Compute("valid" + invalid));
        Assert.ThrowsExactly<EncoderFallbackException>(() => PgHash.Compute(invalid + "valid"));
        Assert.ThrowsExactly<EncoderFallbackException>(() => PgHash.Compute(invalid + invalid));
    }

    /// <summary>
    /// Rejects reversed or separated surrogate pairs rather than hashing replacement characters.
    /// </summary>
    [TestMethod]
    public void InvalidSurrogatePairsAreRejected()
    {
        Assert.ThrowsExactly<EncoderFallbackException>(() => PgHash.Compute("\uDC00\uD800"));
        Assert.ThrowsExactly<EncoderFallbackException>(() => PgHash.Compute("\uD800x\uDC00"));
    }

    /// <summary>
    /// A null string is invalid while an empty default span remains a valid byte representation.
    /// </summary>
    [TestMethod]
    public void NullTextAndEmptyBytesRemainDistinct()
    {
        string missing = null!;
        Assert.AreEqual("value", Assert.ThrowsExactly<ArgumentNullException>(() => PgHash.Compute(missing)).ParamName);
        Assert.AreEqual(628087993, PgHash.Compute(default(ReadOnlySpan<byte>)));
    }

    /// <summary>
    /// Pins unsigned values to exactly eight little-endian bytes, including both endpoints and the signed boundary.
    /// </summary>
    /// <param name="value">The complete unsigned value.</param>
    /// <param name="littleEndian">The independent eight-byte encoding.</param>
    /// <param name="expected">The independent reference hash.</param>
    [TestMethod]
    [DataRow(0UL, "0000000000000000", -1536329377)]
    [DataRow(1UL, "0100000000000000", -604746904)]
    [DataRow(0x0102030405060708UL, "0807060504030201", 1942809490)]
    [DataRow(0x8000000000000000UL, "0000000000000080", 721385558)]
    [DataRow(ulong.MaxValue, "FFFFFFFFFFFFFFFF", 851917799)]
    public void UnsignedValuesUseEightLittleEndianBytes(ulong value, string littleEndian, int expected)
    {
        Assert.AreEqual(expected, PgHash.Compute(value));
        Assert.AreEqual(expected, PgHash.Compute(Convert.FromHexString(littleEndian)));
    }

    /// <summary>
    /// Repeated, interleaved calls retain the fixed initial state without sharing prior input.
    /// </summary>
    [TestMethod]
    public void InterleavedCallsDoNotShareHashState()
    {
        Assert.AreEqual(494167945, PgHash.Compute("hello"));
        Assert.AreEqual(851917799, PgHash.Compute(ulong.MaxValue));
        Assert.AreEqual(-855795698, PgHash.Compute("a\0b"));
        Assert.AreEqual(494167945, PgHash.Compute("hello"));
        Assert.AreEqual(628087993, PgHash.Compute(ReadOnlySpan<byte>.Empty));
    }
}
