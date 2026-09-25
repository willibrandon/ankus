namespace Ankus;

/// <summary>
/// Identifies the kind of a pg_proc catalog entry.
/// </summary>
public enum PgFunctionKind
{
    /// <summary>
    /// An ordinary function.
    /// </summary>
    Function,

    /// <summary>
    /// A procedure invoked by CALL.
    /// </summary>
    Procedure,

    /// <summary>
    /// An aggregate function.
    /// </summary>
    Aggregate,

    /// <summary>
    /// A window function.
    /// </summary>
    Window,
}
