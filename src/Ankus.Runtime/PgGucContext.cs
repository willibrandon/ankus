namespace Ankus;

/// <summary>
/// Specifies when PostgreSQL permits a configuration setting to change.
/// </summary>
public enum PgGucContext
{
    /// <summary>
    /// Allows only internal server changes.
    /// </summary>
    Internal = 0,

    /// <summary>
    /// Allows changes only during server startup; shared preload registration is required.
    /// </summary>
    Postmaster = 1,

    /// <summary>
    /// Allows startup configuration and configuration-file reload changes.
    /// </summary>
    Sighup = 2,

    /// <summary>
    /// Allows superuser connection-start changes and startup configuration.
    /// </summary>
    SuperuserBackend = 3,

    /// <summary>
    /// Allows connection-start changes and startup configuration.
    /// </summary>
    Backend = 4,

    /// <summary>
    /// Allows superusers to change the value during a session.
    /// </summary>
    SuperuserSet = 5,

    /// <summary>
    /// Allows users to change the value during a session.
    /// </summary>
    UserSet = 6,
}
