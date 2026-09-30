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
internal sealed class PgTestDeclaration(IMethodSymbol method, string name, string functionName, string? schema, string? expectedError, string? ignoreReason)
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS023", "Invalid PostgreSQL test declaration", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Gets the original method whose body executes inside the backend.
    /// </summary>
    internal IMethodSymbol Method { get; } = method;

    /// <summary>
    /// Gets the stable SQL test function name.
    /// </summary>
    internal string FunctionName { get; } = functionName;

    /// <summary>
    /// Identifies test methods independently of whether this build enables their native exports.
    /// </summary>
    internal static bool IsTest(IMethodSymbol method)
        => method.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgTestAttribute");

    /// <summary>
    /// Validates the test shape, catalog ownership and report metadata without executing author code.
    /// </summary>
    internal static PgTestDeclaration? Create(IMethodSymbol method, SourceProductionContext context)
    {
        if (!method.IsStatic || method.IsAsync || method.IsGenericMethod || method.IsAbstract || !method.ReturnsVoid ||
            method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal) ||
            FunctionParameter.Create(method).Any(static parameter => !parameter.IsInjected || parameter.Symbol.RefKind != RefKind.None))
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
        return declaration is null ? null : new(method, name, sqlName, declaration.Schema, expected, ignored);

        PgTestDeclaration? Invalid(string message)
        {
            context.ReportDiagnostic(Diagnostic.Create(s_invalid, method.Locations.FirstOrDefault(), method.Name, message));
            return null;
        }
    }

    /// <summary>
    /// Emits immutable catalogs on their author-declared partial classes, independent of native test inclusion.
    /// </summary>
    internal static string EmitCatalogs(IEnumerable<PgTestDeclaration> tests)
    {
        var source = new StringBuilder("// <auto-generated />\n#nullable enable\n");
        foreach (IGrouping<INamedTypeSymbol, PgTestDeclaration> group in tests.GroupBy(static test => test.Method.ContainingType,
            (IEqualityComparer<INamedTypeSymbol>)SymbolEqualityComparer.Default))
        {
            bool namespaced = !group.Key.ContainingNamespace.IsGlobalNamespace;
            if (namespaced)
            {
                source.AppendLine("namespace " + group.Key.ContainingNamespace.ToDisplayString() + "\n{");
            }

            var containers = new Stack<INamedTypeSymbol>();
            for (INamedTypeSymbol? type = group.Key; type is not null; type = type.ContainingType)
            {
                containers.Push(type);
            }

            foreach (INamedTypeSymbol type in containers)
            {
                source.AppendLine((type.DeclaredAccessibility == Accessibility.Public ? "public " : "internal ") +
                    (type.IsStatic ? "static " : string.Empty) + "partial " + (type.IsRecord ? "record class" : "class") + " @" + type.Name + "\n{");
            }

            source.AppendLine("/// <summary>\n/// Supplies backend test metadata without initializing its declaring type.\n/// </summary>");
            source.AppendLine("public static class PostgresTests\n{");
            source.AppendLine("/// <summary>\n/// Gets the statically discovered PostgreSQL tests declared by this type.\n/// </summary>");
            source.AppendLine("public static global::System.Collections.Generic.IReadOnlyList<global::Ankus.PgTestCase> Cases { get; } =");
            source.AppendLine("global::System.Array.AsReadOnly(new global::Ankus.PgTestCase[]\n{");
            foreach (PgTestDeclaration test in group)
            {
                source.AppendLine("new global::Ankus.PgTestCase(" + test.Arguments() + "),");
            }

            source.AppendLine("});");
            source.AppendLine("}");
            foreach (INamedTypeSymbol _ in containers)
            {
                source.AppendLine("}");
            }

            if (namespaced)
            {
                source.AppendLine("}");
            }
        }

        return source.ToString();
    }

    private string Arguments() => string.Join(", ", new[] { name, schema, FunctionName, expectedError, ignoreReason }
        .Select(static value => value is null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true)));
}
