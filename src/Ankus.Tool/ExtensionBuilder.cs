using System.Globalization;
using System.Runtime.InteropServices;
using Ankus.PgConfig;

namespace Ankus.Tool;

/// <summary>
/// Drives Native AOT publishing with an exact PostgreSQL installation selection.
/// </summary>
internal static class ExtensionBuilder
{
    /// <summary>
    /// Resolves a single extension project without choosing arbitrarily among multiple projects.
    /// </summary>
    /// <param name="path">An optional project or directory.</param>
    /// <returns>The absolute project path.</returns>
    internal static string ResolveProject(string? path)
    {
        path = Path.GetFullPath(path ?? Environment.CurrentDirectory);
        if (Directory.Exists(path))
        {
            string[] projects = Directory.GetFiles(path, "*.csproj");
            if (projects.Length != 1)
            {
                throw new ArgumentException("Specify --project with one extension .csproj file.");
            }

            return projects[0];
        }

        if (!File.Exists(path) || !path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("The extension project was not found.", path);
        }

        return path;
    }

    /// <summary>
    /// Publishes for the host runtime and returns the original dotnet exit code.
    /// </summary>
    /// <param name="project">The extension project or directory.</param>
    /// <param name="configuration">The build configuration.</param>
    /// <param name="installation">The exact PostgreSQL installation.</param>
    /// <param name="output">The output directory.</param>
    /// <param name="token">Cancels publishing and terminates its process tree.</param>
    /// <returns>The dotnet publish exit code.</returns>
    internal static async Task<int> PublishAsync(string? project, string configuration, PostgresInstallation installation,
        string output, CancellationToken token)
    {
        string path = ResolveProject(project);
        Directory.CreateDirectory(output);
        // A failed build must not leave an earlier manifest that looks installable.
        File.Delete(Path.Combine(output, PublishedExtension.FileName));
        int exitCode = await ToolProcess.RunAsync("dotnet",
        [
            "publish", path, "--configuration", configuration, "--runtime", RuntimeInformation.RuntimeIdentifier,
            "--self-contained", "true", "--output", Path.GetFullPath(output),
            "-p:AnkusPostgresMajor=" + installation.Version.Major.ToString(CultureInfo.InvariantCulture),
            "-p:AnkusPgConfigPath=" + EscapeProperty(installation.PgConfigPath),
        ], token);
        if (exitCode == 0)
        {
            PublishedExtension manifest = PublishedExtension.Read(output);
            if (manifest.PostgresMajor != installation.Version.Major || manifest.RuntimeIdentifier != RuntimeInformation.RuntimeIdentifier)
            {
                throw new InvalidOperationException("The published extension does not match the selected PostgreSQL target.");
            }
        }

        return exitCode;
    }

    private static string EscapeProperty(string value)
    {
        // MSBuild treats these characters as property-list separators or expansion syntax.
        return string.Concat(value.Select(static c => c is '%' or ';' or ',' or '$' or '@' or '(' or ')' or '\'' or '*' or '?' or '"'
            ? "%" + ((int)c).ToString("X2", CultureInfo.InvariantCulture) : c.ToString()));
    }
}
