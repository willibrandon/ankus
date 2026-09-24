namespace Ankus.TestExtension;

/// <summary>
/// Exercises resolved aggregate state, managed ownership, moving frames, and ordered comparisons.
/// </summary>
[PgSchema("aggregate_values", Create = false)]
public static class PolymorphicAggregateFunctions
{
    private static int s_transitions;
    private static int s_combines;
    private static int s_inverses;
    private static int s_created;
    private static int s_disposed;
    private static PgMemoryContext? s_owner;
    private static PgAggregateContext? s_nestedContext;
    private static Holder? s_nestedHolder;

    /// <summary>
    /// Resets observable callback and payload lifetime counts.
    /// </summary>
    [PgFunction(Requires = ["aggregate-support"])]
    public static void PolyAggregateReset()
    {
        (s_transitions, s_combines, s_inverses, s_created, s_disposed) = (0, 0, 0, 0, 0);
        s_owner = null;
    }

    /// <summary>
    /// Reports transition, combine, inverse, creation, and disposal counts.
    /// </summary>
    [PgFunction(Requires = ["aggregate-support"])]
    public static int[] PolyAggregateCounts() => [s_transitions, s_combines, s_inverses, s_created, s_disposed];

    /// <summary>
    /// Reports whether the last captured aggregate owner remains live after query completion.
    /// </summary>
    [PgFunction(Requires = ["aggregate-support"])]
    public static bool PolyAggregateOwnerAlive() => s_owner?.IsAlive == true;

    /// <summary>
    /// Retains a nested scalar argument under the enclosing aggregate's owner.
    /// </summary>
    [PgFunction(Requires = ["aggregate-support"])]
    public static bool PolyAggregateCapture(PgAnyElement value)
    {
        s_nestedHolder!.Value = value.CopyTo(s_nestedContext!.MemoryContext);
        return true;
    }

    /// <summary>
    /// Reenters the extension through a scalar function before retaining aggregate state.
    /// </summary>
    [PgAggregate(Name = "nested_poly", FinalExtra = true, Requires = ["aggregate-support"])]
    public static class Nested
    {
        /// <summary>
        /// Restores nested capture bindings even when a PostgreSQL call fails.
        /// </summary>
        public static PgAggregateState<Holder> Transition(PgAggregateContext context, PgAggregateState<Holder>? state, PgAnyElement? value)
        {
            state ??= new(new Holder());
            (PgAggregateContext? Context, Holder? Holder) previous = (s_nestedContext, s_nestedHolder);
            (s_nestedContext, s_nestedHolder) = (context, state.Value);
            try
            {
                if (value is not null)
                {
                    _ = PgFunctions.Call<bool>("aggregate_values.poly_aggregate_capture", PgFunctionArgument.Create(value));
                }

                return state;
            }
            finally
            {
                (s_nestedContext, s_nestedHolder) = previous;
            }
        }

        /// <summary>
        /// Reads storage retained after the nested scalar and transition callbacks have ended.
        /// </summary>
        public static PgAnyElement? Final(PgAggregateState<Holder>? state, PgAnyElement? witness) => Owned.Final(state, witness);
    }

    /// <summary>
    /// Matches pgrx's strict first-anyelement aggregate and supports native parallel state transport.
    /// </summary>
    [PgAggregate(Name = "first_poly", ParallelSafety = PgParallelSafety.Safe, Requires = ["aggregate-support"])]
    public static class First
    {
        /// <summary>
        /// Retains the state seeded by PostgreSQL without interpreting its type.
        /// </summary>
        public static PgAnyElement Transition(PgAnyElement state, PgAnyElement value)
        {
            s_transitions++;
            return state;
        }

        /// <summary>
        /// Retains one non-null partial state and records actual leader-side combination.
        /// </summary>
        public static PgAnyElement Combine(PgAnyElement state, PgAnyElement other)
        {
            s_combines++;
            return state;
        }
    }

    /// <summary>
    /// Matches pgrx's strict first-anyarray aggregate.
    /// </summary>
    [PgAggregate(Name = "first_poly_array", Requires = ["aggregate-support"])]
    public static class FirstArray
    {
        /// <summary>
        /// Keeps shape, bounds, and element identity from the first present array.
        /// </summary>
        public static PgAnyArray Transition(PgAnyArray state, PgAnyArray value) => state;
    }

    /// <summary>
    /// Resolves an empty textual initial state and calls a polymorphic built-in from each transition.
    /// </summary>
    [PgAggregate(Name = "concat_poly", InitialCondition = "{}", Requires = ["aggregate-support"])]
    public static class Concat
    {
        /// <summary>
        /// Concatenates arrays using PostgreSQL's resolved element type and bounds.
        /// </summary>
        public static PgAnyArray Transition(PgAnyArray state, PgAnyArray value)
            => PgFunctions.Call<PgAnyArray>("pg_catalog.array_cat", PgFunctionArgument.Create(state), PgFunctionArgument.Create(value));
    }

    /// <summary>
    /// Retains the last present raw input inside managed state with an explicit aggregate owner.
    /// </summary>
    [PgAggregate(Name = "owned_poly", FinalExtra = true, Requires = ["aggregate-support"])]
    public static class Owned
    {
        /// <summary>
        /// Copies borrowed inputs before the callback's temporary storage is reclaimed.
        /// </summary>
        public static PgAggregateState<Holder> Transition(PgAggregateContext context, PgAggregateState<Holder>? state, PgAnyElement? value)
        {
            s_owner = context.MemoryContext;
            state ??= new(new Holder());
            if (value is not null)
            {
                state.Value.Value = value.CopyTo(context.MemoryContext);
            }

            return state;
        }

        /// <summary>
        /// Returns retained state while proving extra input slots contain typed NULLs.
        /// </summary>
        public static PgAnyElement? Final(PgAggregateState<Holder>? state, PgAnyElement? witness)
            => witness is null ? state?.Value.Value : throw new InvalidOperationException("FinalExtra supplied a present value.");
    }

    /// <summary>
    /// Maintains nullable raw values across advancing and restarting moving-window owners.
    /// </summary>
    [PgAggregate(Name = "moving_poly", FinalExtra = true, MovingFinalExtra = true, Requires = ["aggregate-support"])]
    public static class Moving
    {
        /// <summary>
        /// Builds ordinary state with the same ownership contract as moving execution.
        /// </summary>
        public static PgAggregateState<Queue<PgAnyElement?>> Transition(PgAggregateContext context,
            PgAggregateState<Queue<PgAnyElement?>>? state, PgAnyElement? value)
        {
            state ??= new(new Queue<PgAnyElement?>());
            state.Value.Enqueue(value?.CopyTo(context.MemoryContext));
            return state;
        }

        /// <summary>
        /// Reads the earliest frame value without changing state.
        /// </summary>
        public static PgAnyElement? Final(PgAggregateState<Queue<PgAnyElement?>>? state, PgAnyElement? witness)
            => state is not null && state.Value.Count > 0 ? state.Value.Peek() : null;

        /// <summary>
        /// Adds the entering value under the moving state owner.
        /// </summary>
        public static PgAggregateState<Queue<PgAnyElement?>> MovingTransition(PgAggregateContext context,
            PgAggregateState<Queue<PgAnyElement?>>? state, PgAnyElement? value) => Transition(context, state, value);

        /// <summary>
        /// Removes the departing value and records that PostgreSQL used the inverse path.
        /// </summary>
        public static PgAggregateState<Queue<PgAnyElement?>> MovingInverse(
            PgAggregateState<Queue<PgAnyElement?>>? state, PgAnyElement? value)
        {
            s_inverses++;
            _ = state!.Value.Dequeue();
            return state;
        }

        /// <summary>
        /// Reads the current moving frame using its resolved result type.
        /// </summary>
        public static PgAnyElement? MovingFinal(PgAggregateState<Queue<PgAnyElement?>>? state, PgAnyElement? witness) => Final(state, witness);
    }

    /// <summary>
    /// Selects the first value according to PostgreSQL's ORDER BY operator and collation.
    /// </summary>
    [PgAggregate(Name = "ordered_poly", Kind = PgAggregateKind.OrderedSet, FinalExtra = true, Requires = ["aggregate-support"])]
    public static class Ordered
    {
        /// <summary>
        /// Compares real PostgreSQL types and copies the selected value into aggregate-owned storage.
        /// </summary>
        public static PgAggregateState<Holder> Transition(PgAggregateContext context, PgAggregateState<Holder>? state, PgAnyElement? value)
        {
            state ??= new(new Holder());
            if (value is not null && (state.Value.Value is null || context.Compare(value, state.Value.Value) < 0))
            {
                state.Value.Value = value.CopyTo(context.MemoryContext);
            }

            return state;
        }

        /// <summary>
        /// Returns the selected value with its original SQL identity.
        /// </summary>
        public static PgAnyElement? Final(PgAggregateState<Holder>? state, PgAnyElement? witness) => Owned.Final(state, witness);
    }

    /// <summary>
    /// Produces an intentionally incompatible final result.
    /// </summary>
    [PgAggregate(Name = "wrong_poly", Requires = ["aggregate-support"])]
    public static class Wrong
    {
        /// <summary>
        /// Retains a nullable input state.
        /// </summary>
        public static PgAnyElement? Transition(PgAnyElement? state, PgAnyElement? value) => value ?? state;

        /// <summary>
        /// Returns text regardless of the resolved aggregate result type.
        /// </summary>
        public static PgAnyElement Final(PgAnyElement? state) => Spi.ExecuteScalar<PgAnyElement>("SELECT 'wrong'::text");
    }

    /// <summary>
    /// Exposes resolved domain checks for SQL NULL aggregate results.
    /// </summary>
    [PgAggregate(Name = "null_poly", Requires = ["aggregate-support"])]
    public static class Null
    {
        /// <summary>
        /// Retains a nullable input state.
        /// </summary>
        public static PgAnyElement? Transition(PgAnyElement? state, PgAnyElement? value) => value ?? state;

        /// <summary>
        /// Returns SQL NULL under the resolved result's domain constraints.
        /// </summary>
        public static PgAnyElement? Final(PgAnyElement? state) => null;
    }

    /// <summary>
    /// Deliberately retains a callback-owned input to verify stale native state is rejected safely.
    /// </summary>
    [PgAggregate(Name = "borrowed_poly", FinalExtra = true, Requires = ["aggregate-support"])]
    public static class Borrowed
    {
        /// <summary>
        /// Omits the required aggregate-owner copy for the first present input.
        /// </summary>
        public static PgAggregateState<PgAnyElement>? Transition(PgAggregateState<PgAnyElement>? state, PgAnyElement? value)
            => state ?? (value is null ? null : new(value));

        /// <summary>
        /// Attempts to return the expired input after its original callback has ended.
        /// </summary>
        public static PgAnyElement? Final(PgAggregateState<PgAnyElement>? state, PgAnyElement? witness) => state?.Value;
    }

    /// <summary>
    /// Counts managed payload release without accessing native values during PostgreSQL cleanup.
    /// </summary>
    public sealed class Holder : IDisposable
    {
        /// <summary>
        /// Records creation of one state payload.
        /// </summary>
        public Holder() => s_created++;

        /// <summary>
        /// Gets or sets an aggregate-owned raw value.
        /// </summary>
        public PgAnyElement? Value { get; set; }

        /// <summary>
        /// Records each release.
        /// </summary>
        public void Dispose() => s_disposed++;
    }
}
