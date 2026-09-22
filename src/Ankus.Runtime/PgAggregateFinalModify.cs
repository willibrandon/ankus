namespace Ankus;

/// <summary>
/// Describes whether an aggregate final callback may modify its transition state.
/// </summary>
public enum PgAggregateFinalModify
{
    /// <summary>
    /// Uses PostgreSQL's default for this aggregate kind.
    /// </summary>
    Default = 0,

    /// <summary>
    /// Leaves state unchanged and permits sharing or repeated final evaluation.
    /// </summary>
    ReadOnly = 1,

    /// <summary>
    /// May modify state but allows subsequent final callbacks to use it.
    /// </summary>
    Shareable = 2,

    /// <summary>
    /// May modify or consume state, preventing subsequent use of the same transition state.
    /// </summary>
    ReadWrite = 3,
}
