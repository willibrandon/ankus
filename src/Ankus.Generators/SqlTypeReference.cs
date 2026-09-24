using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Resolves named SQL bindings on parameters, scalar returns and individual TABLE columns.
/// </summary>
internal sealed class SqlTypeReference(string name, string? schema, bool raw = false, bool array = false)
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS009", "Invalid PostgreSQL composite binding", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor s_invalidRaw = new(
        "ANKUS016", "Invalid PostgreSQL raw type binding", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Gets whether the binding declares a raw datum rather than a managed heap tuple.
    /// </summary>
    internal bool IsRaw { get; } = raw;

    /// <summary>
    /// Gets whether the binding names PostgreSQL's internal callback type.
    /// </summary>
    internal bool IsInternal => IsBuiltin && name == "internal";

    /// <summary>
    /// Gets whether a built-in binding needs a polymorphic input to resolve its output type.
    /// </summary>
    internal bool IsPolymorphic => IsBuiltin && name is "anyelement" or "anyarray" or "anynonarray" or "anyenum" or
        "anyrange" or "anymultirange" or "anycompatible" or "anycompatiblearray" or "anycompatiblenonarray" or
        "anycompatiblerange" or "anycompatiblemultirange";

    /// <summary>
    /// Gets whether this scalar binding identifies a built-in type.
    /// </summary>
    private bool IsBuiltin => IsRaw && !array && Schema is null or "pg_catalog";

    /// <summary>
    /// Gets a schema dependency when installation must preserve a user-selected type schema.
    /// </summary>
    internal string? DependencySchema => IsRaw && Schema == "pg_catalog" ? null : Schema;

    /// <summary>
    /// Gets the explicitly selected schema, or null for an unqualified type reference.
    /// </summary>
    internal string? Schema { get; } = schema;

    /// <summary>
    /// Gets the quoted SQL type identifier with its optional schema.
    /// </summary>
    internal string Sql => (Schema is null ? string.Empty : SqlText.Identifier(Schema) + ".") + SqlText.Identifier(name) +
        (array ? "[]" : string.Empty);

    /// <summary>
    /// Reads the single binding on an already validated parameter or scalar return.
    /// </summary>
    internal static SqlTypeReference? Read(ImmutableArray<AttributeData> attributes)
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
                    int[] candidates = [.. Enumerable.Range(0, set.Columns.Length).Where(candidate => Matches(set.Columns[candidate], attribute))];
                    if (candidates.Length != 1)
                    {
                        Error(attribute, "A return binding without Column requires exactly one matching output; select each TABLE column explicitly when ambiguous.");
                        continue;
                    }

                    index = candidates[0];
                }

                if (!Matches(set.Columns[index], attribute))
                {
                    Error(attribute, Requirement(attribute));
                    continue;
                }

                if (!bound.Add(index))
                {
                    Error(attribute, "Each output may have only one SQL type binding.");
                    continue;
                }

                set.Columns[index] = FunctionType.Create(set.Types[index], Read(attribute))!;
            }

            for (int index = 0; index < set.Columns.Length; index++)
            {
                if (set.Columns[index].IsRaw && set.Columns[index].Binding is null)
                {
                    Error(null, "Each PgDatum output requires a PgSqlType binding.");
                }
            }
        }

        return valid;

        void ValidateScalar(ITypeSymbol type, ImmutableArray<AttributeData> attributes, string target)
        {
            AttributeData[] bindings = [.. Bindings(attributes)];
            if (bindings.Length > 1)
            {
                Error(bindings[1], $"'{target}' may have only one SQL type binding.");
                return;
            }

            if (bindings.Length == 0 && FunctionType.Create(type)?.IsRaw == true)
            {
                Error(null, $"'{target}' requires a PgSqlType binding for its PgDatum value.");
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
                else if (!Matches(FunctionType.Create(type), attribute))
                {
                    Error(attribute, Requirement(attribute));
                }
            }
        }

        bool ValidateName(AttributeData attribute)
        {
            string? typeName = attribute.ConstructorArguments.FirstOrDefault().Value as string;
            string? schemaName = AttributeValues.Get<string?>(attribute, "Schema", null);
            if (!SqlText.IsIdentifier(typeName) || schemaName is not null && !SqlText.IsIdentifier(schemaName))
            {
                Error(attribute, "Type and schema names must be nonempty identifiers of at most 63 UTF-8 bytes, without zero characters or invalid Unicode.");
                return false;
            }

            return true;
        }

        void Error(AttributeData? attribute, string message)
        {
            valid = false;
            context.ReportDiagnostic(Diagnostic.Create(attribute is null || IsRawBinding(attribute) ? s_invalidRaw : s_invalid,
                attribute?.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? method.Locations.FirstOrDefault(),
                method.Name, message));
        }
    }

    /// <summary>
    /// Checks the managed representation selected by a binding attribute.
    /// </summary>
    private static bool Matches(FunctionType? type, AttributeData attribute)
        => IsRawBinding(attribute) ? type?.IsRaw == true : (type?.Element ?? type)?.IsComposite == true;

    /// <summary>
    /// Identifies an explicit raw datum binding.
    /// </summary>
    private static bool IsRawBinding(AttributeData attribute)
        => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgSqlTypeAttribute";

    /// <summary>
    /// Describes the managed representation required by an invalid binding.
    /// </summary>
    private static string Requirement(AttributeData attribute) => IsRawBinding(attribute)
        ? "PgSqlType requires a PgDatum value. Use IsArray to bind a datum containing an array."
        : "PgCompositeType requires a PgHeapTuple value or array element.";

    /// <summary>
    /// Selects attributes that bind a managed value to a named SQL type.
    /// </summary>
    private static IEnumerable<AttributeData> Bindings(ImmutableArray<AttributeData> attributes)
        => attributes.Where(static attribute => attribute.AttributeClass?.ToDisplayString() is "Ankus.PgCompositeTypeAttribute" or "Ankus.PgSqlTypeAttribute");

    /// <summary>
    /// Reads the type identity and representation from a binding attribute.
    /// </summary>
    private static SqlTypeReference Read(AttributeData attribute)
        => new(attribute.ConstructorArguments.FirstOrDefault().Value as string ?? string.Empty,
            AttributeValues.Get<string?>(attribute, "Schema", null), IsRawBinding(attribute),
            AttributeValues.Get(attribute, "IsArray", false));
}
