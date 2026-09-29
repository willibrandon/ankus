using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Ankus.Tool;

internal static partial class FrameworkUpgrade
{
    /// <summary>
    /// Resolves an explicit project, a solution, or the single project/solution in a selected directory.
    /// </summary>
    /// <param name="path">The optional input path.</param>
    /// <param name="package">An optional project name within the selected solution.</param>
    /// <returns>The selected project paths.</returns>
    internal static string[] SelectProjects(string? path, string? package)
    {
        path = Path.GetFullPath(path ?? Environment.CurrentDirectory);
        if (Directory.Exists(path))
        {
            string[] solutions = [.. Directory.GetFiles(path, "*.sln"), .. Directory.GetFiles(path, "*.slnx")];
            string[] projects = Directory.GetFiles(path, "*.csproj");
            path = solutions.Length == 1 ? solutions[0] : solutions.Length == 0 && projects.Length == 1 ? projects[0]
                : throw new ArgumentException("Specify --project or --solution when the directory does not contain one unambiguous project or solution.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The selected project or solution was not found.", path);
        }

        string directory = Path.GetDirectoryName(path)!;
        IEnumerable<string> selected = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".csproj" => [path],
            ".slnx" => XDocument.Load(path).Descendants("Project").Select(static element => (string?)element.Attribute("Path")
                ?? throw new InvalidOperationException("A solution project is missing its path.")),
            ".sln" => SolutionProject().Matches(File.ReadAllText(path)).Select(static match => match.Groups[1].Value),
            _ => throw new ArgumentException("Select a .csproj, .slnx, or .sln file."),
        };
        string[] result = [.. selected.Select(project => Path.GetFullPath(project.Replace('\\', Path.DirectorySeparatorChar), directory))
            .Where(static project => project.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(project => package is null || string.Equals(Path.GetFileNameWithoutExtension(project), package, StringComparison.OrdinalIgnoreCase))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)];
        if (result.Length == 0 || package is not null && result.Length != 1)
        {
            throw new ArgumentException("The selection does not identify the requested C# project or projects.");
        }

        foreach (string project in result)
        {
            if (!File.Exists(project))
            {
                throw new FileNotFoundException("A selected solution project was not found.", project);
            }
        }

        return result;
    }

    [GeneratedRegex("^Project\\([^\\r\\n]+\\)\\s*=\\s*\"[^\"]*\",\\s*\"([^\"]+)\"", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex SolutionProject();
}
