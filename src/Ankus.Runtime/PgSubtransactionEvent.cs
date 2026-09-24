namespace Ankus;

/// <summary>
/// Identifies a PostgreSQL subtransaction callback phase.
/// </summary>
public enum PgSubtransactionEvent
{
    /// <summary>
    /// Runs after the subtransaction aborts.
    /// </summary>
    Abort,

    /// <summary>
    /// Runs after the subtransaction commits.
    /// </summary>
    Commit,

    /// <summary>
    /// Runs immediately before the subtransaction commits.
    /// </summary>
    PreCommit,

    /// <summary>
    /// Runs after the subtransaction has started.
    /// </summary>
    Start,
}
