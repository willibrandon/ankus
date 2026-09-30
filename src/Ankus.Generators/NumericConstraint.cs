using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Reads declarative numeric constraints without loading or executing extension code.
/// </summary>
internal static class NumericConstraint
{
    private static readonly DiagnosticDescriptor s_invalidConstraint = new(
        "ANKUS003", "Invalid numeric precision constraint",
        "PgNumericPrecision requires a PgNumeric or decimal value, precision from 1 through 1000, and scale from -1000 through 1000",
        "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Validates numeric precision attributes on a function's parameters and result, reporting each invalid declaration.
    /// </summary>
    /// <param name="method">The attributed function.</param>
    /// <param name="context">The generator context receiving diagnostics.</param>
    /// <param name="set">The optional set result whose scalar element may be constrained.</param>
    /// <returns>Whether every declared numeric constraint is valid.</returns>
    internal static bool Validate(IMethodSymbol method, SourceProductionContext context, SetResult? set = null)
    {
        bool valid = ValidateValue(set is { Columns.Length: 1, Names: null } ? set.Types[0] : method.ReturnType,
            method.GetReturnTypeAttributes(), context);
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            valid &= ValidateValue(parameter.Type, parameter.GetAttributes(), context);
        }

        return valid;
    }

    /// <summary>
    /// Emits the managed rescaling suffix for an already validated numeric constraint.
    /// </summary>
    /// <param name="attributes">The parameter or return-value attributes.</param>
    /// <returns>A Rescale invocation suffix, or an empty string when no constraint is declared.</returns>
    internal static string Rescale(ImmutableArray<AttributeData> attributes)
    {
        AttributeData? attribute = Find(attributes);
        return attribute is null ? string.Empty : ".Rescale(" +
            ((int)attribute.ConstructorArguments[0].Value!).ToString(CultureInfo.InvariantCulture) + ", " +
            ((int)attribute.ConstructorArguments[1].Value!).ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// Validates the numeric constraint selected for one scalar SQL value.
    /// </summary>
    internal static bool ValidateValue(ITypeSymbol type, ImmutableArray<AttributeData> attributes, SourceProductionContext context, bool grouped = false)
    {
        AttributeData[] constraints = [.. attributes.Where(static attribute => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgNumericPrecisionAttribute")];
        AttributeData? attribute = constraints.FirstOrDefault();
        if (attribute is null)
        {
            return true;
        }

        if (constraints.Length == 1 && (grouped || AttributeValues.Get<string?>(attribute, "Element", null) is null) &&
            FunctionType.Create(type)?.Reader == "numeric" && attribute.ConstructorArguments.Length == 2 &&
            attribute.ConstructorArguments[0].Value is int and >= 1 and <= 1000 &&
            attribute.ConstructorArguments[1].Value is int and >= -1000 and <= 1000)
        {
            return true;
        }

        context.ReportDiagnostic(Diagnostic.Create(s_invalidConstraint,
            attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()));
        return false;
    }

    private static AttributeData? Find(ImmutableArray<AttributeData> attributes)
        => attributes.FirstOrDefault(static attribute => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgNumericPrecisionAttribute");
}
