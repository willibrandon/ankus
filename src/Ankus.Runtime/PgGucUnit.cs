namespace Ankus;

/// <summary>
/// Specifies the base unit PostgreSQL uses when parsing and displaying a numeric configuration setting.
/// </summary>
public enum PgGucUnit
{
    /// <summary>
    /// Uses no unit conversion.
    /// </summary>
    None = 0,

    /// <summary>
    /// Uses bytes.
    /// </summary>
    Bytes = 1,

    /// <summary>
    /// Uses kilobytes.
    /// </summary>
    Kilobytes = 2,

    /// <summary>
    /// Uses database blocks sized by the server build.
    /// </summary>
    Blocks = 3,

    /// <summary>
    /// Uses write-ahead-log blocks sized by the server build.
    /// </summary>
    WalBlocks = 4,

    /// <summary>
    /// Uses megabytes.
    /// </summary>
    Megabytes = 5,

    /// <summary>
    /// Uses milliseconds.
    /// </summary>
    Milliseconds = 6,

    /// <summary>
    /// Uses seconds.
    /// </summary>
    Seconds = 7,

    /// <summary>
    /// Uses minutes.
    /// </summary>
    Minutes = 8,
}
