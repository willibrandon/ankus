using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Retains authored custom-function identities without parsing or executing SQL during compilation.
/// </summary>
internal static class SqlFunctionProviders
{
    /// <summary>
    /// Associates declared functions with their custom SQL graph nodes.
    /// </summary>
    /// <param name="providers">The detached assembly provider declarations.</param>
    /// <param name="blocks">The validated custom SQL blocks.</param>
    /// <param name="graph">The graph and its declaration diagnostics.</param>
    /// <param name="compilation">The compiler state owning current diagnostic coordinates.</param>
    internal static void Add(EquatableArray<SqlProviderModel> providers, IReadOnlyDictionary<string, SqlEntity> blocks, SqlGraph graph,
        Compilation compilation)
    {
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        foreach (SqlProviderModel provider in providers.Where(static item => item.Function))
        {
            Location? location = provider.Location?.Resolve(compilation);
            string? blockId = provider.BlockId;
            string? signature = provider.Name;
            if (string.IsNullOrWhiteSpace(blockId) || !blocks.TryGetValue(blockId!, out SqlEntity? block))
            {
                graph.Error(location, "A function provider must name an existing PgSql or PgSqlFile block.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(signature) || !SqlText.IsText(signature!))
            {
                graph.Error(location, "A function provider requires a nonempty SQL signature with valid Unicode and no zero characters.");
                continue;
            }

            if (!claimed.Add(signature!))
            {
                graph.Error(location, "A SQL function signature may have only one custom provider: " + signature + ".");
                continue;
            }

            block.SelectionNames.Add(signature!);
            block.Attachments.Add("FUNCTION " + signature);
        }
    }
}
