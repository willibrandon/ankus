namespace Ankus;

/// <summary>
/// Declares where PostgreSQL may execute a function in a parallel query.
/// </summary>
public enum PgParallelSafety
{
    /// <summary>
    /// Disables parallel plans for queries containing the function. This is the default.
    /// </summary>
    Unsafe,

    /// <summary>
    /// Allows parallel plans but executes the function only in the leader process.
    /// </summary>
    Restricted,

    /// <summary>
    /// Allows execution in parallel workers as well as the leader.
    /// </summary>
    Safe,
}
