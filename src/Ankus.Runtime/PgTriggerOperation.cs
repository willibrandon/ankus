namespace Ankus;

/// <summary>
/// Identifies the single PostgreSQL operation that caused a trigger invocation.
/// </summary>
public enum PgTriggerOperation
{
    /// <summary>
    /// Inserts rows into the target relation.
    /// </summary>
    Insert = 0,

    /// <summary>
    /// Deletes rows from the target relation.
    /// </summary>
    Delete = 1,

    /// <summary>
    /// Updates existing rows in the target relation.
    /// </summary>
    Update = 2,

    /// <summary>
    /// Truncates the relation in a statement-level trigger.
    /// </summary>
    Truncate = 3,
}
