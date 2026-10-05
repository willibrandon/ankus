namespace Ankus;

/// <summary>
/// Runs recoverable work and registers callbacks for the current PostgreSQL transaction.
/// </summary>
public static class PgTransaction
{
    /// <summary>
    /// Runs synchronous work in an internal subtransaction, rolling it back if the callback fails.
    /// </summary>
    /// <param name="action">The work to run on the active backend thread.</param>
    /// <remarks>
    /// Catch failures outside this callback, after native resources have been recovered.
    /// An unrecovered native error, including a value-operation error, prevents further backend work in this scope, even if caught.
    /// Success retains changes in the enclosing transaction; it does not commit that transaction.
    /// The callback must not perform transaction control or asynchronous work. This operation
    /// is unavailable during transaction callbacks and abort cleanup. Internal guard
    /// subtransactions do not invoke consumer subtransaction callbacks.
    /// </remarks>
    public static void RunInSubtransaction(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        NativeSubtransaction.Run(() =>
        {
            action();
            return 0;
        });
    }

    /// <summary>
    /// Runs synchronous work in an internal subtransaction and returns its result after successful release.
    /// </summary>
    /// <typeparam name="TResult">The callback result type.</typeparam>
    /// <param name="action">The synchronous work to run on the active backend thread.</param>
    /// <returns>The result after the subtransaction succeeds.</returns>
    /// <remarks>
    /// Failures roll back this scope before propagating to the caller. Catch native errors outside the callback;
    /// catching one inside cannot turn a failed scope into a successful one.
    /// Nested scopes may recover independently. Native results retain PostgreSQL's memory-context
    /// lifetimes; rollback invalidates allocations and resources owned by the aborted scope.
    /// Cleanup aggregates that contain the original native failure retain all their causes after rollback.
    /// Do not perform transaction control or asynchronous work in the callback. This operation
    /// is unavailable during transaction callbacks and abort cleanup.
    /// </remarks>
    public static TResult RunInSubtransaction<TResult>(Func<TResult> action) => NativeSubtransaction.Run(action);

    /// <summary>
    /// Registers a one-shot callback for an outer-transaction phase.
    /// </summary>
    /// <param name="event">The phase that runs the callback.</param>
    /// <param name="callback">The action to run in registration order.</param>
    /// <returns>A registration that can cancel the pending callback.</returns>
    /// <remarks>
    /// PreCommit and PrePrepare exceptions reject the transaction. Unhandled exceptions in completed
    /// commit, abort, and prepare phases cause PostgreSQL crash recovery; committed changes remain committed.
    /// </remarks>
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
    /// <remarks>
    /// Start and PreCommit exceptions reject the operation. Unhandled exceptions during commit or abort
    /// cleanup cause PostgreSQL crash recovery.
    /// </remarks>
    public static PgSubtransactionCallback RegisterSubtransactionCallback(
        PgSubtransactionEvent @event,
        Action<PgSubtransactionId, PgSubtransactionId> callback)
        => NativeTransactionCallbacks.Register(@event, callback);
}
