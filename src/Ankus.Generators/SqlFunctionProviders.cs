using System.Collections.Immutable;
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
    /// <param name="attributes">The assembly's tracked SQL attributes.</param>
    /// <param name="blocks">The validated custom SQL blocks.</param>
    /// <param name="graph">The graph and its declaration diagnostics.</param>
    internal static void Add(ImmutableArray<AttributeData> attributes, IReadOnlyDictionary<string, SqlEntity> blocks, SqlGraph graph)
    {
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        foreach (AttributeData attribute in attributes.Where(static item => item.AttributeClass?.ToDisplayString() == "Ankus.PgSqlFunctionProviderAttribute"))
        {
            Location? location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
            string? blockId = attribute.ConstructorArguments.Length == 2 ? attribute.ConstructorArguments[0].Value as string : null;
            string? signature = attribute.ConstructorArguments.Length == 2 ? attribute.ConstructorArguments[1].Value as string : null;
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
