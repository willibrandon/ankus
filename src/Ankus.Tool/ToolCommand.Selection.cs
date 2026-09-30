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
    /// <returns>The selected and version-checked installation.</returns>
    private static async Task<PostgresInstallation> SelectAsync(ParseResult result, Option<string?> home, CancellationToken token,
        string? testProject = null, string? testConfiguration = null)
    {
        int? major = ExplicitMajor(result);
        string? path = result.GetValue<string?>("--pg-config");
        if (major is null && path is null)
        {
            if (result.CommandResult.Command.Name is "install" or "package" && result.GetValue<string?>("--from") is string publication)
            {
                major = PublishedExtension.Read(publication).PostgresMajor;
            }
            else if (await ProjectSelectionAsync(result, token, testProject, testConfiguration) is { } project)
            {
                major = project.PostgresMajor;
                path = project.PgConfigPath;
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

        if (result.GetValue<string?>("--pg-config") is string path)
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
    /// <returns>The evaluated project settings, or null for commands without a project.</returns>
    private static async Task<PostgresProjectSettings?> ProjectSelectionAsync(ParseResult result, CancellationToken token,
        string? testProject = null, string? testConfiguration = null)
    {
        bool test = result.CommandResult.Command.Name == "test";
        if (!test && !result.CommandResult.Command.Options.Any(static option => option.Name == "--project"))
        {
            return null;
        }

        string project = ExtensionBuilder.ResolveProject(test ? testProject : result.GetValue<string?>("--project"));
        return await PostgresProjectSettings.ReadAsync(project, testConfiguration ?? GetConfiguration(result), cancellationToken: token);
    }
}
