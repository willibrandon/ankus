namespace Ankus;

/// <summary>
/// Selects PostgreSQL's aggregate argument and ordering semantics.
/// </summary>
public enum PgAggregateKind
{
    /// <summary>
    /// Accepts ordinary aggregated arguments.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Accepts direct arguments and ordered aggregated arguments through WITHIN GROUP.
    /// </summary>
    OrderedSet = 1,

    /// <summary>
    /// Accepts hypothetical direct values corresponding to the ordered aggregated values.
    /// </summary>
    HypotheticalSet = 2,
}
