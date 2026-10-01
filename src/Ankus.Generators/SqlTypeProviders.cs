using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Matches explicit catalog bindings to generated declarations and custom SQL provider inventories.
/// </summary>
/// <param name="graph">The installation graph and diagnostic sink.</param>
internal sealed class SqlTypeProviders(SqlGraph graph)
{
    private readonly Dictionary<(string? Schema, string Name), (SqlEntity Entity, bool Custom)> _providers = [];
    private readonly Dictionary<ManagedTypeIdentity, SqlEntity> _managed = [];

    /// <summary>
    /// Checks whether a named type is supplied by this extension's SQL graph.
    /// </summary>
    /// <param name="reference">The declared type identity.</param>
    /// <returns>Whether selection must qualify the type with the extension's schema.</returns>
    internal bool Contains(SqlTypeReference reference) => _providers.ContainsKey((reference.Schema, reference.Name));

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
    /// <param name="providers">The detached assembly provider declarations.</param>
    /// <param name="blocks">Valid inline and file SQL blocks.</param>
    /// <param name="schemas">Declared schema nodes.</param>
    /// <param name="mappings">Validated closed mappings requiring managed provider identities.</param>
    /// <param name="compilation">The compilation owning current provider diagnostic coordinates.</param>
    /// <returns>Whether all provider names are independent of a fixed schema.</returns>
    internal bool Add(EquatableArray<SqlProviderModel> providers, IReadOnlyDictionary<string, SqlEntity> blocks,
        IReadOnlyDictionary<string, SqlEntity> schemas, IReadOnlyList<DatumTypeModel> mappings, Compilation compilation)
    {
        bool relocatable = true;
        var namedClaims = new HashSet<(string? Schema, string Name)>();
        foreach (SqlProviderModel provider in providers.Where(static item => !item.Function))
        {
            Location? location = provider.Location?.Resolve(compilation);
            string? sqlId = provider.BlockId;
            string? name = provider.Name;
            string? schema = provider.Schema;
            bool managed = provider.Managed;
            DatumTypeModel? mapping = null;
            if (string.IsNullOrWhiteSpace(sqlId) || !SqlText.IsText(sqlId!))
            {
                graph.Error(location, "A type provider requires a nonempty SQL block identifier with valid Unicode and no zero characters.");
                continue;
            }

            if (managed)
            {
                mapping = provider.Type is { } supplied ? mappings.FirstOrDefault(item => item.Reference.Type == supplied) : null;
                if (mapping is null)
                {
                    graph.Error(location, "A managed type provider must name a registered PgDatumType mapping.");
                    continue;
                }

                if (mapping.Reference.External)
                {
                    graph.Error(location, "External datum mappings cannot have an extension type provider.");
                    continue;
                }

                if (provider.SchemaAuthored)
                {
                    graph.Error(location, "A managed type provider obtains its schema from PgDatumType and cannot specify Schema.");
                    continue;
                }

                name = mapping.Reference.Name;
                schema = mapping.Reference.Schema;
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

            if (mapping is not null && _managed.ContainsKey(mapping.Reference.Type))
            {
                graph.Error(location, "Managed datum type '" + mapping.Reference.Managed + "' has more than one provider.");
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

            string qualified = new SqlTypeReference(name!, schema).Sql;
            block.SelectionNames.UnionWith([name!, qualified]);
            block.Attachments.Add("TYPE " + (schema is null ? "\0" : string.Empty) + qualified);
            _providers[(schema, name!)] = (block, true);
            if (mapping is not null)
            {
                _managed.Add(mapping.Reference.Type, block);
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

        foreach (DatumTypeModel mapping in mappings)
        {
            if (!mapping.Reference.External && !_managed.ContainsKey(mapping.Reference.Type))
            {
                graph.Error(mapping.Location?.Resolve(compilation), "Managed datum type '" + mapping.Reference.Managed + "' requires a PgSqlTypeProvider naming its managed identity.");
            }

            if (!mapping.Reference.External && mapping.RangeBound is { External: false } bound &&
                _managed.TryGetValue(mapping.Reference.Type, out SqlEntity? rangeProvider) &&
                _managed.TryGetValue(bound.Type, out SqlEntity? boundProvider) && rangeProvider != boundProvider)
            {
                rangeProvider.Dependencies.Add(boundProvider);
            }
        }

        return relocatable;
    }

    /// <summary>
    /// Adds a leaf-type prerequisite for a raw or named composite signature slot.
    /// </summary>
    /// <param name="consumer">The generated function or aggregate helper.</param>
    /// <param name="contract">The scalar, array or output-column contract.</param>
    /// <param name="requireComplete">Whether the complete type must precede this consumer even along an explicit reverse dependency.</param>
    internal void Require(SqlEntity consumer, FunctionType? contract, bool requireComplete = false)
    {
        DatumTypeReference? mapping = (contract?.Element ?? contract)?.DatumType;
        if (mapping is not null)
        {
            if (!mapping.External && _managed.TryGetValue(mapping.Type, out SqlEntity? managed))
            {
                if (requireComplete)
                {
                    consumer.Dependencies.Add(managed);
                }
                else
                {
                    consumer.TypeDependencies.Add(managed);
                }
            }

            return;
        }

        SqlTypeReference? binding = (contract?.Element ?? contract)?.Binding;
        if (binding is not null && _providers.TryGetValue((binding.Schema, binding.Name), out (SqlEntity Entity, bool Custom) provider))
        {
            if (provider.Custom && !requireComplete)
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
