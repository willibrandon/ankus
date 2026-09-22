using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Resolves an enumerable return into scalar SETOF or named TABLE columns.
/// </summary>
internal sealed class SetResult
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS008", "Invalid PostgreSQL set result", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Gets the managed iterator element type, including tuple names and nullable annotations.
    /// </summary>
    internal string Managed { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the ordered output conversion contracts.
    /// </summary>
    internal FunctionType[] Columns { get; private set; } = [];

    /// <summary>
    /// Gets the managed source types corresponding to output columns.
    /// </summary>
    internal ITypeSymbol[] Types { get; private set; } = [];

    /// <summary>
    /// Gets the column names for TABLE, or null for scalar SETOF.
    /// </summary>
    internal string[]? Names { get; private set; }

    /// <summary>
    /// Gets expressions that read each output column from the iterator's current value.
    /// </summary>
    internal string[] Values { get; private set; } = [];

    /// <summary>
    /// Gets the complete SQL return clause following RETURNS.
    /// </summary>
    internal string Sql => Names is null ? "SETOF " + Columns[0].Sql : "TABLE (" +
        string.Join(", ", Columns.Select((column, index) => SqlText.Identifier(Names[index]) + " " + column.Sql)) + ")";

    /// <summary>
    /// Identifies the explicit generic enumerable return contract without treating scalar collections as sets.
    /// </summary>
    internal static bool IsSequence(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Collections_Generic_IEnumerable_T };

    /// <summary>
    /// Validates a method's set shape and optional column names, reporting invalid result declarations.
    /// </summary>
    internal static SetResult? Create(IMethodSymbol method, SourceProductionContext context, out bool valid)
    {
        valid = true;
        AttributeData? namesAttribute = method.GetReturnTypeAttributes().FirstOrDefault(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "Ankus.PgColumnNamesAttribute");
        if (!IsSequence(method.ReturnType))
        {
            if (namesAttribute is not null)
            {
                valid = false;
                Error("PgColumnNames requires an IEnumerable return.");
            }

            return null;
        }

        ITypeSymbol row = ((INamedTypeSymbol)method.ReturnType).TypeArguments[0];
        var result = new SetResult
        {
            Managed = row.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)),
        };
        if (row is INamedTypeSymbol { IsTupleType: true } tuple)
        {
            result.Types = [.. tuple.TupleElements.Select(static field => field.Type)];
            result.Values = [.. tuple.TupleElements.Select(static field => "value.@" + field.Name)];
            if (tuple.TupleElements.All(static field => !field.IsImplicitlyDeclared))
            {
                result.Names = [.. tuple.TupleElements.Select(static field => SqlText.SnakeCase(field.Name))];
            }
        }
        else if (row is INamedTypeSymbol { Name: "ValueTuple", Arity: 1 } single && single.ContainingNamespace.ToDisplayString() == "System")
        {
            result.Types = [single.TypeArguments[0]];
            result.Values = ["value.Item1"];
        }
        else
        {
            result.Types = [row];
            result.Values = ["value"];
        }

        FunctionType?[] columns = [.. result.Types.Select(FunctionType.Create)];
        if (columns.Length > 1664 || columns.Any(static column => column is null || column.Managed == "void"))
        {
            valid = false;
            Error("Set elements must be supported scalar or array values, or flat tuples of supported values with at most 1664 columns.");
            return null;
        }

        result.Columns = [.. columns.Select(static column => column!)];

        if (namesAttribute is not null)
        {
            if (namesAttribute.ConstructorArguments.Length != 1 || namesAttribute.ConstructorArguments[0].IsNull)
            {
                valid = false;
                Error("PgColumnNames requires non-null column names.");
                return null;
            }

            result.Names = [.. namesAttribute.ConstructorArguments[0].Values.Select(static value => value.Value as string ?? string.Empty)];
        }

        if (result.Values[0] != "value" && result.Names is null)
        {
            valid = false;
            Error("TABLE tuple elements must be named, or use PgColumnNames to name every column.");
            return null;
        }

        if (result.Names is not null && (result.Names.Length != result.Columns.Length ||
            result.Names.Any(static name => !SqlText.IsIdentifier(name)) ||
            result.Names.Distinct(StringComparer.Ordinal).Count() != result.Names.Length))
        {
            valid = false;
            Error("TABLE requires one distinct SQL identifier per column, each at most 63 UTF-8 bytes.");
            return null;
        }

        return result;

        void Error(string message)
            => context.ReportDiagnostic(Diagnostic.Create(s_invalid, method.Locations.FirstOrDefault(), method.Name, message));
    }
}
