namespace Ankus;

/// <summary>
/// Identifies PostgreSQL's origin for a proposed configuration value passed to a check hook.
/// </summary>
public enum PgGucSource
{
    /// <summary>
    /// Identifies the compiled default.
    /// </summary>
    Default = 0,

    /// <summary>
    /// Identifies a default computed during initialization.
    /// </summary>
    DynamicDefault = 1,

    /// <summary>
    /// Identifies the postmaster environment.
    /// </summary>
    Environment = 2,

    /// <summary>
    /// Identifies a configuration file.
    /// </summary>
    File = 3,

    /// <summary>
    /// Identifies a server command-line option.
    /// </summary>
    CommandLine = 4,

    /// <summary>
    /// Identifies a global database setting.
    /// </summary>
    Global = 5,

    /// <summary>
    /// Identifies a per-database setting.
    /// </summary>
    Database = 6,

    /// <summary>
    /// Identifies a per-user setting.
    /// </summary>
    User = 7,

    /// <summary>
    /// Identifies a per-user and per-database setting.
    /// </summary>
    DatabaseUser = 8,

    /// <summary>
    /// Identifies a client connection startup option.
    /// </summary>
    Client = 9,

    /// <summary>
    /// Identifies an internal forced default.
    /// </summary>
    Override = 10,

    /// <summary>
    /// Identifies PostgreSQL's boundary between noninteractive and interactive error reporting.
    /// </summary>
    Interactive = 11,

    /// <summary>
    /// Identifies validation of a value for later use, without applying it.
    /// </summary>
    Test = 12,

    /// <summary>
    /// Identifies a session SET command.
    /// </summary>
    Session = 13,
}
