using System.Collections;
using System.Text;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies owned C-string bytes, terminators, explicit encoding and value semantics without a backend.
/// </summary>
[TestClass]
public sealed class PgCStringTests
{
    /// <summary>
    /// Construction and copies retain all unsigned payload bytes independently of their source.
    /// </summary>
    /// <param name="payload">The independent expected bytes, excluding the terminator.</param>
    [TestMethod]
    [DataRow(new byte[0])]
    [DataRow(new byte[] { 1 })]
    [DataRow(new byte[] { 127, 128, 255, 195 })]
    public void OwnedBytesRetainExactPayloadAndTerminator(byte[] payload)
    {
        byte[] source = [.. payload];
        var value = new PgCString(source);
        Array.Fill(source, (byte)42);
        Assert.AreEqual(payload.Length, value.Count);
        Assert.AreSequenceEqual<byte>(payload, [.. value.AsSpan()]);
        Func<byte[]> copy = value.ToArray;
        Assert.AreSequenceEqual(payload, copy());
        Assert.AreSequenceEqual<byte>([.. payload, 0], [.. value.AsNullTerminatedSpan()]);
        Assert.AreSequenceEqual(payload, value);
        IEnumerable nonGeneric = value;
        IEnumerator cursor = nonGeneric.GetEnumerator();
        foreach (byte expected in payload)
        {
            Assert.IsTrue(cursor.MoveNext());
            Assert.AreEqual(expected, Assert.IsInstanceOfType<byte>(cursor.Current));
        }

        Assert.IsFalse(cursor.MoveNext());
        byte[] copied = copy();
        Array.Fill(copied, (byte)43);
        Assert.AreSequenceEqual(payload, value);
        for (int index = 0; index < payload.Length; index++)
        {
            Assert.AreEqual(payload[index], value[index]);
        }

        Assert.ThrowsExactly<IndexOutOfRangeException>(() => _ = value[-1]);
        Assert.ThrowsExactly<IndexOutOfRangeException>(() => _ = value[value.Count]);
    }

    /// <summary>
    /// Complete inputs require exactly one final zero and never silently truncate data after a zero.
    /// </summary>
    [TestMethod]
    public void TerminatorFactoriesRejectLossAndPreserveEmpty()
    {
        var empty = new PgCString([]);
        PgCString terminatedEmpty = PgCString.FromNullTerminatedBytes([0]);
        Assert.AreEqual(empty, terminatedEmpty);
        Assert.AreEqual(0, terminatedEmpty.Count);
        Assert.AreSequenceEqual<byte>([0], [.. terminatedEmpty.AsNullTerminatedSpan()]);
        byte[] terminated = [255, 1, 0];
        PgCString value = PgCString.FromNullTerminatedBytes(terminated);
        terminated[0] = 2;
        Assert.AreSequenceEqual<byte>([255, 1], value);
        Assert.AreSequenceEqual<byte>([255, 1, 0], [.. value.AsNullTerminatedSpan()]);
    }

    /// <summary>
    /// Payload constructors reject zero at every position instead of truncating it.
    /// </summary>
    /// <param name="payload">An invalid non-terminated payload.</param>
    [TestMethod]
    [DataRow(new byte[] { 0 })]
    [DataRow(new byte[] { 0, 1 })]
    [DataRow(new byte[] { 1, 0, 2 })]
    [DataRow(new byte[] { 1, 0 })]
    public void PayloadRejectsEmbeddedTerminator(byte[] payload)
    {
        Assert.AreEqual("bytes", Assert.ThrowsExactly<ArgumentException>(() => new PgCString(payload)).ParamName);
    }

    /// <summary>
    /// Complete C strings reject missing terminators, repeated terminators and trailing payload after zero.
    /// </summary>
    /// <param name="bytes">The invalid complete representation.</param>
    [TestMethod]
    [DataRow(new byte[0])]
    [DataRow(new byte[] { 1 })]
    [DataRow(new byte[] { 0, 0 })]
    [DataRow(new byte[] { 1, 0, 2, 0 })]
    [DataRow(new byte[] { 0, 2 })]
    public void CompleteRepresentationRejectsInvalidTerminator(byte[] bytes)
    {
        Assert.AreEqual("bytes", Assert.ThrowsExactly<ArgumentException>(() => PgCString.FromNullTerminatedBytes(bytes)).ParamName);
    }

    /// <summary>
    /// Encoding is explicit, strict and independent of native server state.
    /// </summary>
    [TestMethod]
    public void Utf8ConversionRejectsReplacementWithoutChangingBytes()
    {
        PgCString text = PgCString.FromUtf8("café 🐘");
        Assert.AreSequenceEqual<byte>([99, 97, 102, 195, 169, 32, 240, 159, 144, 152], text);
        Assert.AreEqual("café 🐘", text.ToUtf8String());
        Assert.AreEqual(string.Empty, PgCString.FromUtf8(string.Empty).ToUtf8String());
        Assert.AreEqual("text", Assert.ThrowsExactly<ArgumentNullException>(() => PgCString.FromUtf8(null!)).ParamName);
        Assert.ThrowsExactly<EncoderFallbackException>(() => PgCString.FromUtf8("\ud800"));
        Assert.ThrowsExactly<ArgumentException>(() => PgCString.FromUtf8("a\0b"));
        var arbitrary = new PgCString([255, 195]);
        Assert.ThrowsExactly<DecoderFallbackException>(() => arbitrary.ToUtf8String());
        Assert.AreSequenceEqual<byte>([255, 195], arbitrary);
    }

    /// <summary>
    /// Copies reject insufficient capacity without writing a prefix and leave spare destination bytes untouched.
    /// </summary>
    [TestMethod]
    public void CopiesRequireWholePayloadCapacity()
    {
        var value = new PgCString([1, 128, 255]);
        byte[] small = [9, 9];
        Assert.ThrowsExactly<ArgumentException>(() => value.CopyTo(small));
        Assert.AreSequenceEqual<byte>([9, 9], small);
        byte[] exact = new byte[3];
        value.CopyTo(exact);
        Assert.AreSequenceEqual<byte>([1, 128, 255], exact);
        byte[] large = [7, 7, 7, 7];
        value.CopyTo(large);
        Assert.AreSequenceEqual<byte>([1, 128, 255, 7], large);
        new PgCString([]).CopyTo(large);
        Assert.AreSequenceEqual<byte>([1, 128, 255, 7], large);
    }

    /// <summary>
    /// Comparisons use complete unsigned bytes, preserve prefixes and order null before every present value.
    /// </summary>
    [TestMethod]
    public void ValueSemanticsUseCompleteUnsignedBytes()
    {
        PgCString?[] ordered = [null, new([]), new([1]), new([1, 1]), new([127]), new([128]), new([255])];
        for (int left = 0; left < ordered.Length; left++)
        {
            for (int right = 0; right < ordered.Length; right++)
            {
                PgCString? first = ordered[left];
                PgCString? second = ordered[right] is { } present ? new PgCString(present.AsSpan()) : null;
                Assert.AreEqual(left == right, first == second);
                Assert.AreEqual(left != right, first != second);
                Assert.AreEqual(left < right, first < second);
                Assert.AreEqual(left > right, first > second);
                Assert.AreEqual(left <= right, first <= second);
                Assert.AreEqual(left >= right, first >= second);
                Assert.AreEqual(left == right, Equals(first, second));
                if (first is not null)
                {
                    Assert.AreEqual(Math.Sign(left - right), Math.Sign(first.CompareTo(second)));
                    Assert.AreEqual(Math.Sign(left - right), Math.Sign(((IComparable)first).CompareTo(second)));
                    if (left == right)
                    {
                        Assert.AreEqual(first.GetHashCode(), second!.GetHashCode());
                    }
                }
            }
        }

        var value = new PgCString([1]);
        Assert.IsFalse(value.Equals(new object()));
        Assert.IsFalse(value.Equals(null));
        Assert.AreEqual("obj", Assert.ThrowsExactly<ArgumentException>(() => ((IComparable)value).CompareTo("1")).ParamName);
    }
}
