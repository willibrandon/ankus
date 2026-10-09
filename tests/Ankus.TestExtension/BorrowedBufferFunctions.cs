using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises native bytea and text borrowing through generated and explicit datum paths.
/// </summary>
[PgSchema("borrowed_buffers")]
public static partial class BorrowedBufferFunctions
{
    /// <summary>
    /// Returns a nullable native binary input before its callback lease expires.
    /// </summary>
    /// <param name="value">The binary value or SQL NULL.</param>
    /// <returns>The unchanged native value.</returns>
    [PgFunction]
    public static PgByteaView? ByteaIdentity(PgByteaView? value) => value;

    /// <summary>
    /// Reports a borrowed bytea's length and emptiness, as pgrx's <c>is_empty</c> test does.
    /// </summary>
    /// <param name="value">The borrowed bytes.</param>
    /// <returns>The byte count and whether it is zero.</returns>
    [PgFunction]
    public static string ByteaLength(PgByteaView value) => $"{value.Count}|{value.Count == 0}";

    /// <summary>
    /// Returns original server-encoded text before its callback lease expires.
    /// </summary>
    /// <param name="value">The text or SQL NULL.</param>
    /// <returns>The unchanged native value.</returns>
    [PgFunction]
    public static PgTextView? TextIdentity(PgTextView? value) => value;

    /// <summary>
    /// Observes exact text bytes independently of its character length and original server representation.
    /// </summary>
    /// <param name="value">The borrowed text argument.</param>
    /// <returns>Type identity, UTF-8 length, decoded text and exact UTF-8 bytes.</returns>
    [PgFunction]
    public static string[] TextSnapshot(PgTextView value)
        => [value.TypeOid.ToString(CultureInfo.InvariantCulture), value.Utf8Length.ToString(CultureInfo.InvariantCulture),
            value.ToString(), Convert.ToHexString(value.DangerousGetUtf8Span())];

    /// <summary>
    /// Reads an existing domain, varchar or bpchar datum without changing its declared SQL identity.
    /// </summary>
    /// <param name="value">The arbitrary native value to validate as text.</param>
    /// <returns>The exact text representation and original type metadata.</returns>
    [PgFunction]
    public static string[] RawTextSnapshot(PgAnyElement value)
    {
        using PgTextView text = value.Datum.Read<PgTextView>();
        return TextSnapshot(text);
    }

    /// <summary>
    /// Reads bytea domains through the raw API while retaining every payload byte.
    /// </summary>
    /// <param name="value">The arbitrary native value to validate as bytea.</param>
    /// <returns>Original type, length and exact hexadecimal bytes.</returns>
    [PgFunction]
    public static string[] RawByteaSnapshot(PgAnyElement value)
    {
        using PgByteaView binary = value.Datum.Read<PgByteaView>();
        return [binary.TypeOid.ToString(CultureInfo.InvariantCulture), binary.Count.ToString(CultureInfo.InvariantCulture),
            Convert.ToHexString(binary.DangerousGetSpan())];
    }

    /// <summary>
    /// Retains the generated binary snapshot across successive lazy set callbacks.
    /// </summary>
    /// <param name="value">The binary snapshot or SQL NULL.</param>
    /// <returns>The same exact binary value twice.</returns>
    [PgFunction]
    public static IEnumerable<PgByteaView?> ByteaRows(PgByteaView? value)
    {
        using (value)
        {
            yield return value;
            _ = Spi.ExecuteScalar<int>("SELECT 42");
            yield return value;
        }
    }

    /// <summary>
    /// Splits borrowed text lazily, rereading its native storage after every set callback, as pgrx's
    /// <c>split_set_with_borrow</c> does.
    /// </summary>
    /// <param name="value">The text, which may be stored compressed or out of line.</param>
    /// <param name="separator">A single ASCII separator.</param>
    /// <returns>Each token in order.</returns>
    [PgFunction]
    public static IEnumerable<string> SplitBorrowedText(PgTextView value, string separator)
    {
        using (value)
        {
            byte delimiter = AsciiSeparator(separator);
            int start = 0;
            while (start <= value.Utf8Length)
            {
                ReadOnlySpan<byte> rest = value.DangerousGetUtf8Span()[start..];
                int length = rest.IndexOf(delimiter);
                length = length < 0 ? rest.Length : length;
                string token = System.Text.Encoding.UTF8.GetString(rest[..length]);
                start += length + 1;
                yield return token;
            }
        }
    }

    /// <summary>
    /// Splits borrowed text into numbered rows, as pgrx's <c>split_table_with_borrow</c> does.
    /// </summary>
    /// <param name="value">The text, which may be stored compressed or out of line.</param>
    /// <param name="separator">A single ASCII separator.</param>
    /// <returns>Each one-based position and token.</returns>
    [PgFunction]
    public static IEnumerable<(int Position, string Token)> SplitBorrowedTable(PgTextView value, string separator)
    {
        int position = 0;
        foreach (string token in SplitBorrowedText(value, separator))
        {
            yield return (++position, token);
        }
    }

    /// <summary>
    /// Retains the generated text snapshot across successive lazy set callbacks.
    /// </summary>
    /// <param name="value">The text snapshot or SQL NULL.</param>
    /// <returns>The same exact text value twice.</returns>
    [PgFunction]
    public static IEnumerable<PgTextView?> TextRows(PgTextView? value)
    {
        using (value)
        {
            yield return value;
            _ = Spi.ExecuteScalar<int>("SELECT 42");
            yield return value;
        }
    }

    /// <summary>
    /// Exercises typed SPI results, raw rows, catalog calls and native function addresses with independent result owners.
    /// </summary>
    /// <returns>Exact returned values and typed NULL observations.</returns>
    [PgFunction]
    public static string[] BufferSpi()
    {
        using PgTextView text = Spi.ExecuteScalar<PgTextView>("SELECT 'café'::text");
        using PgByteaView binary = Spi.ExecuteScalar<PgByteaView>("SELECT '\\x0000ff'::bytea");
        using PgTextView concatenated = PgFunctions.Call<PgTextView>("pg_catalog.textcat",
            PgFunctionArgument.Create(text), PgFunctionArgument.Create("!"));
        using PgByteaView appended = PgFunctions.Call<PgByteaView>("pg_catalog.byteacat",
            PgFunctionArgument.Create(binary), PgFunctionArgument.Create<byte[]>([0x7f]));
        using PgTextView echoed = Spi.ExecuteScalar<PgTextView>("SELECT $1", [SpiParameter.Create(concatenated)]);
        using SpiRawResult raw = Spi.QueryRaw("SELECT 'raw'::text, '\\x007f'::bytea, NULL::text, NULL::bytea");
        using PgTextView rawText = raw[0][0].Read<PgTextView>();
        using PgByteaView rawBytes = raw[0][1].Read<PgByteaView>();
        nint address = unchecked((nint)Spi.ExecuteScalar<long>("SELECT tests.function_address('pg_catalog.textcat(text,text)'::regprocedure)"));
        PgTextView result;
        unsafe
        {
            result = PgFunctions.DangerousCall<PgTextView>(address, 0, [text.Datum, echoed.Datum]);
        }

        using PgTextView direct = result;
        return [echoed.ToString(), Convert.ToHexString(appended.DangerousGetSpan()), rawText.ToString(),
            Convert.ToHexString(rawBytes.DangerousGetSpan()), direct.ToString(),
            (raw[0][2].Read<PgTextView?>() is null).ToString(),
            (raw[0][3].Read<PgByteaView?>() is null).ToString(),
            (Spi.ExecuteScalar<PgTextView?>("SELECT NULL::text") is null).ToString(),
            (Spi.ExecuteScalar<PgByteaView?>("SELECT NULL::bytea") is null).ToString(),
            SpiParameter.Create<PgTextView?>(null).TypeOid.ToString(CultureInfo.InvariantCulture),
            SpiParameter.Create<PgByteaView?>(null).TypeOid.ToString(CultureInfo.InvariantCulture)];
    }

    private static byte AsciiSeparator(string separator)
        => separator is [char only] && char.IsAscii(only)
            ? (byte)only
            : throw new ArgumentException("The separator must be one ASCII character.", nameof(separator));
}
