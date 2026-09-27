namespace Ankus;

/// <summary>
/// Describes a worker's observed process state or a wait that cannot be tracked by this backend.
/// </summary>
public enum PgBackgroundWorkerStatus
{
    /// <summary>
    /// The worker is running and has an observed process identifier.
    /// </summary>
    Started,
    /// <summary>
    /// The postmaster has not yet attempted to start the worker.
    /// </summary>
    NotYetStarted,
    /// <summary>
    /// The worker is stopped; a failed worker configured for restart may start again later.
    /// </summary>
    Stopped,
    /// <summary>
    /// The postmaster died while the backend waited for a transition.
    /// </summary>
    PostmasterDied,
    /// <summary>
    /// The registering backend was not selected to receive worker transition notifications.
    /// </summary>
    Untracked,
}
