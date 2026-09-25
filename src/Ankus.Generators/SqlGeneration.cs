using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Applies declarative SQL suppression or replacement while preserving native wrappers and installation dependencies.
/// </summary>
internal static class SqlGeneration
{
    /// <summary>
    /// Applies a declaration's SQL policy while retaining compiled contracts and dependency nodes.
    /// </summary>
    /// <param name="attribute">The optional declaration attribute.</param>
    /// <param name="entity">The declaration's graph node.</param>
    /// <param name="related">Additional declarations owned by this replacement.</param>
    /// <param name="substitutions">Context-specific tokens and their values; null marks an unavailable token.</param>
    /// <param name="graph">The installation graph and diagnostic sink.</param>
    /// <returns>Whether this policy permits schema relocation.</returns>
    internal static bool Apply(AttributeData? attribute, SqlEntity entity, IReadOnlyList<SqlEntity> related,
        IReadOnlyList<(string Token, string? Value)> substitutions, SqlGraph graph)
    {
        if (attribute is null)
        {
            return true;
        }

        bool enabled = AttributeValues.Get(attribute, "GenerateSql", true);
        string? sql = AttributeValues.Get<string?>(attribute, "Sql", null);
        if (!enabled && sql is not null)
        {
            graph.Error(entity.Location, "GenerateSql cannot be false when Sql supplies a replacement, including empty text.");
            return false;
        }

        if (sql is not null && !SqlText.IsText(sql))
        {
            graph.Error(entity.Location, "A Sql replacement must contain valid Unicode without zero characters.");
            return false;
        }

        if (!enabled)
        {
            entity.Sql = string.Empty;
            foreach (SqlEntity member in related)
            {
                member.Sql = string.Empty;
            }
        }
        else if (sql is not null)
        {
            sql = sql.Replace("@MODULE_PATHNAME@", "MODULE_PATHNAME");
            foreach ((string token, string? value) in substitutions)
            {
                if (value is not null)
                {
                    sql = sql.Replace(token, value);
                }
                else if (sql.IndexOf(token, StringComparison.Ordinal) >= 0)
                {
                    graph.Error(entity.Location, "The Sql replacement token " + token + " requires BinaryProtocol = true.");
                    return false;
                }
            }

            graph.Replace(entity, sql, related);
            return AttributeValues.Get(attribute, "SqlRelocatable", false);
        }

        return true;
    }
}
