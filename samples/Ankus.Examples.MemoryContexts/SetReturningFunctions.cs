namespace Ankus.Examples.MemoryContexts;

/// <summary>
/// Ports pgrx's set-returning memory-lifetime examples to ordinary C# sequences.
/// </summary>
/// <remarks>
/// Ankus implements PostgreSQL's multi-call protocol and keeps its native iterator state in the invocation's
/// multi-call context. Managed iterator state stays on the managed heap across row requests. Add a
/// <see cref="PgMemoryContext"/> parameter when native state must explicitly live in that multi-call context.
/// </remarks>
public static class SetReturningFunctions
{
    /// <summary>
    /// Streams each value from the inclusive start to the exclusive end, one row per request.
    /// </summary>
    /// <param name="start">The first value.</param>
    /// <param name="end">The exclusive upper bound; an end at or before the start produces no rows.</param>
    /// <returns>The values in ascending order.</returns>
    [PgFunction]
    public static IEnumerable<long> IterCount(long start, long end)
    {
        for (long value = start; value < end; value++)
        {
            yield return value;
        }
    }

    /// <summary>
    /// Computes every row before returning, then lets PostgreSQL request them from the completed list.
    /// </summary>
    /// <param name="n">The number of rows; zero or a negative value produces no rows.</param>
    /// <returns>Each zero-based index and its square.</returns>
    [PgFunction]
    public static IEnumerable<(int Idx, long Square)> MaterializedPairs(int n)
    {
        List<(int Idx, long Square)> rows = new(Math.Max(n, 0));
        for (int index = 0; index < n; index++)
        {
            rows.Add((index, (long)index * index));
        }

        return rows;
    }
}
