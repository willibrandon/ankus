namespace Ankus.Generators;

/// <summary>
/// Retains rendered SQL type spelling separately from current extension-provider qualification.
/// </summary>
/// <param name="Sql">The quoted leaf catalog spelling, including exact array suffixes.</param>
/// <param name="DefaultSchema">Whether this type is intrinsically owned by the extension's default schema.</param>
/// <param name="Provider">An unqualified named binding whose ownership is selected by the current graph.</param>
internal sealed record SqlTypeTemplate(string Sql, bool DefaultSchema, SqlTypeReference? Provider)
{
    /// <summary>
    /// Extracts only catalog rendering values from a detached conversion contract.
    /// </summary>
    /// <param name="type">The validated scalar or array datum contract.</param>
    /// <returns>The immutable catalog fragment and ownership policy.</returns>
    internal static SqlTypeTemplate Create(FunctionType type)
    {
        if (type.Element is { } element)
        {
            SqlTypeTemplate leaf = Create(element);
            return leaf with { Sql = leaf.Sql + "[]" };
        }

        return new(type.Sql, type.Enumeration is { Schema: null } || type.CustomType is { Schema: null } ||
            type.DatumType is { Schema: null, External: false }, type.Binding is { Schema: null } binding ? binding : null);
    }

    /// <summary>
    /// Qualifies typed identifiers against current graph ownership without rewriting authored SQL.
    /// </summary>
    /// <param name="providers">The current graph's validated type inventory, or null when unavailable.</param>
    /// <returns>The catalog spelling with an extension-default schema marker when required.</returns>
    internal string Emit(SqlTypeProviders? providers)
        => (DefaultSchema || Provider is { } binding && providers?.Contains(binding) == true ? "\0" : string.Empty) + Sql;
}
