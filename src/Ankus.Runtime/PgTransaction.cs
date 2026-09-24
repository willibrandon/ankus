namespace Ankus;

/// <summary>
/// Registers managed callbacks for the current PostgreSQL transaction.
/// </summary>
public static class PgTransaction
{
    /// <summary>
    /// Registers a one-shot callback for an outer-transaction phase.
    /// </summary>
    /// <param name="event">The phase that runs the callback.</param>
    /// <param name="callback">The action to run in registration order.</param>
    /// <returns>A registration that can cancel the pending callback.</returns>
    public static PgTransactionCallback RegisterCallback(PgTransactionEvent @event, Action callback)
        => NativeTransactionCallbacks.Register(@event, callback);

    /// <summary>
    /// Registers a callback for every matching subtransaction in the current outer transaction.
    /// </summary>
    /// <param name="event">The subtransaction phase that runs the callback.</param>
    /// <param name="callback">
    /// The action to run with the current subtransaction ID followed by its parent subtransaction ID.
    /// </param>
    /// <returns>A registration that can cancel future invocations.</returns>
    public static PgSubtransactionCallback RegisterSubtransactionCallback(
        PgSubtransactionEvent @event,
        Action<uint, uint> callback)
        => NativeTransactionCallbacks.Register(@event, callback);
}
