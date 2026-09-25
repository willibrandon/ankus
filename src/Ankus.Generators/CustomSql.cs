using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Resolves assembly SQL declarations from compile-time constants and tracked compiler inputs.
/// </summary>
internal static class CustomSql
{
    /// <summary>
    /// Adds custom SQL nodes and reports whether all blocks permit schema relocation.
    /// </summary>
    /// <param name="attributes">The assembly's SQL attributes.</param>
    /// <param name="files">AdditionalFiles paths and their tracked content.</param>
    /// <param name="projectDirectory">The compiler-visible project directory.</param>
    /// <param name="graph">The installation dependency graph.</param>
    /// <param name="blocks">The successfully read SQL blocks, indexed by dependency identifier.</param>
    /// <returns>Whether every custom block explicitly permits relocation.</returns>
    internal static bool Add(ImmutableArray<AttributeData> attributes,
        ImmutableArray<(string Path, string? Text)> files, string projectDirectory, SqlGraph graph,
        out Dictionary<string, SqlEntity> blocks)
    {
        blocks = new(StringComparer.Ordinal);
        bool relocatable = true;
        foreach (AttributeData attribute in attributes.Where(static attribute => attribute.AttributeClass?.ToDisplayString() is
            "Ankus.PgSqlAttribute" or "Ankus.PgSqlFileAttribute"))
        {
            Location? location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
            if (attribute.ConstructorArguments.Length != 2)
            {
                graph.Error(location, "Custom SQL declarations require a name and a SQL string or file path.");
                continue;
            }

            string? name = attribute.ConstructorArguments[0].Value as string;
            string? sql = attribute.ConstructorArguments[1].Value as string;
            if (string.IsNullOrWhiteSpace(name) || !SqlText.IsText(name!))
            {
                graph.Error(location, "Custom SQL requires a nonempty dependency name with valid Unicode and no zero characters.");
                continue;
            }

            if (attribute.AttributeClass?.Name == "PgSqlFileAttribute")
            {
                sql = ReadFile(sql, location, files, projectDirectory, graph);
                if (sql is null)
                {
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(sql) || !SqlText.IsText(sql!))
            {
                graph.Error(location, $"Custom SQL '{name}' must contain nonempty SQL with valid Unicode and no zero characters.");
                continue;
            }

            int order = AttributeValues.Get(attribute, "Order", 0);
            if (order is < 0 or > 2)
            {
                graph.Error(location, $"Custom SQL '{name}' has an undefined PgSqlOrder value.");
                continue;
            }

            var entity = new SqlEntity("2:sql:" + name, sql!, location) { Order = order };
            graph.Configure(entity, attribute, name);
            graph.Add(entity);
            if (!blocks.ContainsKey(name!))
            {
                blocks.Add(name!, entity);
            }

            relocatable &= AttributeValues.Get(attribute, "Relocatable", false);
        }

        return relocatable;
    }

    private static string? ReadFile(string? path, Location? location,
        ImmutableArray<(string Path, string? Text)> files, string projectDirectory, SqlGraph graph)
    {
        string? fullPath = Normalize(path, projectDirectory);
        if (fullPath is null)
        {
            graph.Error(location, "PgSqlFile requires a valid path and a compiler-visible MSBuildProjectDirectory for relative paths. Use Ankus.Sdk or expose that property with CompilerVisibleProperty.");
            return null;
        }

        StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        (string Path, string? Text)[] matches = [.. files.Where(file => string.Equals(Normalize(file.Path, projectDirectory), fullPath, comparison))];
        string? relativePath = NormalizeRelative(path, projectDirectory);
        if (matches.Length == 0 && relativePath is not null)
        {
            string suffix = Path.DirectorySeparatorChar + relativePath;
            matches =
            [
                .. files.Where(file => Normalize(file.Path, projectDirectory) is string candidate
                    && candidate.EndsWith(suffix, comparison)),
            ];
        }

        if (matches.Length != 1 || matches[0].Text is null)
        {
            graph.Error(location, $"SQL file '{path}' must resolve to exactly one readable AdditionalFiles input. Include it with <AdditionalFiles Include=\"...\" />.");
            return null;
        }

        return matches[0].Text;
    }

    private static string? NormalizeRelative(string? path, string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return null;
        }

        if (!Path.IsPathRooted(projectDirectory))
        {
            return null;
        }

        string root = Path.GetPathRoot(projectDirectory)!;
        string anchor = Path.Combine(root, "__ankus_project__");
        string? normalized = Normalize(path, anchor);
        string prefix = anchor + Path.DirectorySeparatorChar;
        StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (normalized is null || !normalized.StartsWith(prefix, comparison))
        {
            return null;
        }

        return normalized.Substring(prefix.Length);
    }

    private static string? Normalize(string? path, string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(path) || !SqlText.IsText(path!))
        {
            return null;
        }

        try
        {
            path = path!.Replace('\\', '/');
            if (Path.DirectorySeparatorChar == '\\' && Path.IsPathRooted(path) && Path.GetPathRoot(path).Length < 3)
            {
                return null;
            }

            if (!Path.IsPathRooted(path))
            {
                if (!Path.IsPathRooted(projectDirectory))
                {
                    return null;
                }

                path = Path.Combine(projectDirectory, path);
            }

            return Path.GetFullPath(path);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
