using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Stores authored SQL generation and dependency options independently of compiler attributes.
/// </summary>
/// <param name="Id">The optional explicit dependency identifier.</param>
/// <param name="Requires">The ordered prerequisite names, retaining invalid null values for diagnostics.</param>
/// <param name="Before">The ordered successor names, retaining invalid null values for diagnostics.</param>
/// <param name="GenerateSql">Whether the declaration emits installation SQL.</param>
/// <param name="Sql">The optional exact replacement SQL.</param>
/// <param name="SqlRelocatable">Whether replacement SQL permits schema relocation.</param>
internal sealed record SqlDeclarationOptions(string? Id, EquatableArray<string?> Requires, EquatableArray<string?> Before,
    bool GenerateSql, string? Sql, bool SqlRelocatable)
{
    /// <summary>
    /// Extracts authored constants while preserving existing default and invalid-array semantics.
    /// </summary>
    /// <param name="attribute">The optional declaration attribute.</param>
    /// <returns>The immutable options, or null when no declaration attribute exists.</returns>
    internal static SqlDeclarationOptions? Read(AttributeData? attribute)
        => attribute is null ? null : new(AttributeValues.Get<string?>(attribute, "Id", null),
            new(AttributeValues.Strings(attribute, "Requires")), new(AttributeValues.Strings(attribute, "Before")),
            AttributeValues.Get(attribute, "GenerateSql", true), AttributeValues.Get<string?>(attribute, "Sql", null),
            AttributeValues.Get(attribute, "SqlRelocatable", false));
}
