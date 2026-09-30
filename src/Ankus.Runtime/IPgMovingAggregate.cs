namespace Ankus;

/// <summary>
/// Maintains aggregate state as rows enter and leave a moving window.
/// </summary>
/// <typeparam name="TState">The moving state, including SQL nullability.</typeparam>
/// <typeparam name="TArgs">The aggregated inputs, with the same SQL types as the ordinary transition.</typeparam>
public interface IPgMovingAggregate<TState, TArgs>
{
    /// <summary>
    /// Incorporates an entering row and returns a present moving state.
    /// </summary>
    /// <param name="context">The window invocation and state owner.</param>
    /// <param name="state">The current moving state.</param>
    /// <param name="arguments">The entering row's inputs.</param>
    /// <returns>A present state; PostgreSQL rejects a SQL NULL forward result.</returns>
    static abstract TState MovingTransition(PgAggregateContext context, TState state, TArgs arguments);

    /// <summary>
    /// Removes a departing row, or requests recomputation by returning SQL NULL.
    /// </summary>
    /// <param name="context">The window invocation and state owner.</param>
    /// <param name="state">The current moving state.</param>
    /// <param name="arguments">The departing row's inputs.</param>
    /// <returns>The updated state, or SQL NULL when the declared state permits it.</returns>
    static abstract TState MovingInverse(PgAggregateContext context, TState state, TArgs arguments);
}
