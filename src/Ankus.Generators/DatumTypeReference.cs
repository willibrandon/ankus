namespace Ankus.Generators;

/// <summary>
/// Carries a validated mapped datum's identity and capabilities without retaining converter symbols.
/// </summary>
/// <param name="Type">The exact managed provider identity.</param>
/// <param name="Name">The exact catalog leaf name.</param>
/// <param name="Schema">The fixed catalog schema, or null for the extension schema.</param>
/// <param name="Managed">The qualified managed spelling preserving nested nullable annotations.</param>
/// <param name="External">Whether the catalog type exists independently of this extension.</param>
/// <param name="CanRead">Whether the converter accepts input values.</param>
/// <param name="CanWrite">Whether the converter writes output values.</param>
internal sealed record DatumTypeReference(ManagedTypeIdentity Type, string Name, string? Schema, string Managed,
    bool External, bool CanRead, bool CanWrite)
{
    /// <summary>
    /// Detaches function conversion and provider values from validated converter analysis.
    /// </summary>
    /// <param name="declaration">The validated closed datum mapping.</param>
    /// <returns>The immutable conversion reference.</returns>
    internal static DatumTypeReference Create(DatumTypeDeclaration declaration)
        => new(ManagedTypeIdentity.Create(declaration.Type), declaration.Name, declaration.Schema, declaration.Managed,
            declaration.External, declaration.CanRead, declaration.CanWrite);
}
