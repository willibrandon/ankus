namespace Ankus;

/// <summary>
/// Selects collation and variadic binding for a PostgreSQL function call.
/// </summary>
public sealed class PgFunctionCallOptions
{
    /// <summary>
    /// Gets the explicit input collation OID, or null to use PostgreSQL's argument collation rules.
    /// Zero explicitly selects no collation.
    /// </summary>
    public uint? CollationOid { get; init; }

    /// <summary>
    /// Gets whether a named call supplies the final variadic array directly, as with SQL VARIADIC.
    /// Calls by OID always use the declared argument list, including any variadic array.
    /// </summary>
    public bool Variadic { get; init; }
}
