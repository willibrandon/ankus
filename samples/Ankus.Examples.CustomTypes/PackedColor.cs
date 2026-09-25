using System.Runtime.InteropServices;

namespace Ankus.Examples.CustomTypes;

/// <summary>
/// Stores an RGB color as three native payload bytes with hexadecimal SQL text.
/// </summary>
/// <param name="Red">The red component.</param>
/// <param name="Green">The green component.</param>
/// <param name="Blue">The blue component.</param>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[PgType(NativeLayout = true, TextCodec = typeof(PackedColorTextCodec), BinaryProtocol = true)]
public readonly record struct PackedColor(byte Red, byte Green, byte Blue);

/// <summary>
/// Reuses the color text contract while native layout supplies the stored bytes.
/// </summary>
public sealed class PackedColorTextCodec : PgTypeTextCodec<PackedColor>
{
    private readonly RgbColorTextCodec _text = new();

    /// <inheritdoc />
    public override PackedColor Parse(string text)
    {
        RgbColor value = _text.Parse(text);
        return new(value.Red, value.Green, value.Blue);
    }

    /// <inheritdoc />
    public override string Format(PackedColor value) => _text.Format(new(value.Red, value.Green, value.Blue));
}
