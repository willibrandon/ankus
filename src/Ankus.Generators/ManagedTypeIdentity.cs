using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Preserves exact managed type identity and source distinctions used by default Roslyn symbol equality.
/// </summary>
/// <param name="Declaration">The assembly-qualified structural identity.</param>
/// <param name="Signature">The qualified type spelling retaining tuple labels, dynamic and native integer distinctions.</param>
internal sealed record ManagedTypeIdentity(DeclarationIdentity Declaration, string Signature)
{
    /// <summary>
    /// Extracts an immutable identity while excluding reference-type nullable annotations.
    /// </summary>
    /// <param name="type">The resolved managed type.</param>
    /// <returns>The detached type key.</returns>
    internal static ManagedTypeIdentity Create(ITypeSymbol type)
        => new(DeclarationIdentity.Create(type), type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
}
