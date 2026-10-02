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
    /// <param name="properties">Literal global properties used by evaluation and publication.</param>
    /// <returns>The dotnet publish exit code.</returns>
    internal static async Task<int> PublishAsync(string? project, string configuration, PostgresInstallation installation,
        string output, CancellationToken token, bool diagnosticsToStandardError = false, IReadOnlyDictionary<string, string>? properties = null)
    {
        string path = ResolveProject(project);
        Directory.CreateDirectory(output);
        // A failed build must not leave an earlier manifest that looks installable.
        PublishedExtension.Invalidate(output);
        int exitCode = await ToolProcess.RunAsync("dotnet",
        [
            "publish", path, .. PropertyArguments(properties), "-p:Configuration=" + EscapeProperty(configuration), "--runtime", RuntimeInformation.RuntimeIdentifier,
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

    /// <summary>
    /// Escapes a literal value for one MSBuild command-line property.
    /// </summary>
    /// <param name="value">The literal property value.</param>
    /// <returns>The escaped property value.</returns>
    internal static string EscapeProperty(string value)
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
    /// <param name="properties">Literal global properties for evaluating extension identity.</param>
    /// <returns>The explicit extension name or the SDK's normalized target name.</returns>
    internal static Task<string> GetExtensionNameAsync(string? project, string configuration,
        PostgresInstallation installation, CancellationToken token, IReadOnlyDictionary<string, string>? properties = null)
        => GetExtensionNameAsync(project, configuration, installation.Version.Major, installation.PgConfigPath, token, properties);

    /// <summary>
    /// Evaluates extension identity without requiring PostgreSQL discovery or managed compilation.
    /// </summary>
    /// <param name="project">The extension project or directory.</param>
    /// <param name="configuration">The selected build configuration.</param>
    /// <param name="postgresMajor">The selected PostgreSQL major.</param>
    /// <param name="pgConfigPath">An optional explicit PostgreSQL configuration path.</param>
    /// <param name="token">Cancels project evaluation.</param>
    /// <param name="properties">Literal global properties for evaluating extension identity.</param>
    /// <returns>The explicit extension name or the SDK's normalized target name.</returns>
    internal static async Task<string> GetExtensionNameAsync(string? project, string configuration,
        int postgresMajor, string? pgConfigPath, CancellationToken token, IReadOnlyDictionary<string, string>? properties = null)
    {
        string path = ResolveProject(project);
        using var output = new MemoryStream();
        List<string> arguments = ["msbuild", path, "-nologo", "-verbosity:quiet", "-getProperty:AnkusExtensionName,TargetName", .. PropertyArguments(properties),
            "-p:Configuration=" + EscapeProperty(configuration),
            "-p:AnkusPostgresMajor=" + postgresMajor.ToString(CultureInfo.InvariantCulture)];
        if (pgConfigPath is not null)
        {
            arguments.Add("-p:AnkusPgConfigPath=" + EscapeProperty(pgConfigPath));
        }

        int code = await ToolProcess.RunAsync("dotnet", arguments, token, outputStream: output);
        if (code != 0)
        {
            Console.Error.Write(System.Text.Encoding.UTF8.GetString(output.ToArray()));
            throw new InvalidOperationException($"Project evaluation failed ({code}).");
        }

        using JsonDocument document = JsonDocument.Parse(output.ToArray());
        JsonElement evaluated = document.RootElement.GetProperty("Properties");
        string name = evaluated.GetProperty("AnkusExtensionName").GetString()!;
        if (name.Length == 0)
        {
            name = evaluated.GetProperty("TargetName").GetString()!.ToLowerInvariant().Replace('.', '_');
        }

        ArgumentException.ThrowIfNullOrEmpty(name);
        return name;
    }

    /// <summary>
    /// Generates validated primary control metadata through managed compilation without native publication.
    /// </summary>
    /// <param name="project">The extension project or directory.</param>
    /// <param name="configuration">The build configuration.</param>
    /// <param name="installation">The PostgreSQL development installation.</param>
    /// <param name="output">The control output file owned by the caller.</param>
    /// <param name="token">Cancels compilation and its child process tree.</param>
    /// <param name="properties">Literal global properties for compiling control metadata.</param>
    /// <returns>The original build exit code.</returns>
    internal static Task<int> GenerateControlAsync(string project, string configuration,
        PostgresInstallation installation, string output, CancellationToken token, IReadOnlyDictionary<string, string>? properties = null)
        => ToolProcess.RunAsync("dotnet",
            ["build", ResolveProject(project), "-t:AnkusGenerateControlFile", .. PropertyArguments(properties),
                "-p:Configuration=" + EscapeProperty(configuration), "--runtime", RuntimeInformation.RuntimeIdentifier,
                "-p:AnkusPostgresMajor=" + installation.Version.Major.ToString(CultureInfo.InvariantCulture),
                "-p:AnkusPgConfigPath=" + EscapeProperty(installation.PgConfigPath),
                "-p:AnkusControlOutput=" + EscapeProperty(Path.GetFullPath(output))], token, diagnosticsToStandardError: true);

    /// <summary>
    /// Escapes each literal property separately so separators and expansion syntax stay in the value.
    /// </summary>
    /// <param name="properties">The literal property assignments.</param>
    /// <returns>Individual MSBuild command arguments.</returns>
    private static IEnumerable<string> PropertyArguments(IReadOnlyDictionary<string, string>? properties)
        => properties is null ? [] : properties.Select(static pair => "-p:" + pair.Key + "=" + EscapeProperty(pair.Value));
}
