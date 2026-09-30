using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

internal sealed partial class SqlGraph
{
    private static readonly DiagnosticDescriptor s_invalidReference = new(
        "ANKUS026", "Invalid managed SQL dependency reference", "{0}", "Ankus", DiagnosticSeverity.Error,
        isEnabledByDefault: true, helpLinkUri: "https://willibrandon.github.io/ankus/custom-sql/#reference-managed-declarations");
    private static readonly DiagnosticDescriptor s_invalidSupport = new(
        "ANKUS027", "Invalid planner support function", "{0}", "Ankus", DiagnosticSeverity.Error,
        isEnabledByDefault: true, helpLinkUri: "https://willibrandon.github.io/ankus/function-declarations/#planner-support-functions");
    private readonly Dictionary<ISymbol, List<SqlEntity>> _declarations = new(SymbolEqualityComparer.Default);
    private readonly List<(SqlEntity Source, SqlEntity Target)> _inheritedRequirements = [];

    /// <summary>
    /// Keeps aggregate prerequisites ahead of every helper, including helpers shared by multiple aggregates.
    /// </summary>
    internal void InheritRequirements(SqlEntity source, SqlEntity target) => _inheritedRequirements.Add((source, target));

    /// <summary>
    /// Associates an exact managed declaration with a SQL node, including shared schema aliases.
    /// </summary>
    internal void Register(ISymbol declaration, SqlEntity entity)
    {
        entity.ManagedSources.Add(declaration.ToDisplayString());
        if (!_declarations.TryGetValue(declaration, out List<SqlEntity>? entities))
        {
            entities = [];
            _declarations.Add(declaration, entities);
        }

        if (!entities.Contains(entity))
        {
            entities.Add(entity);
        }
    }

    /// <summary>
    /// Identifies explicit dependencies whose destination is a C# declaration rather than a string ID.
    /// </summary>
    internal static bool IsReference(AttributeData attribute)
        => attribute.AttributeClass?.ToDisplayString() is "Ankus.PgRequiresAttribute" or "Ankus.PgBeforeAttribute" or "Ankus.PgSupportFunctionAttribute";

    /// <summary>
    /// Resolves typed edges after all declarations exist and before ordering or encoding the graph.
    /// </summary>
    internal void ResolveReferences(IEnumerable<ISymbol> declarations)
    {
        foreach (ISymbol declaration in declarations.Distinct(SymbolEqualityComparer.Default)
            .OrderBy(static symbol => symbol.ToDisplayString(), StringComparer.Ordinal))
        {
            foreach (AttributeData attribute in declaration.GetAttributes().Where(IsReference))
            {
                _context.CancellationToken.ThrowIfCancellationRequested();
                bool plannerSupport = attribute.AttributeClass!.Name == "PgSupportFunctionAttribute";
                if (plannerSupport && (attribute.ConstructorArguments.Length != 2 || attribute.ConstructorArguments[1].Value is not string))
                {
                    ReferenceError(attribute, "A planner support reference requires a non-null method name.");
                    continue;
                }

                SqlEntity? source = Source(declaration, attribute);
                if (source is null)
                {
                    continue;
                }

                SqlEntity? target = Target(attribute);
                if (target is null)
                {
                    continue;
                }

                if (plannerSupport)
                {
                    BindPlannerSupport(declaration, attribute, source, target);
                }
                else if (attribute.AttributeClass!.Name == "PgBeforeAttribute")
                {
                    target.DeclaredDependencies.Add(source);
                }
                else
                {
                    source.RequiredDeclarations.Add(target);
                }
            }
        }

        foreach ((SqlEntity source, SqlEntity target) in _inheritedRequirements)
        {
            target.Requires.UnionWith(source.Requires.Where(required => !target.Names.Contains(required)));
            target.RequiredDeclarations.UnionWith(source.RequiredDeclarations.Where(required => required != target));
        }
    }

    private void BindPlannerSupport(ISymbol declaration, AttributeData attribute, SqlEntity source, SqlEntity target)
    {
        if (source.Function is null)
        {
            ReferenceError(attribute, "PgSupportFunction must annotate a generated PostgreSQL function.");
            return;
        }

        AttributeData? function = declaration.GetAttributes().FirstOrDefault(static candidate =>
            candidate.AttributeClass?.ToDisplayString() == "Ankus.PgFunctionAttribute");
        if (function is not null && AttributeValues.Get<string?>(function, "SupportFunction", null) is not null)
        {
            ReferenceError(attribute, "Choose either PgSupportFunction or the external PgFunction.SupportFunction SQL name, not both.");
            return;
        }

        if (target.Function is not { IsPlannerSupport: true } support)
        {
            ReferenceError(attribute, "A planner support function must take exactly one nonvariadic SQL internal argument and return scalar SQL internal.");
            return;
        }

        if (support.RequiresAggregateContext)
        {
            ReferenceError(attribute, "An aggregate helper requires an aggregate invocation and cannot serve as planner support; select an ordinary PgFunction method.");
            return;
        }

        source.RequiredDeclarations.Add(target);
        source.Function.Declaration.SetPlannerSupport(support.Declaration.TemplateName);
    }

    private SqlEntity? Source(ISymbol declaration, AttributeData attribute)
    {
        string? id = AttributeValues.Get<string?>(attribute, "DeclarationId", null);
        _declarations.TryGetValue(declaration, out List<SqlEntity>? entities);
        if (id is not null)
        {
            if (!ValidName(id))
            {
                return ReferenceError(attribute, "DeclarationId must be nonempty text without zero characters or invalid Unicode.");
            }

            SqlEntity[] matches = [.. (declaration is IAssemblySymbol ? _entities : entities ?? [])
                .Where(entity => entity.Names.Contains(id))];
            return matches.Length == 1 ? matches[0] : ReferenceError(attribute,
                $"DeclarationId '{id}' must identify exactly one SQL declaration belonging to the attributed declaration.");
        }

        if (declaration is IAssemblySymbol)
        {
            return ReferenceError(attribute, "An assembly-level SQL dependency requires DeclarationId to identify the declaration being ordered.");
        }

        return Primary(declaration, attribute);
    }

    private SqlEntity? Target(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length != 2 ||
            attribute.ConstructorArguments[0].Value is not INamedTypeSymbol { TypeKind: not TypeKind.Error, IsUnboundGenericType: false } type)
        {
            return ReferenceError(attribute, "A SQL dependency requires a non-null declared type.");
        }

        string? member = attribute.ConstructorArguments[1].Value as string;
        TypedConstant parameters = attribute.NamedArguments.FirstOrDefault(static argument => argument.Key == "ParameterTypes").Value;
        bool selectedParameters = parameters.Kind == TypedConstantKind.Array && !parameters.IsNull;
        if (member is null)
        {
            return selectedParameters ? ReferenceError(attribute, "ParameterTypes requires a method name.") : Primary(type, attribute);
        }

        if (string.IsNullOrWhiteSpace(member) || !SqlText.IsText(member))
        {
            return ReferenceError(attribute, "A SQL dependency method name must be nonempty valid text.");
        }

        if (selectedParameters && parameters.Values.Any(static parameter => parameter.Value is not ITypeSymbol { TypeKind: not TypeKind.Error }))
        {
            return ReferenceError(attribute, "ParameterTypes must contain non-null managed types.");
        }

        ISymbol[] members = [];
        for (INamedTypeSymbol? container = type; container is not null && members.Length == 0; container = container.BaseType)
        {
            members = [.. container.GetMembers(member)];
        }

        IMethodSymbol[] methods = [.. members.OfType<IMethodSymbol>().Where(method => !selectedParameters ||
            method.Parameters.Length == parameters.Values.Length && method.Parameters.Select(static parameter => parameter.Type)
                .SequenceEqual(parameters.Values.Select(static parameter => (ITypeSymbol)parameter.Value!), SymbolEqualityComparer.Default))];
        if (methods.Length != 1)
        {
            return ReferenceError(attribute, methods.Length == 0
                ? $"SQL dependency method '{type.ToDisplayString()}.{member}' was not found with the selected parameter types."
                : $"SQL dependency method '{type.ToDisplayString()}.{member}' is ambiguous; set ParameterTypes to select one overload.");
        }

        return Primary(methods[0], attribute);
    }

    private SqlEntity? Primary(ISymbol declaration, AttributeData attribute)
    {
        if (!_declarations.TryGetValue(declaration, out List<SqlEntity>? entities))
        {
            return ReferenceError(attribute, $"'{declaration.ToDisplayString()}' does not declare a generated SQL object in this extension.");
        }

        SqlEntity[] primary = [.. entities.Where(entity => declaration is IMethodSymbol
            ? entity.Kind == "function" : entity.Kind is "schema" or "type" or "enum" or "aggregate")];
        if (primary.Length == 0)
        {
            primary = [.. entities];
        }

        return primary.Length == 1 ? primary[0] : ReferenceError(attribute,
            $"'{declaration.ToDisplayString()}' declares multiple SQL objects; use their explicit dependency IDs.");
    }

    private SqlEntity? ReferenceError(AttributeData attribute, string message)
    {
        _invalid = true;
        _context.ReportDiagnostic(Diagnostic.Create(attribute.AttributeClass?.Name == "PgSupportFunctionAttribute" ? s_invalidSupport : s_invalidReference,
            attribute.ApplicationSyntaxReference?.GetSyntax(_context.CancellationToken).GetLocation(), message));
        return null;
    }
}
