using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Validates operator and cast contracts and adds their backing-function dependencies to installation SQL.
/// </summary>
internal static class OperatorCastDeclaration
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS007", "Invalid PostgreSQL operator or cast", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Adds cached operator and cast declarations with current source attribution, providers and graph policy.
    /// </summary>
    /// <param name="outputs">The independently analyzed and rendered attached declarations.</param>
    /// <param name="dependency">The current backing-function graph node.</param>
    /// <param name="graph">The current installation graph.</param>
    /// <param name="names">The current operator/cast signature inventory.</param>
    /// <param name="context">The diagnostic destination.</param>
    /// <param name="operators">The boolean-operator inventory for related declarations.</param>
    /// <param name="providers">The current catalog provider inventory.</param>
    /// <param name="compilation">The current compilation used to reattach diagnostic coordinates.</param>
    /// <returns>The current attached nodes and their prerequisites.</returns>
    internal static List<SqlEntity> Add(IEnumerable<OperatorCastPipeline.Output> outputs, SqlEntity dependency,
        SqlGraph graph, HashSet<string> names, GeneratorDiagnostics context, Dictionary<string, SqlEntity> operators,
        SqlTypeProviders providers, GeneratorSourceResolver compilation)
    {
        var result = new List<SqlEntity>();
        foreach (OperatorCastPipeline.Output output in outputs)
        {
            OperatorCastPipeline.Analysis analysis = output.Analysis;
            foreach (GeneratorProblem problem in analysis.Problems)
            {
                problem.Report(compilation, context);
            }

            if (output.Emission is null)
            {
                continue;
            }

            Location? location = analysis.Location?.Resolve(compilation);
            if (!names.Add(analysis.Kind + ":" + analysis.Signature))
            {
                graph.Error(location, "Duplicate PostgreSQL " + analysis.Kind + " signature " + analysis.Signature + ".");
            }

            (string sql, string attachment) = output.Emission.Compose(providers);
            var entity = new SqlEntity("2:" + analysis.Kind + ":" + analysis.Display, sql, location) { Kind = analysis.Kind };
            entity.SelectionNames.UnionWith(analysis.Names);
            entity.Attachments.Add(attachment);
            entity.Dependencies.Add(dependency);
            graph.ConfigureOptions(entity, analysis.Options);
            graph.Add(entity);
            graph.Register(analysis.Identity, analysis.Display, entity);
            result.Add(entity);
            if (analysis.BooleanOperator)
            {
                operators[analysis.Signature!] = entity;
            }
        }

        return result;
    }

    /// <summary>
    /// Validates one attached declaration using the common backing-function contracts and detached diagnostics.
    /// </summary>
    /// <param name="method">The semantically analyzed backing method.</param>
    /// <param name="function">The validated backing declaration and conversion contracts.</param>
    /// <param name="attribute">The authored operator or cast attribute.</param>
    /// <param name="compilation">The analyzed compilation owning source coordinates.</param>
    /// <param name="cancellationToken">The current semantic-analysis cancellation token.</param>
    /// <returns>The immutable catalog declaration, policy and diagnostics.</returns>
    internal static OperatorCastPipeline.Analysis Analyze(IMethodSymbol method, FunctionPipeline.FunctionAnalysis function,
        AttributeData attribute, Compilation compilation, CancellationToken cancellationToken)
    {
        string kind = attribute.AttributeClass?.Name == "PgOperatorAttribute" ? "operator" : "cast";
        var problems = new List<GeneratorProblem>();
        var diagnostics = new GeneratorDiagnostics((descriptor, location, arguments) =>
            problems.Add(new(descriptor, GeneratorLocation.Create(location, compilation), new(arguments))), cancellationToken);
        FunctionPipeline.FunctionModel conversion = function.Model!;
        FunctionParameter[] parameters = [.. conversion.Parameters.Where(static parameter => !parameter.IsInjected)];
        OperatorCastModel? model = null;
        if (conversion.Set is not null)
        {
            diagnostics.Report(s_invalid, method.Locations.FirstOrDefault(), method.Name, "Operators and casts cannot return sets.");
        }
        else
        {
            model = kind == "operator" ? CreateOperator(method, parameters, function.Declaration!, attribute, diagnostics)
                : CreateCast(method, parameters, function.Declaration!, attribute, diagnostics);
        }

        string signature = model?.Operator is { } operation
            ? operation.Name + "(" + (operation.Left?.Sql ?? "NONE") + "," + operation.Right.Sql + ")"
            : model?.Cast is { } cast ? cast.Arguments[0].Sql + " AS " + cast.Result.Sql : string.Empty;
        var names = new List<string>();
        if (model is not null)
        {
            names.Add(signature);
            if (model.Operator is not null)
            {
                string name = (string)attribute.ConstructorArguments[0].Value!;
                names.Add(name);
                if (function.Declaration!.Schema is { } schema)
                {
                    names.Add(schema + "." + name);
                }
            }
        }

        return new(function.Identity, method.ToDisplayString(), kind, signature, new(names),
            kind == "operator" && conversion.Result?.Sql == "boolean", model, SqlDeclarationOptions.Read(attribute)!,
            new(problems), GeneratorLocation.Create(method.Locations.FirstOrDefault(), compilation));
    }

    private static OperatorCastModel? CreateOperator(IMethodSymbol method, FunctionParameter[] parameters, FunctionDeclaration function,
        AttributeData attribute, GeneratorDiagnostics context)
    {
        string? name = attribute.ConstructorArguments.FirstOrDefault().Value as string;
        string? qualified = OperatorReference(name, function.Schema);
        if (name is null || name.Contains('.') || qualified is null)
        {
            return Invalid("The operator name must contain 1-63 valid PostgreSQL operator characters, without comment starts or ambiguous trailing + or -.");
        }

        if (parameters.Length is < 1 or > 2 || method.ReturnsVoid || parameters.Any(static parameter => parameter.IsParams))
        {
            return Invalid("An operator requires one prefix operand or two binary operands, no variadic parameters, and a non-void result.");
        }

        string? commutator = AttributeValues.Get<string?>(attribute, "Commutator", null);
        string? negator = AttributeValues.Get<string?>(attribute, "Negator", null);
        string? restrict = AttributeValues.Get<string?>(attribute, "RestrictionEstimator", null);
        string? join = AttributeValues.Get<string?>(attribute, "JoinEstimator", null);
        bool hashes = AttributeValues.Get(attribute, "Hashes", false);
        bool merges = AttributeValues.Get(attribute, "Merges", false);
        if (negator is not null && OperatorReference(negator, function.Schema) == qualified)
        {
            return Invalid("An operator cannot be its own negator.");
        }

        if (parameters.Length == 1 && (commutator is not null || join is not null || hashes || merges))
        {
            return Invalid("Only binary operators can declare a commutator, join estimator, Hashes, or Merges.");
        }

        if (FunctionType.CreateResult(method)!.Sql != "boolean" && (negator is not null || restrict is not null || join is not null || hashes || merges))
        {
            return Invalid("Only boolean operators can declare a negator, selectivity estimators, Hashes, or Merges.");
        }

        string? left = parameters.Length == 2 ? parameters[0].Type!.Sql : null;
        var options = new List<string>();
        if (!AddReference("COMMUTATOR", commutator, true) || !AddReference("NEGATOR", negator, true) ||
            !AddReference("RESTRICT", restrict, false) || !AddReference("JOIN", join, false))
        {
            return Invalid("Operator references require an operator name and optional schema; estimator references require a function identifier and optional schema.");
        }

        if (hashes)
        {
            options.Add("HASHES");
        }

        if (merges)
        {
            options.Add("MERGES");
        }

        string templateName = (function.Schema is null ? "\0" : string.Empty) + qualified;
        return new(new(qualified, templateName, function.TemplateName,
            left is null ? null : SqlTypeTemplate.Create(parameters[0].Type!),
            SqlTypeTemplate.Create(parameters[parameters.Length - 1].Type!), new(options)), null);

        bool AddReference(string option, string? reference, bool isOperator)
        {
            if (reference is null)
            {
                return true;
            }

            string? sql = isOperator ? OperatorReference(reference, function.Schema) : FunctionReference(reference);
            if (sql is null)
            {
                return false;
            }

            options.Add(option + " = " + (isOperator ? "OPERATOR(" +
                (function.Schema is null && !reference.Contains('.') ? "\0" : string.Empty) + sql + ")" : sql));
            return true;
        }

        OperatorCastModel? Invalid(string reason)
        {
            context.Report(s_invalid, method.Locations.FirstOrDefault(), method.Name, reason);
            return null;
        }
    }

    private static OperatorCastModel? CreateCast(IMethodSymbol method, FunctionParameter[] parameters, FunctionDeclaration function,
        AttributeData attribute, GeneratorDiagnostics context)
    {
        int castContext = attribute.ConstructorArguments.FirstOrDefault().Value is int value ? value : 0;
        if (castContext is < 0 or > 2)
        {
            return Invalid("The cast context must be Explicit, Assignment, or Implicit.");
        }

        if (parameters.Length is < 1 or > 3 || method.ReturnsVoid || parameters.Any(static parameter => parameter.IsParams))
        {
            return Invalid("A cast requires one to three non-variadic parameters and a non-void result.");
        }

        if (parameters.Length > 1 && parameters[1].DeclaredSpecialType != SpecialType.System_Int32 ||
            parameters.Length > 2 && parameters[2].DeclaredSpecialType != SpecialType.System_Boolean)
        {
            return Invalid("A cast's optional second parameter must be non-nullable int (type modifier), and its third must be non-nullable bool (explicit conversion).");
        }

        string source = parameters[0].Type!.Sql;
        string target = FunctionType.CreateResult(method)!.Sql;
        if (source == "record" || target == "record")
        {
            return Invalid("PostgreSQL casts cannot use the record pseudo-type; bind composite source and result values with PgCompositeType.");
        }

        if (source == target && parameters.Length == 1)
        {
            return Invalid("A one-parameter cast must convert between distinct PostgreSQL types; CLR aliases and nullability do not create distinct SQL types.");
        }

        return new(null, new(function.TemplateName,
            new(parameters.Select(static parameter => SqlTypeTemplate.Create(parameter.Type!))),
            SqlTypeTemplate.Create(FunctionType.CreateResult(method)!), castContext));

        OperatorCastModel? Invalid(string reason)
        {
            context.Report(s_invalid, method.Locations.FirstOrDefault(), method.Name, reason);
            return null;
        }
    }

    private static string? FunctionReference(string value)
    {
        string[] parts = value.Split('.');
        return parts.Length is >= 1 and <= 2 && parts.All(SqlText.IsIdentifier)
            ? string.Join(".", parts.Select(SqlText.Identifier)) : null;
    }

    private static string? OperatorReference(string? value, string? defaultSchema)
    {
        if (value is null)
        {
            return null;
        }

        string[] parts = value.Split('.');
        if (parts.Length is < 1 or > 2 || (parts.Length == 2 && !SqlText.IsIdentifier(parts[0])))
        {
            return null;
        }

        string name = parts[parts.Length - 1];
        if (name.Length is < 1 or > 63 || name == "=>" || name.Any(static c => "~!@#^&|`?+-*/%<>=".IndexOf(c) < 0) ||
            name.Contains("--") || name.Contains("/*") ||
            (name.Length > 1 && name[name.Length - 1] is '+' or '-' && !name.Any(static c => "~!@#^&|`?%".IndexOf(c) >= 0)))
        {
            return null;
        }

        // The PostgreSQL lexer uses <> for both spellings of inequality.
        name = name == "!=" ? "<>" : name;
        string? schema = parts.Length == 2 ? parts[0] : defaultSchema;
        return (schema is null ? string.Empty : SqlText.Identifier(schema) + ".") + name;
    }
}
