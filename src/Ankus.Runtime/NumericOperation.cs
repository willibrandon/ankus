namespace Ankus;

/// <summary>
/// Selects a PostgreSQL numeric routine within the native guard.
/// </summary>
internal enum NumericOperation
{
    /// <summary>
    /// Parses text with PostgreSQL's numeric input routine.
    /// </summary>
    Parse,

    /// <summary>
    /// Adds two numeric values using PostgreSQL arithmetic.
    /// </summary>
    Add,

    /// <summary>
    /// Subtracts the second numeric operand from the first.
    /// </summary>
    Subtract,

    /// <summary>
    /// Multiplies two numeric values.
    /// </summary>
    Multiply,

    /// <summary>
    /// Divides numeric values with PostgreSQL's result-scale rules.
    /// </summary>
    Divide,

    /// <summary>
    /// Computes the remainder of numeric division.
    /// </summary>
    Remainder,

    /// <summary>
    /// Applies numeric unary negation.
    /// </summary>
    Negate,

    /// <summary>
    /// Computes the numeric absolute value.
    /// </summary>
    Abs,

    /// <summary>
    /// Rounds to the requested scale, with ties away from zero.
    /// </summary>
    Round,

    /// <summary>
    /// Truncates toward zero at the requested scale.
    /// </summary>
    Truncate,

    /// <summary>
    /// Rounds upward to the nearest integral numeric value.
    /// </summary>
    Ceiling,

    /// <summary>
    /// Rounds downward to the nearest integral numeric value.
    /// </summary>
    Floor,

    /// <summary>
    /// Computes a numeric square root.
    /// </summary>
    Sqrt,

    /// <summary>
    /// Raises e to a numeric power.
    /// </summary>
    Exp,

    /// <summary>
    /// Computes the natural logarithm.
    /// </summary>
    Log,

    /// <summary>
    /// Computes a logarithm using the supplied numeric base.
    /// </summary>
    LogBase,

    /// <summary>
    /// Raises a numeric base to a numeric exponent.
    /// </summary>
    Power,

    /// <summary>
    /// Computes the greatest common divisor of two numeric values.
    /// </summary>
    Gcd,

    /// <summary>
    /// Computes the least common multiple of two numeric values.
    /// </summary>
    Lcm,

    /// <summary>
    /// Applies PostgreSQL's double-precision-to-numeric cast.
    /// </summary>
    FromDouble,

    /// <summary>
    /// Applies PostgreSQL's numeric-to-double-precision cast, including rounding and range checks.
    /// </summary>
    ToDouble,

    /// <summary>
    /// Applies numeric precision and scale with native type-modifier rounding and overflow checks.
    /// </summary>
    Rescale,

    /// <summary>
    /// Applies PostgreSQL's real-to-numeric cast.
    /// </summary>
    FromSingle,

    /// <summary>
    /// Applies PostgreSQL's numeric-to-real cast, including rounding and range checks.
    /// </summary>
    ToSingle,

    /// <summary>
    /// Casts numeric to smallint using PostgreSQL's rounding and overflow rules.
    /// </summary>
    ToInt16,

    /// <summary>
    /// Casts numeric to integer using PostgreSQL's rounding and overflow rules.
    /// </summary>
    ToInt32,

    /// <summary>
    /// Casts numeric to bigint using PostgreSQL's rounding and overflow rules.
    /// </summary>
    ToInt64,
}
