namespace Ankus.Examples.Hello;

/// <summary>
/// Demonstrates exposing an ordinary C# method as a PostgreSQL function.
/// </summary>
public static class Hello
{
    /// <summary>
    /// Adds two integers, reporting overflow as a PostgreSQL error.
    /// </summary>
    /// <param name="left">The first operand.</param>
    /// <param name="right">The second operand.</param>
    /// <returns>The sum of the operands.</returns>
    [PgFunction]
    public static int Add(int left, int right) => checked(left + right);
}
