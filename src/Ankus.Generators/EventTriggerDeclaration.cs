using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Validates event callbacks independently of ordinary SQL function and row trigger contracts.
/// </summary>
internal static class EventTriggerDeclaration
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS011", "Invalid PostgreSQL event trigger declaration", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Gets whether a method declares a PostgreSQL event trigger callback.
    /// </summary>
    /// <param name="method">The candidate method.</param>
    /// <returns>Whether PgEventTrigger is present.</returns>
    internal static bool IsEventTrigger(IMethodSymbol method)
        => method.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgEventTriggerAttribute");

    /// <summary>
    /// Validates the event callback signature and rejects SQL metadata that has no event trigger meaning.
    /// </summary>
    /// <param name="method">The attributed callback.</param>
    /// <param name="context">The generator context receiving diagnostics.</param>
    /// <returns>Whether the event trigger callback can be generated.</returns>
    internal static bool Validate(IMethodSymbol method, SourceProductionContext context)
    {
        if (!method.IsStatic || method.IsAsync || method.IsGenericMethod || method.IsAbstract ||
            method.ReturnsByRef || method.ReturnsByRefReadonly || !method.ReturnsVoid ||
            method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal) || method.Parameters.Length != 1)
        {
            return Invalid("An event trigger must be an accessible, synchronous, non-generic static void method with one nonnull PgEventTriggerContext parameter.");
        }

        IParameterSymbol parameter = method.Parameters[0];
        if (parameter.RefKind != RefKind.None || parameter.IsParams || parameter.IsOptional ||
            parameter.NullableAnnotation == NullableAnnotation.Annotated || parameter.Type.ToDisplayString() != "Ankus.PgEventTriggerContext")
        {
            return Invalid("The event trigger context must be one nonnull, required PgEventTriggerContext parameter passed by value.");
        }

        for (INamedTypeSymbol? type = method.ContainingType; type is not null; type = type.ContainingType)
        {
            if (type.IsGenericType || type.IsFileLocal || type.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            {
                return Invalid("Event trigger callbacks must be declared in accessible, non-generic, non-file-local types.");
            }
        }

        if (method.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() is
                "Ankus.PgTriggerAttribute" or "Ankus.PgOperatorAttribute" or "Ankus.PgCastAttribute") ||
            method.GetReturnTypeAttributes().Concat(parameter.GetAttributes()).Any(static attribute => attribute.AttributeClass?.ToDisplayString() is
                "Ankus.PgCompositeTypeAttribute" or "Ankus.PgNumericPrecisionAttribute" or "Ankus.PgColumnNamesAttribute" or "Ankus.PgParameterAttribute"))
        {
            return Invalid("Event triggers cannot declare row triggers, operators, casts, SQL parameter metadata, or composite, numeric, or TABLE result bindings.");
        }

        AttributeData? function = method.GetAttributes().FirstOrDefault(static attribute => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgFunctionAttribute");
        if (function?.NamedArguments.Any(static argument => argument.Key is "Rows" or "SetMode") == true)
        {
            return Invalid("Event triggers cannot declare Rows or SetMode.");
        }

        return true;

        bool Invalid(string reason)
        {
            context.ReportDiagnostic(Diagnostic.Create(s_invalid, method.Locations.FirstOrDefault(), method.Name, reason));
            return false;
        }
    }
}
