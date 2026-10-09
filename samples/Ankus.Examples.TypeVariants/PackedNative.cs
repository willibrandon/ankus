using System.Globalization;
using System.Runtime.InteropServices;

namespace Ankus.Examples.TypeVariants;

/// <summary>
/// Variant 3: an RGB color stored as exactly three native bytes, with <c>#rrggbb</c> SQL text.
/// </summary>
/// <remarks>
/// pgrx uses <c>#[pgvarlena_inoutfuncs]</c> and <c>PgVarlena&lt;Rgb&gt;</c>. Ankus uses
/// <see cref="PgTypeAttribute.NativeLayout"/> with a text codec, and <see cref="PgVarlena{T}"/> borrows the stored
/// bytes without deserializing them. Changing the field order or types requires a data migration.
/// </remarks>
/// <param name="R">The red component.</param>
/// <param name="G">The green component.</param>
/// <param name="B">The blue component.</param>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[PgType(NativeLayout = true, TextCodec = typeof(RgbTextCodec))]
public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>
/// Parses and writes <c>#rrggbb</c> with lowercase hexadecimal output.
/// </summary>
public sealed class RgbTextCodec : PgTypeTextCodec<Rgb>
{
    /// <inheritdoc />
    public override Rgb Parse(string text)
    {
        if (!text.StartsWith('#'))
        {
            throw new PgException(PgSqlStates.InvalidTextRepresentation, "expected leading '#'");
        }

        ReadOnlySpan<char> hex = text.AsSpan(1);
        if (hex.Length != 6)
        {
            throw new PgException(PgSqlStates.InvalidTextRepresentation, "expected 6 hex digits");
        }

        return new(Component(hex[..2], "bad red"), Component(hex[2..4], "bad green"), Component(hex[4..], "bad blue"));
    }

    /// <inheritdoc />
    public override string Format(Rgb value) => string.Create(CultureInfo.InvariantCulture, $"#{value.R:x2}{value.G:x2}{value.B:x2}");

    private static byte Component(ReadOnlySpan<char> digits, string error)
        => byte.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte value)
            ? value
            : throw new PgException(PgSqlStates.InvalidTextRepresentation, error);
}

/// <summary>
/// Functions that read <see cref="Rgb"/> storage in place.
/// </summary>
public static class PackedNativeFunctions
{
    /// <summary>
    /// Computes Rec. 601 luma directly from the stored bytes.
    /// </summary>
    /// <param name="c">The borrowed color.</param>
    /// <returns>The weighted sum of the components, from 0 to 255.</returns>
    [PgFunction]
    public static double RgbLuminance(PgVarlena<Rgb> c)
    {
        Rgb color = c.Value;
        return 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
    }
}
