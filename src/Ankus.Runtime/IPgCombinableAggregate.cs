namespace Ankus;

/// <summary>
/// Combines partial aggregate states for parallel or partial aggregation.
/// </summary>
/// <typeparam name="TState">The state, including SQL nullability.</typeparam>
public interface IPgCombinableAggregate<TState>
{
    /// <summary>
    /// Merges a partial state into state owned by the destination invocation.
    /// </summary>
    /// <param name="context">The destination invocation and state owner.</param>
    /// <param name="state">The destination state.</param>
    /// <param name="other">The borrowed partial state.</param>
    /// <returns>State owned by the destination.</returns>
    static abstract TState Combine(PgAggregateContext context, TState state, TState other);
}
