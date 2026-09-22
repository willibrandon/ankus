namespace Ankus;

/// <summary>
/// Selects how PostgreSQL consumes a generated set-returning function.
/// </summary>
public enum PgSetMode
{
    /// <summary>
    /// Produces one row per call when supported, otherwise materializes the set.
    /// </summary>
    Auto,

    /// <summary>
    /// Requires a caller that supports producing one row per call.
    /// </summary>
    ValuePerCall,

    /// <summary>
    /// Requires a caller that accepts a complete, spillable PostgreSQL tuple store.
    /// </summary>
    Materialize,
}
