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
    /// Adds separately addressable operator and cast entities for an already validated function.
    /// </summary>
    internal static void Add(IMethodSymbol method, FunctionDeclaration function, SqlEntity dependency,
        SqlGraph graph, HashSet<string> names, SourceProductionContext context)
    {
        foreach (AttributeData attribute in method.GetAttributes())
        {
            string? kind = attribute.AttributeClass?.ToDisplayString() switch
            {
                "Ankus.PgOperatorAttribute" => "operator",
                "Ankus.PgCastAttribute" => "cast",
                _ => null,
            };
            if (kind is null)
            {
                continue;
            }

            (string Sql, string Signature)? declaration = kind == "operator"
                ? CreateOperator(method, function, attribute, context)
                : CreateCast(method, function, attribute, context);
            if (declaration is not { } declared)
            {
                continue;
            }

            if (!names.Add(kind + ":" + declared.Signature))
            {
                graph.Error(method.Locations.FirstOrDefault(), "Duplicate PostgreSQL " + kind + " signature " + declared.Signature + ".");
            }

            var entity = new SqlEntity("2:" + kind + ":" + method.ToDisplayString(), declared.Sql, method.Locations.FirstOrDefault());
            entity.Dependencies.Add(dependency);
            graph.Configure(entity, attribute);
            graph.Add(entity);
        }
    }

    private static (string Sql, string Signature)? CreateOperator(IMethodSymbol method, FunctionDeclaration function,
        AttributeData attribute, SourceProductionContext context)
    {
        string? name = attribute.ConstructorArguments.FirstOrDefault().Value as string;
        string? qualified = OperatorReference(name, function.Schema);
        if (name is null || name.Contains('.') || qualified is null)
        {
            return Invalid("The operator name must contain 1-63 valid PostgreSQL operator characters, without comment starts or ambiguous trailing + or -.");
        }

        if (method.Parameters.Length is < 1 or > 2 || method.ReturnsVoid || method.Parameters.Any(static parameter => parameter.IsParams))
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

        if (method.Parameters.Length == 1 && (commutator is not null || join is not null || hashes || merges))
        {
            return Invalid("Only binary operators can declare a commutator, join estimator, Hashes, or Merges.");
        }

        if (FunctionType.Create(method.ReturnType)!.Sql != "boolean" && (negator is not null || restrict is not null || join is not null || hashes || merges))
        {
            return Invalid("Only boolean operators can declare a negator, selectivity estimators, Hashes, or Merges.");
        }

        string right = FunctionType.Create(method.Parameters[method.Parameters.Length - 1].Type)!.Sql;
        string? left = method.Parameters.Length == 2 ? FunctionType.Create(method.Parameters[0].Type)!.Sql : null;
        var options = new List<string> { "FUNCTION = " + function.QualifiedName };
        if (left is not null)
        {
            options.Add("LEFTARG = " + left);
        }

        options.Add("RIGHTARG = " + right);
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

        return ("CREATE OPERATOR " + qualified + " (" + string.Join(", ", options) + ");\n",
            qualified + "(" + (left ?? "NONE") + "," + right + ")");

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

            options.Add(option + " = " + (isOperator ? "OPERATOR(" + sql + ")" : sql));
            return true;
        }

        (string, string)? Invalid(string reason)
        {
            context.ReportDiagnostic(Diagnostic.Create(s_invalid, method.Locations.FirstOrDefault(), method.Name, reason));
            return null;
        }
    }

    private static (string Sql, string Signature)? CreateCast(IMethodSymbol method, FunctionDeclaration function,
        AttributeData attribute, SourceProductionContext context)
    {
        int castContext = attribute.ConstructorArguments.FirstOrDefault().Value is int value ? value : 0;
        if (castContext is < 0 or > 2)
        {
            return Invalid("The cast context must be Explicit, Assignment, or Implicit.");
        }

        if (method.Parameters.Length is < 1 or > 3 || method.ReturnsVoid || method.Parameters.Any(static parameter => parameter.IsParams))
        {
            return Invalid("A cast requires one to three non-variadic parameters and a non-void result.");
        }

        if (method.Parameters.Length > 1 && method.Parameters[1].Type.SpecialType != SpecialType.System_Int32 ||
            method.Parameters.Length > 2 && method.Parameters[2].Type.SpecialType != SpecialType.System_Boolean)
        {
            return Invalid("A cast's optional second parameter must be non-nullable int (type modifier), and its third must be non-nullable bool (explicit conversion).");
        }

        string source = FunctionType.Create(method.Parameters[0].Type)!.Sql;
        string target = FunctionType.Create(method.ReturnType)!.Sql;
        if (source == target && method.Parameters.Length == 1)
        {
            return Invalid("A one-parameter cast must convert between distinct PostgreSQL types; CLR aliases and nullability do not create distinct SQL types.");
        }

        string signature = source + " AS " + target;
        string arguments = string.Join(", ", method.Parameters.Select(static parameter => FunctionType.Create(parameter.Type)!.Sql));
        string suffix = castContext switch { 1 => " AS ASSIGNMENT", 2 => " AS IMPLICIT", _ => string.Empty };
        return ("CREATE CAST (" + signature + ") WITH FUNCTION " + function.QualifiedName + "(" + arguments + ")" + suffix + ";\n", signature);

        (string, string)? Invalid(string reason)
        {
            context.ReportDiagnostic(Diagnostic.Create(s_invalid, method.Locations.FirstOrDefault(), method.Name, reason));
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
