using System.CommandLine;
using Ankus.PgConfig;

namespace Ankus.Tool;

internal static partial class ToolCommand
{
    /// <summary>
    /// Selects an installation using explicit command options before evaluated project defaults.
    /// </summary>
    /// <param name="result">The parsed command.</param>
    /// <param name="home">The registry home option.</param>
    /// <param name="token">Cancels project evaluation and installation discovery.</param>
    /// <param name="testProject">An optional project selected in forwarded test arguments.</param>
    /// <param name="testConfiguration">The effective forwarded test configuration.</param>
    /// <param name="testProperties">Explicit MSBuild properties forwarded to the test build.</param>
    /// <returns>The selected and version-checked installation.</returns>
    private static async Task<PostgresInstallation> SelectAsync(ParseResult result, Option<string?> home, CancellationToken token,
        string? testProject = null, string? testConfiguration = null, IReadOnlyDictionary<string, string>? testProperties = null)
    {
        testProperties ??= BuildProperties(result);
        if (testConfiguration is null && result.CommandResult.Command.Options.Any(static option => option.Name == "--configuration"))
        {
            testConfiguration = GetConfiguration(result);
        }

        int? major = ExplicitMajor(result);
        string? path = result.GetValue<string?>("--pg-config");
        string? forwardedPath = testProperties?.GetValueOrDefault("AnkusPgConfigPath");
        if (testProperties?.GetValueOrDefault("AnkusPostgresMajor") is string value)
        {
            if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int forwarded) ||
                forwarded is < 13 or > 19)
            {
                throw new ArgumentException("The forwarded AnkusPostgresMajor property must select PostgreSQL 13–19.");
            }

            if (major is not null && major != forwarded)
            {
                throw new ArgumentException("Select the same PostgreSQL major for --pg and forwarded AnkusPostgresMajor.");
            }

            major = forwarded;
        }

        if (forwardedPath is not null)
        {
            string? normalized = string.IsNullOrWhiteSpace(forwardedPath) ? null :
                forwardedPath.Contains(Path.DirectorySeparatorChar) || forwardedPath.Contains(Path.AltDirectorySeparatorChar)
                    ? Path.GetFullPath(forwardedPath) : forwardedPath;
            if (path is not null && !string.Equals(Path.GetFullPath(path), normalized is null ? null : Path.GetFullPath(normalized),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new ArgumentException("Select the same pg_config for --pg-config and forwarded AnkusPgConfigPath.");
            }

            path ??= normalized;
        }

        if (major is null && path is null)
        {
            if (result.CommandResult.Command.Name is "install" or "package" && result.GetValue<string?>("--from") is string publication)
            {
                major = PublishedExtension.Read(publication).PostgresMajor;
            }
            else if (await ProjectSelectionAsync(result, token, testProject, testConfiguration, testProperties) is { } project)
            {
                major = project.PostgresMajor;
                path = forwardedPath is null ? project.PgConfigPath : null;
            }
        }

        if (path is null)
        {
            return await new PostgresRegistry(result.GetValue(home)).GetAsync(major ?? 18, token);
        }

        PostgresInstallation installation = await PostgresInstallation.CreateAsync(path, token);
        if (major is int expected && installation.Version.Major != expected)
        {
            throw new ArgumentException($"Expected PostgreSQL {expected}, but '{path}' is {installation.Label}.");
        }

        return installation;
    }

    /// <summary>
    /// Resolves build metadata without requiring a registered or discovered server installation.
    /// </summary>
    /// <param name="result">The parsed command.</param>
    /// <param name="token">Cancels evaluation.</param>
    /// <returns>The explicit or project-selected PostgreSQL major.</returns>
    private static async Task<int> SelectMajorAsync(ParseResult result, CancellationToken token)
    {
        if (ExplicitMajor(result) is int major)
        {
            return major;
        }

        if ((result.GetValue<string?>("--pg-config") ?? BuildProperties(result).GetValueOrDefault("AnkusPgConfigPath")) is string path)
        {
            return (await PostgresInstallation.CreateAsync(path, token)).Version.Major;
        }

        return (await ProjectSelectionAsync(result, token))?.PostgresMajor ?? 18;
    }

    /// <summary>
    /// Rejects an invalid explicit major instead of silently using project defaults.
    /// </summary>
    /// <param name="result">The parsed command.</param>
    /// <returns>The explicit major, or null when the option was omitted.</returns>
    private static int? ExplicitMajor(ParseResult result)
    {
        int? major = result.GetValue<int?>("--pg");
        if (BuildProperties(result).GetValueOrDefault("AnkusPostgresMajor") is string value)
        {
            if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int forwarded) ||
                forwarded is < 13 or > 19)
            {
                throw new ArgumentException("AnkusPostgresMajor must select PostgreSQL 13–19.");
            }

            if (major is not null && major != forwarded)
            {
                throw new ArgumentException("Select the same PostgreSQL major for --pg and AnkusPostgresMajor.");
            }

            major = forwarded;
        }

        if (major is int selected)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(selected, 13, nameof(major));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(selected, 19, nameof(major));
        }

        return major;
    }

    /// <summary>
    /// Evaluates project commands with their effective configuration; standalone cluster commands retain the default.
    /// </summary>
    /// <param name="result">The parsed command.</param>
    /// <param name="token">Cancels evaluation.</param>
    /// <param name="testProject">The project selected by forwarded test arguments.</param>
    /// <param name="testConfiguration">The effective test configuration.</param>
    /// <param name="properties">Literal global properties shared with the eventual build.</param>
    /// <returns>The evaluated project settings, or null for commands without a project.</returns>
    private static async Task<PostgresProjectSettings?> ProjectSelectionAsync(ParseResult result, CancellationToken token,
        string? testProject = null, string? testConfiguration = null, IReadOnlyDictionary<string, string>? properties = null)
    {
        properties ??= BuildProperties(result);
        bool test = result.CommandResult.Command.Name == "test";
        bool cluster = result.CommandResult.Command.Name is "start" or "stop" or "status" or "info";
        if (!test && !cluster && !result.CommandResult.Command.Options.Any(static option => option.Name == "--project"))
        {
            return null;
        }

        if (!test && !cluster)
        {
            string project = ExtensionBuilder.ResolveProject(result.GetValue<string?>("--project"));
            return await PostgresProjectSettings.ReadAsync(project, GetConfiguration(result), properties, cancellationToken: token);
        }

        string input = Path.GetFullPath(testProject ?? Environment.CurrentDirectory);
        string[] projects;
        try
        {
            projects = FrameworkUpgrade.SelectProjects(input, package: null);
        }
        catch (ArgumentException) when (Directory.Exists(input))
        {
            return null;
        }

        return await PostgresProjectSettings.TryReadAsync(projects,
            testConfiguration ?? (cluster ? "Debug" : GetConfiguration(result)), properties, cancellationToken: token);
    }
}
