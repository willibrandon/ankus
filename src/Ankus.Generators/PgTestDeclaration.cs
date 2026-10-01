using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.Generators;

/// <summary>
/// Validates backend test declarations and emits finite catalogs for ordinary host test discovery.
/// </summary>
/// <param name="Owner">The detached lexical owner of the host discovery catalog.</param>
/// <param name="Case">The exact immutable discovery metadata.</param>
/// <param name="Order">The managed display key used for deterministic catalog ordering.</param>
/// <param name="Function">The validated native test's SQL declaration.</param>
internal sealed record PgTestDeclaration(PgTestCatalogModel.Owner Owner, PgTestCatalogModel.Case Case, string Order, FunctionDeclaration Function)
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS023", "Invalid PostgreSQL test declaration", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Identifies test methods independently of whether this build enables their native exports.
    /// </summary>
    internal static bool IsTest(IMethodSymbol method)
        => method.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgTestAttribute");

    /// <summary>
    /// Validates the test shape, catalog ownership and report metadata without executing author code.
    /// </summary>
    /// <param name="method">The attributed backend test method.</param>
    /// <param name="context">The current diagnostic destination.</param>
    /// <returns>The detached discovery and SQL contracts, or null after a validation failure.</returns>
    internal static PgTestDeclaration? Create(IMethodSymbol method, GeneratorDiagnostics context)
    {
        if (!method.IsStatic || method.IsAsync || method.IsGenericMethod || method.IsAbstract || !method.ReturnsVoid ||
            method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal) ||
            FunctionParameter.Create(method).Any(static parameter => !parameter.IsInjected || parameter.RefKind != RefKind.None))
        {
            return Invalid("PgTest requires an accessible synchronous static void method with no SQL arguments.");
        }

        foreach (AttributeData attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() is "Ankus.PgFunctionAttribute" or "Ankus.PgOperatorAttribute" or
                "Ankus.PgCastAttribute" or "Ankus.PgTriggerAttribute" or "Ankus.PgEventTriggerAttribute" or
                "Ankus.PgInitializeAttribute" or "Ankus.PgModuleLoadAttribute" or "Ankus.PgBackgroundWorkerAttribute")
            {
                return Invalid("PgTest cannot also declare a production export or initialization callback.");
            }
        }

        for (INamedTypeSymbol? type = method.ContainingType; type is not null; type = type.ContainingType)
        {
            if (type.TypeKind != TypeKind.Class || type.IsGenericType || type.IsFileLocal ||
                type.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal) ||
                !type.DeclaringSyntaxReferences.All(static reference => reference.GetSyntax() is TypeDeclarationSyntax declaration &&
                    declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
            {
                return Invalid("Backend tests require accessible, nongeneric partial classes, including every containing type.");
            }
        }

        if (method.ContainingType.Name == "PostgresTests")
        {
            return Invalid("PostgresTests is reserved for the generated backend test catalog.");
        }

        for (INamedTypeSymbol? type = method.ContainingType; type is not null; type = type.BaseType)
        {
            if (type.GetMembers("PostgresTests").Length != 0)
            {
                return Invalid("PostgresTests is reserved for the generated backend test catalog.");
            }
        }

        AttributeData test = method.GetAttributes().First(static attribute => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgTestAttribute");
        string? expected = test.NamedArguments.FirstOrDefault(static argument => argument.Key == "ExpectedError").Value.Value as string;
        string? ignored = test.NamedArguments.FirstOrDefault(static argument => argument.Key == "IgnoreReason").Value.Value as string;
        if (expected is not null && !SqlText.IsText(expected) ||
            ignored is not null && (string.IsNullOrWhiteSpace(ignored) || !SqlText.IsText(ignored)))
        {
            return Invalid("ExpectedError and IgnoreReason must contain valid Unicode without zero characters; an ignore reason cannot be empty.");
        }

        string name = method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        using SHA256 hash = SHA256.Create();
        byte[] digest = hash.ComputeHash(Encoding.UTF8.GetBytes(method.ContainingAssembly.Name + ":" + name));
        string sqlName = "ankus_test_" + string.Concat(digest.Take(16).Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
        FunctionDeclaration? declaration = FunctionDeclaration.Create(method, sqlName, context);
        return declaration is null ? null : new(PgTestCatalogModel.Owner.Create(method.ContainingType),
            new(name, sqlName, declaration.Schema, expected, ignored), method.ToDisplayString(), declaration);

        PgTestDeclaration? Invalid(string message)
        {
            context.Report(s_invalid, method.Locations.FirstOrDefault(), method.Name, message);
            return null;
        }
    }

    /// <summary>
    /// Emits immutable catalogs on their author-declared partial classes, independent of native test inclusion.
    /// </summary>
    /// <param name="catalog">The ordered metadata belonging to one lexical owner.</param>
    /// <returns>The owner's catalog source without the shared generated-file header.</returns>
    internal static string EmitCatalog(PgTestCatalogModel catalog)
    {
        var source = new StringBuilder();
        bool namespaced = catalog.Container.Namespace is not null;
        if (namespaced)
        {
            source.AppendLine("namespace " + catalog.Container.Namespace + "\n{");
        }

        foreach (string container in catalog.Container.Declarations)
        {
            source.AppendLine(container + "\n{");
        }

        source.AppendLine("/// <summary>\n/// Supplies backend test metadata without initializing its declaring type.\n/// </summary>");
        source.AppendLine("public static class PostgresTests\n{");
        source.AppendLine("/// <summary>\n/// Gets the statically discovered PostgreSQL tests declared by this type.\n/// </summary>");
        source.AppendLine("public static global::System.Collections.Generic.IReadOnlyList<global::Ankus.PgTestCase> Cases { get; } =");
        source.AppendLine("global::System.Array.AsReadOnly(new global::Ankus.PgTestCase[]\n{");
        foreach (PgTestCatalogModel.Case test in catalog.Cases)
        {
            source.AppendLine("new global::Ankus.PgTestCase(" + string.Join(", ", new[]
                { test.Name, test.Schema, test.FunctionName, test.ExpectedError, test.IgnoreReason }
                .Select(static value => value is null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true))) + "),");
        }

        source.AppendLine("});");
        source.AppendLine("}");
        foreach (string _ in catalog.Container.Declarations)
        {
            source.AppendLine("}");
        }

        if (namespaced)
        {
            source.AppendLine("}");
        }

        return source.ToString();
    }
}
