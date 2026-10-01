namespace Ankus.TestExtension;

/// <summary>
/// Exposes final-function sharing policy and distinct mutable ordinary versus readonly moving final paths.
/// </summary>
[PgSchema("aggregate_values", Create = false)]
public static class AggregateFinalFunctions
{
    private static int s_transitions;
    private static readonly List<bool> s_shared = [];

    /// <summary>
    /// Resets callback counts and final-state sharing observations.
    /// </summary>
    [PgFunction(Requires = ["aggregate-support"])]
    public static void FinalReset()
    {
        s_transitions = 0;
        s_shared.Clear();
    }

    /// <summary>
    /// Returns the number of actual transition invocations.
    /// </summary>
    [PgFunction(Requires = ["aggregate-support"])]
    public static int FinalTransitions() => s_transitions;

    /// <summary>
    /// Returns immutable observations of whether PostgreSQL shared a final's state.
    /// </summary>
    [PgFunction(Requires = ["aggregate-support"])]
    public static bool[] FinalShared() => [.. s_shared];

    /// <summary>
    /// Offers a read-only final whose transition can be shared with another native aggregate declaration.
    /// </summary>
    [PgAggregate(Name = "shared_sum", InitialCondition = "0", FinalModify = PgAggregateFinalModify.ReadOnly,
        Requires = ["aggregate-support"])]
    public sealed class SharedSum : IPgAggregate<int, int>, IPgFinalizingAggregate<int, ValueTuple, int>
    {
        /// <summary>
        /// Counts transition invocations independently of output equality.
        /// </summary>
        public static int Transition(PgAggregateContext context, int state, int value)
        {
            s_transitions++;
            return state + value;
        }

        /// <summary>
        /// Reads the state without changing it and retains PostgreSQL's sharing decision.
        /// </summary>
        public static int Final(PgAggregateContext context, int state, ValueTuple arguments)
        {
            s_shared.Add(context.IsStateShared);
            return state;
        }
    }

    /// <summary>
    /// Uses a mutable ordinary final and a separate read-only moving implementation legal for windows.
    /// </summary>
    [PgAggregate(Name = "mutable_final_sum", InitialCondition = "0", MovingInitialCondition = "0",
        FinalModify = PgAggregateFinalModify.ReadWrite, MovingFinalModify = PgAggregateFinalModify.ReadOnly,
        Requires = ["aggregate-support"])]
    public sealed class MutableFinalSum : IPgAggregate<int, int>, IPgFinalizingAggregate<int, ValueTuple, int>,
        IPgMovingAggregate<int, int>, IPgMovingFinalizingAggregate<int, ValueTuple, int>
    {
        /// <summary>
        /// Adds a value to the ordinary state.
        /// </summary>
        public static int Transition(PgAggregateContext context, int state, int value) => state + value;

        /// <summary>
        /// Identifies the ordinary final path in the returned value.
        /// </summary>
        public static int Final(PgAggregateContext context, int state, ValueTuple arguments) => state + 1000;

        /// <summary>
        /// Adds a value to the moving state.
        /// </summary>
        public static int MovingTransition(PgAggregateContext context, int state, int value) => state + value;

        /// <summary>
        /// Subtracts a value from the moving state.
        /// </summary>
        public static int MovingInverse(PgAggregateContext context, int state, int value) => state - value;

        /// <summary>
        /// Returns the unmodified moving state for repeated final evaluations.
        /// </summary>
        public static int MovingFinal(PgAggregateContext context, int state, ValueTuple arguments) => state;
    }

    /// <summary>
    /// Exposes PostgreSQL's min/max index optimization through a qualified native sort operator.
    /// </summary>
    [PgAggregate(Name = "native_min", SortOperator = "pg_catalog.<", Requires = ["aggregate-support"])]
    public sealed class NativeMinimum : IPgAggregate<int, int>
    {
        /// <summary>
        /// Computes minimum after PostgreSQL's strict state seeding and NULL skipping.
        /// </summary>
        public static int Transition(PgAggregateContext context, int state, int value) => Math.Min(state, value);
    }

    /// <summary>
    /// Uses bigint ordinary state and an independent bigint-array moving state with the same text result.
    /// </summary>
    [PgAggregate(Name = "distinct_moving_state", InitialCondition = "0", MovingInitialCondition = "{0,0}", Requires = ["aggregate-support"])]
    public sealed class DistinctMovingState : IPgAggregate<long, int?>, IPgFinalizingAggregate<long, ValueTuple, string>,
        IPgMovingAggregate<long[], int?>, IPgMovingFinalizingAggregate<long[], ValueTuple, string>
    {
        /// <summary>
        /// Adds input to scalar ordinary state.
        /// </summary>
        public static long Transition(PgAggregateContext context, long state, int? value) => state + (value ?? 0);

        /// <summary>
        /// Returns a pass-by-reference result from the scalar ordinary state.
        /// </summary>
        public static string Final(PgAggregateContext context, long state, ValueTuple arguments)
            => "sum:" + state.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// Maintains independent sum and row-count cells in moving state.
        /// </summary>
        public static long[] MovingTransition(PgAggregateContext context, long[] state, int? value) => [state[0] + (value ?? 0), state[1] + 1];

        /// <summary>
        /// Removes both the sum contribution and its frame row count.
        /// </summary>
        public static long[] MovingInverse(PgAggregateContext context, long[] state, int? value) => [state[0] - (value ?? 0), state[1] - 1];

        /// <summary>
        /// Returns independently owned text that must survive later moving-state updates and resets.
        /// </summary>
        public static string MovingFinal(PgAggregateContext context, long[] state, ValueTuple arguments)
            => "sum:" + state[0].ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
