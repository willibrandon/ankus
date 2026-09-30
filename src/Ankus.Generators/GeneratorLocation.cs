using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Ankus.Generators;

/// <summary>
/// Retains diagnostic coordinates without keeping a syntax tree or an old compilation alive.
/// </summary>
/// <param name="TreeIndex">The source tree's ordinal in the analyzed compilation.</param>
/// <param name="Span">The exact authored source span.</param>
internal readonly record struct GeneratorLocation(int TreeIndex, TextSpan Span)
{
    /// <summary>
    /// Detaches a source location from the semantic analysis that produced it.
    /// </summary>
    /// <param name="location">The optional authored location.</param>
    /// <param name="compilation">The compilation that owns the source tree.</param>
    /// <returns>The coordinates, or null for a location outside the compilation.</returns>
    internal static GeneratorLocation? Create(Location? location, Compilation compilation)
    {
        int index = 0;
        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            if (tree == location?.SourceTree)
            {
                return new(index, location.SourceSpan);
            }

            index++;
        }

        return null;
    }

    /// <summary>
    /// Reattaches the coordinates to the current tree, preserving line mappings and editor navigation.
    /// </summary>
    /// <param name="compilation">The current compilation used to report diagnostics.</param>
    /// <returns>The current source location.</returns>
    internal Location Resolve(Compilation compilation)
        => Location.Create(compilation.SyntaxTrees.ElementAt(TreeIndex), Span);
}
