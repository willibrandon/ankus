using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace Ankus.CodeFixes;

/// <summary>
/// Gives raw native operations an explicit lexical unsafe context without changing their implementation.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NativeUnsafeAccessCodeFixProvider))]
[Shared]
public sealed class NativeUnsafeAccessCodeFixProvider : CodeFixProvider
{
    /// <summary>
    /// Gets the raw-access diagnostic corrected by this provider.
    /// </summary>
    public override ImmutableArray<string> FixableDiagnosticIds => ["ANKUS129"];

    /// <summary>
    /// Gets Roslyn batch support for independent unsafe contexts.
    /// </summary>
    /// <returns>The standard batch provider.</returns>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <summary>
    /// Registers a correction only for the current runtime-owned native contract and a compilable context.
    /// </summary>
    /// <param name="context">The editable document, diagnostics and cancellation token.</param>
    /// <returns>The registration operation.</returns>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        SemanticModel? model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return;
        }

        SyntaxNode expression = root.FindNode(context.Span, getInnermostNodeForTie: true);
        ISymbol? symbol = model.GetSymbolInfo(expression, context.CancellationToken).Symbol;
        INamedTypeSymbol? attribute = model.Compilation.GetTypeByMetadataName("Ankus.CompilerServices.NativeUnsafeAccessAttribute");
        if (attribute?.ContainingAssembly.Name != "Ankus.Runtime" || symbol is null ||
            !symbol.GetAttributes().Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attribute)) ||
            expression.AncestorsAndSelf().Any(static node => node is UnsafeStatementSyntax ||
                node is MemberDeclarationSyntax member && member.Modifiers.Any(SyntaxKind.UnsafeKeyword) ||
                node is LocalFunctionStatementSyntax local && local.Modifiers.Any(SyntaxKind.UnsafeKeyword)) ||
            IsMetadataReference(model.GetOperation(expression, context.CancellationToken)))
        {
            return;
        }

        SyntaxNode? owner = expression.AncestorsAndSelf().FirstOrDefault(static node =>
            node is BlockSyntax or ArrowExpressionClauseSyntax or LambdaExpressionSyntax);
        SyntaxNode? replacement = owner switch
        {
            BlockSyntax block => block.WithStatements(SyntaxFactory.SingletonList<StatementSyntax>(
                SyntaxFactory.UnsafeStatement(SyntaxFactory.Block(block.Statements)))),
            ArrowExpressionClauseSyntax arrow => ConvertArrow(arrow, model, context.CancellationToken),
            LambdaExpressionSyntax lambda when lambda.Body is ExpressionSyntax body => ConvertLambda(lambda, body, model, context.CancellationToken),
            _ => null,
        };
        SyntaxNode? target = owner is ArrowExpressionClauseSyntax ? owner.Parent : owner;
        if (replacement is null || target is null)
        {
            return;
        }

        Document corrected = context.Document.WithSyntaxRoot(root.ReplaceNode(target,
            replacement.WithAdditionalAnnotations(Formatter.Annotation)));
        Compilation? output = await corrected.Project.GetCompilationAsync(context.CancellationToken).ConfigureAwait(false);
        if (output is null || IntroducesCompilerErrors(model.Compilation, output, context.CancellationToken))
        {
            return;
        }

        foreach (Diagnostic diagnostic in context.Diagnostics.Where(static value => value.Id == "ANKUS129"))
        {
            context.RegisterCodeFix(CodeAction.Create("Use an unsafe block", token =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(corrected);
            },
                nameof(NativeUnsafeAccessCodeFixProvider)), diagnostic);
        }
    }

    /// <summary>
    /// Changes an expression body to an equivalent unsafe block, preserving return and throw semantics.
    /// </summary>
    /// <param name="arrow">The original expression body.</param>
    /// <param name="model">The bound declaration and return contract.</param>
    /// <param name="cancellationToken">The editor cancellation token.</param>
    /// <returns>The corrected declaration, or null when no declaration can be converted.</returns>
    private static SyntaxNode? ConvertArrow(ArrowExpressionClauseSyntax arrow, SemanticModel model, CancellationToken cancellationToken)
    {
        SyntaxNode declaration = arrow.Parent!;
        ISymbol? symbol = model.GetDeclaredSymbol(declaration, cancellationToken);
        bool returnsValue = symbol is IPropertySymbol || symbol is IMethodSymbol { ReturnsVoid: false };
        BlockSyntax body = CreateBody(arrow.Expression, returnsValue);
        StatementSyntax statement = body.Statements[0];
        body = body.ReplaceNode(statement, statement.WithLeadingTrivia(arrow.ArrowToken.LeadingTrivia
            .AddRange(arrow.ArrowToken.TrailingTrivia).AddRange(statement.GetLeadingTrivia())));
        SyntaxToken semicolon = declaration.GetLastToken();
        body = body.WithCloseBraceToken(body.CloseBraceToken.WithTrailingTrivia(semicolon.TrailingTrivia));
        return declaration switch
        {
            MethodDeclarationSyntax method => method.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
            LocalFunctionStatementSyntax local => local.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
            ConstructorDeclarationSyntax constructor => constructor.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
            DestructorDeclarationSyntax destructor => destructor.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
            OperatorDeclarationSyntax operation => operation.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
            ConversionOperatorDeclarationSyntax conversion => conversion.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
            AccessorDeclarationSyntax accessor => accessor.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
            PropertyDeclarationSyntax property => property.WithExpressionBody(null).WithSemicolonToken(default)
                .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithBody(body)))),
            IndexerDeclarationSyntax indexer => indexer.WithExpressionBody(null).WithSemicolonToken(default)
                .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithBody(body)))),
            _ => null,
        };
    }

    /// <summary>
    /// Preserves the delegate's value or void return contract when changing a lambda body.
    /// </summary>
    /// <param name="lambda">The bound lambda expression.</param>
    /// <param name="expression">Its original expression body.</param>
    /// <param name="model">The current semantic model.</param>
    /// <param name="cancellationToken">The editor cancellation token.</param>
    /// <returns>The corrected lambda, or null for an incompatible conversion.</returns>
    private static SyntaxNode? ConvertLambda(LambdaExpressionSyntax lambda, ExpressionSyntax expression,
        SemanticModel model, CancellationToken cancellationToken)
    {
        if (model.GetTypeInfo(lambda, cancellationToken).ConvertedType is not INamedTypeSymbol { TypeKind: TypeKind.Delegate } type ||
            type.DelegateInvokeMethod is not { } invoke)
        {
            return null;
        }

        BlockSyntax body = CreateBody(expression, !invoke.ReturnsVoid);
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.WithBody(body),
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.WithBody(body),
            _ => null,
        };
    }

    /// <summary>
    /// Keeps the original expression and its evaluation order inside a single native-contract acknowledgment.
    /// </summary>
    /// <param name="expression">The unchanged expression to execute.</param>
    /// <param name="returnsValue">Whether the surrounding callable returns a value.</param>
    /// <returns>The new body containing an explicit unsafe statement.</returns>
    private static BlockSyntax CreateBody(ExpressionSyntax expression, bool returnsValue)
    {
        StatementSyntax statement = expression is ThrowExpressionSyntax thrown
            ? SyntaxFactory.ThrowStatement(thrown.Expression).WithTriviaFrom(expression)
            : returnsValue ? SyntaxFactory.ReturnStatement(expression) : SyntaxFactory.ExpressionStatement(expression);
        return SyntaxFactory.Block(SyntaxFactory.UnsafeStatement(SyntaxFactory.Block(statement)));
    }

    /// <summary>
    /// Excludes metadata-only references that do not invoke or access native storage.
    /// </summary>
    /// <param name="operation">The operation at the diagnostic's current location.</param>
    /// <returns>Whether a nameof operation contains the reference.</returns>
    private static bool IsMetadataReference(IOperation? operation)
    {
        for (IOperation? current = operation; current is not null; current = current.Parent)
        {
            if (current.Kind == OperationKind.NameOf)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Refuses invalid unsafe regions while allowing unrelated compiler errors to remain available for their own corrections.
    /// </summary>
    /// <param name="original">The current compilation, which may contain unrelated errors.</param>
    /// <param name="corrected">The compilation after the proposed edit.</param>
    /// <param name="cancellationToken">The editor cancellation token.</param>
    /// <returns>Whether the proposed context introduces a compiler error.</returns>
    private static bool IntroducesCompilerErrors(Compilation original, Compilation corrected, CancellationToken cancellationToken)
    {
        Dictionary<(string Id, string Message), int> allowed = original.GetDiagnostics(cancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .GroupBy(static diagnostic => (diagnostic.Id, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)))
            .ToDictionary(static group => group.Key, static group => group.Count());
        foreach (Diagnostic diagnostic in corrected.GetDiagnostics(cancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            (string Id, string Message) key = (diagnostic.Id, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
            if (!allowed.TryGetValue(key, out int count) || count == 0)
            {
                return true;
            }

            allowed[key] = count - 1;
        }

        return false;
    }
}
