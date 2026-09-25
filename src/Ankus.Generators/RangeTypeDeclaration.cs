using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Selects finite SQL range identities associated with exact mapped scalar bounds.
/// </summary>
internal static class RangeTypeDeclaration
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS020", "Invalid PostgreSQL range mapping", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Finds a mapped scalar bound inside the exact Ankus range container.
    /// </summary>
    internal static INamedTypeSymbol? Bound(INamedTypeSymbol type)
        => type is { Name: "PgRange", Arity: 1 } && type.ContainingNamespace.ToDisplayString() == "Ankus" &&
            type.TypeArguments[0] is INamedTypeSymbol bound && DatumTypeDeclaration.IsMapped(bound) ? bound : null;

    /// <summary>
    /// Validates default and exact targets independently of which finite roots are subsequently selected.
    /// </summary>
    internal static AttributeData[]? Declarations(INamedTypeSymbol type, SourceProductionContext? context)
    {
        AttributeData[] attributes = [.. type.GetAttributes().Where(static item =>
            item.AttributeClass?.ToDisplayString() == "Ankus.PgRangeTypeAttribute")];
        if (attributes.Length != 0 && (!type.IsValueType || type.IsRefLikeType || !DatumTypeDeclaration.IsMapped(type)))
        {
            return Invalid("PgRangeType requires a non-ref-like value type carrying PgDatumType for its scalar bounds.");
        }

        var targets = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        bool hasDefault = false;
        foreach (AttributeData attribute in attributes)
        {
            if (attribute.ConstructorArguments.Length == 1)
            {
                if (hasDefault)
                {
                    return Invalid("A managed scalar type may have only one default PgRangeType declaration.");
                }

                hasDefault = true;
            }
            else if (attribute.ConstructorArguments.Length != 2 ||
                attribute.ConstructorArguments[0].Value is not INamedTypeSymbol target || !DatumTypeDeclaration.IsClosed(target) ||
                !SymbolEqualityComparer.Default.Equals(target.OriginalDefinition, type.OriginalDefinition))
            {
                return Invalid("An explicit PgRangeType target must be a closed construction of the annotated scalar bound type.");
            }
            else if (!targets.Add(target))
            {
                return Invalid("A closed scalar bound type may have only one exact PgRangeType declaration.");
            }
        }

        return attributes;

        AttributeData[]? Invalid(string message)
        {
            if (context is { } output)
            {
                Error(type, message, output);
            }

            return null;
        }
    }

    /// <summary>
    /// Creates an optional closed range contract from a previously validated scalar mapping.
    /// </summary>
    internal static bool TryCreate(DatumTypeDeclaration scalar, out DatumTypeDeclaration? range, SourceProductionContext? context = null)
    {
        range = null;
        AttributeData[]? attributes = Declarations(scalar.Type, context);
        if (attributes is null)
        {
            return false;
        }

        if (attributes.Length == 0)
        {
            return true;
        }

        AttributeData? attribute = attributes.FirstOrDefault(item => item.ConstructorArguments.Length == 2 &&
            SymbolEqualityComparer.Default.Equals(item.ConstructorArguments[0].Value as ITypeSymbol, scalar.Type)) ??
            attributes.FirstOrDefault(static item => item.ConstructorArguments.Length == 1);
        if (attribute is null)
        {
            return true;
        }

        string? name = attribute.ConstructorArguments[attribute.ConstructorArguments.Length - 1].Value as string;
        string? schema = AttributeValues.Get<string?>(attribute, "Schema", null);
        if (!SqlText.IsIdentifier(name) || schema is not null && !SqlText.IsIdentifier(schema))
        {
            return Invalid("Range type and schema names must be nonempty identifiers of at most 63 UTF-8 bytes, without zero characters or invalid Unicode.");
        }

        int origin = AttributeValues.Get(attribute, "Origin", 0);
        if (origin is not (0 or 1))
        {
            return Invalid("Range Origin must be ThisExtension or External.");
        }

        if (origin == 1 && schema is null)
        {
            return Invalid("External range mappings require an explicit Schema.");
        }

        INamedTypeSymbol? definition = attribute.AttributeClass!.ContainingAssembly.GetTypeByMetadataName("Ankus.PgRange`1");
        if (definition is null)
        {
            return Invalid("The range declaration must resolve the Ankus PgRange<T> runtime type.");
        }

        range = new(definition.Construct(scalar.Type), scalar.Converter, name!, schema, origin == 1,
            scalar.CanRead, scalar.CanWrite, inferred: false, rangeBound: scalar);
        return true;

        bool Invalid(string message)
        {
            if (context is { } output)
            {
                Error(scalar.Type, message, output);
            }

            return false;
        }
    }

    /// <summary>
    /// Reports a precise source-located range contract error before generated output.
    /// </summary>
    internal static void Error(ISymbol symbol, string message, SourceProductionContext context)
        => context.ReportDiagnostic(Diagnostic.Create(s_invalid, symbol.Locations.FirstOrDefault(), symbol.Name, message));
}
