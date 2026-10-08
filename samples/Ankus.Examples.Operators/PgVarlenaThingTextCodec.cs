using System.Globalization;

namespace Ankus.Examples.Operators;

/// <summary>
/// Reads and writes <see cref="PgVarlenaThing"/> as <c>a;b;c;[d0, d1, d2, d3, d4]</c> while native layout supplies storage.
/// </summary>
/// <remarks>
/// Integers accept an optional leading plus sign, and the signed field also accepts a minus sign, like Rust's
/// integer parsing. Spaces may follow the commas between bytes, so formatted values can be read back.
/// </remarks>
public sealed class PgVarlenaThingTextCodec : PgTypeTextCodec<PgVarlenaThing>
{
    /// <inheritdoc />
    public override PgVarlenaThing Parse(string text)
    {
        string[] fields = text.Split(';');
        if (fields.Length != 4 ||
            !TryParseUnsigned(fields[0], out ulong a) ||
            !TryParseUnsigned(fields[1], out ulong b) ||
            !TryParseSigned(fields[2], out int c) ||
            !fields[3].StartsWith('[') || !fields[3].EndsWith(']'))
        {
            throw Invalid(text);
        }

        string[] items = fields[3][1..^1].Split(',');
        if (items.Length != PgVarlenaThing.DLength)
        {
            throw Invalid(text);
        }

        Span<byte> d = stackalloc byte[PgVarlenaThing.DLength];
        for (int index = 0; index < items.Length; index++)
        {
            string item = index == 0 ? items[index] : items[index].TrimStart(' ');
            if (!TryParseByte(item, out d[index]))
            {
                throw Invalid(text);
            }
        }

        return new(a, b, c, d);
    }

    /// <inheritdoc />
    public override unsafe string Format(PgVarlenaThing value) => string.Create(CultureInfo.InvariantCulture,
        $"{value.A};{value.B};{value.C};[{value.D[0]}, {value.D[1]}, {value.D[2]}, {value.D[3]}, {value.D[4]}]");

    /// <summary>
    /// Parses an unsigned field, rejecting a minus sign that .NET accepts for zero.
    /// </summary>
    private static bool TryParseUnsigned(string text, out ulong value)
    {
        value = 0;
        return !text.StartsWith('-') && ulong.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Parses the signed field without surrounding whitespace, separators or exponents.
    /// </summary>
    private static bool TryParseSigned(string text, out int value)
        => int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// Parses one byte, rejecting a minus sign that .NET accepts for zero.
    /// </summary>
    private static bool TryParseByte(string text, out byte value)
    {
        value = 0;
        return !text.StartsWith('-') && byte.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Creates PostgreSQL's invalid text representation error for this type.
    /// </summary>
    private static PgException Invalid(string text)
        => new("22P02", $"invalid input syntax for type pgvarlenathing: \"{text}\"");
}
