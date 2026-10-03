using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

namespace Ankus.CodeFixes;

/// <summary>
/// Adds a missing typed combine capability when the existing callback implements its exact contract.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AggregateCombineCodeFixProvider))]
[Shared]
public sealed class AggregateCombineCodeFixProvider : CodeFixProvider
{
    /// <summary>
    /// Gets the diagnostic for an aggregate callback without its capability.
    /// </summary>
    public override ImmutableArray<string> FixableDiagnosticIds => ["ANKUS111"];

    /// <summary>
    /// Gets batch support for independent aggregate declarations.
    /// </summary>
    /// <returns>The standard Roslyn batch provider.</returns>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <summary>
    /// Registers compiler-verified additions of the existing callback's combine contract.
    /// </summary>
    /// <param name="context">The current document and diagnostic context.</param>
    /// <returns>The registration operation.</returns>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        SemanticModel? model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null ||
            root.FindNode(context.Span, getInnermostNodeForTie: true).FirstAncestorOrSelf<MethodDeclarationSyntax>() is not { } declaration ||
            model.GetDeclaredSymbol(declaration, context.CancellationToken) is not { Name: "Combine" } method ||
            model.Compilation.GetTypeByMetadataName("Ankus.IPgCombinableAggregate`1") is not { } definition ||
            !IsRuntimeType(definition, "IPgCombinableAggregate", 1))
        {
            return;
        }

        Solution? corrected = await AddCapabilitiesAsync(context.Document.Project, method, definition, context.CancellationToken).ConfigureAwait(false);
        if (corrected is null)
        {
            return;
        }

        CodeAction action = CodeAction.Create("Add missing typed aggregate combine interfaces", token =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(corrected);
        }, nameof(AggregateCombineCodeFixProvider));
        context.RegisterCodeFix(action, [.. context.Diagnostics.Where(static diagnostic => diagnostic.Id == "ANKUS111")]);
    }

    /// <summary>
    /// Finds the attributed users of the diagnosed callback and validates the complete immutable correction with the compiler.
    /// </summary>
    /// <param name="project">The current editable project.</param>
    /// <param name="diagnosed">The original callback, possibly declared by a generic base class.</param>
    /// <param name="definition">The actual runtime combine interface definition.</param>
    /// <param name="cancellationToken">The editor cancellation token.</param>
    /// <returns>The verified solution, or null when no valid correction exists.</returns>
    private static async Task<Solution?> AddCapabilitiesAsync(Project project, IMethodSymbol diagnosed, INamedTypeSymbol definition,
        CancellationToken cancellationToken)
    {
        Compilation? compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (compilation is null)
        {
            return null;
        }

        var corrections = new List<(DocumentId Document, TypeDeclarationSyntax Declaration, INamedTypeSymbol Capability, string Identity, string Implementation)>();
        foreach (INamedTypeSymbol type in Types(compilation.Assembly.GlobalNamespace))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!type.GetAttributes().Any(static attribute => attribute.AttributeClass is { } name && IsRuntimeType(name, "PgAggregateAttribute", 0)) ||
                type.AllInterfaces.Any(static contract => IsRuntimeType(contract, "IPgCombinableAggregate", 1)))
            {
                continue;
            }

            INamedTypeSymbol[] contracts = [.. type.AllInterfaces.Where(static contract => IsRuntimeType(contract, "IPgAggregate", 2))];
            IMethodSymbol? implementation = VisibleCombine(type, compilation);
            if (contracts.Length != 1 || implementation is null ||
                !SymbolEqualityComparer.Default.Equals(implementation.OriginalDefinition, diagnosed.OriginalDefinition) ||
                !MatchesContract(implementation, contracts[0].TypeArguments[0]))
            {
                continue;
            }

            TypeDeclarationSyntax? declaration = type.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(cancellationToken)).OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (declaration is null || project.GetDocument(declaration.SyntaxTree) is not { } document ||
                type.GetDocumentationCommentId() is not { } identity ||
                implementation.OriginalDefinition.GetDocumentationCommentId() is not { } implementationIdentity)
            {
                continue;
            }

            corrections.Add((document.Id, declaration, definition.Construct(contracts[0].TypeArguments[0]), identity, implementationIdentity));
        }

        if (corrections.Count == 0)
        {
            return null;
        }

        Solution solution = project.Solution;
        SymbolDisplayFormat display = SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
        foreach (IGrouping<DocumentId, (DocumentId Document, TypeDeclarationSyntax Declaration, INamedTypeSymbol Capability, string Identity, string Implementation)> group
            in corrections.GroupBy(static correction => correction.Document))
        {
            Document document = solution.GetDocument(group.Key)!;
            SyntaxNode documentRoot = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            Dictionary<TypeDeclarationSyntax, INamedTypeSymbol> interfaces = group.ToDictionary(static correction => correction.Declaration,
                static correction => correction.Capability);
            SyntaxNode correctedRoot = documentRoot.ReplaceNodes(interfaces.Keys, (original, rewritten) =>
                rewritten.AddBaseListTypes(SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(interfaces[original].ToDisplayString(display)))
                    .WithAdditionalAnnotations(Simplifier.Annotation, Formatter.Annotation)));
            solution = solution.WithDocumentSyntaxRoot(group.Key, correctedRoot);
        }

        Compilation? corrected = await solution.GetProject(project.Id)!.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (corrected is null || corrected.GetDiagnostics(cancellationToken).Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return null;
        }

        foreach ((DocumentId _, TypeDeclarationSyntax _, INamedTypeSymbol _, string identity, string implementation) in corrections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DocumentationCommentId.GetFirstSymbolForDeclarationId(identity, corrected) is not INamedTypeSymbol type ||
                type.AllInterfaces.SingleOrDefault(static contract => IsRuntimeType(contract, "IPgCombinableAggregate", 1)) is not { } capability ||
                capability.GetMembers("Combine").SingleOrDefault() is not { } member ||
                type.FindImplementationForInterfaceMember(member) is not IMethodSymbol selected ||
                selected.OriginalDefinition.GetDocumentationCommentId() != implementation)
            {
                return null;
            }
        }

        return solution;
    }

    /// <summary>
    /// Enumerates source-assembly types without loading metadata assemblies or inspecting method bodies.
    /// </summary>
    /// <param name="owner">A source namespace or containing type.</param>
    /// <returns>All contained named types, including nested aggregate declarations.</returns>
    private static IEnumerable<INamedTypeSymbol> Types(INamespaceOrTypeSymbol owner)
    {
        foreach (ISymbol member in owner.GetMembers())
        {
            if (member is INamedTypeSymbol type)
            {
                yield return type;
                foreach (INamedTypeSymbol nested in Types(type))
                {
                    yield return nested;
                }
            }
            else if (member is INamespaceSymbol space)
            {
                foreach (INamedTypeSymbol nested in Types(space))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>
    /// Resolves the same declared or visible inherited role that receives the generator diagnostic.
    /// </summary>
    /// <param name="type">The attributed aggregate.</param>
    /// <param name="compilation">The current accessibility context.</param>
    /// <returns>The reserved callback, or null when none is visible.</returns>
    private static IMethodSymbol? VisibleCombine(INamedTypeSymbol type, Compilation compilation)
    {
        for (INamedTypeSymbol? owner = type; owner is not null; owner = owner.BaseType)
        {
            IMethodSymbol? method = owner.GetMembers("Combine").OfType<IMethodSymbol>().FirstOrDefault(candidate =>
                SymbolEqualityComparer.Default.Equals(owner, type) || compilation.IsSymbolAccessibleWithin(candidate, type));
            if (method is not null)
            {
                return method;
            }
        }

        return null;
    }

    /// <summary>
    /// Requires the compiler's exact state and NULL contract without modifying callback signatures or access.
    /// </summary>
    /// <param name="method">The actual callback after any inherited generic substitution.</param>
    /// <param name="state">The declared aggregate state.</param>
    /// <returns>Whether the callback can implement the combine interface unchanged.</returns>
    private static bool MatchesContract(IMethodSymbol method, ITypeSymbol state)
        => method is { IsStatic: true, IsAbstract: false, IsAsync: false, IsGenericMethod: false,
            DeclaredAccessibility: Accessibility.Public, MethodKind: MethodKind.Ordinary, ReturnsByRef: false, ReturnsByRefReadonly: false } &&
            method.Parameters.Length == 3 && method.Parameters.All(static parameter => parameter.RefKind == RefKind.None && !parameter.IsOptional && !parameter.IsParams) &&
            method.Parameters[0].Type is INamedTypeSymbol context && IsRuntimeType(context, "PgAggregateContext", 0) &&
            method.Parameters[0].GetAttributes().IsEmpty &&
            SymbolEqualityComparer.IncludeNullability.Equals(state, method.ReturnType) &&
            SymbolEqualityComparer.IncludeNullability.Equals(state, method.Parameters[1].Type) &&
            SymbolEqualityComparer.IncludeNullability.Equals(state, method.Parameters[2].Type);

    /// <summary>
    /// Matches the runtime's actual declarations without accepting similarly named consumer types.
    /// </summary>
    /// <param name="type">The bound symbol.</param>
    /// <param name="name">The expected type name.</param>
    /// <param name="arity">The expected generic arity.</param>
    /// <returns>Whether the symbol belongs to the Ankus runtime contract.</returns>
    private static bool IsRuntimeType(INamedTypeSymbol type, string name, int arity)
        => type.Name == name && type.Arity == arity && type.ContainingType is null &&
            type.ContainingNamespace.ToDisplayString() == "Ankus" && type.ContainingAssembly.Name == "Ankus.Runtime";
}
