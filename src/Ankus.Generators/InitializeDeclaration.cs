using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Selects and validates the assembly's managed library initialization callback.
/// </summary>
internal static class InitializeDeclaration
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS013", "Invalid PostgreSQL initialization declaration", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Identifies initialization callbacks independently of SQL function discovery.
    /// </summary>
    /// <param name="method">The attributed method.</param>
    /// <returns>Whether the initialization marker is present.</returns>
    internal static bool IsInitializer(IMethodSymbol method)
        => method.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() is
            "Ankus.PgInitializeAttribute" or "Ankus.PgModuleLoadAttribute");

    /// <summary>
    /// Validates every initialization declaration and selects at most one callback for each phase.
    /// </summary>
    /// <param name="methods">The discovered attributed methods.</param>
    /// <param name="context">The generator context receiving diagnostics.</param>
    /// <returns>The initialization and module-load callbacks, or no callbacks when declarations are invalid.</returns>
    internal static (IMethodSymbol? Initialize, IMethodSymbol? ModuleLoad) Select(IEnumerable<IMethodSymbol> methods, SourceProductionContext context)
    {
        IMethodSymbol[] initializers = [.. methods.Where(IsInitializer).Select(static method => method.PartialDefinitionPart ?? method)
            .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default).OrderBy(static method => method.ToDisplayString(), StringComparer.Ordinal)];
        bool valid = true;
        foreach (IMethodSymbol method in initializers)
        {
            valid &= Validate(method, context);
        }

        IMethodSymbol[] ready = [.. initializers.Where(static method => HasAttribute(method, "Ankus.PgInitializeAttribute"))];
        IMethodSymbol[] load = [.. initializers.Where(static method => HasAttribute(method, "Ankus.PgModuleLoadAttribute"))];
        foreach ((IMethodSymbol[] declarations, string attribute) in new[] { (ready, "PgInitialize"), (load, "PgModuleLoad") })
        {
            if (declarations.Length > 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(s_invalid, declarations[1].Locations.FirstOrDefault(), declarations[1].Name,
                    $"An assembly can declare only one {attribute} callback."));
                valid = false;
            }
        }

        return valid ? (ready.SingleOrDefault(), load.SingleOrDefault()) : (null, null);
    }

    /// <summary>
    /// Rejects unsupported signatures, inaccessible containers, and SQL-only metadata.
    /// </summary>
    private static bool Validate(IMethodSymbol method, SourceProductionContext context)
    {
        if (HasAttribute(method, "Ankus.PgInitializeAttribute") && HasAttribute(method, "Ankus.PgModuleLoadAttribute"))
        {
            return Invalid("A method cannot declare both PgInitialize and PgModuleLoad phases.");
        }

        if (method.MethodKind != MethodKind.Ordinary || !method.IsStatic || method.IsAsync || method.PartialImplementationPart?.IsAsync == true ||
            method.IsGenericMethod || method.IsAbstract || method.IsVirtual || method.IsExtern || !method.ReturnsVoid || method.Parameters.Length != 0 ||
            method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal) ||
            method.IsPartialDefinition && method.PartialImplementationPart is null)
        {
            return Invalid("Initialization requires an accessible, synchronous, non-generic, parameterless static void method with an implementation.");
        }

        for (INamedTypeSymbol? type = method.ContainingType; type is not null; type = type.ContainingType)
        {
            if (type.IsGenericType || type.IsFileLocal || type.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            {
                return Invalid("Initialization callbacks must be declared in accessible, non-generic, non-file-local types.");
            }
        }

        if (method.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() is
                "System.Diagnostics.ConditionalAttribute" or "System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute"))
        {
            return Invalid("Initialization callbacks must be callable unconditionally from managed code and cannot use Conditional or UnmanagedCallersOnly.");
        }

        if (method.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() is
                "Ankus.PgFunctionAttribute" or "Ankus.PgTriggerAttribute" or "Ankus.PgEventTriggerAttribute" or
                "Ankus.PgOperatorAttribute" or "Ankus.PgCastAttribute") ||
            method.GetReturnTypeAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() is
                "Ankus.PgCompositeTypeAttribute" or "Ankus.PgNumericPrecisionAttribute" or "Ankus.PgColumnNamesAttribute"))
        {
            return Invalid("Initialization callbacks cannot declare SQL functions, triggers, operators, casts, or SQL result metadata.");
        }

        return true;

        bool Invalid(string reason)
        {
            context.ReportDiagnostic(Diagnostic.Create(s_invalid, method.Locations.FirstOrDefault(), method.Name, reason));
            return false;
        }
    }

    /// <summary>
    /// Finds a phase marker on a normalized method declaration.
    /// </summary>
    private static bool HasAttribute(IMethodSymbol method, string name)
        => method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == name);
}
