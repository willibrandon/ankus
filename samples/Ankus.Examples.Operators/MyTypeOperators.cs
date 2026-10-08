namespace Ankus.Examples.Operators;

/// <summary>
/// Declares an operator manually from an ordinary static method.
/// </summary>
public static class MyTypeOperators
{
    /// <summary>
    /// Compares two values with the record's member equality.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>Whether both values contain the same integer.</returns>
    [PgOperator("=")]
    public static bool MyEq(MyType left, MyType right) => left == right;
}
