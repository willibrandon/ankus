using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Resolves and validates SQL declaration options without evaluating extension code.
/// </summary>
internal sealed class FunctionDeclaration
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS004", "Invalid PostgreSQL declaration", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Gets the fixed schema, or null to use the extension's installation schema.
    /// </summary>
    internal string? Schema { get; private set; }

    /// <summary>
    /// Gets the quoted function name, qualified when a fixed schema is declared.
    /// </summary>
    internal string QualifiedName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the named SQL argument declarations, including variadic and default clauses.
    /// </summary>
    internal string Arguments { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the validated SQL execution options.
    /// </summary>
    internal string Options { get; private set; } = string.Empty;

    /// <summary>
    /// Gets whether the declaration replaces an existing compatible function.
    /// </summary>
    internal bool Replace { get; private set; }

    /// <summary>
    /// Gets whether PostgreSQL skips this function when any SQL input is null.
    /// </summary>
    internal bool Strict { get; private set; }

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
    internal static FunctionDeclaration? Create(IMethodSymbol method, string name, SourceProductionContext context, SetResult? set = null,
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
                schema = schemaAttribute.ConstructorArguments[0].Value as string;
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

        declaration.Schema = schema;
        declaration.QualifiedName = (schema is null ? string.Empty : SqlText.Identifier(schema) + ".") + SqlText.Identifier(name);
        declaration.Replace = Value(attribute, "CreateOrReplace", false);
        declaration.Strict = nullInput == 1 || (nullInput == 0 && allRequired);
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

        declaration.Options = string.Join(" ", options);
        if (contextParameter)
        {
            return declaration;
        }

        var parameters = new List<string>();
        var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        bool defaultSeen = false;
        foreach (FunctionParameter model in parameterModels ?? FunctionParameter.Create(method))
        {
            IParameterSymbol parameter = model.Symbol;
            if (model.IsInjected)
            {
                if (parameter.GetAttributes().Any(static value => value.AttributeClass?.ToDisplayString() == "Ankus.PgParameterAttribute"))
                {
                    return Invalid($"An injected {parameter.Type.Name} has no SQL parameter name or default; remove PgParameter from it.");
                }

                continue;
            }

            FunctionType type = model.Type!;
            AttributeData? parameterAttribute = parameter.GetAttributes().FirstOrDefault(static value =>
                value.AttributeClass?.ToDisplayString() == "Ankus.PgParameterAttribute");
            string parameterName = Value<string?>(parameterAttribute, "Name", null) ?? SqlText.SnakeCase(parameter.Name);
            if (!SqlText.IsIdentifier(parameterName) || !parameterNames.Add(parameterName))
            {
                return Invalid("SQL parameter names must be distinct identifiers of at most 63 UTF-8 bytes.");
            }

            if (set?.Names?.Contains(parameterName, StringComparer.Ordinal) == true)
            {
                return Invalid("Input and TABLE output parameters must have distinct SQL names.");
            }

            string? expression = Value<string?>(parameterAttribute, "Default", null);
            if (expression is not null && (string.IsNullOrWhiteSpace(expression) || !SqlText.IsText(expression)))
            {
                return Invalid("A SQL default must be a nonempty expression with valid Unicode and no zero characters.");
            }

            if (expression is null && parameter.HasExplicitDefaultValue)
            {
                expression = ParameterDefault.Create(parameter, type);
                if (expression is null)
                {
                    return Invalid($"The optional default for '{parameter.Name}' needs an explicit PgParameter.Default SQL expression.");
                }
            }

            if (defaultSeen && expression is null)
            {
                return Invalid("Every input parameter after a defaulted parameter must also declare a SQL default.");
            }

            defaultSeen |= expression is not null;
            parameters.Add((parameter.IsParams ? "VARIADIC " : string.Empty) + SqlText.Identifier(parameterName) + " " + type.Sql +
                (expression is null ? string.Empty : " DEFAULT (" + expression + ")"));
        }

        declaration.Arguments = string.Join(", ", parameters);
        return declaration;

        FunctionDeclaration? Invalid(string reason)
        {
            context.ReportDiagnostic(Diagnostic.Create(s_invalid, method.Locations.FirstOrDefault(), method.Name, reason));
            return null;
        }
    }

    /// <summary>
    /// Validates and resolves an explicitly attributed schema, including classes without generated functions.
    /// </summary>
    /// <param name="type">The schema-bearing class.</param>
    /// <param name="context">The generator context receiving invalid-schema diagnostics.</param>
    /// <returns>The schema identifier and creation policy, or null after an invalid declaration.</returns>
    internal static (string Name, bool Create)? ReadSchema(INamedTypeSymbol type, SourceProductionContext context)
    {
        AttributeData attribute = type.GetAttributes().First(static value => value.AttributeClass?.ToDisplayString() == "Ankus.PgSchemaAttribute");
        string? name = attribute.ConstructorArguments[0].Value as string;
        bool create = Value(attribute, "Create", true);
        if (!SqlText.IsIdentifier(name) || (create && name!.StartsWith("pg_", StringComparison.OrdinalIgnoreCase)))
        {
            context.ReportDiagnostic(Diagnostic.Create(s_invalid, type.Locations.FirstOrDefault(), type.Name,
                "A fixed schema must be a nonempty identifier of at most 63 UTF-8 bytes outside the reserved pg_ namespace."));
            return null;
        }

        return (name!, create);
    }

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
