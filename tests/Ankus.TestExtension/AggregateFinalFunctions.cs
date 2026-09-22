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
    public static class SharedSum
    {
        /// <summary>
        /// Counts transition invocations independently of output equality.
        /// </summary>
        public static int Transition(int state, int value)
        {
            s_transitions++;
            return state + value;
        }

        /// <summary>
        /// Reads the state without changing it and retains PostgreSQL's sharing decision.
        /// </summary>
        public static int Final(PgAggregateContext context, int state)
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
    public static class MutableFinalSum
    {
        /// <summary>
        /// Adds a value to the ordinary state.
        /// </summary>
        public static int Transition(int state, int value) => state + value;

        /// <summary>
        /// Identifies the ordinary final path in the returned value.
        /// </summary>
        public static int Final(int state) => state + 1000;

        /// <summary>
        /// Adds a value to the moving state.
        /// </summary>
        public static int MovingTransition(int state, int value) => state + value;

        /// <summary>
        /// Subtracts a value from the moving state.
        /// </summary>
        public static int MovingInverse(int state, int value) => state - value;

        /// <summary>
        /// Returns the unmodified moving state for repeated final evaluations.
        /// </summary>
        public static int MovingFinal(int state) => state;
    }

    /// <summary>
    /// Exposes PostgreSQL's min/max index optimization through a qualified native sort operator.
    /// </summary>
    [PgAggregate(Name = "native_min", SortOperator = "pg_catalog.<", Requires = ["aggregate-support"])]
    public static class NativeMinimum
    {
        /// <summary>
        /// Computes minimum after PostgreSQL's strict state seeding and NULL skipping.
        /// </summary>
        public static int Transition(int state, int value) => Math.Min(state, value);
    }
    /// <summary>
    /// Uses bigint ordinary state and an independent bigint-array moving state with the same text result.
    /// </summary>
    [PgAggregate(Name = "distinct_moving_state", InitialCondition = "0", MovingInitialCondition = "{0,0}", Requires = ["aggregate-support"])]
    public static class DistinctMovingState
    {
        /// <summary>
        /// Adds input to scalar ordinary state.
        /// </summary>
        public static long Transition(long state, int? value) => state + (value ?? 0);

        /// <summary>
        /// Returns a pass-by-reference result from the scalar ordinary state.
        /// </summary>
        public static string Final(long state) => "sum:" + state.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// Maintains independent sum and row-count cells in moving state.
        /// </summary>
        public static long[] MovingTransition(long[] state, int? value) => [state[0] + (value ?? 0), state[1] + 1];

        /// <summary>
        /// Removes both the sum contribution and its frame row count.
        /// </summary>
        public static long[] MovingInverse(long[] state, int? value) => [state[0] - (value ?? 0), state[1] - 1];

        /// <summary>
        /// Returns independently owned text that must survive later moving-state updates and resets.
        /// </summary>
        public static string MovingFinal(long[] state) => "sum:" + state[0].ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

}
