using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Applies declarative SQL suppression or replacement while preserving native wrappers and installation dependencies.
/// </summary>
internal static class SqlGeneration
{
    /// <summary>
    /// Applies a function's SQL policy to its declaration and attached operator or cast nodes.
    /// </summary>
    /// <param name="attribute">The optional function attribute.</param>
    /// <param name="function">The backing function's graph node.</param>
    /// <param name="related">Attached operator and cast declarations.</param>
    /// <param name="nativeName">The generated PostgreSQL entry point.</param>
    /// <param name="graph">The installation graph and diagnostic sink.</param>
    /// <returns>Whether this policy permits schema relocation.</returns>
    internal static bool Apply(AttributeData? attribute, SqlEntity function, IReadOnlyList<SqlEntity> related,
        string nativeName, SqlGraph graph)
    {
        if (attribute is null)
        {
            return true;
        }

        bool enabled = AttributeValues.Get(attribute, "GenerateSql", true);
        string? sql = AttributeValues.Get<string?>(attribute, "Sql", null);
        if (!enabled && sql is not null)
        {
            graph.Error(function.Location, "GenerateSql cannot be false when Sql supplies a replacement, including empty text.");
            return false;
        }

        if (sql is not null && !SqlText.IsText(sql))
        {
            graph.Error(function.Location, "A Sql replacement must contain valid Unicode without zero characters.");
            return false;
        }

        if (!enabled)
        {
            function.Sql = string.Empty;
            foreach (SqlEntity entity in related)
            {
                entity.Sql = string.Empty;
            }
        }
        else if (sql is not null)
        {
            graph.Replace(function, sql.Replace("@FUNCTION_NAME@", nativeName).Replace("@MODULE_PATHNAME@", "MODULE_PATHNAME"), related);
            return AttributeValues.Get(attribute, "SqlRelocatable", false);
        }

        return true;
    }
}
