namespace Ankus;

/// <summary>
/// Describes conversion of an unsigned value to a version-specific built-in OID.
/// </summary>
public enum PgOidLookupError
{
    /// <summary>
    /// Conversion succeeded.
    /// </summary>
    None,

    /// <summary>
    /// The value is PostgreSQL's invalid object identifier, zero.
    /// </summary>
    Invalid,

    /// <summary>
    /// The value fits an OID but is absent from the selected built-in catalog.
    /// </summary>
    Ambiguous,

    /// <summary>
    /// The value exceeds the unsigned 32-bit OID representation.
    /// </summary>
    TooBig,
}
