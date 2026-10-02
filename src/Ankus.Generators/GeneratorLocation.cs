using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Ankus.Generators;

/// <summary>
/// Retains declaration-relative diagnostic coordinates without keeping a syntax tree or an old compilation alive.
/// </summary>
/// <param name="Path">The source tree's physical path.</param>
/// <param name="TreeOccurrence">The occurrence among source trees sharing that exact path.</param>
/// <param name="MemberIndex">The declaration ordinal, or minus one for a compilation-unit location.</param>
/// <param name="Span">The exact source span relative to its enclosing declaration.</param>
internal readonly record struct GeneratorLocation(string Path, int TreeOccurrence, int MemberIndex, TextSpan Span)
{
    /// <summary>
    /// Detaches a source location from the semantic analysis that produced it.
    /// </summary>
    /// <param name="location">The optional authored location.</param>
    /// <param name="compilation">The compilation that owns the source tree.</param>
    /// <returns>The coordinates, or null for a location outside the compilation.</returns>
    internal static GeneratorLocation? Create(Location? location, Compilation compilation)
    {
        if (location?.SourceTree is not { } source)
        {
            return null;
        }

        int occurrence = 0;
        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            if (tree == source)
            {
                SyntaxNode root = tree.GetRoot();
                MemberDeclarationSyntax? member = root.FindNode(location.SourceSpan).AncestorsAndSelf()
                    .OfType<MemberDeclarationSyntax>().FirstOrDefault();
                int index = member is null ? -1 : Array.FindIndex(Members(root), candidate => candidate == member);
                SyntaxNode anchor = member ?? root;
                return new(tree.FilePath, occurrence, index,
                    new TextSpan(location.SourceSpan.Start - anchor.SpanStart, location.SourceSpan.Length));
            }

            if (tree.FilePath == source.FilePath)
            {
                occurrence++;
            }
        }

        return null;
    }

    /// <summary>
    /// Reattaches the coordinates to the current tree, preserving line mappings and editor navigation.
    /// </summary>
    /// <param name="compilation">The current compilation used to report diagnostics.</param>
    /// <returns>The current source location.</returns>
    internal Location Resolve(Compilation compilation)
    {
        string path = Path;
        SyntaxTree tree = compilation.SyntaxTrees.Where(tree => tree.FilePath == path).ElementAt(TreeOccurrence);
        SyntaxNode root = tree.GetRoot();
        SyntaxNode anchor = MemberIndex < 0 ? root : Members(root)[MemberIndex];
        return Location.Create(tree, new TextSpan(anchor.SpanStart + Span.Start, Span.Length));
    }

    /// <summary>
    /// Enumerates authored declarations without descending into implementation bodies.
    /// </summary>
    /// <param name="root">The current compilation-unit syntax.</param>
    /// <returns>The ordered declaration anchors, independent of body text length.</returns>
    private static MemberDeclarationSyntax[] Members(SyntaxNode root)
        => [.. root.DescendantNodes(static node => node is not BaseMethodDeclarationSyntax and not AccessorDeclarationSyntax
            and not AnonymousFunctionExpressionSyntax).OfType<MemberDeclarationSyntax>()];

    /// <summary>
    /// Resolves graph attribution without retaining or consulting a compiler object.
    /// </summary>
    /// <param name="sources">The detached physical and mapped source positions.</param>
    /// <returns>A transient graph location with the original source coordinates.</returns>
    internal Location Resolve(GeneratorSourceResolver sources) => sources.Resolve(this);
}
