namespace Ankus.Examples.GenericAggregates;

/// <summary>
/// Counts how many times a polymorphic input differs from the previous non-null input.
/// </summary>
/// <remarks>
/// Declares <c>count_changes(anyelement) RETURNS bigint</c> with <c>internal</c> state.
/// Use an aggregate <c>ORDER BY</c> clause, because PostgreSQL does not otherwise define input order.
/// </remarks>
[PgAggregate(Name = "count_changes")]
public sealed class CountChanges : IPgAggregate<PgAggregateState<ChangeState>?, PgAnyElement?>,
    IPgFinalizingAggregate<PgAggregateState<ChangeState>?, ValueTuple, long>
{
    /// <summary>
    /// Ignores SQL NULL inputs and otherwise compares the input with the group's previous value.
    /// </summary>
    /// <param name="context">The current aggregate invocation and state owner.</param>
    /// <param name="state">The group's state, or null before its first non-null input.</param>
    /// <param name="value">The current input, whose type PostgreSQL resolves from the call.</param>
    /// <returns>The unchanged state for NULL input; otherwise the present state.</returns>
    public static PgAggregateState<ChangeState>? Transition(PgAggregateContext context, PgAggregateState<ChangeState>? state,
        PgAnyElement? value)
    {
        if (value is null)
        {
            return state;
        }

        state ??= new(new ChangeState(context.MemoryContext, value.TypeOid));
        state.Value.Observe(value);
        return state;
    }

    /// <summary>
    /// Returns the change count, including zero for empty and all-NULL input.
    /// </summary>
    /// <param name="context">The current aggregate invocation.</param>
    /// <param name="state">The group's state, or null when no non-null input arrived.</param>
    /// <param name="arguments">The empty direct argument group.</param>
    /// <returns>The number of changes.</returns>
    public static long Final(PgAggregateContext context, PgAggregateState<ChangeState>? state, ValueTuple arguments)
        => state?.Value.Changes ?? 0;
}
