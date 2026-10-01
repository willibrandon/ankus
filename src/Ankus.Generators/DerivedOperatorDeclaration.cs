using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Binds declared value semantics to generated PostgreSQL operators and default index operator classes.
/// </summary>
internal static class DerivedOperatorDeclaration
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS018", "Invalid generated PostgreSQL operators", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Identifies explicit operator-generation attributes without requiring a valid storage declaration.
    /// </summary>
    internal static bool IsAttribute(AttributeData attribute) => attribute.AttributeClass?.ToDisplayString() is
        "Ankus.PgEqualityAttribute" or "Ankus.PgOrderingAttribute" or "Ankus.PgHashingAttribute";

    /// <summary>
    /// Emits statically bound helpers through the shared scalar boundary and adds their installation dependencies.
    /// </summary>
    internal static bool Emit(DerivedOperatorModel model, Dictionary<string, SqlEntity> types, SqlTypeProviders providers,
        Dictionary<string, SqlEntity> schemas, HashSet<string> functions,
        HashSet<string> relatedNames, Dictionary<string, SqlEntity> operators, SqlGraph graph, GeneratorDiagnostics context,
        GeneratorSourceResolver compilation, IReadOnlyDictionary<string, DerivedHelperEmission> helpers, DerivedSqlEmission? sql, bool ensureInitialized,
        GeneratorSourceBuilder managed, GeneratorSourceBuilder native, GeneratorSourceBuilder exports, out bool relocatable)
    {
        relocatable = true;
        SqlDeclarationOptions? equality = model.Equality;
        SqlDeclarationOptions? ordering = model.Ordering;
        SqlDeclarationOptions? hashing = model.Hashing;
        string managedType = model.Managed;
        FunctionType? value = model.Value;
        types.TryGetValue(managedType, out SqlEntity? typeEntity);
        if (value is null || sql is null || value.DatumType is null &&
            (value.CustomType is null && value.Enumeration is null || typeEntity is null))
        {
            return Invalid("Generated operators require a valid, accessible PgType, PgEnum or PgDatumType declaration.");
        }

        if (model.Error is not null)
        {
            return Invalid(model.Error);
        }

        string? schema = value.CustomType?.Schema ?? value.Enumeration?.Schema ?? value.DatumType?.Schema;
        relocatable = schema is null;
        string equalitySignature = sql.EqualitySignature.Compose(providers).Replace("\0", string.Empty);
        if (equality is null && !operators.ContainsKey(equalitySignature))
        {
            return Invalid("PgOrdering and PgHashing require PgEquality or a boolean same-schema PgOperator(\"=\") for the exact SQL type.");
        }

        Dictionary<string, DerivedSqlEmission.Function> sqlFunctions = sql.Functions.ToDictionary(static function => function.Role, StringComparer.Ordinal);
        Dictionary<string, DerivedSqlEmission.Comparison> sqlOperators = sql.Operators.ToDictionary(static comparison => comparison.Role, StringComparer.Ordinal);

        if (equality is not null)
        {
            SqlEntity group = Group("equality", equality);
            SqlEntity equalFunction = Function("eq", group);
            SqlEntity unequalFunction = Function("ne", group);
            SqlEntity equalOperator = Comparison("eq", equalFunction);
            SqlEntity unequalOperator = Comparison("ne", unequalFunction);
            group.Dependencies.UnionWith([equalOperator, unequalOperator]);
        }

        SqlEntity equalityOperator = operators[equalitySignature];
        if (ordering is not null)
        {
            SqlEntity group = Group("ordering", ordering);
            group.Dependencies.Add(equalityOperator);
            SqlEntity comparison = Function("cmp", group);
            foreach (string role in new[] { "lt", "gt", "le", "ge" })
            {
                SqlEntity function = Function(role, group);
                group.Dependencies.Add(Comparison(role, function));
            }

            group.Dependencies.Add(comparison);
            Family(group, sql.Ordering!);
            relocatable &= SqlGeneration.ApplyOptions(ordering, group, [], [("@COMPARISON_FUNCTION_SQL@", sqlFunctions["cmp"].Name)], graph);
        }

        if (hashing is not null)
        {
            SqlEntity group = Group("hashing", hashing);
            group.Dependencies.Add(equalityOperator);
            group.Dependencies.Add(Function("hash", group));
            Family(group, sql.Hashing!);
            relocatable &= SqlGeneration.ApplyOptions(hashing, group, [], [("@HASH_FUNCTION_SQL@", sqlFunctions["hash"].Name)], graph);
        }

        return true;

        bool Invalid(string message)
        {
            context.Report(s_invalid, model.Location?.Resolve(compilation), model.DiagnosticName, message);
            return false;
        }

        void Family(SqlEntity group, DerivedSqlEmission.Family family)
        {
            group.SelectionNames.UnionWith([family.Identifier, family.Name.Replace("\0", string.Empty)]);
            group.Attachments.UnionWith(["OPERATOR FAMILY " + family.Name + " USING " + family.AccessMethod,
                "OPERATOR CLASS " + family.Name + " USING " + family.AccessMethod]);
            group.Sql = family.Definition.Compose(providers);
        }

        SqlEntity Group(string role, SqlDeclarationOptions options)
        {
            var entity = new SqlEntity("3:derived-" + role + ":" + managedType, string.Empty, model.Location?.Resolve(compilation)) { Kind = role };
            entity.SelectionNames.Add(model.Display + "." + role);
            RequireType(entity);
            graph.ConfigureOptions(entity, options);
            graph.Add(entity);
            graph.Register(model.Identity, model.Display, entity);
            return entity;
        }

        SqlEntity Function(string role, SqlEntity group)
        {
            DerivedHelperEmission emission = helpers[role];
            DerivedSqlEmission.Function definition = sqlFunctions[role];
            managed.Append(emission.Managed);
            emission.Native.AppendTo(native, ensureInitialized);
            exports.Append(emission.Exports);
            string signature = definition.Signature.Compose(providers);
            if (!functions.Add(signature.Replace("\0", string.Empty)))
            {
                graph.Error(model.Location?.Resolve(compilation), "Duplicate PostgreSQL function signature " + signature + ".");
            }

            var entity = new SqlEntity("1:derived-function:" + managedType + ":" + role, definition.Definition.Compose(providers),
                model.Location?.Resolve(compilation)) { Kind = "function", Owner = group };
            entity.SelectionNames.UnionWith([definition.Identifier, definition.Name.Replace("\0", string.Empty), signature.Replace("\0", string.Empty)]);
            entity.Attachments.Add("FUNCTION " + signature);
            if (schema is not null)
            {
                entity.SelectionNames.Add(schema + "." + definition.Identifier);
            }

            RequireType(entity);
            entity.Requires.UnionWith(group.Requires);
            graph.Add(entity);
            return entity;
        }

        void RequireType(SqlEntity entity)
        {
            if (typeEntity is not null)
            {
                entity.Dependencies.Add(typeEntity);
            }

            providers.Require(entity, value, requireComplete: true);
            if (schema is not null && schemas.TryGetValue(schema, out SqlEntity? schemaEntity))
            {
                entity.Dependencies.Add(schemaEntity);
            }
        }

        SqlEntity Comparison(string role, SqlEntity function)
        {
            DerivedSqlEmission.Comparison definition = sqlOperators[role];
            string signature = definition.Signature.Compose(providers);
            if (!relatedNames.Add("operator:" + signature.Replace("\0", string.Empty)))
            {
                graph.Error(model.Location?.Resolve(compilation), "Duplicate PostgreSQL operator signature " + signature + ".");
            }

            var entity = new SqlEntity("2:derived-operator:" + managedType + ":" + role, definition.Definition.Compose(providers),
                model.Location?.Resolve(compilation)) { Kind = "operator", Owner = function.Owner };
            entity.SelectionNames.UnionWith([definition.Token, signature.Replace("\0", string.Empty)]);
            entity.Attachments.Add("OPERATOR " + signature);
            entity.Dependencies.Add(function);
            operators[signature.Replace("\0", string.Empty)] = entity;
            graph.Add(entity);
            return entity;
        }
    }
}
