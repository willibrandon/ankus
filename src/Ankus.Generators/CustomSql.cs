using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Resolves assembly SQL declarations from compile-time constants and tracked compiler inputs.
/// </summary>
internal static class CustomSql
{
    /// <summary>
    /// Adds resolved blocks using current graph options and diagnostic source trees.
    /// </summary>
    /// <param name="outputs">The cached SQL blocks and current detached declaration metadata.</param>
    /// <param name="compilation">The current compilation used to resolve diagnostic coordinates.</param>
    /// <param name="graph">The installation dependency graph.</param>
    /// <param name="blocks">The successfully read SQL blocks, indexed by dependency identifier.</param>
    /// <returns>Whether every custom block explicitly permits relocation.</returns>
    internal static bool Add(EquatableArray<CustomSqlPipeline.Output> outputs, GeneratorSourceResolver compilation, SqlGraph graph,
        out Dictionary<string, SqlEntity> blocks)
    {
        blocks = new(StringComparer.Ordinal);
        bool relocatable = true;
        foreach (CustomSqlPipeline.Output output in outputs)
        {
            CustomSqlPipeline.Analysis analysis = output.Analysis;
            CustomSqlPipeline.Resolution resolution = output.Resolution;
            Location? location = analysis.Location?.Resolve(compilation);
            if (resolution.Error is not null)
            {
                graph.Error(location, resolution.Error);
                continue;
            }

            if (analysis.Order is < 0 or > 2)
            {
                graph.Error(location, $"Custom SQL '{resolution.Name}' has an undefined PgSqlOrder value.");
                continue;
            }

            var entity = new SqlEntity("2:sql:" + resolution.Name, resolution.Sql!, location)
            {
                Order = analysis.Order,
                SourceFile = analysis.Input.File ? analysis.Input.Content : null,
            };
            graph.ConfigureOptions(entity, analysis.Options, resolution.Name);
            graph.Add(entity);
            if (!blocks.ContainsKey(resolution.Name!))
            {
                blocks.Add(resolution.Name!, entity);
            }

            relocatable &= analysis.Relocatable;
        }

        return relocatable;
    }

    /// <summary>
    /// Selects exact tracked content without reading the filesystem or retaining compiler objects.
    /// </summary>
    /// <param name="input">The detached authored name, content and declaration kind.</param>
    /// <param name="files">The compiler's tracked file paths and content.</param>
    /// <param name="projectDirectory">The compiler-visible project directory.</param>
    /// <param name="trackedPaths">All compiler-tracked paths, retaining ambiguity even when only selected contents were read.</param>
    /// <returns>The selected content or an owned diagnostic message.</returns>
    internal static CustomSqlPipeline.Selection Select(CustomSqlPipeline.Input input,
        EquatableArray<CustomSqlPipeline.FileInput> files, string projectDirectory, IEnumerable<string>? trackedPaths = null)
    {
        if (!input.ValidArguments)
        {
            return new(input.Name, null, "Custom SQL declarations require a name and a SQL string or file path.");
        }

        if (string.IsNullOrWhiteSpace(input.Name) || !SqlText.IsText(input.Name!))
        {
            return new(input.Name, null, "Custom SQL requires a nonempty dependency name with valid Unicode and no zero characters.");
        }

        if (!input.File)
        {
            return new(input.Name, input.Content, null);
        }

        CustomSqlPipeline.FileSelection selection = SelectFilePath(input.Content, trackedPaths ?? files.Select(static file => file.Path), projectDirectory);
        if (selection.Error is not null)
        {
            return new(input.Name, null, selection.Error);
        }

        CustomSqlPipeline.FileInput selected = files.Single(file => file.Path == selection.Path);
        return selected.Text is null ? new(input.Name, null, FileError(input.Content)) : new(input.Name, selected.Text, null);
    }

    /// <summary>
    /// Resolves a unique tracked path before reading contents, retaining exact-match precedence and relative suffix fallback.
    /// </summary>
    /// <param name="path">The authored SQL file path.</param>
    /// <param name="files">The compiler's tracked paths without file contents.</param>
    /// <param name="projectDirectory">The compiler-visible project directory.</param>
    /// <returns>The unique original path or the existing path-selection diagnostic.</returns>
    internal static CustomSqlPipeline.FileSelection SelectFilePath(string? path, IEnumerable<string> files, string projectDirectory)
    {
        string? fullPath = Normalize(path, projectDirectory);
        if (fullPath is null)
        {
            return new(null, "PgSqlFile requires a valid path and a compiler-visible MSBuildProjectDirectory for relative paths. Use Ankus.Sdk or expose that property with CompilerVisibleProperty.");
        }

        StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string[] matches = [.. files.Where(file => string.Equals(Normalize(file, projectDirectory), fullPath, comparison))];
        string? relativePath = NormalizeRelative(path, projectDirectory);
        if (matches.Length == 0 && relativePath is not null)
        {
            string suffix = Path.DirectorySeparatorChar + relativePath;
            matches =
            [
                .. files.Where(file => Normalize(file, projectDirectory) is string candidate
                    && candidate.EndsWith(suffix, comparison)),
            ];
        }

        return matches.Length == 1 ? new(matches[0], null) : new(null, FileError(path));
    }

    /// <summary>
    /// Preserves the required unique/readable tracked-file diagnostic.
    /// </summary>
    /// <param name="path">The authored path.</param>
    /// <returns>The diagnostic text for missing, ambiguous or unreadable input.</returns>
    private static string FileError(string? path)
        => $"SQL file '{path}' must resolve to exactly one readable AdditionalFiles input. Include it with <AdditionalFiles Include=\"...\" />.";

    /// <summary>
    /// Validates selected SQL text independently of graph options, source coordinates and other files.
    /// </summary>
    /// <param name="selection">The selected name, exact text and input diagnostic.</param>
    /// <returns>The validated block or an owned diagnostic message.</returns>
    internal static CustomSqlPipeline.Resolution Resolve(CustomSqlPipeline.Selection selection)
        => selection.Error is not null ? new(selection.Name, null, selection.Error) :
            string.IsNullOrWhiteSpace(selection.Sql) || !SqlText.IsText(selection.Sql!)
                ? new(selection.Name, null, $"Custom SQL '{selection.Name}' must contain nonempty SQL with valid Unicode and no zero characters.")
                : new(selection.Name, selection.Sql, null);

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
