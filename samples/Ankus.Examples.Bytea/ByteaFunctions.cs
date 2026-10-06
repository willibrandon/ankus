using System.Text;

namespace Ankus.Examples.Bytea;

/// <summary>
/// Ports pgrx's bytea example with gzip streams and exact UTF-8 decoding.
/// </summary>
public static class ByteaFunctions
{
    /// <summary>
    /// Rejects invalid UTF-8 instead of replacing original decompressed bytes.
    /// </summary>
    private static readonly UTF8Encoding s_utf8 = new(false, true);

    /// <summary>
    /// Compresses an owned bytea input into one complete gzip member.
    /// </summary>
    /// <param name="input">The exact bytes, including embedded zero values.</param>
    /// <returns>The compressed bytes with their gzip header and checksum.</returns>
    [PgFunction]
    public static byte[] Gzip(byte[] input) => GzipMemberCodec.Encode(input);

    /// <summary>
    /// Decodes the first gzip member with its complete trailer and exact checksum.
    /// </summary>
    /// <param name="bytes">The complete compressed member and any subsequent content.</param>
    /// <returns>The exact decompressed payload.</returns>
    /// <exception cref="InvalidDataException">The gzip input is incomplete or corrupt.</exception>
    [PgFunction]
    public static byte[] Gunzip(byte[] bytes) => GzipMemberCodec.Decode(bytes);

    /// <summary>
    /// Decodes gzip bytes into text without replacing invalid UTF-8 sequences.
    /// </summary>
    /// <param name="bytes">The complete compressed UTF-8 text.</param>
    /// <returns>The exact decoded text.</returns>
    /// <exception cref="InvalidDataException">The gzip input is incomplete or corrupt.</exception>
    /// <exception cref="DecoderFallbackException">The payload is not valid UTF-8.</exception>
    [PgFunction]
    public static string GunzipAsText(byte[] bytes) => s_utf8.GetString(Gunzip(bytes));
}
