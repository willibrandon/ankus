namespace Ankus;

/// <summary>
/// Identifies whether a PostgreSQL trigger invocation applies to a row or an entire statement.
/// </summary>
public enum PgTriggerLevel
{
    /// <summary>
    /// Runs once for each affected row and provides the operation's OLD and NEW values.
    /// </summary>
    Row,

    /// <summary>
    /// Runs once for the statement and has no OLD or NEW row value.
    /// </summary>
    Statement,
}
