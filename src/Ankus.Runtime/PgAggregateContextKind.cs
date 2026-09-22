namespace Ankus;

/// <summary>
/// Identifies the executor context that owns an aggregate callback's state.
/// </summary>
public enum PgAggregateContextKind
{
    /// <summary>
    /// Runs in an aggregate executor context.
    /// </summary>
    Aggregate = 1,

    /// <summary>
    /// Runs in a window aggregate executor context.
    /// </summary>
    Window = 2,
}
