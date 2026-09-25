namespace Ankus;

/// <summary>
/// Identifies whether this extension supplies a mapped PostgreSQL type or uses an existing external type.
/// </summary>
public enum PgTypeOrigin
{
    /// <summary>
    /// Requires a declared SQL provider and follows the extension schema when no fixed schema is supplied.
    /// </summary>
    ThisExtension,

    /// <summary>
    /// Uses an existing type in an explicitly supplied schema without an extension-owned provider.
    /// </summary>
    External,
}
