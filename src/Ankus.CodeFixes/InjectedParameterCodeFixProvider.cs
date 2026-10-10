using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.CodeFixes;

/// <summary>
/// Removes SQL-only parameter metadata from injected PostgreSQL invocation contexts.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InjectedParameterCodeFixProvider))]
[Shared]
public sealed class InjectedParameterCodeFixProvider : CodeFixProvider
{
    /// <summary>
    /// Gets the injected-parameter diagnostic corrected by this provider.
    /// </summary>
    public override ImmutableArray<string> FixableDiagnosticIds => ["ANKUS056"];

    /// <summary>
    /// Gets batch support for independent parameter corrections.
    /// </summary>
    /// <returns>The standard Roslyn batch provider.</returns>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <summary>
    /// Registers a semantic correction for the current parameter declaration.
    /// </summary>
    /// <param name="context">The document, diagnostics and cancellation context.</param>
    /// <returns>The registration operation.</returns>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        SemanticModel? model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null ||
            root.FindNode(context.Span, getInnermostNodeForTie: true).FirstAncestorOrSelf<AttributeSyntax>() is not { } attribute ||
            attribute.Parent?.Parent is not ParameterSyntax parameter ||
            model.GetDeclaredSymbol(parameter, context.CancellationToken) is not IParameterSymbol symbol ||
            symbol.RefKind != RefKind.None ||
            !IsRuntimeType(symbol.Type, "PgFunctionContext") && !IsRuntimeType(symbol.Type, "PgMemoryContext") ||
            !IsSqlMetadata(attribute, model, context.CancellationToken))
        {
            return;
        }

        ImmutableArray<AttributeSyntax> metadata = [.. parameter.AttributeLists.SelectMany(static list => list.Attributes)
            .Where(candidate => IsSqlMetadata(candidate, model, context.CancellationToken))];
        foreach (Diagnostic diagnostic in context.Diagnostics.Where(static value => value.Id == "ANKUS056"))
        {
            context.RegisterCodeFix(CodeAction.Create("Remove SQL metadata from injected context",
                token => RemoveMetadataAsync(context.Document, metadata, token), nameof(InjectedParameterCodeFixProvider)), diagnostic);
        }
    }

    /// <summary>
    /// Resolves an attribute against the actual runtime type, including qualified and aliased syntax.
    /// </summary>
    /// <param name="attribute">The authored attribute.</param>
    /// <param name="model">The current semantic model.</param>
    /// <param name="cancellationToken">The editor cancellation token.</param>
    /// <returns>Whether this is Ankus SQL parameter metadata.</returns>
    private static bool IsSqlMetadata(AttributeSyntax attribute, SemanticModel model, CancellationToken cancellationToken)
        => model.GetSymbolInfo(attribute, cancellationToken).Symbol is IMethodSymbol constructor &&
            IsRuntimeType(constructor.ContainingType, "PgParameterAttribute");

    /// <summary>
    /// Matches a concrete top-level runtime type without confusing another assembly's similarly named declaration.
    /// </summary>
    /// <param name="type">The bound type.</param>
    /// <param name="name">The expected metadata name.</param>
    /// <returns>Whether the symbol is the expected Ankus runtime type.</returns>
    private static bool IsRuntimeType(ITypeSymbol type, string name)
        => type is INamedTypeSymbol { Arity: 0, ContainingType: null } named && named.Name == name &&
            named.ContainingNamespace.ToDisplayString() == "Ankus" && named.ContainingAssembly.Name == "Ankus.Runtime";

    /// <summary>
    /// Removes the bound metadata and keeps every other attribute, list and comment, with no whitespace left inside
    /// brackets or doubled where a list disappeared.
    /// </summary>
    /// <param name="document">The immutable document snapshot for the action.</param>
    /// <param name="metadata">The actual SQL attributes belonging to the injected parameter.</param>
    /// <param name="cancellationToken">The editor cancellation token.</param>
    /// <returns>The corrected document.</returns>
    private static async Task<Document> RemoveMetadataAsync(Document document, ImmutableArray<AttributeSyntax> metadata,
        CancellationToken cancellationToken)
    {
        SyntaxNode root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        var parameter = (ParameterSyntax)metadata[0].Parent!.Parent!;
        var removed = new HashSet<AttributeSyntax>(metadata);
        var lists = new List<AttributeListSyntax>();
        SyntaxTriviaList carried = default;
        foreach (AttributeListSyntax list in parameter.AttributeLists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AttributeSyntax[] kept = [.. list.Attributes.Where(attribute => !removed.Contains(attribute))];
            if (kept.Length == 0)
            {
                // The comments around a removed list stay, ahead of whatever follows it.
                carried = Collapse(carried.AddRange(list.GetLeadingTrivia()).AddRange(list.GetTrailingTrivia()));
                continue;
            }

            AttributeListSyntax retained = kept.Length == list.Attributes.Count ? list : list.WithAttributes(SyntaxFactory.SeparatedList(kept,
                Enumerable.Repeat(SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space), kept.Length - 1)));
            lists.Add(retained.WithLeadingTrivia(Collapse(carried.AddRange(retained.GetLeadingTrivia()))));
            carried = default;
        }

        ParameterSyntax repaired = parameter.WithAttributeLists(SyntaxFactory.List(lists));
        if (carried.Count != 0)
        {
            // The preceding comma or parenthesis may already end with a space.
            SyntaxTriviaList preceding = parameter.GetFirstToken().GetPreviousToken().TrailingTrivia;
            if (preceding.Count != 0 && preceding[preceding.Count - 1].IsKind(SyntaxKind.WhitespaceTrivia) &&
                carried[0].IsKind(SyntaxKind.WhitespaceTrivia))
            {
                carried = carried.RemoveAt(0);
            }

            SyntaxToken first = repaired.GetFirstToken();
            repaired = repaired.ReplaceToken(first, first.WithLeadingTrivia(Collapse(carried.AddRange(first.LeadingTrivia))));
        }

        return document.WithSyntaxRoot(root.ReplaceNode(parameter, repaired));
    }

    /// <summary>
    /// Replaces each run of adjacent spaces from joined trivia with a single space, leaving line breaks and comments.
    /// </summary>
    /// <param name="trivia">The joined trivia.</param>
    /// <returns>The trivia without doubled whitespace.</returns>
    private static SyntaxTriviaList Collapse(SyntaxTriviaList trivia)
    {
        var result = new List<SyntaxTrivia>(trivia.Count);
        foreach (SyntaxTrivia item in trivia)
        {
            if (item.IsKind(SyntaxKind.WhitespaceTrivia) && result.Count != 0 && result[result.Count - 1].IsKind(SyntaxKind.WhitespaceTrivia))
            {
                continue;
            }

            result.Add(item);
        }

        return SyntaxFactory.TriviaList(result);
    }
}
