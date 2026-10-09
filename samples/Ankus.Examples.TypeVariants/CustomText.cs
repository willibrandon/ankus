using System.Globalization;
using System.Text.Json.Serialization;

namespace Ankus.Examples.TypeVariants;

/// <summary>
/// Variant 2: a complex number with generated CBOR storage and a custom SQL text form such as <c>3+4i</c>.
/// </summary>
/// <remarks>
/// pgrx adds <c>#[inoutfuncs]</c> and implements <c>InOutFuncs</c>; Ankus sets <see cref="PgTypeAttribute.TextCodec"/>.
/// Both keep the serialized storage contract and replace only the text conversion.
/// </remarks>
/// <param name="Re">The real part.</param>
/// <param name="Im">The imaginary part.</param>
[PgType(TextCodec = typeof(ComplexTextCodec))]
public sealed record Complex([property: JsonPropertyName("re")] double Re, [property: JsonPropertyName("im")] double Im);

/// <summary>
/// Parses <c>&lt;re&gt;+&lt;im&gt;i</c> or <c>&lt;re&gt;-&lt;im&gt;i</c> and writes the same form.
/// </summary>
public sealed class ComplexTextCodec : PgTypeTextCodec<Complex>
{
    private const NumberStyles Number = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    /// <inheritdoc />
    public override Complex Parse(string text)
    {
        string trimmed = text.Trim();
        int imaginary = trimmed.LastIndexOf('i');
        if (imaginary < 0)
        {
            throw new PgException(PgSqlStates.InvalidTextRepresentation, "expected trailing 'i'");
        }

        // As in pgrx, the last sign after the first character separates the parts.
        string body = trimmed[..imaginary];
        int split = body.LastIndexOfAny(['+', '-']);
        if (split <= 0)
        {
            throw new PgException(PgSqlStates.InvalidTextRepresentation, "expected sign between real and imaginary parts");
        }

        if (!double.TryParse(body.AsSpan(0, split), Number, CultureInfo.InvariantCulture, out double re))
        {
            throw new PgException(PgSqlStates.InvalidTextRepresentation, "invalid real part");
        }

        if (!double.TryParse(body.AsSpan(split), Number, CultureInfo.InvariantCulture, out double im))
        {
            throw new PgException(PgSqlStates.InvalidTextRepresentation, "invalid imaginary part");
        }

        return new(re, im);
    }

    /// <inheritdoc />
    public override string Format(Complex value)
        => value.Im >= 0.0
            ? string.Create(CultureInfo.InvariantCulture, $"{value.Re}+{value.Im}i")
            : string.Create(CultureInfo.InvariantCulture, $"{value.Re}{value.Im}i");
}

/// <summary>
/// Functions over the custom-text <see cref="Complex"/> type.
/// </summary>
public static class CustomTextFunctions
{
    /// <summary>
    /// Adds two complex numbers.
    /// </summary>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <returns>The component-wise sum.</returns>
    [PgFunction]
    public static Complex ComplexAdd(Complex a, Complex b) => new(a.Re + b.Re, a.Im + b.Im);
}
