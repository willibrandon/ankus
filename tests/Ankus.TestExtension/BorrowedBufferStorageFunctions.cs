using System.Globalization;

namespace Ankus.TestExtension;

public static partial class BorrowedBufferFunctions
{
    /// <summary>
    /// Compares generated binary borrowing against independent addresses supplied by its native caller.
    /// </summary>
    /// <param name="value">The directly borrowed binary argument.</param>
    /// <param name="address">The original physical datum address.</param>
    /// <param name="payload">The original flat or short payload address, or zero for toasted inputs.</param>
    /// <param name="kind">The physical form independently observed before managed execution.</param>
    /// <returns>Storage identity, allocation owner, length and exact bytes.</returns>
    [PgFunction]
    public static unsafe string[] ByteaStorage(PgByteaView value, long address, long payload, string kind)
    {
        bool shared;
        fixed (byte* bytes = value.DangerousGetSpan())
        {
            shared = unchecked((long)bytes) == payload;
        }

        nuint datum = value.Datum.DangerousGetBits();
        bool copied = datum != unchecked((nuint)address);
        string owner = copied ? Spi.ExecuteScalar<string>("SELECT tests.array_owner($1)",
            [SpiParameter.Create(unchecked((long)datum))]) : "borrowed";
        return [kind, copied.ToString(), shared.ToString(), owner,
            value.Count.ToString(CultureInfo.InvariantCulture), Convert.ToHexString(value.DangerousGetSpan())];
    }

    /// <summary>
    /// Compares generated text borrowing against the original datum and independently observed server bytes.
    /// </summary>
    /// <param name="value">The directly borrowed text argument.</param>
    /// <param name="address">The original physical datum address.</param>
    /// <param name="payload">The original flat or short server payload address, or zero for toasted inputs.</param>
    /// <param name="kind">The physical form independently observed before managed execution.</param>
    /// <returns>Storage identity, allocation owner, UTF-8 length and exact text.</returns>
    [PgFunction]
    public static unsafe string[] TextStorage(PgTextView value, long address, long payload, string kind)
    {
        bool shared;
        fixed (byte* bytes = value.DangerousGetUtf8Span())
        {
            shared = unchecked((long)bytes) == payload;
        }

        nuint datum = value.Datum.DangerousGetBits();
        bool copied = datum != unchecked((nuint)address);
        string owner = copied ? Spi.ExecuteScalar<string>("SELECT tests.array_owner($1)",
            [SpiParameter.Create(unchecked((long)datum))]) : "borrowed";
        return [kind, copied.ToString(), shared.ToString(), owner,
            value.Utf8Length.ToString(CultureInfo.InvariantCulture), value.ToString()];
    }
}
