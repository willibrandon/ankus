namespace Ankus;

/// <summary>
/// Distinguishes invalid, explicitly custom, and recognized built-in OID values.
/// </summary>
public enum PgOidKind
{
    /// <summary>
    /// PostgreSQL's invalid object identifier, zero.
    /// </summary>
    Invalid,

    /// <summary>
    /// A value outside the selected built-in catalog, or explicitly marked custom by the caller.
    /// </summary>
    Custom,

    /// <summary>
    /// A value recognized by the selected pgrx built-in constant catalog.
    /// </summary>
    BuiltIn,
}
