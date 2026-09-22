namespace Ankus.Examples.Sets;

/// <summary>
/// Demonstrates PostgreSQL sets and tables without a custom iterator wrapper.
/// </summary>
public static class SetFunctions
{
    /// <summary>
    /// Produces a checked integer sequence one requested row at a time.
    /// </summary>
    /// <param name="start">The first value.</param>
    /// <param name="count">The nonnegative number of values to produce.</param>
    /// <returns>The integer sequence.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static IEnumerable<int> Series(int start, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        for (int offset = 0; offset < count; offset++)
        {
            yield return checked(start + offset);
        }
    }

    /// <summary>
    /// Splits text into a table of one-based word numbers and words.
    /// </summary>
    /// <param name="text">The whitespace-delimited input.</param>
    /// <returns>Named rows in input order.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static IEnumerable<(int WordNumber, string Word)> Words(string text)
    {
        string[] words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < words.Length; index++)
        {
            yield return (index + 1, words[index]);
        }
    }

    /// <summary>
    /// Materializes the word table in PostgreSQL's spillable tuple store before returning it.
    /// </summary>
    /// <param name="text">The whitespace-delimited input.</param>
    /// <returns>Named rows in input order.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe, SetMode = PgSetMode.Materialize)]
    public static IEnumerable<(int WordNumber, string Word)> MaterializedWords(string text) => Words(text);
}
