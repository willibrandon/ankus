namespace Ankus.Examples.Composites;

/// <summary>
/// Sums the scritches received by dogs, roughly <c>CREATE AGGREGATE sum_scritches(value Dog)</c> with an integer state.
/// </summary>
/// <remarks>
/// The strict transition skips NULL dogs. A NULL count inside a present dog adds zero.
/// </remarks>
[PgAggregate(Name = "sum_scritches", InitialCondition = "0")]
public sealed class SumScritches : IPgAggregate<int, PgHeapTuple>
{
    /// <summary>
    /// Adds one dog's scritches to the running total with overflow checking.
    /// </summary>
    /// <param name="context">The current aggregate invocation.</param>
    /// <param name="state">The running total, starting at zero.</param>
    /// <param name="value">The next dog.</param>
    /// <returns>The new total.</returns>
    public static int Transition(PgAggregateContext context, int state, [PgCompositeType(CompositeTypes.Dog)] PgHeapTuple value)
        => checked(state + value.Get<int?>("scritches").GetValueOrDefault());
}
