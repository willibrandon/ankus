using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Resolves named SQL bindings on parameters, scalar returns and individual TABLE columns.
/// </summary>
/// <param name="Name">The exact unquoted catalog name.</param>
/// <param name="Schema">The fixed catalog schema, or null for an unqualified binding.</param>
/// <param name="IsRaw">Whether the value carries a raw PostgreSQL datum.</param>
/// <param name="IsArray">Whether the binding identifies an array type.</param>
internal sealed record SqlTypeReference(string Name, string? Schema, bool IsRaw = false, bool IsArray = false)
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS009", "Invalid PostgreSQL composite binding", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor s_invalidRaw = new(
        "ANKUS016", "Invalid PostgreSQL raw type binding", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Gets whether the binding names PostgreSQL's internal callback type.
    /// </summary>
    internal bool IsInternal => IsBuiltin && Name == "internal";

    /// <summary>
    /// Gets whether a built-in binding needs a polymorphic input to resolve its output type.
    /// </summary>
    internal bool IsPolymorphic => IsBuiltin && Name is "anyelement" or "anyarray" or "anynonarray" or "anyenum" or
        "anyrange" or "anymultirange" or "anycompatible" or "anycompatiblearray" or "anycompatiblenonarray" or
        "anycompatiblerange" or "anycompatiblemultirange";

    /// <summary>
    /// Gets whether this scalar binding identifies a built-in type.
    /// </summary>
    private bool IsBuiltin => IsRaw && !IsArray && Schema is null or "pg_catalog";

    /// <summary>
    /// Gets a schema dependency when installation must preserve a user-selected type schema.
    /// </summary>
    internal string? DependencySchema => IsRaw && Schema == "pg_catalog" ? null : Schema;

    /// <summary>
    /// Gets the quoted SQL type identifier with its optional schema.
    /// </summary>
    internal string Sql => (Schema is null ? string.Empty : SqlText.Identifier(Schema) + ".") + SqlText.Identifier(Name) +
        (IsArray ? "[]" : string.Empty);

    /// <summary>
    /// Reads the single binding on an already validated parameter or scalar return.
    /// </summary>
    internal static SqlTypeReference? Read(ImmutableArray<AttributeData> attributes)
    {
        AttributeData? attribute = Bindings(attributes).FirstOrDefault();
        return attribute is null ? null : Read(attribute);
    }

    /// <summary>
    /// Validates scalar bindings without retaining a set result.
    /// </summary>
    /// <param name="method">The attributed scalar method.</param>
    /// <param name="context">The generator diagnostic destination.</param>
    /// <returns>Whether every parameter and result binding is valid.</returns>
    internal static bool Validate(IMethodSymbol method, GeneratorDiagnostics context)
    {
        SetResult? set = null;
        return Validate(method, ref set, context);
    }

    /// <summary>
    /// Validates context-specific bindings and replaces the immutable set model with bound output columns.
    /// </summary>
    internal static bool Validate(IMethodSymbol method, ref SetResult? set, GeneratorDiagnostics context)
    {
        bool valid = true;
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            valid &= ValidateValue(parameter.Type, parameter.GetAttributes(), method, parameter.Name, context);
        }

        if (set is null)
        {
            valid &= ValidateValue(method.ReturnType, method.GetReturnTypeAttributes(), method, "return", context);
        }
        else
        {
            var bound = new HashSet<int>();
            FunctionType[] columns = [.. set.Columns];
            ITypeSymbol[] outputTypes = SetResult.OutputTypes(method);
            foreach (AttributeData attribute in Bindings(method.GetReturnTypeAttributes()))
            {
                if (!ValidateName(attribute, Error))
                {
                    continue;
                }

                if (AttributeValues.Get<string?>(attribute, "Element", null) is not null)
                {
                    Error(attribute, "Element selects a typed aggregate tuple input; use Column for SQL TABLE outputs.");
                    continue;
                }

                string? column = AttributeValues.Get<string?>(attribute, "Column", null);
                int index;
                if (column is not null)
                {
                    index = set.Names is null ? -1 : Array.IndexOf<string>([.. set.Names], column);
                    if (index < 0)
                    {
                        Error(attribute, "Column must name an existing SQL TABLE output; scalar and SETOF returns cannot select a column.");
                        continue;
                    }
                }
                else
                {
                    int[] candidates = [.. Enumerable.Range(0, columns.Length).Where(candidate => Matches(columns[candidate], attribute))];
                    if (candidates.Length != 1)
                    {
                        Error(attribute, "A return binding without Column requires exactly one matching output; select each TABLE column explicitly when ambiguous.");
                        continue;
                    }

                    index = candidates[0];
                }

                if (!Matches(columns[index], attribute))
                {
                    Error(attribute, Requirement(attribute));
                    continue;
                }

                if (!bound.Add(index))
                {
                    Error(attribute, "Each output may have only one SQL type binding.");
                    continue;
                }

                columns[index] = FunctionType.Create(outputTypes[index], Read(attribute))!;
            }

            set = set with { Columns = new(columns) };

            for (int index = 0; index < columns.Length; index++)
            {
                if (columns[index].IsRaw && columns[index].Binding is null)
                {
                    Error(null, "Each PgDatum output requires a PgSqlType binding.");
                }
            }
        }

        return valid;

        void Error(AttributeData? attribute, string message)
        {
            valid = false;
            Report(attribute, method, message, context);
        }
    }

    /// <summary>
    /// Validates one SQL value after any aggregate tuple-element selection.
    /// </summary>
    internal static bool ValidateValue(ITypeSymbol type, ImmutableArray<AttributeData> attributes, IMethodSymbol method,
        string target, GeneratorDiagnostics context, bool grouped = false)
    {
        bool valid = true;
        AttributeData[] bindings = [.. Bindings(attributes)];
        if (bindings.Length > 1)
        {
            Error(bindings[1], $"'{target}' may have only one SQL type binding.");
            return false;
        }

        if (bindings.Length == 0 && FunctionType.Create(type)?.IsRaw == true)
        {
            Error(null, $"'{target}' requires a PgSqlType binding for its PgDatum value.");
        }

        foreach (AttributeData attribute in bindings)
        {
            if (!ValidateName(attribute, Error))
            {
                continue;
            }

            if (AttributeValues.Get<string?>(attribute, "Column", null) is not null)
            {
                Error(attribute, "Column may be used only to select a SQL TABLE output.");
            }
            else if (!grouped && AttributeValues.Get<string?>(attribute, "Element", null) is not null)
            {
                Error(attribute, "Element may be used only to select a typed aggregate tuple input.");
            }
            else if (!Matches(FunctionType.Create(type), attribute))
            {
                Error(attribute, Requirement(attribute));
            }
        }

        return valid;

        void Error(AttributeData? attribute, string message)
        {
            valid = false;
            Report(attribute, method, message, context);
        }
    }

    /// <summary>
    /// Checks identifiers shared by scalar, TABLE and aggregate element bindings.
    /// </summary>
    private static bool ValidateName(AttributeData attribute, Action<AttributeData?, string> error)
    {
        string? typeName = attribute.ConstructorArguments.FirstOrDefault().Value as string;
        string? schemaName = AttributeValues.Get<string?>(attribute, "Schema", null);
        if (!SqlText.IsIdentifier(typeName) || schemaName is not null && !SqlText.IsIdentifier(schemaName))
        {
            error(attribute, "Type and schema names must be nonempty identifiers of at most 63 UTF-8 bytes, without zero characters or invalid Unicode.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reports a binding diagnostic at its attribute or declaring method.
    /// </summary>
    private static void Report(AttributeData? attribute, IMethodSymbol method, string message, GeneratorDiagnostics context)
        => context.Report(attribute is null || IsRawBinding(attribute) ? s_invalidRaw : s_invalid,
            attribute?.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? method.Locations.FirstOrDefault(),
            method.Name, message);

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
