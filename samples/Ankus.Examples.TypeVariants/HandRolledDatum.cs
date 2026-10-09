using System.Globalization;
using Ankus;
using Ankus.Examples.TypeVariants;

// pgrx's extension_sql! blocks: a bootstrap shell type, then the completed type after its input and output functions.
[assembly: PgSql("u24_shell", "CREATE TYPE u24;", Order = PgSqlOrder.Bootstrap)]
[assembly: PgSql("u24_concrete", """
    CREATE TYPE u24 (
        INPUT = u24_in,
        OUTPUT = u24_out,
        LIKE = int4
    );
    """, Requires = ["u24_shell", "u24_in", "u24_out"])]
[assembly: PgSqlTypeProvider("u24_concrete", typeof(U24))]

namespace Ankus.Examples.TypeVariants;

/// <summary>
/// Variant 4: a 24-bit unsigned integer stored in the low bits of an <c>int4</c>-like datum, defined by hand.
/// </summary>
/// <remarks>
/// pgrx implements <c>FromDatum</c>, <c>IntoDatum</c> and the SQL translation traits and writes the type with
/// <c>extension_sql!</c>. Ankus pairs a <see cref="PgDatumTypeAttribute"/> converter with <see cref="PgSqlAttribute"/>
/// blocks, and <see cref="PgSqlTypeProviderAttribute"/> orders functions that use the type after its completion.
/// </remarks>
/// <param name="Value">The unsigned value.</param>
[PgDatumType("u24", typeof(U24Converter))]
public readonly record struct U24(uint Value);

/// <summary>
/// Reads and writes the by-value datum word directly.
/// </summary>
public sealed class U24Converter : IPgDatumReader<U24>, IPgDatumWriter<U24>
{
    /// <inheritdoc />
    public U24 Read(PgDatum value) => new((uint)value.DangerousGetBits() & 0x00FF_FFFF);

    /// <inheritdoc />
    public PgDatum Write(U24 value, uint typeOid, PgMemoryContext destination)
    {
        unsafe
        {
            return PgDatum.DangerousCreate(value.Value, typeOid, destination);
        }
    }
}

/// <summary>
/// The hand-written input and output functions for <see cref="U24"/>.
/// </summary>
public static class HandRolledFunctions
{
    /// <summary>
    /// Parses an unsigned decimal integer of at most 24 bits.
    /// </summary>
    /// <param name="input">The native input string.</param>
    /// <returns>The parsed value.</returns>
    [PgFunction(Name = "u24_in", Id = "u24_in", Requires = ["u24_shell"], Volatility = PgVolatility.Immutable,
        ParallelSafety = PgParallelSafety.Safe)]
    public static U24 Input(PgCStringView input)
    {
        uint value = ParseUInt32(input.ToUtf8String());
        return value > 0x00FF_FFFF ? throw new PgException(PgSqlStates.DataException, "value exceeds 24 bits") : new(value);
    }

    /// <summary>
    /// Writes the value as an unsigned decimal integer.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The native output string.</returns>
    [PgFunction(Name = "u24_out", Id = "u24_out", Requires = ["u24_shell"], Volatility = PgVolatility.Immutable,
        ParallelSafety = PgParallelSafety.Safe)]
    public static PgCString Output(U24 value) => PgCString.FromUtf8(value.Value.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Parses like Rust's <c>u32::from_str</c>, with its error messages, which pgrx reports as SQLSTATE 22000.
    /// </summary>
    private static uint ParseUInt32(string text)
    {
        if (text.Length == 0)
        {
            throw new PgException(PgSqlStates.DataException, "cannot parse integer from empty string");
        }

        ReadOnlySpan<char> digits = text.StartsWith('+') ? text.AsSpan(1) : text;
        if (digits.IsEmpty || digits.ContainsAnyExceptInRange('0', '9'))
        {
            throw new PgException(PgSqlStates.DataException, "invalid digit found in string");
        }

        return uint.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out uint value)
            ? value
            : throw new PgException(PgSqlStates.DataException, "number too large to fit in target type");
    }
}
