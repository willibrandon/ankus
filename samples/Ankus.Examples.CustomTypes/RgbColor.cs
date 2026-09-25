using System.Globalization;

namespace Ankus.Examples.CustomTypes;

/// <summary>
/// Stores an RGB color as generated CBOR with hexadecimal SQL text.
/// </summary>
/// <param name="Red">The red component.</param>
/// <param name="Green">The green component.</param>
/// <param name="Blue">The blue component.</param>
[PgType(TextCodec = typeof(RgbColorTextCodec), BinaryProtocol = true,
    NullInputErrorMessage = "Color input must not be NULL.")]
public readonly record struct RgbColor(byte Red, byte Green, byte Blue);

/// <summary>
/// Reads and writes hexadecimal colors while retaining generated storage.
/// </summary>
public sealed class RgbColorTextCodec : PgTypeTextCodec<RgbColor>
{
    /// <inheritdoc />
    public override RgbColor Parse(string text)
    {
        if (text.Length != 7 || text[0] != '#' ||
            !byte.TryParse(text.AsSpan(1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte red) ||
            !byte.TryParse(text.AsSpan(3, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte green) ||
            !byte.TryParse(text.AsSpan(5, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte blue))
        {
            throw new PgException("22P02", "Color must use #RRGGBB hexadecimal notation.");
        }

        return new(red, green, blue);
    }

    /// <inheritdoc />
    public override string Format(RgbColor value) =>
        string.Create(CultureInfo.InvariantCulture, $"#{value.Red:X2}{value.Green:X2}{value.Blue:X2}");
}
