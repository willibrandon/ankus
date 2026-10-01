using System.Numerics;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises shared inherited aggregate helpers with distinct closed generic state identities.
/// </summary>
[PgSchema("aggregate_values", Create = false)]
public static class SharedTypedAggregateFunctions
{
    /// <summary>
    /// Implements one compiler-checked transition shared by closed aggregate containers.
    /// </summary>
    /// <typeparam name="T">The exact numeric transition state.</typeparam>
    public abstract class SumBase<T> : IPgAggregate<T, int> where T : INumber<T>
    {
        /// <summary>
        /// Adds each input through the closed numeric contract without exposing a generic static API.
        /// </summary>
        /// <param name="context">The current aggregate invocation.</param>
        /// <param name="state">The retained transition value.</param>
        /// <param name="value">The current integer input.</param>
        /// <returns>The exact updated transition state.</returns>
        [PgFunction(Name = "typed_shared_step")]
        static T IPgAggregate<T, int>.Transition(PgAggregateContext context, T state, int value)
            => checked(state + T.CreateChecked(value));
    }

    /// <summary>
    /// Shares the integer transition with a zero initial condition.
    /// </summary>
    [PgAggregate(Name = "typed_shared_first", InitialCondition = "0", Requires = ["aggregate-support"])]
    public sealed class First : SumBase<int>;

    /// <summary>
    /// Shares the same transition while preserving its independent initial condition.
    /// </summary>
    [PgAggregate(Name = "typed_shared_second", InitialCondition = "7", Requires = ["aggregate-support"])]
    public sealed class Second : SumBase<int>;

    /// <summary>
    /// Uses a distinct closed bigint transition under the same SQL helper name.
    /// </summary>
    [PgAggregate(Name = "typed_shared_long", InitialCondition = "0", Requires = ["aggregate-support"])]
    public sealed class Wide : SumBase<long>;
}
