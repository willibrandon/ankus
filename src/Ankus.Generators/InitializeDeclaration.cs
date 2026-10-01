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
    /// Validates one canonical phase declaration and detaches its invocation and callback identity.
    /// </summary>
    /// <param name="method">The attributed method normalized to its partial definition.</param>
    /// <param name="context">The current semantic diagnostic receiver.</param>
    /// <returns>The immutable phase contract, or null after validation fails.</returns>
    internal static LifecycleDeclaration? Create(IMethodSymbol method, GeneratorDiagnostics context)
        => Validate(method, context) ? new(HasAttribute(method, "Ankus.PgModuleLoadAttribute"),
            MethodInvocation.Create(method).Target, PgFunctionGenerator.GetCallbackName(method,
                HasAttribute(method, "Ankus.PgModuleLoadAttribute") ? "module_load" : "initialize")) : null;

    /// <summary>
    /// Reports extension-wide phase collisions against the current declaring source location.
    /// </summary>
    /// <param name="location">The current canonical definition location.</param>
    /// <param name="name">The original method name.</param>
    /// <param name="moduleLoad">Whether the conflicting phase is module registration.</param>
    /// <param name="context">The current diagnostic receiver.</param>
    internal static void ReportDuplicate(Location? location, string name, bool moduleLoad, GeneratorDiagnostics context)
        => context.Report(s_invalid, location, name, $"An assembly can declare only one {(moduleLoad ? "PgModuleLoad" : "PgInitialize")} callback.");

    /// <summary>
    /// Rejects unsupported signatures, inaccessible containers, and SQL-only metadata.
    /// </summary>
    private static bool Validate(IMethodSymbol method, GeneratorDiagnostics context)
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
            context.Report(s_invalid, method.Locations.FirstOrDefault(), method.Name, reason);
            return false;
        }
    }

    /// <summary>
    /// Finds a phase marker on a normalized method declaration.
    /// </summary>
    internal static bool HasAttribute(IMethodSymbol method, string name)
        => method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == name);
}
