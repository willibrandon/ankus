namespace Ankus.Examples.Operators;

/// <summary>
/// Reads and changes packed native values through checked borrowed storage.
/// </summary>
public static class PgVarlenaThingFunctions
{
    /// <summary>
    /// Replaces the signed field; the first write copies the borrowed input instead of changing the caller's value.
    /// </summary>
    /// <param name="thing">The borrowed input storage.</param>
    /// <param name="c">The replacement signed field.</param>
    /// <returns>The changed private copy.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static PgVarlena<PgVarlenaThing> PgVarlenaThingWithC(PgVarlena<PgVarlenaThing> thing, int c)
    {
        PgVarlenaThing value = thing.Value;
        value.C = c;
        thing.Value = value;
        return thing;
    }
}
