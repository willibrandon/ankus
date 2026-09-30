namespace Ankus.Generators;

/// <summary>
/// Carries a validated custom type's conversion identity without retaining codec or serializer analysis.
/// </summary>
/// <param name="Name">The exact catalog leaf name.</param>
/// <param name="Schema">The fixed catalog schema, or null for the extension schema.</param>
/// <param name="Managed">The globally qualified managed payload type.</param>
internal sealed record CustomTypeReference(string Name, string? Schema, string Managed)
{
    /// <summary>
    /// Detaches the values required by function conversion from the validated declaration.
    /// </summary>
    /// <param name="declaration">The already validated storage and codec declaration.</param>
    /// <returns>The immutable conversion reference.</returns>
    internal static CustomTypeReference Create(CustomTypeDeclaration declaration)
        => new(declaration.Name, declaration.Schema, declaration.Managed);
}
