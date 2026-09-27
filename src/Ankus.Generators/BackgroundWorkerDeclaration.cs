using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Selects finite managed worker entries and validates their native export contracts.
/// </summary>
/// <param name="method">The accessible synchronous managed handler.</param>
/// <param name="entryPoint">The exact exported native symbol.</param>
internal sealed class BackgroundWorkerDeclaration(IMethodSymbol method, string entryPoint)
{
    /// <summary>
    /// Gets the accessible synchronous managed handler.
    /// </summary>
    internal IMethodSymbol Method { get; } = method;

    /// <summary>
    /// Gets the exact exported native symbol.
    /// </summary>
    internal string EntryPoint { get; } = entryPoint;

    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS022", "Invalid PostgreSQL background-worker entry", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly HashSet<string> s_reserved = new(StringComparer.Ordinal)
    {
        "auto", "break", "case", "char", "const", "continue", "default", "do", "double", "else", "enum", "extern",
        "float", "for", "goto", "if", "inline", "int", "long", "register", "restrict", "return", "short", "signed",
        "sizeof", "static", "struct", "switch", "typedef", "union", "unsigned", "void", "volatile", "while", "bool",
        "true", "false", "alignas", "alignof", "constexpr", "nullptr", "static_assert", "thread_local", "typeof",
        "typeof_unqual", "Pg_magic_func", "_PG_init", "_PG_fini",
    };

    /// <summary>
    /// Identifies a worker independently of SQL function discovery.
    /// </summary>
    internal static bool IsWorker(IMethodSymbol method) => method.GetAttributes().Any(static attribute =>
        attribute.AttributeClass?.ToDisplayString() == "Ankus.PgBackgroundWorkerAttribute");

    /// <summary>
    /// Validates signatures, accessible containers, metadata and distinct native exports.
    /// </summary>
    internal static List<BackgroundWorkerDeclaration> Select(IEnumerable<IMethodSymbol> methods, SourceProductionContext context)
    {
        var declarations = new List<BackgroundWorkerDeclaration>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (IMethodSymbol method in methods.Where(IsWorker).Select(static method => method.PartialDefinitionPart ?? method)
            .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default).OrderBy(static method => method.ToDisplayString(), StringComparer.Ordinal))
        {
            if (method.MethodKind != MethodKind.Ordinary || !method.IsStatic || method.IsAsync || method.PartialImplementationPart?.IsAsync == true ||
                method.IsGenericMethod || method.IsAbstract || method.IsVirtual || method.IsExtern || !method.ReturnsVoid ||
                method.Parameters.Length != 1 || method.Parameters[0].RefKind != RefKind.None ||
                method.Parameters[0].Type.SpecialType != SpecialType.System_UIntPtr ||
                method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal) ||
                method.IsPartialDefinition && method.PartialImplementationPart is null)
            {
                Invalid("A worker entry requires an accessible synchronous non-generic static void method with one by-value nuint argument and an implementation.");
                continue;
            }

            bool valid = true;
            for (INamedTypeSymbol? type = method.ContainingType; type is not null; type = type.ContainingType)
            {
                if (type.IsGenericType || type.IsFileLocal || type.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
                {
                    valid = false;
                    Invalid("Worker entries require accessible, non-generic, non-file-local containing types.");
                    break;
                }
            }

            if (!valid)
            {
                continue;
            }

            if (method.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() is
                "System.Diagnostics.ConditionalAttribute" or "System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute" or
                "Ankus.PgInitializeAttribute" or "Ankus.PgModuleLoadAttribute" or "Ankus.PgFunctionAttribute" or
                "Ankus.PgTriggerAttribute" or "Ankus.PgEventTriggerAttribute" or "Ankus.PgOperatorAttribute" or "Ankus.PgCastAttribute") ||
                method.GetReturnTypeAttributes().Any(static attribute => attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "Ankus") ||
                method.Parameters[0].GetAttributes().Any(static attribute => attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "Ankus"))
            {
                Invalid("Worker entries cannot declare SQL metadata, initialization phases, Conditional or UnmanagedCallersOnly.");
                continue;
            }

            AttributeData marker = method.GetAttributes().First(static attribute => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgBackgroundWorkerAttribute");
            string entry = marker.NamedArguments.FirstOrDefault(static pair => pair.Key == "EntryPoint").Value.Value as string ?? method.Name;
            if (entry.Length is 0 or > 95 || !IsLetter(entry[0]) ||
                entry.Any(static value => !IsLetter(value) && value is not (>= '0' and <= '9') && value != '_') ||
                s_reserved.Contains(entry) || entry.StartsWith("ankus_", StringComparison.Ordinal) ||
                entry.StartsWith("pg_finfo_", StringComparison.Ordinal) || !names.Add(entry))
            {
                Invalid("Worker exports must be unique ASCII C identifiers of at most 95 bytes, starting with a letter and not using reserved Ankus or PostgreSQL symbols.");
                continue;
            }

            declarations.Add(new(method, entry));

            void Invalid(string reason) => context.ReportDiagnostic(Diagnostic.Create(s_invalid, method.Locations.FirstOrDefault(), method.Name, reason));
        }

        return declarations;
    }

    /// <summary>
    /// Recognizes the portable ASCII letters admitted in an exported native symbol.
    /// </summary>
    private static bool IsLetter(char value) => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}
