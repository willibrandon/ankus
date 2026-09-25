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
    /// <returns>Whether all provider names are independent of a fixed schema.</returns>
    internal bool Add(ImmutableArray<AttributeData> attributes, IReadOnlyDictionary<string, SqlEntity> blocks,
        IReadOnlyDictionary<string, SqlEntity> schemas)
    {
        bool relocatable = true;
        foreach (AttributeData attribute in attributes.Where(static attribute => attribute.AttributeClass?.ToDisplayString() ==
            "Ankus.PgSqlTypeProviderAttribute"))
        {
            Location? location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
            string? sqlId = attribute.ConstructorArguments.Length == 2 ? attribute.ConstructorArguments[0].Value as string : null;
            string? name = attribute.ConstructorArguments.Length == 2 ? attribute.ConstructorArguments[1].Value as string : null;
            string? schema = AttributeValues.Get<string?>(attribute, "Schema", null);
            if (string.IsNullOrWhiteSpace(sqlId) || !SqlText.IsText(sqlId!))
            {
                graph.Error(location, "A type provider requires a nonempty SQL block identifier with valid Unicode and no zero characters.");
                continue;
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

            if (_providers.ContainsKey((schema, name!)))
            {
                string identity = new SqlTypeReference(name!, schema).Sql;
                graph.Error(location, $"PostgreSQL type {identity} has more than one provider, including generated type or enum declarations.");
                continue;
            }

            _providers.Add((schema, name!), (block, true));
            if (schema is not null)
            {
                relocatable = false;
                if (schemas.TryGetValue(schema, out SqlEntity? dependency))
                {
                    block.Dependencies.Add(dependency);
                }
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
