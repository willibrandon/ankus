namespace Ankus.Examples.Operators;

/// <summary>
/// A PostgreSQL enum whose explicit integer cast exposes its application priority score.
/// </summary>
[PgEnum]
public enum Priority
{
    /// <summary>
    /// Background work with a score of ten.
    /// </summary>
    Low = 10,

    /// <summary>
    /// Ordinary work with a score of twenty.
    /// </summary>
    Normal = 20,

    /// <summary>
    /// Urgent work with a score of thirty.
    /// </summary>
    High = 30,
}
