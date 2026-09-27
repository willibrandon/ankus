namespace Ankus;

/// <summary>
/// Selects the earliest PostgreSQL startup phase at which a worker may run.
/// </summary>
public enum PgBackgroundWorkerStartTime
{
    /// <summary>
    /// Starts when the postmaster starts, before database connections are available.
    /// </summary>
    PostmasterStart,
    /// <summary>
    /// Starts after recovery reaches a consistent state.
    /// </summary>
    ConsistentState,
    /// <summary>
    /// Starts after recovery finishes and ordinary database access is available.
    /// </summary>
    RecoveryFinished,
}
