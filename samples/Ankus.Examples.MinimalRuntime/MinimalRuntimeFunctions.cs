using System.Text.Json.Serialization;

namespace Ankus.Examples.MinimalRuntime;

/// <summary>
/// A record-shaped value whose equality operator is declared by hand, like pgrx's <c>MyType</c> and <c>my_eq</c>.
/// </summary>
/// <param name="Value">The integer value.</param>
[PgType]
public sealed record MyType([property: JsonPropertyName("value")] int Value);

/// <summary>
/// Ports the functions and operator from pgrx's <c>nostd</c> example.
/// </summary>
public static class MinimalRuntimeFunctions
{
    /// <summary>
    /// Compares two <see cref="MyType"/> values through a SQL <c>=</c> operator.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>Whether the values are equal.</returns>
    [PgOperator("=")]
    public static bool MyEq(MyType left, MyType right) => left == right;

    /// <summary>
    /// Returns the example's greeting.
    /// </summary>
    /// <returns>The greeting.</returns>
    [PgFunction]
    public static string HelloNostd() => "Hello, nostd";

    /// <summary>
    /// Returns its input.
    /// </summary>
    /// <param name="input">The text to return.</param>
    /// <returns>The same text.</returns>
    [PgFunction]
    public static string Echo(string input) => input;
}
