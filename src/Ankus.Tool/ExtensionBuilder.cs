using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
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
            if (projects.Length == 0)
            {
                string[] solutions = Directory.GetFiles(path, "*.slnx");
                if (solutions.Length == 1)
                {
                    projects = [.. XDocument.Load(solutions[0]).Descendants("Project")
                        .Select(element => Path.GetFullPath((string)element.Attribute("Path")!, path))
                        .Where(static project => File.Exists(project) &&
                            (string?)XDocument.Load(project).Root?.Attribute("Sdk") is string sdk &&
                             (sdk == "Ankus.Sdk" || sdk.StartsWith("Ankus.Sdk/", StringComparison.Ordinal)))];
                }
            }

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
    /// <param name="diagnosticsToStandardError">Whether build output belongs on stderr, leaving stdout available for SQL.</param>
    /// <returns>The dotnet publish exit code.</returns>
    internal static async Task<int> PublishAsync(string? project, string configuration, PostgresInstallation installation,
        string output, CancellationToken token, bool diagnosticsToStandardError = false)
    {
        string path = ResolveProject(project);
        Directory.CreateDirectory(output);
        // A failed build must not leave an earlier manifest that looks installable.
        PublishedExtension.Invalidate(output);
        int exitCode = await ToolProcess.RunAsync("dotnet",
        [
            "publish", path, "--configuration", configuration, "--runtime", RuntimeInformation.RuntimeIdentifier,
            "--self-contained", "true", "--output", Path.GetFullPath(output),
            "-p:AnkusPostgresMajor=" + installation.Version.Major.ToString(CultureInfo.InvariantCulture),
            "-p:AnkusPgConfigPath=" + EscapeProperty(installation.PgConfigPath),
        ], token, diagnosticsToStandardError);
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

    /// <summary>
    /// Evaluates the extension's database name with MSBuild, including imported and configuration-specific properties.
    /// </summary>
    /// <param name="project">The extension project or directory.</param>
    /// <param name="configuration">The selected build configuration.</param>
    /// <param name="installation">The selected PostgreSQL installation.</param>
    /// <param name="token">Cancels project evaluation.</param>
    /// <returns>The explicit extension name or the SDK's normalized target name.</returns>
    internal static async Task<string> GetExtensionNameAsync(string? project, string configuration,
        PostgresInstallation installation, CancellationToken token)
    {
        string path = ResolveProject(project);
        using var output = new MemoryStream();
        int code = await ToolProcess.RunAsync("dotnet",
            ["msbuild", path, "-nologo", "-verbosity:quiet", "-getProperty:AnkusExtensionName,TargetName",
                "-p:Configuration=" + configuration,
                "-p:AnkusPostgresMajor=" + installation.Version.Major.ToString(CultureInfo.InvariantCulture),
                "-p:AnkusPgConfigPath=" + EscapeProperty(installation.PgConfigPath)], token, outputStream: output);
        if (code != 0)
        {
            Console.Error.Write(System.Text.Encoding.UTF8.GetString(output.ToArray()));
            throw new InvalidOperationException($"Project evaluation failed ({code}).");
        }

        using JsonDocument document = JsonDocument.Parse(output.ToArray());
        JsonElement properties = document.RootElement.GetProperty("Properties");
        string name = properties.GetProperty("AnkusExtensionName").GetString()!;
        if (name.Length == 0)
        {
            name = properties.GetProperty("TargetName").GetString()!.ToLowerInvariant().Replace('.', '_');
        }

        ArgumentException.ThrowIfNullOrEmpty(name);
        return name;
    }
}
