using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Ankus.Examples.Numeric;

/// <summary>
/// Ports pgrx's numeric example using PostgreSQL arithmetic and owned full-range values.
/// </summary>
public static class NumericFunctions
{
    /// <summary>
    /// Adds full-range inputs before rounding the result to precision 1000 and scale 33.
    /// </summary>
    /// <param name="left">The first numeric input, without premature input rounding.</param>
    /// <param name="right">The second numeric input, without premature input rounding.</param>
    /// <returns>The sum constrained by PostgreSQL's numeric type modifier.</returns>
    [PgFunction]
    public static PgNumeric AddNumeric(PgNumeric left, PgNumeric right)
        => (left + right).Rescale(precision: 1000, scale: 33);

    /// <summary>
    /// Samples all 128 signed integer bits and converts them exactly into an owned numeric value.
    /// </summary>
    /// <returns>A numeric integer within the complete signed 128-bit range.</returns>
    [PgFunction]
    public static PgNumeric RandomNumeric()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        Int128 value = BinaryPrimitives.ReadInt128LittleEndian(bytes);
        return PgNumeric.FromInteger(value);
    }

    /// <summary>
    /// Parses numeric input through PostgreSQL, retaining its syntax, range and display scale.
    /// </summary>
    /// <param name="text">The PostgreSQL numeric input text.</param>
    /// <returns>The detached numeric value.</returns>
    [PgFunction]
    public static PgNumeric NumericFromString(string text) => PgNumeric.Parse(text);

    /// <summary>
    /// Preserves pgrx's ordered integer, single-precision and double-precision arithmetic chain.
    /// </summary>
    /// <returns>The final PostgreSQL numeric value with precision 10 and scale 3.</returns>
    [PgFunction]
    public static PgNumeric Math()
    {
        PgNumeric number = 5;
        number *= 5;
        number -= PgNumeric.FromDouble(2.234);
        number /= PgNumeric.FromInteger<Int128>(4);
        number %= 19;
        number += PgNumeric.FromDouble(99.42);

        number *= 42;
        number /= 42;
        number += 42;
        number -= 42;
        number *= PgNumeric.FromSingle(42.0f);
        number /= PgNumeric.FromSingle(42.0f);
        number += PgNumeric.FromSingle(42.0f);
        number -= PgNumeric.FromSingle(42.0f);
        number *= PgNumeric.FromDouble(42.0);
        number /= PgNumeric.FromDouble(42.0);
        number += PgNumeric.FromDouble(42.0);
        number -= PgNumeric.FromDouble(42.0);

        number /= PgNumeric.FromInteger(42);
        number /= PgNumeric.FromInteger(42).Rescale(precision: 1000, scale: 33);
        return number.Rescale(precision: 10, scale: 3);
    }

    /// <summary>
    /// Returns forty-two with the same precision and display scale as pgrx's typed numeric example.
    /// </summary>
    /// <returns>Forty-two constrained to precision 1000 and scale 33.</returns>
    [PgFunction(Name = "forty_twooooooo")]
    public static PgNumeric FortyTwo() => PgNumeric.FromInteger(42).Rescale(precision: 1000, scale: 33);
}
