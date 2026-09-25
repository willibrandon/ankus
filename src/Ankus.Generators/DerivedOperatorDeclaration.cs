using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
    internal static bool Emit(INamedTypeSymbol type, Dictionary<string, SqlEntity> types, SqlTypeProviders providers,
        Dictionary<string, SqlEntity> schemas, HashSet<string> functions,
        HashSet<string> relatedNames, Dictionary<string, SqlEntity> operators, SqlGraph graph, SourceProductionContext context,
        bool ensureInitialized, StringBuilder managed, StringBuilder native, StringBuilder exports, out bool relocatable)
    {
        relocatable = true;
        AttributeData? equality = Attribute("Ankus.PgEqualityAttribute");
        AttributeData? ordering = Attribute("Ankus.PgOrderingAttribute");
        AttributeData? hashing = Attribute("Ankus.PgHashingAttribute");
        string managedType = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        FunctionType? value = FunctionType.Create(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated));
        types.TryGetValue(managedType, out SqlEntity? typeEntity);
        if (value is null || value.DatumType is null &&
            (value.CustomType is null && value.Enumeration is null || typeEntity is null))
        {
            return Invalid("Generated operators require a valid, accessible PgType, PgEnum or PgDatumType declaration.");
        }

        if (value.DatumType is { CanRead: false })
        {
            return Invalid("Generated operators require a datum reader for the exact declared PgDatumType.");
        }

        bool enumeration = type.TypeKind == TypeKind.Enum;
        if (!enumeration && !Implements("System.IEquatable<T>", type))
        {
            return Invalid("Generated equality, ordering and hashing require IEquatable<T> for the exact declared type.");
        }

        if (ordering is not null && !enumeration && !Implements("System.IComparable<T>", type))
        {
            return Invalid("PgOrdering requires IComparable<T> for the exact declared type.");
        }

        if (hashing is not null && !enumeration && !Implements("Ankus.IPgHashable", null))
        {
            return Invalid("PgHashing requires IPgHashable with a stable, equality-compatible GetPostgresHashCode implementation.");
        }

        string name = value.CustomType?.Name ?? value.Enumeration?.Name ?? value.DatumType!.Name;
        string? schema = value.CustomType?.Schema ?? value.Enumeration?.Schema ?? value.DatumType?.Schema;
        relocatable = schema is null;
        string sqlType = value.Sql;
        string binaryArguments = sqlType + "," + sqlType;
        string equalitySignature = Operator("=") + "(" + binaryArguments + ")";
        if (equality is null && !operators.ContainsKey(equalitySignature))
        {
            return Invalid("PgOrdering and PgHashing require PgEquality or a boolean same-schema PgOperator(\"=\") for the exact SQL type.");
        }

        using SHA256 hash = SHA256.Create();
        byte[] digest = hash.ComputeHash(Encoding.UTF8.GetBytes(type.ContainingAssembly.Identity + ":" + managedType));
        string symbol = string.Concat(digest.Take(16).Select(static item => item.ToString("x2", CultureInfo.InvariantCulture)));
        string prefix = "ankus_operator_" + symbol + "_";
        string underlying = type.EnumUnderlyingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty;

        if (equality is not null)
        {
            SqlEntity group = Group("equality", equality);
            string expression = enumeration ? "left == right" : "((global::System.IEquatable<" + managedType + ">)left).Equals(right)";
            SqlEntity equalFunction = Function("eq", expression, comparison: true, unary: false, group);
            SqlEntity unequalFunction = Function("ne", "!" + prefix + "eq(left, right)", comparison: true, unary: false, group);
            SqlEntity equalOperator = Comparison("=", "eq", "=", "<>", "eqsel", "eqjoinsel", true, equalFunction);
            SqlEntity unequalOperator = Comparison("<>", "ne", "<>", "=", "neqsel", "neqjoinsel", false, unequalFunction);
            group.Dependencies.UnionWith([equalOperator, unequalOperator]);
        }

        SqlEntity equalityOperator = operators[equalitySignature];
        if (ordering is not null)
        {
            SqlEntity group = Group("ordering", ordering);
            group.Dependencies.Add(equalityOperator);
            string expression = enumeration
                ? "((" + underlying + ")left).CompareTo((" + underlying + ")right)"
                : "((global::System.IComparable<" + managedType + ">)left).CompareTo(right)";
            SqlEntity comparison = Function("cmp", expression, comparison: false, unary: false, group);
            (string Token, string Role, string Commutator, string Negator, string Restrict, string Join)[] relations =
            [
                ("<", "lt", ">", ">=", "scalarltsel", "scalarltjoinsel"),
                (">", "gt", "<", "<=", "scalargtsel", "scalargtjoinsel"),
                ("<=", "le", ">=", ">", "scalarlesel", "scalarlejoinsel"),
                (">=", "ge", "<=", "<", "scalargesel", "scalargejoinsel"),
            ];
            foreach ((string token, string role, string commutator, string negator, string restrict, string join) in relations)
            {
                SqlEntity function = Function(role, prefix + "cmp(left, right) " + token + " 0", comparison: true, unary: false, group);
                group.Dependencies.Add(Comparison(token, role, commutator, negator, restrict, join, false, function));
            }

            group.Dependencies.Add(comparison);
            string family = Name("btree_ops");
            group.Sql = "CREATE OPERATOR FAMILY " + family + " USING btree;\n" +
                "CREATE OPERATOR CLASS " + family + " DEFAULT FOR TYPE " + sqlType + " USING btree FAMILY " + family + " AS\n" +
                "    OPERATOR 1 " + Operator("<") + " (" + binaryArguments + "),\n" +
                "    OPERATOR 2 " + Operator("<=") + " (" + binaryArguments + "),\n" +
                "    OPERATOR 3 " + Operator("=") + " (" + binaryArguments + "),\n" +
                "    OPERATOR 4 " + Operator(">=") + " (" + binaryArguments + "),\n" +
                "    OPERATOR 5 " + Operator(">") + " (" + binaryArguments + "),\n" +
                "    FUNCTION 1 " + Name("cmp") + "(" + binaryArguments + ");\n";
            relocatable &= SqlGeneration.Apply(ordering, group, [], [("@COMPARISON_FUNCTION_SQL@", Name("cmp"))], graph);
        }

        if (hashing is not null)
        {
            SqlEntity group = Group("hashing", hashing);
            group.Dependencies.Add(equalityOperator);
            string expression = enumeration
                ? "global::Ankus.PgHash.Compute(unchecked((ulong)left))"
                : "((global::Ankus.IPgHashable)left).GetPostgresHashCode()";
            group.Dependencies.Add(Function("hash", expression, comparison: false, unary: true, group));
            string family = Name("hash_ops");
            group.Sql = "CREATE OPERATOR FAMILY " + family + " USING hash;\n" +
                "CREATE OPERATOR CLASS " + family + " DEFAULT FOR TYPE " + sqlType + " USING hash FAMILY " + family + " AS\n" +
                "    OPERATOR 1 " + Operator("=") + " (" + binaryArguments + "),\n" +
                "    FUNCTION 1 " + Name("hash") + "(" + sqlType + ");\n";
            relocatable &= SqlGeneration.Apply(hashing, group, [], [("@HASH_FUNCTION_SQL@", Name("hash"))], graph);
        }

        return true;

        AttributeData? Attribute(string attributeName) => type.GetAttributes().FirstOrDefault(item => item.AttributeClass?.ToDisplayString() == attributeName);

        bool Implements(string definition, INamedTypeSymbol? argument) => type.AllInterfaces.Any(contract =>
            contract.OriginalDefinition.ToDisplayString() == definition &&
            (argument is null || contract.TypeArguments.Length == 1 && SymbolEqualityComparer.Default.Equals(contract.TypeArguments[0], argument)));

        bool Invalid(string message)
        {
            context.ReportDiagnostic(Diagnostic.Create(s_invalid, type.Locations.FirstOrDefault(), type.Name, message));
            return false;
        }

        string Qualify(string identifier) => (schema is null ? string.Empty : SqlText.Identifier(schema) + ".") + SqlText.Identifier(identifier);

        string Name(string role) => Qualify(Encoding.UTF8.GetByteCount(name) + role.Length + 1 <= 63
            ? name + "_" + role : "ankus_" + symbol + "_" + role);

        string Operator(string token) => (schema is null ? string.Empty : SqlText.Identifier(schema) + ".") + token;

        SqlEntity Group(string role, AttributeData attribute)
        {
            var entity = new SqlEntity("3:derived-" + role + ":" + managedType, string.Empty, type.Locations.FirstOrDefault());
            RequireType(entity);
            graph.Configure(entity, attribute);
            graph.Add(entity);
            return entity;
        }

        SqlEntity Function(string role, string expression, bool comparison, bool unary, SqlEntity group)
        {
            FunctionType result = comparison ? FunctionType.ComparisonResult() : FunctionType.IndexSupportResult();
            FunctionType[] parameters = unary ? [value] : [value, value];
            string helper = prefix + role;
            managed.AppendLine($"    internal static {result.Managed} {helper}({managedType} left" + (unary ? string.Empty : $", {managedType} right") + ")");
            managed.AppendLine("        => " + expression + ";");
            managed.AppendLine();
            string invocation = helper + "(" + ManagedConversion.Read(value, "arguments[0]", string.Empty) +
                (unary ? string.Empty : ", " + ManagedConversion.Read(value, "arguments[1]", string.Empty)) + ")";
            string callback = "ankus_managed_operator_" + symbol + "_" + role;
            string nativeName = "ankus_fn_operator_" + symbol + "_" + role;
            PgFunctionEmitter.EmitManaged(callback, result, invocation, string.Empty, false, managed);
            PgFunctionEmitter.EmitNative(nativeName, callback, parameters, result, ensureInitialized, native);
            exports.AppendLine(nativeName);
            exports.AppendLine("pg_finfo_" + nativeName);
            string signature = Name(role) + "(" + (unary ? sqlType : binaryArguments) + ")";
            if (!functions.Add(signature))
            {
                graph.Error(type.Locations.FirstOrDefault(), "Duplicate PostgreSQL function signature " + signature + ".");
            }

            string sql = "CREATE FUNCTION " + signature + " RETURNS " + result.Sql + " AS 'MODULE_PATHNAME', '" + nativeName +
                "' LANGUAGE c IMMUTABLE PARALLEL SAFE STRICT;\n";
            var entity = new SqlEntity("1:derived-function:" + managedType + ":" + role, sql, type.Locations.FirstOrDefault());
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

        SqlEntity Comparison(string token, string role, string commutator, string negator, string restrict, string join, bool equal, SqlEntity function)
        {
            string signature = Operator(token) + "(" + binaryArguments + ")";
            if (!relatedNames.Add("operator:" + signature))
            {
                graph.Error(type.Locations.FirstOrDefault(), "Duplicate PostgreSQL operator signature " + signature + ".");
            }

            string sql = "CREATE OPERATOR " + Operator(token) + " (FUNCTION = " + Name(role) + ", LEFTARG = " + sqlType +
                ", RIGHTARG = " + sqlType + ", COMMUTATOR = OPERATOR(" + Operator(commutator) + "), NEGATOR = OPERATOR(" +
                Operator(negator) + "), RESTRICT = pg_catalog." + restrict + ", JOIN = pg_catalog." + join + (equal ? ", HASHES, MERGES" : string.Empty) + ");\n";
            var entity = new SqlEntity("2:derived-operator:" + managedType + ":" + role, sql, type.Locations.FirstOrDefault());
            entity.Dependencies.Add(function);
            operators[signature] = entity;
            graph.Add(entity);
            return entity;
        }
    }
}
