namespace Ankus;

/// <summary>
/// Declares which value changes PostgreSQL's planner may assume for a function.
/// </summary>
public enum PgVolatility
{
    /// <summary>
    /// Allows side effects and results that change between calls. This is the default.
    /// </summary>
    Volatile,

    /// <summary>
    /// Promises a consistent result for equal arguments throughout one statement and no database writes.
    /// </summary>
    Stable,

    /// <summary>
    /// Promises the same result for equal arguments forever, independent of database or session state.
    /// </summary>
    Immutable,
}
