using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Resolves and validates SQL declaration options without evaluating extension code.
/// </summary>
internal sealed record FunctionDeclaration
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS004", "Invalid PostgreSQL declaration", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Gets the fixed schema, or null to use the extension's installation schema.
    /// </summary>
    internal string? Schema
    {
        get;
        private init;
    }

    /// <summary>
    /// Gets the validated unquoted SQL function name for item selection.
    /// </summary>
    internal string Name
    {
        get;
        private init;
    } = string.Empty;

    /// <summary>
    /// Gets the quoted function name, qualified when a fixed schema is declared.
    /// </summary>
    internal string QualifiedName
    {
        get;
        private init;
    } = string.Empty;

    /// <summary>
    /// Gets the function name with a compiler marker for an extension-controlled default schema.
    /// </summary>
    internal string TemplateName => (Schema is null ? "\0" : string.Empty) + QualifiedName;

    /// <summary>
    /// Gets the validated SQL argument contracts before extension-provider qualification.
    /// </summary>
    internal EquatableArray<SqlFunctionParameter> Parameters
    {
        get;
        private init;
    } = new([]);

    /// <summary>
    /// Renders named SQL arguments with the current extension's catalog providers.
    /// </summary>
    /// <param name="providers">The graph's validated catalog identities, or null when no provider inventory is available.</param>
    /// <returns>The ordered argument declarations, including variadic and exact default clauses.</returns>
    internal string Arguments(SqlTypeProviders? providers = null)
        => string.Join(", ", Parameters.Select(parameter => parameter.Emit(providers)));

    /// <summary>
    /// Gets the validated SQL execution options.
    /// </summary>
    internal string Options
    {
        get;
        private init;
    } = string.Empty;

    /// <summary>
    /// Gets whether the declaration replaces an existing compatible function.
    /// </summary>
    internal bool Replace
    {
        get;
        private init;
    }

    /// <summary>
    /// Gets whether PostgreSQL skips this function when any SQL input is null.
    /// </summary>
    internal bool Strict
    {
        get;
        private init;
    }

    /// <summary>
    /// Resolves schema inheritance, parameter contracts, and planner/execution options for one function.
    /// </summary>
    /// <param name="method">The attributed static method.</param>
    /// <param name="name">The validated SQL function name.</param>
    /// <param name="context">The generator context receiving declaration diagnostics.</param>
    /// <param name="set">The validated set return, or null for a scalar function.</param>
    /// <param name="contextParameter">Whether the managed parameter is a backend invocation context instead of a SQL argument.</param>
    /// <param name="sqlNullability">Explicit SQL parameter nullability for a specialized callback, excluding synthetic arguments that cannot be null.</param>
    /// <param name="schemaFallback">The specialized declaration's schema when the callback does not override it.</param>
    /// <param name="parameterModels">The ordered SQL and injected parameters, or null to resolve them from the method.</param>
    /// <returns>The declaration, or null after reporting an invalid contract.</returns>
    internal static FunctionDeclaration? Create(IMethodSymbol method, string name, GeneratorDiagnostics context, SetResult? set = null,
        bool contextParameter = false, IReadOnlyList<bool>? sqlNullability = null, string? schemaFallback = null, FunctionParameter[]? parameterModels = null)
    {
        AttributeData? attribute = method.GetAttributes().FirstOrDefault(static value => value.AttributeClass?.ToDisplayString() == "Ankus.PgFunctionAttribute");
        var declaration = new FunctionDeclaration();
        int volatility = Value(attribute, "Volatility", 0);
        int parallel = Value(attribute, "ParallelSafety", 0);
        int nullInput = Value(attribute, "NullInput", 0);
        double cost = Value(attribute, "Cost", 1d);
        if (volatility is < 0 or > 2 || parallel is < 0 or > 2 || nullInput is < 0 or > 2)
        {
            return Invalid("Volatility, ParallelSafety, and NullInput must be defined enum values.");
        }

        if (double.IsNaN(cost) || double.IsInfinity(cost) || cost <= 0 || cost > float.MaxValue || (float)cost == 0)
        {
            return Invalid("Cost must be positive, finite, and representable as PostgreSQL's real planner cost.");
        }

        FunctionParameter[] sqlParameters = contextParameter ? [] :
            [.. (parameterModels ?? FunctionParameter.Create(method)).Where(static parameter => !parameter.IsInjected)];
        if (!contextParameter && (set?.Columns.Any(static column => column.IsSqlInternal) ?? FunctionType.CreateResult(method)?.IsSqlInternal == true) &&
            !sqlParameters.Any(static parameter => parameter.Type?.IsSqlInternal == true))
        {
            return Invalid("A PostgreSQL internal result requires an internal SQL input.");
        }

        if (!contextParameter && (set?.Columns.Any(static column => column.IsSqlPolymorphic) ?? FunctionType.CreateResult(method)?.IsSqlPolymorphic == true) &&
            !sqlParameters.Any(static parameter => parameter.Type?.IsSqlPolymorphic == true))
        {
            return Invalid("A polymorphic result requires a polymorphic SQL input to resolve its type.");
        }

        bool allNullable = sqlNullability?.All(static nullable => nullable) ??
            (contextParameter || sqlParameters.All(static parameter => parameter.Type!.Nullable));
        bool allRequired = sqlNullability?.All(static nullable => !nullable) ??
            (!contextParameter && sqlParameters.All(static parameter => !parameter.Type!.Nullable));
        if (nullInput == 2 && !allNullable)
        {
            return Invalid("CalledOnNull requires nullable declarations for every parameter.");
        }

        string? schema = Value(attribute, "Schema", schemaFallback);
        for (INamedTypeSymbol? container = method.ContainingType; schema is null && container is not null; container = container.ContainingType)
        {
            AttributeData? schemaAttribute = container.GetAttributes().FirstOrDefault(static value =>
                value.AttributeClass?.ToDisplayString() == "Ankus.PgSchemaAttribute");
            if (schemaAttribute is not null)
            {
                schema = schemaAttribute.ConstructorArguments.FirstOrDefault().Value as string;
                if (schema is null)
                {
                    return Invalid("PgSchema requires a non-null schema identifier.");
                }
            }
        }

        if (schema is not null && !SqlText.IsIdentifier(schema))
        {
            return Invalid("A fixed schema must be a nonempty identifier of at most 63 UTF-8 bytes.");
        }

        declaration = declaration with
        {
            Schema = schema,
            Name = name,
            QualifiedName = (schema is null ? string.Empty : SqlText.Identifier(schema) + ".") + SqlText.Identifier(name),
            Replace = Value(attribute, "CreateOrReplace", false),
            Strict = nullInput == 1 || (nullInput == 0 && allRequired),
        };

        var options = new List<string>
        {
            volatility switch { 1 => "STABLE", 2 => "IMMUTABLE", _ => "VOLATILE" },
            "PARALLEL " + (parallel switch { 1 => "RESTRICTED", 2 => "SAFE", _ => "UNSAFE" }),
            declaration.Strict ? "STRICT" : "CALLED ON NULL INPUT",
            Value(attribute, "SecurityDefiner", false) ? "SECURITY DEFINER" : "SECURITY INVOKER",
            Value(attribute, "Leakproof", false) ? "LEAKPROOF" : "NOT LEAKPROOF",
            "COST " + cost.ToString("R", CultureInfo.InvariantCulture),
        };

        if (set is not null)
        {
            double rows = Value(attribute, "Rows", 1000d);
            int mode = Value(attribute, "SetMode", 0);
            if (double.IsNaN(rows) || double.IsInfinity(rows) || rows <= 0 || rows > float.MaxValue || (float)rows == 0)
            {
                return Invalid("Rows must be positive, finite, and representable as PostgreSQL's real row estimate.");
            }

            if (mode is < 0 or > 2)
            {
                return Invalid("SetMode must be a defined PgSetMode value.");
            }

            options.Add("ROWS " + rows.ToString("R", CultureInfo.InvariantCulture));
        }
        else if (attribute?.NamedArguments.Any(static argument => argument.Key is "Rows" or "SetMode") == true)
        {
            return Invalid("Rows and SetMode require an IEnumerable return.");
        }

        string? support = Value<string?>(attribute, "SupportFunction", null);
        if (support is not null)
        {
            string[] parts = support.Split('.');
            if (parts.Length is < 1 or > 2 || parts.Any(static part => !SqlText.IsIdentifier(part)))
            {
                return Invalid("SupportFunction must name one function, optionally prefixed by one schema.");
            }

            options.Add("SUPPORT " + string.Join(".", parts.Select(SqlText.Identifier)));
        }

        foreach (KeyValuePair<string, TypedConstant> argument in attribute?.NamedArguments ?? [])
        {
            if (argument.Key != "SearchPath" || argument.Value.IsNull)
            {
                continue;
            }

            string?[] path = [.. argument.Value.Values.Select(static value => value.Value as string)];
            if (path.Any(static entry => !SqlText.IsIdentifier(entry)))
            {
                return Invalid("SearchPath entries must be nonempty schema identifiers of at most 63 UTF-8 bytes.");
            }

            options.Add("SET search_path TO " + (path.Length == 0 ? "''" : string.Join(", ", path.Select(static entry => SqlText.Identifier(entry!)))));
        }

        declaration = declaration with { Options = string.Join(" ", options) };
        if (contextParameter)
        {
            return declaration;
        }

        var parameters = new List<SqlFunctionParameter>();
        var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        bool defaultSeen = false;
        foreach (FunctionParameter model in parameterModels ?? FunctionParameter.Create(method))
        {
            if (model.IsInjected)
            {
                if (!model.SqlOptions.IsEmpty)
                {
                    return Invalid($"An injected {model.DeclaredTypeName} has no SQL parameter name or default; remove PgParameter from it.");
                }

                continue;
            }

            FunctionType type = model.Type!;
            if (model.SqlOptions.Count > 1 || model.SqlOptions.Any(static value => value.Element is not null || value.Variadic))
            {
                return Invalid("Ordinary SQL parameters allow one PgParameter attribute without aggregate element or variadic options; declare variadic functions with params.");
            }

            SqlParameterOptions? parameterOptions = model.SqlOptions.IsEmpty ? null : model.SqlOptions[0];
            string parameterName = parameterOptions?.Name ?? SqlText.SnakeCase(model.Name);
            if (!SqlText.IsIdentifier(parameterName) || !parameterNames.Add(parameterName))
            {
                return Invalid("SQL parameter names must be distinct identifiers of at most 63 UTF-8 bytes.");
            }

            if (set?.Names?.Contains(parameterName, StringComparer.Ordinal) == true)
            {
                return Invalid("Input and TABLE output parameters must have distinct SQL names.");
            }

            string? expression = parameterOptions?.Default;
            if (expression is not null && (string.IsNullOrWhiteSpace(expression) || !SqlText.IsText(expression)))
            {
                return Invalid("A SQL default must be a nonempty expression with valid Unicode and no zero characters.");
            }

            if (expression is null && model.HasExplicitDefaultValue)
            {
                expression = model.OptionalDefaultSql;
                if (expression is null)
                {
                    return Invalid($"The optional default for '{model.Name}' needs an explicit PgParameter.Default SQL expression.");
                }
            }

            if (defaultSeen && expression is null)
            {
                return Invalid("Every input parameter after a defaulted parameter must also declare a SQL default.");
            }

            defaultSeen |= expression is not null;
            parameters.Add(new(parameterName, type, model.IsParams, expression));
        }

        declaration = declaration with { Parameters = new(parameters) };
        return declaration;

        FunctionDeclaration? Invalid(string reason)
        {
            ReportInvalid(context, method.Locations.FirstOrDefault(), method.Name, reason);
            return null;
        }
    }

    /// <summary>
    /// Reports declaration validation using the established diagnostic contract.
    /// </summary>
    /// <param name="context">The diagnostic destination.</param>
    /// <param name="location">The current declaration location.</param>
    /// <param name="name">The authored managed name.</param>
    /// <param name="reason">The exact validation failure.</param>
    internal static void ReportInvalid(GeneratorDiagnostics context, Location? location, string name, string reason)
        => context.Report(s_invalid, location, name, reason);

    private static T Value<T>(AttributeData? attribute, string name, T fallback)
    {
        if (attribute is not null)
        {
            foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
            {
                if (argument.Key == name && argument.Value.Value is T value)
                {
                    return value;
                }
            }
        }

        return fallback;
    }
}
