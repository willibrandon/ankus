namespace Ankus;

/// <summary>
/// Selects a PostgreSQL relation lock. The native lock manager determines conflicts and transaction behavior.
/// </summary>
public enum PgLockMode
{
    /// <summary>
    /// Acquires no lock; only explicit unsafe relation operations accept this value.
    /// </summary>
    None,

    /// <summary>
    /// Prevents concurrent access-exclusive operations while allowing ordinary readers and writers.
    /// </summary>
    AccessShare,

    /// <summary>
    /// Acquires PostgreSQL's row-share relation lock.
    /// </summary>
    RowShare,

    /// <summary>
    /// Acquires PostgreSQL's row-exclusive relation lock.
    /// </summary>
    RowExclusive,

    /// <summary>
    /// Acquires PostgreSQL's share-update-exclusive relation lock.
    /// </summary>
    ShareUpdateExclusive,

    /// <summary>
    /// Acquires PostgreSQL's share relation lock.
    /// </summary>
    Share,

    /// <summary>
    /// Acquires PostgreSQL's share-row-exclusive relation lock.
    /// </summary>
    ShareRowExclusive,

    /// <summary>
    /// Acquires PostgreSQL's exclusive relation lock.
    /// </summary>
    Exclusive,

    /// <summary>
    /// Conflicts with every other relation lock mode, including ordinary reads.
    /// </summary>
    AccessExclusive,
}
