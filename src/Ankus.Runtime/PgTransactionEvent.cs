namespace Ankus;

/// <summary>
/// Identifies a PostgreSQL outer-transaction callback phase.
/// </summary>
public enum PgTransactionEvent
{
    /// <summary>
    /// Runs after the transaction aborts.
    /// </summary>
    Abort,

    /// <summary>
    /// Runs after the transaction commits.
    /// </summary>
    Commit,

    /// <summary>
    /// Runs immediately before the transaction commits.
    /// </summary>
    PreCommit,

    /// <summary>
    /// Runs after a parallel worker transaction aborts.
    /// </summary>
    ParallelAbort,

    /// <summary>
    /// Runs after a parallel worker transaction commits.
    /// </summary>
    ParallelCommit,

    /// <summary>
    /// Runs immediately before a parallel worker transaction commits.
    /// </summary>
    ParallelPreCommit,

    /// <summary>
    /// Runs after a transaction has been prepared for two-phase commit.
    /// </summary>
    Prepare,

    /// <summary>
    /// Runs immediately before a transaction is prepared for two-phase commit.
    /// </summary>
    PrePrepare,
}
