namespace Ankus;

/// <summary>
/// Selects a PostgreSQL reporting severity, independently of version-specific native numeric values.
/// </summary>
public enum PgLogLevel
{
    /// <summary>
    /// Reports the most detailed debugging messages.
    /// </summary>
    Debug5,
    /// <summary>
    /// Reports debugging messages at detail level four.
    /// </summary>
    Debug4,
    /// <summary>
    /// Reports debugging messages at detail level three.
    /// </summary>
    Debug3,
    /// <summary>
    /// Reports debugging messages at detail level two.
    /// </summary>
    Debug2,
    /// <summary>
    /// Reports the least detailed debugging messages.
    /// </summary>
    Debug1,
    /// <summary>
    /// Reports server operational messages, routed to the server log by default.
    /// </summary>
    Log,
    /// <summary>
    /// Reports operational messages to the server log only, regardless of client settings.
    /// </summary>
    ServerOnly,
    /// <summary>
    /// Reports information to clients regardless of client_min_messages.
    /// </summary>
    Info,
    /// <summary>
    /// Reports expected events helpful to clients.
    /// </summary>
    Notice,
    /// <summary>
    /// Reports unexpected events while allowing execution to continue.
    /// </summary>
    Warning,
    /// <summary>
    /// Throws a catchable PgException; an unhandled exception becomes PostgreSQL ERROR after managed unwinding.
    /// </summary>
    Error,
    /// <summary>
    /// Unwinds managed code, then terminates the backend connection at the native reporting boundary.
    /// </summary>
    Fatal,
    /// <summary>
    /// Unwinds managed code, then aborts the backend and triggers PostgreSQL cluster crash recovery.
    /// </summary>
    Panic,
}
