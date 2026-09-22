using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Resolves named composite bindings on parameters, scalar returns and individual TABLE columns.
/// </summary>
internal sealed class CompositeReference(string name, string? schema)
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS009", "Invalid PostgreSQL composite binding", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Gets the explicitly selected schema, or null for an unqualified type reference.
    /// </summary>
    internal string? Schema { get; } = schema;

    /// <summary>
    /// Gets the quoted SQL type identifier with its optional schema.
    /// </summary>
    internal string Sql => (Schema is null ? string.Empty : SqlText.Identifier(Schema) + ".") + SqlText.Identifier(name);

    /// <summary>
    /// Reads the single binding on an already validated parameter or scalar return.
    /// </summary>
    internal static CompositeReference? Read(ImmutableArray<AttributeData> attributes)
    {
        AttributeData? attribute = Bindings(attributes).FirstOrDefault();
        return attribute is null ? null : Read(attribute);
    }

    /// <summary>
    /// Validates context-specific bindings and applies them to already resolved set output columns.
    /// </summary>
    internal static bool Validate(IMethodSymbol method, SetResult? set, SourceProductionContext context)
    {
        bool valid = true;
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            ValidateScalar(parameter.Type, parameter.GetAttributes(), parameter.Name);
        }

        if (set is null)
        {
            ValidateScalar(method.ReturnType, method.GetReturnTypeAttributes(), "return");
        }
        else
        {
            var bound = new HashSet<int>();
            foreach (AttributeData attribute in Bindings(method.GetReturnTypeAttributes()))
            {
                if (!ValidateName(attribute))
                {
                    continue;
                }

                string? column = AttributeValues.Get<string?>(attribute, "Column", null);
                int index;
                if (column is not null)
                {
                    index = set.Names is null ? -1 : Array.IndexOf(set.Names, column);
                    if (index < 0)
                    {
                        Error(attribute, "Column must name an existing SQL TABLE output; scalar and SETOF returns cannot select a column.");
                        continue;
                    }
                }
                else
                {
                    int[] candidates = [.. Enumerable.Range(0, set.Columns.Length).Where(candidate => IsComposite(set.Columns[candidate]))];
                    if (candidates.Length != 1)
                    {
                        Error(attribute, "A return binding without Column requires exactly one composite output; select each TABLE column explicitly when ambiguous.");
                        continue;
                    }

                    index = candidates[0];
                }

                if (!IsComposite(set.Columns[index]))
                {
                    Error(attribute, "PgCompositeType requires a PgHeapTuple value or array element.");
                    continue;
                }

                if (!bound.Add(index))
                {
                    Error(attribute, "Each composite output may have only one PgCompositeType binding.");
                    continue;
                }

                set.Columns[index] = FunctionType.Create(set.Types[index], Read(attribute))!;
            }
        }

        return valid;

        void ValidateScalar(ITypeSymbol type, ImmutableArray<AttributeData> attributes, string target)
        {
            AttributeData[] bindings = [.. Bindings(attributes)];
            if (bindings.Length > 1)
            {
                Error(bindings[1], $"'{target}' may have only one PgCompositeType binding.");
                return;
            }

            foreach (AttributeData attribute in bindings)
            {
                if (!ValidateName(attribute))
                {
                    continue;
                }

                if (AttributeValues.Get<string?>(attribute, "Column", null) is not null)
                {
                    Error(attribute, "Column may be used only to select a SQL TABLE output.");
                }
                else if (!IsComposite(FunctionType.Create(type)))
                {
                    Error(attribute, "PgCompositeType requires a PgHeapTuple value or array element.");
                }
            }
        }

        bool ValidateName(AttributeData attribute)
        {
            string? typeName = attribute.ConstructorArguments.FirstOrDefault().Value as string;
            string? schemaName = AttributeValues.Get<string?>(attribute, "Schema", null);
            if (!SqlText.IsIdentifier(typeName) || schemaName is not null && !SqlText.IsIdentifier(schemaName))
            {
                Error(attribute, "Composite type and schema names must be nonempty identifiers of at most 63 UTF-8 bytes, without zero characters or invalid Unicode.");
                return false;
            }

            return true;
        }

        void Error(AttributeData attribute, string message)
        {
            valid = false;
            context.ReportDiagnostic(Diagnostic.Create(s_invalid,
                attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? method.Locations.FirstOrDefault(),
                method.Name, message));
        }
    }

    private static bool IsComposite(FunctionType? type) => (type?.Element ?? type)?.IsComposite == true;

    private static IEnumerable<AttributeData> Bindings(ImmutableArray<AttributeData> attributes)
        => attributes.Where(static attribute => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgCompositeTypeAttribute");

    private static CompositeReference Read(AttributeData attribute)
        => new(attribute.ConstructorArguments.FirstOrDefault().Value as string ?? string.Empty,
            AttributeValues.Get<string?>(attribute, "Schema", null));
}
