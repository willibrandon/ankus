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

    internal static bool Validate(IMethodSymbol method, SourceProductionContext context)
    {
        bool valid = Validate(method.ReturnType, method.GetReturnTypeAttributes(), context);
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            valid &= Validate(parameter.Type, parameter.GetAttributes(), context);
        }

        return valid;
    }

    internal static string Rescale(ImmutableArray<AttributeData> attributes)
    {
        AttributeData? attribute = Find(attributes);
        return attribute is null ? string.Empty : ".Rescale(" +
            ((int)attribute.ConstructorArguments[0].Value!).ToString(CultureInfo.InvariantCulture) + ", " +
            ((int)attribute.ConstructorArguments[1].Value!).ToString(CultureInfo.InvariantCulture) + ")";
    }

    private static bool Validate(ITypeSymbol type, ImmutableArray<AttributeData> attributes, SourceProductionContext context)
    {
        AttributeData? attribute = Find(attributes);
        if (attribute is null)
        {
            return true;
        }

        if (FunctionType.Create(type)?.Reader == "numeric" && attribute.ConstructorArguments.Length == 2 &&
            attribute.ConstructorArguments[0].Value is int precision and >= 1 and <= 1000 &&
            attribute.ConstructorArguments[1].Value is int scale and >= -1000 and <= 1000)
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
