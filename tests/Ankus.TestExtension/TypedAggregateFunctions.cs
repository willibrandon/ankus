using OwnedState = Ankus.PgAggregateState<Ankus.TestExtension.AggregateFunctions.TrackedState>;
using ParallelState = Ankus.PgAggregateState<Ankus.TestExtension.AggregateParallelFunctions.ParallelState>;
using PolymorphicState = Ankus.PgAggregateState<System.Collections.Generic.Queue<Ankus.PgAnyElement?>>;
using RankState = Ankus.PgAggregateState<System.Collections.Generic.List<(string? Text, int? Number)>>;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises typed interface dispatch through actual PostgreSQL aggregate entry points.
/// </summary>
[PgSchema("aggregate_values", Create = false)]
public static class TypedAggregateFunctions
{
    /// <summary>
    /// Compares typed direct arguments with aggregated tuple inputs through PostgreSQL ordering.
    /// </summary>
    [PgAggregate(Name = "typed_rank", Kind = PgAggregateKind.HypotheticalSet, Requires = ["aggregate-support"])]
    public sealed class Rank : IPgAggregate<RankState?, (string? Text, int? Number)>,
        IPgFinalizingAggregate<RankState?, (string? TargetText, int? TargetNumber), long>
    {
        /// <summary>
        /// Retains the exact tuple, including rows with NULL fields.
        /// </summary>
        public static RankState Transition(PgAggregateContext context, RankState? state, (string? Text, int? Number) arguments)
            => AggregateOrderedFunctions.HypotheticalRank.Transition(state, arguments.Text, arguments.Number);

        /// <summary>
        /// Uses the two direct arguments in PostgreSQL's selected ordering.
        /// </summary>
        public static long Final(PgAggregateContext context, RankState? state, (string? TargetText, int? TargetNumber) arguments)
            => AggregateOrderedFunctions.HypotheticalRank.Final(context, state, arguments.TargetText, arguments.TargetNumber);
    }

    /// <summary>
    /// Preserves ordered-set collation, NULL placement and ordering direction with an empty direct group.
    /// </summary>
    [PgAggregate(Name = "typed_ordered", Kind = PgAggregateKind.OrderedSet, Requires = ["aggregate-support"])]
    public sealed class Ordered : IPgAggregate<PgAggregateState<List<string?>>?, string?>,
        IPgFinalizingAggregate<PgAggregateState<List<string?>>?, ValueTuple, string?[]>
    {
        /// <summary>
        /// Retains text before its SQL input memory expires.
        /// </summary>
        public static PgAggregateState<List<string?>> Transition(PgAggregateContext context, PgAggregateState<List<string?>>? state, string? arguments)
            => AggregateOrderedFunctions.OrderedText.Transition(state, arguments);

        /// <summary>
        /// Sorts retained values with PostgreSQL's ordering metadata.
        /// </summary>
        public static string?[] Final(PgAggregateContext context, PgAggregateState<List<string?>>? state, ValueTuple arguments)
            => AggregateOrderedFunctions.OrderedText.Final(context, state);
    }

    /// <summary>
    /// Resolves polymorphic final results through generated extra slots in ordinary and moving aggregation.
    /// </summary>
    [PgAggregate(Name = "typed_polymorphic", FinalExtra = true, MovingFinalExtra = true, Requires = ["aggregate-support"])]
    public sealed class Polymorphic : IPgAggregate<PolymorphicState?, PgAnyElement?>,
        IPgFinalizingAggregate<PolymorphicState?, ValueTuple, PgAnyElement?>,
        IPgMovingAggregate<PolymorphicState?, PgAnyElement?>,
        IPgMovingFinalizingAggregate<PolymorphicState?, ValueTuple, PgAnyElement?>
    {
        /// <summary>
        /// Copies each raw value under the aggregate owner before retaining it.
        /// </summary>
        public static PolymorphicState Transition(PgAggregateContext context, PolymorphicState? state, PgAnyElement? arguments)
            => PolymorphicAggregateFunctions.Moving.Transition(context, state, arguments);

        /// <summary>
        /// Returns the first retained value with its original SQL identity.
        /// </summary>
        public static PgAnyElement? Final(PgAggregateContext context, PolymorphicState? state, ValueTuple arguments)
            => PolymorphicAggregateFunctions.Moving.Final(state, null);

        /// <summary>
        /// Copies the newly entering window value into its current state owner.
        /// </summary>
        public static PolymorphicState MovingTransition(PgAggregateContext context, PolymorphicState? state, PgAnyElement? arguments)
            => PolymorphicAggregateFunctions.Moving.MovingTransition(context, state, arguments);

        /// <summary>
        /// Verifies and removes the departing value through a distinct moving capability.
        /// </summary>
        public static PolymorphicState MovingInverse(PgAggregateContext context, PolymorphicState? state, PgAnyElement? arguments)
            => PolymorphicAggregateFunctions.Moving.MovingInverse(state, arguments);

        /// <summary>
        /// Returns the first value of the current frame through its resolved polymorphic signature.
        /// </summary>
        public static PgAnyElement? MovingFinal(PgAggregateContext context, PolymorphicState? state, ValueTuple arguments)
            => PolymorphicAggregateFunctions.Moving.MovingFinal(state, null);
    }

    /// <summary>
    /// Applies independent numeric constraints to the members of a typed input group.
    /// </summary>
    [PgAggregate(Name = "typed_prices", InitialCondition = "0", Requires = ["aggregate-support"])]
    public readonly struct Prices : IPgAggregate<decimal, (decimal Price, decimal Charge)>
    {
        /// <summary>
        /// Adds values after each input crosses its own PostgreSQL numeric boundary.
        /// </summary>
        public static decimal Transition(PgAggregateContext context, decimal state,
            [PgNumericPrecision(5, 2, Element = "Price")]
            [PgNumericPrecision(6, 3, Element = "Charge")]
            (decimal Price, decimal Charge) arguments)
            => state + arguments.Price + arguments.Charge;
    }

    /// <summary>
    /// Collects a tuple's trailing SQL inputs into a nullable variadic array.
    /// </summary>
    [PgAggregate(Name = "typed_variadic", InitialCondition = "0", Requires = ["aggregate-support"])]
    public sealed class Variadic : IPgAggregate<long, (int Factor, int?[]? Values)>
    {
        /// <summary>
        /// Distinguishes empty arrays, NULL arrays and NULL elements while applying a scalar factor.
        /// </summary>
        public static long Transition(PgAggregateContext context, long state,
            [PgParameter(Element = "Values", Variadic = true)] (int Factor, int?[]? Values) arguments)
            => checked(state + arguments.Factor * (arguments.Values is null ? 1000 : arguments.Values.Sum(static value => (long)(value ?? 0))));
    }

    /// <summary>
    /// Receives exact raw and composite SQL identities through separate tuple slots.
    /// </summary>
    [PgAggregate(Name = "typed_bound_values", InitialCondition = "", Requires = ["aggregate-support", "raw-types"])]
    public sealed class BoundValues : IPgAggregate<string, (PgDatum Number, PgHeapTuple Row)>
    {
        /// <summary>
        /// Reads the bound scalar and named composite without flattening their native representation.
        /// </summary>
        public static string Transition(PgAggregateContext context, string state,
            [PgSqlType("int4", Schema = "pg_catalog", Element = "Number")]
            [PgCompositeType("pair", Schema = "raw_values", Element = "Row")]
            (PgDatum Number, PgHeapTuple Row) arguments)
            => state + FormattableString.Invariant($"{arguments.Number.Read<int>()}:{arguments.Row.Get<int>("number")}:{arguments.Row.Get<string>("label")};");
    }

    /// <summary>
    /// Flattens named tuple inputs and dispatches a private explicit implementation.
    /// </summary>
    [PgAggregate(Name = "typed_weighted", InitialCondition = "0", Requires = ["aggregate-support"])]
    public ref struct Weighted : IPgAggregate<long, (int? Amount, int? Weight)>
    {
        /// <summary>
        /// Adds a weighted row, preserving explicit NULL behavior.
        /// </summary>
        static long IPgAggregate<long, (int? Amount, int? Weight)>.Transition(PgAggregateContext context, long state, (int? Amount, int? Weight) arguments)
            => checked(state + (arguments.Amount ?? 7) * (arguments.Weight ?? 11));
    }

    /// <summary>
    /// Provides a reusable default transition with no SQL inputs.
    /// </summary>
    public interface IRowCounter : IPgAggregate<long, ValueTuple>
    {
        /// <summary>
        /// Counts a row through the compiler's default static implementation.
        /// </summary>
        static long IPgAggregate<long, ValueTuple>.Transition(PgAggregateContext context, long state, ValueTuple arguments)
            => checked(state + 1);
    }

    /// <summary>
    /// Exercises zero SQL inputs through a default interface implementation.
    /// </summary>
    [PgAggregate(Name = "typed_rows", InitialCondition = "0", Requires = ["aggregate-support"])]
    public sealed class Rows : IRowCounter
    {
    }

    /// <summary>
    /// Reuses observable state operations behind typed ordinary and moving capabilities.
    /// </summary>
    [PgAggregate(Name = "typed_owned", Requires = ["aggregate-support"])]
    public sealed class Owned : IPgAggregate<OwnedState?, int?>, IPgFinalizingAggregate<OwnedState?, ValueTuple, long>,
        IPgMovingAggregate<OwnedState?, int?>, IPgMovingFinalizingAggregate<OwnedState?, ValueTuple, long>
    {
        /// <summary>
        /// Creates and updates state in the native aggregate owner.
        /// </summary>
        public static OwnedState Transition(PgAggregateContext context, OwnedState? state, int? arguments)
            => AggregateFunctions.ManagedSum.Transition(context, state, arguments);

        /// <summary>
        /// Observes the owned result without releasing state early.
        /// </summary>
        public static long Final(PgAggregateContext context, OwnedState? state, ValueTuple arguments)
            => AggregateFunctions.ManagedSum.Final(context, state);

        /// <summary>
        /// Maintains independently owned moving state.
        /// </summary>
        public static OwnedState MovingTransition(PgAggregateContext context, OwnedState? state, int? arguments)
            => AggregateFunctions.ManagedSum.MovingTransition(context, state, arguments);

        /// <summary>
        /// Removes a row or asks PostgreSQL to restart the moving state.
        /// </summary>
        public static OwnedState? MovingInverse(PgAggregateContext context, OwnedState? state, int? arguments)
            => AggregateFunctions.ManagedSum.MovingInverse(state, arguments);

        /// <summary>
        /// Reads the current moving result through its optional capability.
        /// </summary>
        public static long MovingFinal(PgAggregateContext context, OwnedState? state, ValueTuple arguments)
            => AggregateFunctions.ManagedSum.MovingFinal(context, state);
    }

    /// <summary>
    /// Transports observed worker state through explicit static interface implementations.
    /// </summary>
    [PgAggregate(Name = "typed_parallel", ParallelSafety = PgParallelSafety.Safe, Requires = ["aggregate-support"])]
    public sealed class Parallel : IPgAggregate<ParallelState?, int?>, IPgFinalizingAggregate<ParallelState?, ValueTuple, long[]>,
        IPgCombinableAggregate<ParallelState?>, IPgSerializableAggregate<ParallelState>
    {
        /// <summary>
        /// Records exact input values and the worker process identity.
        /// </summary>
        static ParallelState IPgAggregate<ParallelState?, int?>.Transition(PgAggregateContext context, ParallelState? state, int? arguments)
            => AggregateParallelFunctions.ParallelSum.Transition(state, arguments);

        /// <summary>
        /// Copies borrowed partial state into the destination's owner.
        /// </summary>
        static ParallelState? IPgCombinableAggregate<ParallelState?>.Combine(PgAggregateContext context, ParallelState? state, ParallelState? other)
            => AggregateParallelFunctions.ParallelSum.Combine(state, other);

        /// <summary>
        /// Encodes worker data without process-local pointers.
        /// </summary>
        static byte[]? IPgSerializableAggregate<ParallelState>.Serialize(PgAggregateContext context, ParallelState state)
            => AggregateParallelFunctions.ParallelSum.Serialize(state);

        /// <summary>
        /// Restores a temporary worker state from its bytes.
        /// </summary>
        static ParallelState IPgSerializableAggregate<ParallelState>.Deserialize(PgAggregateContext context, byte[] bytes)
            => AggregateParallelFunctions.ParallelSum.Deserialize(bytes);

        /// <summary>
        /// Returns values, role counters and observed process identities.
        /// </summary>
        static long[] IPgFinalizingAggregate<ParallelState?, ValueTuple, long[]>.Final(PgAggregateContext context, ParallelState? state, ValueTuple arguments)
            => AggregateParallelFunctions.ParallelSum.Final(state);
    }
}
