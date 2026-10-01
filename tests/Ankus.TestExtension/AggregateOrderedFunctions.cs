using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises PostgreSQL ordering metadata, direct arguments, extra arguments, and contextual comparisons.
/// </summary>
[PgSchema("aggregate_values", Create = false)]
public static class AggregateOrderedFunctions
{
    private static string[] s_keys = [];
    private static string s_context = "";
    private static PgAggregateContext? s_retained;

    /// <summary>
    /// Returns detached sort-key metadata after the aggregate has completed.
    /// </summary>
    [PgFunction(Requires = ["aggregate-support"])]
    public static string[] AggregateKeys() => s_keys;

    /// <summary>
    /// Returns aggregate identity, collation and comparison recovery observations.
    /// </summary>
    [PgFunction(Requires = ["aggregate-support"])]
    public static string AggregateContext() => s_context;

    /// <summary>
    /// Orders every value, including NULL, through PostgreSQL's supplied ordering operator and collation.
    /// </summary>
    [PgAggregate(Name = "ordered_text", Kind = PgAggregateKind.OrderedSet, Requires = ["aggregate-support"])]
    public sealed class OrderedText : IPgAggregate<PgAggregateState<List<string?>>?, string?>,
        IPgFinalizingAggregate<PgAggregateState<List<string?>>?, ValueTuple, string?[]>
    {
        /// <summary>
        /// Retains owned input strings across per-row memory resets.
        /// </summary>
        public static PgAggregateState<List<string?>> Transition(PgAggregateContext context, PgAggregateState<List<string?>>? state, string? value)
        {
            state ??= new PgAggregateState<List<string?>>([]);
            state.Value.Add(value);
            return state;
        }

        /// <summary>
        /// Sorts a copy so PostgreSQL NULL placement and text collation remain authoritative.
        /// </summary>
        public static string?[] Final(PgAggregateContext context, PgAggregateState<List<string?>>? state, ValueTuple arguments)
        {
            Capture(context);
            string?[] values = state is null ? [] : [.. state.Value];
            Array.Sort(values, (left, right) => context.Compare(left, right));
            return values;
        }
    }

    /// <summary>
    /// Implements SQL rank for a hypothetical row using two native ordering keys.
    /// </summary>
    [PgAggregate(Name = "hypothetical_rank", Kind = PgAggregateKind.HypotheticalSet, Requires = ["aggregate-support"])]
    public sealed class HypotheticalRank : IPgAggregate<PgAggregateState<List<(string? Text, int? Number)>>?, (string? text, int? number)>,
        IPgFinalizingAggregate<PgAggregateState<List<(string? Text, int? Number)>>?, (string? targetText, int? targetNumber), long>
    {
        /// <summary>
        /// Includes rows containing NULL values in hypothetical ranking.
        /// </summary>
        public static PgAggregateState<List<(string? Text, int? Number)>> Transition(
            PgAggregateContext context, PgAggregateState<List<(string? Text, int? Number)>>? state, (string? text, int? number) arguments)
        {
            state ??= new PgAggregateState<List<(string? Text, int? Number)>>([]);
            state.Value.Add(arguments);
            return state;
        }

        /// <summary>
        /// Counts rows preceding the direct arguments in PostgreSQL's lexicographic order.
        /// </summary>
        public static long Final(PgAggregateContext context, PgAggregateState<List<(string? Text, int? Number)>>? state,
            (string? targetText, int? targetNumber) arguments)
        {
            Capture(context);
            long rank = 1;
            if (state is not null)
            {
                foreach ((string? candidate, int? value) in state.Value)
                {
                    int comparison = context.Compare(candidate, arguments.targetText, 0);
                    if (comparison < 0 || (comparison == 0 && context.Compare(value, arguments.targetNumber, 1) < 0))
                    {
                        rank++;
                    }
                }
            }

            return rank;
        }
    }

    /// <summary>
    /// Keeps final dummy SQL arguments outside managed callbacks in normal and moving execution.
    /// </summary>
    [PgAggregate(Name = "extra_sum", InitialCondition = "0", MovingInitialCondition = "0",
        FinalExtra = true, MovingFinalExtra = true, Requires = ["aggregate-support"])]
    public sealed class ExtraSum : IPgAggregate<int, int?>, IPgFinalizingAggregate<int, ValueTuple, int>,
        IPgMovingAggregate<int, int?>, IPgMovingFinalizingAggregate<int, ValueTuple, int>
    {
        /// <summary>
        /// Adds ordinary input values.
        /// </summary>
        public static int Transition(PgAggregateContext context, int state, int? value) => state + (value ?? 0);

        /// <summary>
        /// Returns ordinary state independently of the final dummy SQL argument.
        /// </summary>
        public static int Final(PgAggregateContext context, int state, ValueTuple arguments) => state;

        /// <summary>
        /// Adds a row to a moving frame.
        /// </summary>
        public static int MovingTransition(PgAggregateContext context, int state, int? value) => state + (value ?? 0);

        /// <summary>
        /// Removes a row from a moving frame.
        /// </summary>
        public static int MovingInverse(PgAggregateContext context, int state, int? value) => state - (value ?? 0);

        /// <summary>
        /// Returns moving state independently of the final dummy SQL argument.
        /// </summary>
        public static int MovingFinal(PgAggregateContext context, int state, ValueTuple arguments) => state;
    }

    /// <summary>
    /// Exercises native comparison errors and reentrant callback restoration without losing the active aggregate.
    /// </summary>
    [PgAggregate(Name = "comparison_probe", Kind = PgAggregateKind.OrderedSet, Requires = ["aggregate-support"])]
    public sealed class ComparisonProbe : IPgAggregate<int?, int?>, IPgFinalizingAggregate<int?, ValueTuple, string>
    {
        /// <summary>
        /// Retains the smallest input as ordinary SQL state.
        /// </summary>
        public static int? Transition(PgAggregateContext context, int? state, int? value) => state is null ? value : Math.Min(state.Value, value ?? state.Value);

        /// <summary>
        /// Recovers from a native type mismatch and verifies exact parent context reactivation after nested SPI aggregation.
        /// </summary>
        public static string Final(PgAggregateContext context, int? state, ValueTuple arguments)
        {
            string mismatch = "none";
            try
            {
                context.Compare("wrong", "type");
            }
            catch (PgException error)
            {
                mismatch = error.SqlState;
            }

            string worker = Task.Run(() =>
            {
                try
                {
                    context.Compare(1, 2);
                    return "unprotected";
                }
                catch (InvalidOperationException)
                {
                    return "protected";
                }
            }).GetAwaiter().GetResult();
            s_retained = context;
            int nested = Spi.ExecuteScalar<int>("SELECT aggregate_values.nested_comparison() WITHIN GROUP(ORDER BY v) FROM (VALUES(3),(7)) AS input(v)");
            string nestedError = "none";
            try
            {
                Spi.Execute("SELECT aggregate_values.nested_comparison() WITHIN GROUP(ORDER BY v) FROM (VALUES(-999)) AS input(v)");
            }
            catch (PgException error)
            {
                nestedError = error.SqlState;
            }

            return string.Create(CultureInfo.InvariantCulture, $"{state}:{mismatch}:{worker}:{nested}:{nestedError}:{context.Compare(1, 2)}:{Spi.ExecuteScalar<int>("SELECT 42")}");
        }
    }

    /// <summary>
    /// Attempts to use an outer context while its nested child is active.
    /// </summary>
    [PgAggregate(Name = "nested_comparison", Kind = PgAggregateKind.OrderedSet, Requires = ["aggregate-support"])]
    public sealed class NestedComparison : IPgAggregate<int?, int?>, IPgFinalizingAggregate<int?, ValueTuple, int>
    {
        /// <summary>
        /// Accumulates a native scalar state.
        /// </summary>
        public static int? Transition(PgAggregateContext context, int? state, int? value) => (state ?? 0) + (value ?? 0);

        /// <summary>
        /// Rejects parent comparison while allowing comparison in this nested aggregate.
        /// </summary>
        public static int Final(PgAggregateContext context, int? state, ValueTuple arguments)
        {
            if (state == -999)
            {
                throw new PgException("P7820", "nested aggregate failed");
            }

            try
            {
                s_retained!.Compare(1, 2);
                return -999;
            }
            catch (InvalidOperationException)
            {
                return (state ?? 0) + context.Compare(2, 1);
            }
        }
    }

    /// <summary>
    /// Captures immutable aggregate metadata independently of state ownership.
    /// </summary>
    private static void Capture(PgAggregateContext context)
    {
        s_keys = [.. context.SortKeys.Select(static key => string.Create(CultureInfo.InvariantCulture,
            $"{key.ArgumentIndex}:{key.TypeOid}:{key.OperatorOid}:{key.CollationOid}:{key.NullsFirst}"))];
        s_context = string.Create(CultureInfo.InvariantCulture, $"{context.Kind}:{context.AggregateOid}:{context.CollationOid}:{context.IsStateShared}");
    }
}
