using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Selects finite managed worker entries and validates their native export contracts.
/// </summary>
/// <param name="Target">The fully qualified managed invocation target.</param>
/// <param name="EntryPoint">The exact exported native symbol.</param>
/// <param name="Callback">The assembly-specific managed dispatcher symbol.</param>
internal sealed record BackgroundWorkerDeclaration(string Target, string EntryPoint, string Callback)
{
    /// <summary>
    /// Preserves the common native export diagnostic for invalid names and current global collisions.
    /// </summary>
    private const string InvalidExport = "Worker exports must be unique ASCII C identifiers of at most 95 bytes, starting with a letter and not using reserved Ankus or PostgreSQL symbols.";

    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS022", "Invalid PostgreSQL background-worker entry", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: "https://willibrandon.github.io/ankus/background-workers/");

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
    /// Validates one normalized worker signature, container, metadata and portable native identity.
    /// </summary>
    /// <param name="method">The attributed method, normalized to its partial definition.</param>
    /// <param name="context">The current semantic diagnostic receiver.</param>
    /// <returns>The immutable invocation and export contracts, or null after an error.</returns>
    internal static BackgroundWorkerDeclaration? Create(IMethodSymbol method, GeneratorDiagnostics context)
    {
        if (method.MethodKind != MethodKind.Ordinary || !method.IsStatic || method.IsAsync || method.PartialImplementationPart?.IsAsync == true ||
            method.IsGenericMethod || method.IsAbstract || method.IsVirtual || method.IsExtern || !method.ReturnsVoid ||
            method.Parameters.Length != 1 || method.Parameters[0].RefKind != RefKind.None ||
            method.Parameters[0].Type.SpecialType != SpecialType.System_UIntPtr ||
            method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal) ||
            method.IsPartialDefinition && method.PartialImplementationPart is null)
        {
            return Invalid("A worker entry requires an accessible synchronous non-generic static void method with one by-value nuint argument and an implementation.");
        }

        for (INamedTypeSymbol? type = method.ContainingType; type is not null; type = type.ContainingType)
        {
            if (type.IsGenericType || type.IsFileLocal || type.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
            {
                return Invalid("Worker entries require accessible, non-generic, non-file-local containing types.");
            }
        }

        if (method.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() is
            "System.Diagnostics.ConditionalAttribute" or "System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute" or
            "Ankus.PgInitializeAttribute" or "Ankus.PgModuleLoadAttribute" or "Ankus.PgFunctionAttribute" or
            "Ankus.PgTriggerAttribute" or "Ankus.PgEventTriggerAttribute" or "Ankus.PgOperatorAttribute" or "Ankus.PgCastAttribute") ||
            method.GetReturnTypeAttributes().Any(static attribute => attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "Ankus") ||
            method.Parameters[0].GetAttributes().Any(static attribute => attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "Ankus"))
        {
            return Invalid("Worker entries cannot declare SQL metadata, initialization phases, Conditional or UnmanagedCallersOnly.");
        }

        AttributeData marker = method.GetAttributes().First(static attribute => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgBackgroundWorkerAttribute");
        string entry = marker.NamedArguments.FirstOrDefault(static pair => pair.Key == "EntryPoint").Value.Value as string ?? method.Name;
        if (entry.Length is 0 or > 95 || !IsLetter(entry[0]) ||
            entry.Any(static value => !IsLetter(value) && value is not (>= '0' and <= '9') && value != '_') ||
            s_reserved.Contains(entry) || entry.StartsWith("ankus_", StringComparison.Ordinal) ||
            entry.StartsWith("pg_finfo_", StringComparison.Ordinal))
        {
            return Invalid(InvalidExport);
        }

        return new(MethodInvocation.Create(method).Target, entry, PgFunctionGenerator.GetCallbackName(method, "worker"));

        BackgroundWorkerDeclaration? Invalid(string reason)
        {
            context.Report(s_invalid, method.Locations.FirstOrDefault(), method.Name, reason);
            return null;
        }
    }

    /// <summary>
    /// Reports a duplicate native identity only after the current extension-wide worker inventory is known.
    /// </summary>
    /// <param name="location">The current declaring source location.</param>
    /// <param name="name">The original managed method name.</param>
    /// <param name="context">The current production diagnostic receiver.</param>
    internal static void ReportDuplicate(Location? location, string name, GeneratorDiagnostics context)
        => context.Report(s_invalid, location, name, InvalidExport);

    /// <summary>
    /// Recognizes the portable ASCII letters admitted in an exported native symbol.
    /// </summary>
    private static bool IsLetter(char value) => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}
