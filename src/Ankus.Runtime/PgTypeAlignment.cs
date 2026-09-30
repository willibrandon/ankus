namespace Ankus;

/// <summary>
/// Selects the PostgreSQL datum alignment of a generated variable-length base type.
/// </summary>
/// <remarks>
/// Alignment describes database storage and does not change the codec's payload bytes
/// or the managed type's layout. PostgreSQL does not permit smaller alignments for variable-length types.
/// </remarks>
public enum PgTypeAlignment
{
    /// <summary>
    /// Uses PostgreSQL's int4 alignment, the default for generated base types.
    /// </summary>
    FourBytes,

    /// <summary>
    /// Uses PostgreSQL's double alignment.
    /// </summary>
    EightBytes,
}
