namespace Ankus.TestExtension;

/// <summary>
/// Exercises exact C-string bytes through owned, borrowed and native array boundaries.
/// </summary>
[PgSchema("cstrings")]
public static partial class CStringFunctions
{
    /// <summary>
    /// Builds a native C string from exact payload bytes.
    /// </summary>
    /// <param name="bytes">The nonzero payload or SQL NULL.</param>
    /// <returns>The independent C string.</returns>
    [PgFunction(Name = "create_cstring")]
    public static PgCString? CreateCString(byte[]? bytes) => bytes is null ? null : new PgCString(bytes);

    /// <summary>
    /// Returns owned input bytes through a generated C-string result.
    /// </summary>
    /// <param name="value">The C string or SQL NULL.</param>
    /// <returns>The unchanged bytes and NULL state.</returns>
    [PgFunction]
    public static PgCString? OwnedEcho(PgCString? value) => value;

    /// <summary>
    /// Returns borrowed input storage before its callback expires.
    /// </summary>
    /// <param name="value">The C string or SQL NULL.</param>
    /// <returns>The unchanged bytes and NULL state.</returns>
    [PgFunction]
    public static PgCStringView? BorrowedEcho(PgCStringView? value) => value;

    /// <summary>
    /// Exposes owned bytes independently of PostgreSQL's C-string output encoding.
    /// </summary>
    /// <param name="value">The C string or SQL NULL.</param>
    /// <returns>The exact binary payload.</returns>
    [PgFunction]
    public static byte[]? OwnedBytes(PgCString? value) => value?.ToArray();

    /// <summary>
    /// Exposes borrowed bytes independently of PostgreSQL's C-string output encoding.
    /// </summary>
    /// <param name="value">The C string or SQL NULL.</param>
    /// <returns>The exact binary payload.</returns>
    [PgFunction]
    public static byte[]? BorrowedBytes(PgCStringView? value) => value?.ToArray();

    /// <summary>
    /// Observes the complete native representation including one final zero byte.
    /// </summary>
    /// <param name="value">The present C string.</param>
    /// <returns>All bytes including the terminator.</returns>
    [PgFunction]
    public static byte[] TerminatedBytes(PgCStringView value) => value.DangerousGetNullTerminatedSpan().ToArray();

    /// <summary>
    /// Keeps an owned snapshot across successive set callbacks.
    /// </summary>
    /// <param name="value">The input snapshot or SQL NULL.</param>
    /// <returns>Two identical independent binary rows.</returns>
    [PgFunction]
    public static IEnumerable<byte[]?> OwnedRows(PgCString? value)
    {
        yield return value?.ToArray();
        _ = Spi.ExecuteScalar<int>("SELECT 42");
        yield return value?.ToArray();
    }

    /// <summary>
    /// Keeps a native snapshot across successive set callbacks and releases it on iterator disposal.
    /// </summary>
    /// <param name="value">The input snapshot or SQL NULL.</param>
    /// <returns>Two identical independent binary rows.</returns>
    [PgFunction]
    public static IEnumerable<byte[]?> BorrowedRows(PgCStringView? value)
    {
        using (value)
        {
            yield return value?.ToArray();
            _ = Spi.ExecuteScalar<int>("SELECT 42");
            yield return value?.ToArray();
        }
    }

    /// <summary>
    /// Creates shaped C-string cells with distinct NULL, empty and non-UTF-8 values.
    /// </summary>
    /// <returns>A two-dimensional array with nondefault lower bounds.</returns>
    [PgFunction]
    public static PgArray<PgCString?> CreateArray() => new([null, new([]), new([128, 255]), new([65])], [2, 2], [-3, 7]);

    /// <summary>
    /// Round-trips independent array cells and their original shape.
    /// </summary>
    /// <param name="value">The owned C-string array.</param>
    /// <returns>The same cell values and shape.</returns>
    [PgFunction]
    public static PgArray<PgCString?> ArrayIdentity(PgArray<PgCString?> value) => value;

    /// <summary>
    /// Observes array cells as detached hexadecimal text, preserving SQL NULL cells.
    /// </summary>
    /// <param name="value">The owned native array conversion.</param>
    /// <returns>Each exact payload in physical order.</returns>
    [PgFunction]
    public static string?[] ArraySnapshot(PgArray<PgCString?> value)
        => [.. value.Select(static item => item is null ? null : Convert.ToHexString(item.AsSpan()))];

    /// <summary>
    /// Observes lazy borrowed C-string cells and releases each checked view.
    /// </summary>
    /// <param name="value">The lazy native array.</param>
    /// <returns>Each exact payload in physical order.</returns>
    [PgFunction]
    public static string?[] BorrowedArraySnapshot(PgArrayView<PgCStringView?> value)
    {
        string?[] result = new string?[value.Count];
        for (int index = 0; index < result.Length; index++)
        {
            using PgCStringView? cell = value[index];
            result[index] = cell is null ? null : Convert.ToHexString(cell.DangerousGetSpan());
        }

        return result;
    }

    /// <summary>
    /// Exercises ordinary and raw SPI, exact typed parameters and PostgreSQL function results.
    /// </summary>
    /// <returns>Independent hexadecimal observations and NULL checks.</returns>
    [PgFunction]
    public static string[] NativeBoundaries()
    {
        PgCString owned = Spi.ExecuteScalar<PgCString>("SELECT cstrings.create_cstring('\\x0180ff'::bytea)");
        using PgCStringView borrowed = Spi.ExecuteScalar<PgCStringView>("SELECT cstrings.create_cstring('\\xff01'::bytea)");
        PgCString rebound = Spi.ExecuteScalar<PgCString>("SELECT $1", [SpiParameter.Create(owned)]);
        using PgCStringView reborrowed = Spi.ExecuteScalar<PgCStringView>("SELECT $1", [SpiParameter.Create(borrowed)]);
        using SpiRawResult raw = Spi.QueryRaw("SELECT cstrings.create_cstring('\\x80'::bytea), NULL::cstring");
        PgCString rawOwned = raw[0][0].Read<PgCString>();
        using PgCStringView rawBorrowed = raw[0][0].Read<PgCStringView>();
        PgCString formatted = PgFunctions.Call<PgCString>("pg_catalog.int4out", PgFunctionArgument.Create(42));
        using PgCStringView formattedView = PgFunctions.Call<PgCStringView>("pg_catalog.int4out", PgFunctionArgument.Create(-7));
        string[] result = [Convert.ToHexString(rebound.AsSpan()), Convert.ToHexString(reborrowed.DangerousGetSpan()),
            Convert.ToHexString(rawOwned.AsSpan()), Convert.ToHexString(rawBorrowed.DangerousGetSpan()),
            formatted.ToUtf8String(), formattedView.ToUtf8String(),
            (raw[0][1].Read<PgCString>() is null).ToString(), (raw[0][1].Read<PgCStringView>() is null).ToString(),
            (Spi.ExecuteScalar<PgCString?>("SELECT NULL::cstring") is null).ToString(),
            (Spi.ExecuteScalar<PgCStringView?>("SELECT NULL::cstring") is null).ToString()];
        return result;
    }
}
