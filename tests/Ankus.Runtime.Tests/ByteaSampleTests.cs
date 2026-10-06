using System.IO.Compression;
using System.Text;
using Ankus.Examples.Bytea;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Checks the bytea sample's bytes, gzip framing and non-lossy text conversion directly.
/// </summary>
[TestClass]
public sealed class ByteaSampleTests
{
    /// <summary>
    /// Empty and binary payloads retain every byte, including all 256 distinct values.
    /// </summary>
    /// <param name="length">The input length, covering empty, singleton and repeated binary bytes.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(256)]
    [DataRow(65537)]
    public void ByteaSampleRoundTripsExactBytes(int length)
    {
        byte[] input = [.. Enumerable.Range(0, length).Select(static index => unchecked((byte)index))];
        byte[] snapshot = [.. input];
        byte[] compressed = ByteaFunctions.Gzip(input);
        Assert.AreSequenceEqual(snapshot, input);
        Assert.AreSequenceEqual<byte>([0x1f, 0x8b], compressed[..2]);
        Assert.AreSequenceEqual(snapshot, ByteaFunctions.Gunzip(compressed));

        using var encoded = new MemoryStream(compressed, writable: false);
        using var decoder = new GZipStream(encoded, CompressionMode.Decompress);
        using var decoded = new MemoryStream();
        decoder.CopyTo(decoded);
        Assert.AreSequenceEqual(snapshot, decoded.ToArray());
    }

    /// <summary>
    /// An independently specified gzip member decodes to the upstream example's exact greeting.
    /// </summary>
    [TestMethod]
    public void ByteaSampleDecodesIndependentGzipMember()
    {
        byte[] compressed = Convert.FromHexString("1F8B0800000000000003CBC85428C9482D4A0500EC76A3E308000000");
        Assert.AreSequenceEqual("hi there"u8.ToArray(), ByteaFunctions.Gunzip(compressed));
        Assert.AreEqual("hi there", ByteaFunctions.GunzipAsText(compressed));
    }

    /// <summary>
    /// UTF-8 text preserves non-ASCII and supplementary characters without culture-dependent conversion.
    /// </summary>
    /// <param name="text">The independently specified text.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("hi there")]
    [DataRow("中文 € 😀")]
    public void ByteaSamplePreservesUtf8Text(string text)
        => Assert.AreEqual(text, ByteaFunctions.GunzipAsText(ByteaFunctions.Gzip(Encoding.UTF8.GetBytes(text))));

    /// <summary>
    /// Strict decoding rejects incomplete members and corrupted checksums instead of accepting a partial payload.
    /// </summary>
    /// <param name="fault">The malformed framing partition.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public void ByteaSampleRejectsInvalidGzip(int fault)
    {
        byte[] complete = ByteaFunctions.Gzip("hi there"u8.ToArray());
        byte[] corrupt = fault switch
        {
            0 => [],
            1 => complete[..10],
            2 => complete[..^8],
            3 => complete[..^1],
            4 => [.. complete[..^8], (byte)(complete[^8] ^ 1), .. complete[^7..]],
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        Assert.ThrowsExactly<InvalidDataException>(() => ByteaFunctions.Gunzip(corrupt));
    }

    /// <summary>
    /// Invalid UTF-8 remains an error while binary decoding retains its exact bytes.
    /// </summary>
    [TestMethod]
    public void ByteaSampleRejectsInvalidUtf8WithoutLosingBytes()
    {
        byte[] invalid = [0xc3, 0x28];
        byte[] compressed = ByteaFunctions.Gzip(invalid);
        Assert.AreSequenceEqual(invalid, ByteaFunctions.Gunzip(compressed));
        Assert.ThrowsExactly<DecoderFallbackException>(() => ByteaFunctions.GunzipAsText(compressed));
    }

    /// <summary>
    /// The first member is the result even when later bytes describe another member or malformed content.
    /// </summary>
    /// <param name="suffix">Selects a complete member, arbitrary bytes or an incomplete following member.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void ByteaSampleStopsAfterFirstGzipMember(int suffix)
    {
        byte[] first = Convert.FromHexString("1F8B08000000000000FF010300FCFF616263C241243503000000");
        byte[] following = suffix switch
        {
            0 => ByteaFunctions.Gzip("different"u8.ToArray()),
            1 => [0x41, 0x42, 0x43],
            2 => [0x1f, 0x8b, 8],
            _ => throw new ArgumentOutOfRangeException(nameof(suffix)),
        };
        Assert.AreSequenceEqual("abc"u8.ToArray(), ByteaFunctions.Gunzip([.. first, .. following]));
    }

    /// <summary>
    /// libflate reads the full trailer without comparing its ISIZE field or rejecting otherwise unused header flags.
    /// </summary>
    /// <param name="size">Whether to change the nonauthoritative ISIZE rather than an unused header bit.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ByteaSamplePreservesUpstreamFramingPolicy(bool size)
    {
        byte[] member = Convert.FromHexString("1F8B08000000000000FF010300FCFF616263C241243503000000");
        if (size)
        {
            member[^4] ^= 1;
        }
        else
        {
            member[3] |= 0x80;
        }

        Assert.AreSequenceEqual("abc"u8.ToArray(), ByteaFunctions.Gunzip(member));
    }

    /// <summary>
    /// Independent stored blocks retain their data after optional extra fields and zero-terminated metadata.
    /// </summary>
    /// <param name="flags">The extra, filename and comment field combination.</param>
    [TestMethod]
    [DataRow(4)]
    [DataRow(8)]
    [DataRow(16)]
    [DataRow(24)]
    [DataRow(28)]
    public void ByteaSampleReadsOptionalGzipHeaderFields(int flags)
    {
        byte[] member = Convert.FromHexString("1F8B08000000000000FF010300FCFF616263C241243503000000");
        using var framed = new MemoryStream();
        member[3] = (byte)flags;
        framed.Write(member.AsSpan(0, 10));
        if ((flags & 4) != 0)
        {
            framed.Write([7, 0, (byte)'X', (byte)'Y', 3, 0, 1, 2, 3]);
        }

        if ((flags & 8) != 0)
        {
            framed.Write("sample.txt\0"u8);
        }

        if ((flags & 16) != 0)
        {
            framed.Write("sample comment\0"u8);
        }

        framed.Write(member.AsSpan(10));
        Assert.AreSequenceEqual("abc"u8.ToArray(), ByteaFunctions.Gunzip(framed.ToArray()));
    }

    /// <summary>
    /// Invalid stored-block structure and every truncated prefix fail instead of exposing partial decompressed bytes.
    /// </summary>
    [TestMethod]
    public void ByteaSampleRejectsMalformedStoredBlocksAndAllTruncatedPrefixes()
    {
        byte[] member = Convert.FromHexString("1F8B08000000000000FF010300FCFF616263C241243503000000");
        for (int length = 0; length < member.Length; length++)
        {
            byte[] truncated = member[..length];
            Assert.ThrowsExactly<InvalidDataException>(() => ByteaFunctions.Gunzip(truncated),
                $"The prefix ending at byte {length} is not a complete member.");
        }

        member[13] ^= 1;
        Assert.ThrowsExactly<InvalidDataException>(() => ByteaFunctions.Gunzip(member));
    }

    /// <summary>
    /// Independent gzip framing exercises dynamic alphabets and multiple stored blocks without reusing the sample's encoder.
    /// </summary>
    /// <param name="stored">Whether to request uncompressed blocks instead of a dynamic compressed alphabet.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ByteaSampleReadsDynamicAndMultipleStoredBlocks(bool stored)
    {
        byte[] plain = stored
            ? [.. Enumerable.Range(0, 131073).Select(static index => unchecked((byte)index))]
            : Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("The first gzip member retains its exact bytes. 中文 € 😀\n", 4096)));
        using var encoded = new MemoryStream();
        using (var encoder = new GZipStream(encoded, stored ? CompressionLevel.NoCompression : CompressionLevel.Optimal, leaveOpen: true))
        {
            encoder.Write(plain);
        }

        byte[] member = encoded.ToArray();
        Assert.AreEqual(stored ? 0 : 2, member[10] >> 1 & 3, "The fixture must exercise its specified DEFLATE block kind.");
        if (stored)
        {
            Assert.AreEqual(0, member[10] & 1, "A large stored payload must include a later final block.");
        }

        Assert.AreSequenceEqual(plain, ByteaFunctions.Gunzip(member));
    }

    /// <summary>
    /// Header verification uses libflate's reconstructed flags and compression level, with an independently computed CRC fixture.
    /// </summary>
    /// <param name="variant">Selects ordinary, text, reserved-flag, unknown-level or corrupt-checksum input.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public void ByteaSampleValidatesUpstreamHeaderChecksum(int variant)
    {
        byte[] member = Convert.FromHexString("1F8B08000000000000FF010300FCFF616263C241243503000000");
        // GNU cksum's CRC-32B for 1F8B08000000000000FF is 0x2FC8D8B9.
        byte[] header = [.. member[..10], 0xb9, 0xd8];
        header[3] = variant switch
        {
            1 => 3,
            2 => 0x82,
            _ => 2,
        };
        if (variant == 3)
        {
            header[8] = 0x7f;
        }
        else if (variant == 4)
        {
            header[^1] ^= 1;
        }

        byte[] framed = [.. header, .. member[10..]];
        if (variant == 4)
        {
            Assert.ThrowsExactly<InvalidDataException>(() => ByteaFunctions.Gunzip(framed));
        }
        else
        {
            Assert.AreSequenceEqual("abc"u8.ToArray(), ByteaFunctions.Gunzip(framed));
        }
    }
}
