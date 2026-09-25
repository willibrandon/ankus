using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Matches explicit catalog bindings to generated declarations and custom SQL provider inventories.
/// </summary>
/// <param name="graph">The installation graph and diagnostic sink.</param>
internal sealed class SqlTypeProviders(SqlGraph graph)
{
    private readonly Dictionary<(string? Schema, string Name), (SqlEntity Entity, bool Custom)> _providers = [];
    private readonly Dictionary<INamedTypeSymbol, SqlEntity> _managed = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// Reserves a generated type identity, including declarations whose SQL is disabled or replaced.
    /// </summary>
    /// <param name="name">The exact unquoted type name.</param>
    /// <param name="schema">The fixed schema, or null for an unqualified identity.</param>
    /// <param name="entity">The generated type or enum declaration.</param>
    internal void Reserve(string name, string? schema, SqlEntity entity)
    {
        if (!_providers.ContainsKey((schema, name)))
        {
            _providers.Add((schema, name), (entity, false));
        }
    }

    /// <summary>
    /// Validates declared providers and adds their known schema prerequisites.
    /// </summary>
    /// <param name="attributes">The tracked assembly SQL and provider attributes.</param>
    /// <param name="blocks">Valid inline and file SQL blocks.</param>
    /// <param name="schemas">Declared schema nodes.</param>
    /// <param name="mappings">Validated closed mappings requiring managed provider identities.</param>
    /// <returns>Whether all provider names are independent of a fixed schema.</returns>
    internal bool Add(ImmutableArray<AttributeData> attributes, IReadOnlyDictionary<string, SqlEntity> blocks,
        IReadOnlyDictionary<string, SqlEntity> schemas, IReadOnlyList<DatumTypeDeclaration> mappings)
    {
        bool relocatable = true;
        var namedClaims = new HashSet<(string? Schema, string Name)>();
        foreach (AttributeData attribute in attributes.Where(static attribute => attribute.AttributeClass?.ToDisplayString() ==
            "Ankus.PgSqlTypeProviderAttribute"))
        {
            Location? location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
            string? sqlId = attribute.ConstructorArguments.Length == 2 ? attribute.ConstructorArguments[0].Value as string : null;
            string? name = attribute.ConstructorArguments.Length == 2 ? attribute.ConstructorArguments[1].Value as string : null;
            string? schema = AttributeValues.Get<string?>(attribute, "Schema", null);
            bool managed = attribute.AttributeConstructor is { Parameters.Length: 2 } constructor &&
                constructor.Parameters[1].Type.ToDisplayString() == "System.Type";
            DatumTypeDeclaration? mapping = null;
            if (string.IsNullOrWhiteSpace(sqlId) || !SqlText.IsText(sqlId!))
            {
                graph.Error(location, "A type provider requires a nonempty SQL block identifier with valid Unicode and no zero characters.");
                continue;
            }

            if (managed)
            {
                mapping = mappings.FirstOrDefault(item => SymbolEqualityComparer.Default.Equals(item.Type, attribute.ConstructorArguments[1].Value as ITypeSymbol));
                if (mapping is null)
                {
                    graph.Error(location, "A managed type provider must name a registered PgDatumType mapping.");
                    continue;
                }

                if (mapping.External)
                {
                    graph.Error(location, "External datum mappings cannot have an extension type provider.");
                    continue;
                }

                if (attribute.NamedArguments.Any(static item => item.Key == "Schema"))
                {
                    graph.Error(location, "A managed type provider obtains its schema from PgDatumType and cannot specify Schema.");
                    continue;
                }

                name = mapping.Name;
                schema = mapping.Schema;
            }

            if (!SqlText.IsIdentifier(name) || schema is not null && !SqlText.IsIdentifier(schema))
            {
                graph.Error(location, "Provider type and schema names must be nonempty identifiers of at most 63 UTF-8 bytes, without zero characters or invalid Unicode.");
                continue;
            }

            if (!blocks.TryGetValue(sqlId!, out SqlEntity? block))
            {
                graph.Error(location, $"Type provider '{sqlId}' must name a PgSql or PgSqlFile block.");
                continue;
            }

            if (mapping is not null && _managed.ContainsKey(mapping.Type))
            {
                graph.Error(location, "Managed datum type '" + mapping.Managed + "' has more than one provider.");
                continue;
            }

            if ((!managed && !namedClaims.Add((schema, name!))) ||
                _providers.TryGetValue((schema, name!), out (SqlEntity Entity, bool Custom) existing) &&
                (!existing.Custom || existing.Entity != block))
            {
                string identity = new SqlTypeReference(name!, schema).Sql;
                graph.Error(location, $"PostgreSQL type {identity} has more than one provider, including generated type or enum declarations.");
                continue;
            }

            _providers[(schema, name!)] = (block, true);
            if (mapping is not null)
            {
                _managed.Add(mapping.Type, block);
            }

            if (schema is not null)
            {
                relocatable = false;
                if (schemas.TryGetValue(schema, out SqlEntity? dependency))
                {
                    block.Dependencies.Add(dependency);
                }
            }
        }

        foreach (DatumTypeDeclaration mapping in mappings)
        {
            if (!mapping.External && !_managed.ContainsKey(mapping.Type))
            {
                graph.Error(mapping.Type.Locations.FirstOrDefault(), "Managed datum type '" + mapping.Managed + "' requires a PgSqlTypeProvider naming its managed identity.");
            }
        }

        return relocatable;
    }

    /// <summary>
    /// Adds a leaf-type prerequisite for a raw or named composite signature slot.
    /// </summary>
    /// <param name="consumer">The generated function or aggregate helper.</param>
    /// <param name="contract">The scalar, array or output-column contract.</param>
    internal void Require(SqlEntity consumer, FunctionType? contract)
    {
        DatumTypeDeclaration? mapping = (contract?.Element ?? contract)?.DatumType;
        if (mapping is not null)
        {
            if (!mapping.External && _managed.TryGetValue(mapping.Type, out SqlEntity? managed))
            {
                consumer.TypeDependencies.Add(managed);
            }

            return;
        }

        SqlTypeReference? binding = (contract?.Element ?? contract)?.Binding;
        if (binding is not null && _providers.TryGetValue((binding.Schema, binding.Name), out (SqlEntity Entity, bool Custom) provider))
        {
            if (provider.Custom)
            {
                consumer.TypeDependencies.Add(provider.Entity);
            }
            else
            {
                consumer.Dependencies.Add(provider.Entity);
            }
        }
    }
}
