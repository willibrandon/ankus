namespace Ankus.Examples.Aggregates;

/// <summary>
/// Selects a discrete integer percentile using PostgreSQL's WITHIN GROUP ordering.
/// </summary>
[PgAggregate(Name = "integer_percentile", Kind = PgAggregateKind.OrderedSet, FinalModify = PgAggregateFinalModify.ReadOnly)]
public static class DiscretePercentile
{
    /// <summary>
    /// Retains nonnull input values in an owned group state.
    /// </summary>
    /// <param name="state">The current group state, or null before its first value.</param>
    /// <param name="value">The next aggregated input, which PostgreSQL does not sort for ordered-set callbacks.</param>
    /// <returns>The current state, or null if no nonnull input has been observed.</returns>
    public static PgAggregateState<List<int>>? Transition(PgAggregateState<List<int>>? state, int? value)
    {
        if (value is { } number)
        {
            state ??= new([]);
            state.Value.Add(number);
        }

        return state;
    }

    /// <summary>
    /// Sorts a copy with the invocation's native ordering and selects the ceiling-ranked value.
    /// </summary>
    /// <param name="context">The current ordering operators, collations, and comparison boundary.</param>
    /// <param name="state">The accumulated nonnull values.</param>
    /// <param name="fraction">The direct argument between zero and one, or null for a null result.</param>
    /// <returns>The requested value, or null for an empty group or null fraction.</returns>
    public static int? Final(PgAggregateContext context, PgAggregateState<List<int>>? state, double? fraction)
    {
        if (fraction is null)
        {
            return null;
        }

        if (double.IsNaN(fraction.Value) || fraction < 0 || fraction > 1)
        {
            throw new PgException("22003", "Percentile fraction must be between zero and one.");
        }

        if (state is null || state.Value.Count == 0)
        {
            return null;
        }

        int[] values = [.. state.Value];
        Array.Sort(values, (left, right) => context.Compare(left, right));
        int position = Math.Max(0, (int)Math.Ceiling(fraction.Value * values.Length) - 1);
        return values[position];
    }
}
