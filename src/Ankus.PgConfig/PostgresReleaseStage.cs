namespace Ankus.PgConfig;

/// <summary>
/// Identifies whether a PostgreSQL installation is a stable release, beta, or
/// release candidate.
/// </summary>
public enum PostgresReleaseStage
{
    /// <summary>
    /// Identifies a normal PostgreSQL release.
    /// </summary>
    Stable,

    /// <summary>
    /// Identifies a PostgreSQL beta release.
    /// </summary>
    Beta,

    /// <summary>
    /// Identifies a PostgreSQL release candidate.
    /// </summary>
    ReleaseCandidate,
}
