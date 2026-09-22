namespace Ankus.TestExtension;

/// <summary>
/// Exercises PostgreSQL ordering support functions and caught errors from inside native comparison.
/// </summary>
[PgSchema("aggregate_values", Create = false)]
public static class AggregateOperatorFunctions
{
    /// <summary>
    /// Sorts ordinary integer inputs according to the invocation's B-tree ordering operator.
    /// </summary>
    [PgAggregate(Name = "operator_order", Kind = PgAggregateKind.OrderedSet,
        FinalModify = PgAggregateFinalModify.ReadOnly, Requires = ["aggregate-support"])]
    public static class OperatorOrder
    {
        /// <summary>
        /// Retains each nonnull integer in input order.
        /// </summary>
        public static PgAggregateState<List<int>> Transition(PgAggregateState<List<int>>? state, int? value)
        {
            state ??= new([]);
            if (value.HasValue)
            {
                state.Value.Add(value.Value);
            }

            return state;
        }

        /// <summary>
        /// Uses native comparison directly so a guarded operator error can be caught and the context reused.
        /// </summary>
        public static string Final(PgAggregateContext context, PgAggregateState<List<int>>? state, bool recover)
        {
            int[] values = state is null ? [] : [.. state.Value];
            try
            {
                for (int index = 1; index < values.Length; index++)
                {
                    int value = values[index];
                    int position = index;
                    while (position > 0 && context.Compare(value, values[position - 1]) < 0)
                    {
                        values[position] = values[position - 1];
                        position--;
                    }

                    values[position] = value;
                }
            }
            catch (PgException error) when (recover)
            {
                int comparison = context.Compare(1, -2);
                int answer = Spi.ExecuteScalar<int>("SELECT 42");
                return $"{error.SqlState}:{error.Message}:{error.Detail}:{comparison}:{answer}";
            }

            return string.Join(',', values);
        }
    }
}
