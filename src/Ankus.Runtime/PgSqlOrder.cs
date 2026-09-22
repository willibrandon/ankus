namespace Ankus;

/// <summary>
/// Positions custom installation SQL relative to the extension's other declarations.
/// </summary>
public enum PgSqlOrder
{
    /// <summary>
    /// Uses explicit dependencies and deterministic ordering among otherwise independent declarations.
    /// </summary>
    Normal,

    /// <summary>
    /// Runs before every other declaration. An extension may declare only one bootstrap block.
    /// </summary>
    Bootstrap,

    /// <summary>
    /// Runs after every other declaration. An extension may declare only one final block.
    /// </summary>
    Finalize,
}
