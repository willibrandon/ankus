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
    /// <param name="positionalMajor">An optional major selected by an information subcommand's argument.</param>
    /// <param name="resolvedProject">An extension already resolved by the command.</param>
    /// <returns>The selected and version-checked installation.</returns>
    private static async Task<PostgresInstallation> SelectAsync(ParseResult result, Option<string?> home, CancellationToken token,
        string? testProject = null, string? testConfiguration = null, IReadOnlyDictionary<string, string>? testProperties = null,
        int? positionalMajor = null, string? resolvedProject = null)
        => (await SelectWithProjectAsync(result, home, token, testProject, testConfiguration, testProperties, positionalMajor, resolvedProject)).Installation;

    /// <summary>
    /// Retains the evaluated extension project for the remainder of this invocation.
    /// </summary>
    /// <param name="result">The parsed command.</param>
    /// <param name="home">The registry home option.</param>
    /// <param name="token">Cancels evaluation and installation discovery.</param>
    /// <param name="testProject">An optional forwarded test project.</param>
    /// <param name="testConfiguration">The effective test configuration.</param>
    /// <param name="testProperties">The effective global properties.</param>
    /// <param name="positionalMajor">An optional explicit positional major.</param>
    /// <param name="resolvedProject">An extension already resolved by the command.</param>
    /// <returns>The installation and the project already resolved during default selection.</returns>
    private static async Task<PostgresSelection> SelectWithProjectAsync(ParseResult result, Option<string?> home, CancellationToken token,
        string? testProject = null, string? testConfiguration = null, IReadOnlyDictionary<string, string>? testProperties = null,
        int? positionalMajor = null, string? resolvedProject = null)
    {
        testProperties ??= BuildProperties(result);
        if (testConfiguration is null && result.CommandResult.Command.Options.Any(static option => option.Name == "--configuration"))
        {
            testConfiguration = GetConfiguration(result);
        }

        int? major = ExplicitMajor(result);
        if (major is not null && positionalMajor is not null && major != positionalMajor)
        {
            throw new ArgumentException("Select the same PostgreSQL major for the version argument and --pg.");
        }

        major ??= positionalMajor;
        string? selectedProject = resolvedProject;
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
            else
            {
                major = EnvironmentMajor();
                if (major is null && await ProjectSelectionAsync(result, token, testProject, testConfiguration, testProperties, resolvedProject) is { } project)
                {
                    major = project.Settings.PostgresMajor;
                    path = forwardedPath is null ? project.Settings.PgConfigPath : null;
                    selectedProject = project.ProjectPath;
                }
            }
        }

        if (path is null)
        {
            return new(await new PostgresRegistry(result.GetValue(home)).GetAsync(major ?? 18, token), selectedProject);
        }

        PostgresInstallation installation = await PostgresInstallation.CreateAsync(path, token);
        if (major is int expected && installation.Version.Major != expected)
        {
            throw new ArgumentException($"Expected PostgreSQL {expected}, but '{path}' is {installation.Label}.");
        }

        return new(installation, selectedProject);
    }

    /// <summary>
    /// Resolves build metadata without requiring a registered or discovered server installation.
    /// </summary>
    /// <param name="result">The parsed command.</param>
    /// <param name="token">Cancels evaluation.</param>
    /// <param name="resolvedProject">An extension already resolved by the command.</param>
    /// <returns>The explicit or project-selected PostgreSQL major.</returns>
    private static async Task<int> SelectMajorAsync(ParseResult result, CancellationToken token, string? resolvedProject = null)
    {
        if (ExplicitMajor(result) is int major)
        {
            return major;
        }

        if ((result.GetValue<string?>("--pg-config") ?? BuildProperties(result).GetValueOrDefault("AnkusPgConfigPath")) is string path)
        {
            return (await PostgresInstallation.CreateAsync(path, token)).Version.Major;
        }

        return EnvironmentMajor() ?? (await ProjectSelectionAsync(result, token, resolvedProject: resolvedProject))?.Settings.PostgresMajor ?? 18;
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
    /// Reads pgrx-compatible version selection only after explicit command and property overrides have been applied.
    /// </summary>
    /// <returns>The environment-selected major, or no selection when the variable is absent or empty.</returns>
    private static int? EnvironmentMajor()
    {
        string? value = Environment.GetEnvironmentVariable("PG_VERSION");
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        string number = value.StartsWith("pg", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        if (!int.TryParse(number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int major) ||
            major is < 13 or > 19)
        {
            throw new ArgumentException("PG_VERSION must select PostgreSQL 13–19, such as 18 or pg18.");
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
    /// <param name="resolvedProject">An extension already resolved by the command.</param>
    /// <returns>The evaluated project settings, or null for commands without a project.</returns>
    private static async Task<ProjectPostgresSelection?> ProjectSelectionAsync(ParseResult result, CancellationToken token,
        string? testProject = null, string? testConfiguration = null, IReadOnlyDictionary<string, string>? properties = null,
        string? resolvedProject = null)
    {
        properties ??= BuildProperties(result);
        bool test = result.CommandResult.Command.Name == "test";
        bool cluster = result.CommandResult.Command.Name is "start" or "stop" or "status" or "info" ||
            result.CommandResult.Parent is System.CommandLine.Parsing.CommandResult { Command.Name: "info" };
        if (!test && !cluster && !result.CommandResult.Command.Options.Any(static option => option.Name == "--project"))
        {
            return null;
        }

        if (!test && !cluster)
        {
            string project = resolvedProject ?? await ExtensionBuilder.ResolveProjectAsync(result.GetValue<string?>("--project"),
                GetConfiguration(result), token, properties);
            return new(await PostgresProjectSettings.ReadAsync(project, GetConfiguration(result), properties, cancellationToken: token), project);
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

        PostgresProjectSettings? settings = await PostgresProjectSettings.TryReadAsync(projects,
            testConfiguration ?? (cluster ? "Debug" : GetConfiguration(result)), properties, cancellationToken: token);
        return settings is null ? null : new(settings, null);
    }

    /// <summary>
    /// Resolves an extension only once and retains its build context for publication.
    /// </summary>
    /// <param name="result">The parsed extension command.</param>
    /// <param name="home">The registry home option.</param>
    /// <param name="token">Cancels project and installation selection.</param>
    /// <param name="resolvedProject">An extension already resolved before a dry-run decision.</param>
    /// <returns>The exact extension, installation and build properties for this invocation.</returns>
    private static async Task<ExtensionSelection> SelectExtensionAsync(ParseResult result, Option<string?> home, CancellationToken token,
        string? resolvedProject = null)
    {
        string configuration = GetConfiguration(result);
        IReadOnlyDictionary<string, string> properties = BuildProperties(result);
        PostgresSelection selection = await SelectWithProjectAsync(result, home, token, testConfiguration: configuration,
            testProperties: properties, resolvedProject: resolvedProject);
        string project = selection.ProjectPath ?? await ExtensionBuilder.ResolveProjectAsync(result.GetValue<string?>("--project"),
            configuration, token, properties);
        return new(selection.Installation, project, configuration, properties);
    }

    /// <summary>
    /// Captures project defaults without treating a multi-project test selection as one extension.
    /// </summary>
    /// <param name="Settings">The evaluated PostgreSQL settings.</param>
    /// <param name="ProjectPath">The single extension path, or null for a test or cluster selection.</param>
    private sealed record ProjectPostgresSelection(PostgresProjectSettings Settings, string? ProjectPath);

    /// <summary>
    /// Carries an installation and any extension resolved while selecting its defaults.
    /// </summary>
    /// <param name="Installation">The checked native installation.</param>
    /// <param name="ProjectPath">The already resolved extension, when available.</param>
    private sealed record PostgresSelection(PostgresInstallation Installation, string? ProjectPath);

    /// <summary>
    /// Captures a complete build selection without process-wide caches or repeated solution scans.
    /// </summary>
    /// <param name="Installation">The checked native installation.</param>
    /// <param name="Project">The resolved extension project.</param>
    /// <param name="Configuration">The effective build configuration.</param>
    /// <param name="Properties">The literal properties shared by evaluation and publication.</param>
    private sealed record ExtensionSelection(PostgresInstallation Installation, string Project, string Configuration,
        IReadOnlyDictionary<string, string> Properties);
}
