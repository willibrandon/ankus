namespace Ankus.Examples.Operators;

/// <summary>
/// Declares enum equality and an explicit cast while preserving a relocatable installation schema.
/// </summary>
public static class PriorityFunctions
{
    /// <summary>
    /// Compares two priorities for equality.
    /// </summary>
    [PgOperator("===", Commutator = "===", Negator = "!==", Id = "priority-equality")]
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static bool Equal(Priority left, Priority right) => left == right;

    /// <summary>
    /// Compares two priorities for inequality.
    /// </summary>
    [PgOperator("!==", Commutator = "!==", Negator = "===")]
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static bool NotEqual(Priority left, Priority right) => left != right;

    /// <summary>
    /// Converts a priority to its score when SQL explicitly requests an integer cast.
    /// </summary>
    [PgCast(Id = "priority-score")]
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static int Score(Priority value) => (int)value;
}
