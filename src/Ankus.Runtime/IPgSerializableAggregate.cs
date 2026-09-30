namespace Ankus;

/// <summary>
/// Transports internal aggregate state between PostgreSQL processes.
/// </summary>
/// <typeparam name="TState">The internal state representation.</typeparam>
public interface IPgSerializableAggregate<TState>
{
    /// <summary>
    /// Encodes state into a process-independent byte sequence.
    /// </summary>
    /// <param name="context">The current aggregate invocation.</param>
    /// <param name="state">The state to encode.</param>
    /// <returns>The serialized state, without process-local pointers, or SQL NULL for an absent partial state.</returns>
    static abstract byte[]? Serialize(PgAggregateContext context, TState state);

    /// <summary>
    /// Restores state with the temporary owner supplied by PostgreSQL.
    /// </summary>
    /// <param name="context">The current invocation and temporary state owner.</param>
    /// <param name="bytes">The serialized state.</param>
    /// <returns>The restored state.</returns>
    static abstract TState Deserialize(PgAggregateContext context, byte[] bytes);
}
