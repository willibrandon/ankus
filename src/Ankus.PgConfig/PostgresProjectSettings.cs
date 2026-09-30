using System.Globalization;
using System.Text.Json;

namespace Ankus.PgConfig;

/// <summary>
/// Reads a project's evaluated PostgreSQL selection without building the extension.
/// </summary>
public sealed class PostgresProjectSettings
{
    private PostgresProjectSettings(int postgresMajor, string? pgConfigPath)
    {
        PostgresMajor = postgresMajor;
        PgConfigPath = pgConfigPath;
    }

    /// <summary>
    /// Gets the PostgreSQL major selected by the evaluated project, or 18 when none is declared.
    /// </summary>
    public int PostgresMajor { get; }

    /// <summary>
    /// Gets the selected pg_config executable, or null when ordinary installation discovery applies.
    /// Relative file paths are resolved against the project directory; executable names retain PATH lookup.
    /// </summary>
    public string? PgConfigPath { get; }

    /// <summary>
    /// Evaluates the project's PostgreSQL properties with the installed .NET SDK and selected configuration.
    /// Imports and conditions participate; build targets and automatic response files do not run.
    /// A test project without its own selection inherits an unambiguous selection from its project references.
    /// </summary>
    /// <param name="projectPath">The extension project file.</param>
    /// <param name="configuration">The MSBuild configuration used for the eventual publication.</param>
    /// <param name="postgresMajor">An explicit major supplied by the calling build, or null to use project defaults.</param>
    /// <param name="cancellationToken">Cancels evaluation and joins the query process before returning.</param>
    /// <returns>The evaluated PostgreSQL selection.</returns>
    public static async Task<PostgresProjectSettings> ReadAsync(string projectPath, string configuration,
        int? postgresMajor = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);
        string project = Path.GetFullPath(projectPath);
        if (!File.Exists(project) || !project.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("The extension project was not found.", project);
        }

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Configuration"] = configuration };
        if (postgresMajor is int selected)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(selected, 13, nameof(postgresMajor));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(selected, 19, nameof(postgresMajor));
            properties.Add("AnkusPostgresMajor", selected.ToString(CultureInfo.InvariantCulture));
        }

        return await ReadCoreAsync(project, properties, new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal), cancellationToken).ConfigureAwait(false) ?? new(18, null);
    }

    /// <summary>
    /// Evaluates declared references only when the current project does not select PostgreSQL itself.
    /// </summary>
    /// <param name="project">The absolute project path.</param>
    /// <param name="globalProperties">The build properties inherited by this project evaluation.</param>
    /// <param name="visiting">The current reference chain, used to reject cycles.</param>
    /// <param name="cancellationToken">Cancels project evaluation.</param>
    /// <returns>The declared or inherited selection, or null for projects unrelated to PostgreSQL.</returns>
    private static async Task<PostgresProjectSettings?> ReadCoreAsync(string project, Dictionary<string, string> globalProperties,
        HashSet<string> visiting, CancellationToken cancellationToken)
    {
        if (!visiting.Add(project))
        {
            throw new InvalidOperationException("A circular project reference prevents PostgreSQL selection.");
        }

        List<string> arguments = ["msbuild", project, "-nologo", "-noAutoResponse", "-verbosity:quiet",
            "-getProperty:AnkusPostgresMajor,AnkusPgConfigPath", "-getItem:ProjectReference"];
        arguments.AddRange(globalProperties.Select(static pair => "-p:" + pair.Key + "=" + EscapeProperty(pair.Value)));
        string output = await PostgresInstallation.QueryAsync("dotnet", arguments, cancellationToken).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement properties = document.RootElement.GetProperty("Properties");
        string value = properties.GetProperty("AnkusPostgresMajor").GetString() ?? string.Empty;
        int major = 18;
        if (value.Length != 0 && (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out major) || major is < 13 or > 19))
        {
            throw new FormatException("AnkusPostgresMajor must select PostgreSQL 13–19.");
        }

        string? path = properties.GetProperty("AnkusPgConfigPath").GetString();
        if (string.IsNullOrWhiteSpace(path))
        {
            path = null;
        }
        else if (path.Contains(Path.DirectorySeparatorChar) || path.Contains(Path.AltDirectorySeparatorChar))
        {
            path = Path.GetFullPath(path, Path.GetDirectoryName(project)!);
        }

        if (value.Length != 0 || path is not null)
        {
            visiting.Remove(project);
            return new(major, path);
        }

        PostgresProjectSettings? inherited = null;
        foreach (JsonElement reference in document.RootElement.GetProperty("Items").GetProperty("ProjectReference").EnumerateArray())
        {
            string referencePath = reference.GetProperty("FullPath").GetString()!;
            if (!referencePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            PostgresProjectSettings? selection = await ReadCoreAsync(referencePath,
                ReferenceProperties(reference, globalProperties), visiting, cancellationToken).ConfigureAwait(false);
            if (selection is null)
            {
                continue;
            }

            if (inherited is not null && (inherited.PostgresMajor != selection.PostgresMajor ||
                !string.Equals(inherited.PgConfigPath, selection.PgConfigPath, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Referenced projects select different PostgreSQL installations. Set AnkusPostgresMajor and AnkusPgConfigPath explicitly, or use ankus test --pg.");
            }

            inherited = selection;
        }

        visiting.Remove(project);
        return inherited;
    }

    /// <summary>
    /// Preserves the reference's explicit MSBuild property overrides and removals during evaluation.
    /// </summary>
    /// <param name="reference">The evaluated ProjectReference item and metadata.</param>
    /// <param name="parent">The parent's explicit global properties.</param>
    /// <returns>The global properties for the referenced project.</returns>
    private static Dictionary<string, string> ReferenceProperties(JsonElement reference, Dictionary<string, string> parent)
    {
        var properties = new Dictionary<string, string>(parent, StringComparer.OrdinalIgnoreCase);
        string[] removals = ["GlobalPropertiesToRemove", "UndefineProperties"];
        foreach (string metadata in removals)
        {
            if (reference.TryGetProperty(metadata, out JsonElement names))
            {
                foreach (string name in names.GetString()!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    properties.Remove(name);
                }
            }
        }

        string[] overrides = reference.TryGetProperty("Properties", out JsonElement replacement) && !string.IsNullOrEmpty(replacement.GetString())
            ? ["Properties", "AdditionalProperties"]
            : ["SetConfiguration", "SetPlatform", "SetTargetFramework", "AdditionalProperties"];
        foreach (string metadata in overrides)
        {
            if (!reference.TryGetProperty(metadata, out JsonElement values))
            {
                continue;
            }

            foreach (string property in values.GetString()!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int separator = property.IndexOf('=', StringComparison.Ordinal);
                if (separator <= 0)
                {
                    throw new FormatException($"ProjectReference {metadata} must contain name=value properties.");
                }

                properties[property[..separator].Trim()] = Uri.UnescapeDataString(property[(separator + 1)..]);
            }
        }

        return properties;
    }

    /// <summary>
    /// Keeps literal property values separate from MSBuild's property-list and expansion syntax.
    /// </summary>
    /// <param name="value">The literal property value.</param>
    /// <returns>The escaped MSBuild argument value.</returns>
    private static string EscapeProperty(string value)
        => string.Concat(value.Select(static character => character is '%' or ';' or ',' or '$' or '@' or '(' or ')' or '\'' or '*' or '?' or '"'
            ? "%" + ((int)character).ToString("X2", CultureInfo.InvariantCulture) : character.ToString()));
}
