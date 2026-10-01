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
    private readonly Dictionary<DeclarationIdentity, List<SqlEntity>> _declarations = [];
    private readonly List<(SqlEntity Source, SqlEntity Target)> _inheritedRequirements = [];

    /// <summary>
    /// Keeps aggregate prerequisites ahead of every helper, including helpers shared by multiple aggregates.
    /// </summary>
    internal void InheritRequirements(SqlEntity source, SqlEntity target) => _inheritedRequirements.Add((source, target));

    /// <summary>
    /// Associates an exact managed declaration with a SQL node, including shared schema aliases.
    /// </summary>
    internal void Register(ISymbol declaration, SqlEntity entity)
        => Register(DeclarationIdentity.Create(declaration), declaration.ToDisplayString(), entity);

    /// <summary>
    /// Associates detached semantic identity and provenance with one installation node.
    /// </summary>
    /// <param name="declaration">The exact assembly-qualified declaration identity.</param>
    /// <param name="display">The managed name used for source provenance.</param>
    /// <param name="entity">The installation node.</param>
    internal void Register(DeclarationIdentity declaration, string display, SqlEntity entity)
    {
        entity.ManagedSources.Add(display);
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
    internal void ResolveReferences(IEnumerable<SqlReferenceModel> references, Compilation compilation)
    {
        foreach (SqlReferenceModel reference in references)
        {
            _context.CancellationToken.ThrowIfCancellationRequested();
            if (reference.Error is not null)
            {
                ReferenceError(reference, reference.Error, compilation);
                continue;
            }

            SqlEntity? source = Source(reference, compilation);
            if (source is null)
            {
                continue;
            }

            if (reference.TargetError is not null)
            {
                ReferenceError(reference, reference.TargetError, compilation);
                continue;
            }

            SqlEntity? target = Primary(reference.Target!, reference, compilation);
            if (target is null)
            {
                continue;
            }

            if (reference.Kind == "PgSupportFunctionAttribute")
            {
                BindPlannerSupport(reference, source, target, compilation);
            }
            else if (reference.Kind == "PgBeforeAttribute")
            {
                target.DeclaredDependencies.Add(source);
            }
            else
            {
                source.RequiredDeclarations.Add(target);
            }
        }

        foreach ((SqlEntity source, SqlEntity target) in _inheritedRequirements)
        {
            target.Requires.UnionWith(source.Requires.Where(required => !target.Names.Contains(required)));
            target.RequiredDeclarations.UnionWith(source.RequiredDeclarations.Where(required => required != target));
        }
    }

    private void BindPlannerSupport(SqlReferenceModel reference, SqlEntity source, SqlEntity target, Compilation compilation)
    {
        if (source.Function is null)
        {
            ReferenceError(reference, "PgSupportFunction must annotate a generated PostgreSQL function.", compilation);
            return;
        }

        if (reference.ExternalSupport)
        {
            ReferenceError(reference, "Choose either PgSupportFunction or the external PgFunction.SupportFunction SQL name, not both.", compilation);
            return;
        }

        if (target.Function is not { IsPlannerSupport: true } support)
        {
            ReferenceError(reference, "A planner support function must take exactly one nonvariadic SQL internal argument and return scalar SQL internal.", compilation);
            return;
        }

        if (support.RequiresAggregateContext)
        {
            ReferenceError(reference, "An aggregate helper requires an aggregate invocation and cannot serve as planner support; select an ordinary PgFunction method.", compilation);
            return;
        }

        source.RequiredDeclarations.Add(target);
        source.Function.SetPlannerSupport(support.Declaration.TemplateName);
    }

    private SqlEntity? Source(SqlReferenceModel reference, Compilation compilation)
    {
        SqlReferenceModel.Declaration declaration = reference.Source;
        string? id = reference.DeclarationId;
        _declarations.TryGetValue(declaration.Identity, out List<SqlEntity>? entities);
        if (id is not null)
        {
            if (!ValidName(id))
            {
                return ReferenceError(reference, "DeclarationId must be nonempty text without zero characters or invalid Unicode.", compilation);
            }

            SqlEntity[] matches = [.. (declaration.Assembly ? _entities : entities ?? [])
                .Where(entity => entity.Names.Contains(id))];
            return matches.Length == 1 ? matches[0] : ReferenceError(reference,
                $"DeclarationId '{id}' must identify exactly one SQL declaration belonging to the attributed declaration.", compilation);
        }

        if (declaration.Assembly)
        {
            return ReferenceError(reference, "An assembly-level SQL dependency requires DeclarationId to identify the declaration being ordered.", compilation);
        }

        return Primary(declaration, reference, compilation);
    }

    private SqlEntity? Primary(SqlReferenceModel.Declaration declaration, SqlReferenceModel reference, Compilation compilation)
    {
        if (!_declarations.TryGetValue(declaration.Identity, out List<SqlEntity>? entities))
        {
            return ReferenceError(reference, $"'{declaration.Display}' does not declare a generated SQL object in this extension.", compilation);
        }

        SqlEntity[] primary = [.. entities.Where(entity => declaration.Method
            ? entity.Kind == "function" : entity.Kind is "schema" or "type" or "enum" or "aggregate")];
        if (primary.Length == 0)
        {
            primary = [.. entities];
        }

        return primary.Length == 1 ? primary[0] : ReferenceError(reference,
            $"'{declaration.Display}' declares multiple SQL objects; use their explicit dependency IDs.", compilation);
    }

    private SqlEntity? ReferenceError(SqlReferenceModel reference, string message, Compilation compilation)
    {
        _invalid = true;
        _context.ReportDiagnostic(Diagnostic.Create(reference.Kind == "PgSupportFunctionAttribute" ? s_invalidSupport : s_invalidReference,
            reference.Location?.Resolve(compilation), message));
        return null;
    }
}
