namespace Ankus;

/// <summary>
/// Identifies when PostgreSQL invokes a trigger relative to its operation.
/// </summary>
public enum PgTriggerTiming
{
    /// <summary>
    /// Runs before the row or statement operation.
    /// </summary>
    Before,

    /// <summary>
    /// Runs after the row or statement operation; the returned tuple is ignored.
    /// </summary>
    After,

    /// <summary>
    /// Replaces an ordinary row operation on a view.
    /// </summary>
    InsteadOf,
}
