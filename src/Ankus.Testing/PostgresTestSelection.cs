using System.Globalization;
using Ankus.PgConfig;

namespace Ankus.Testing;

/// <summary>
/// Carries explicit fixture, command and build selections through to the matching server and headers.
/// </summary>
internal static class PostgresTestSelection
{
    /// <summary>
    /// Resolves explicit selections before consulting the extension's evaluated project defaults.
    /// </summary>
    /// <param name="options">The fixture's publication and installation options.</param>
    /// <param name="cancellationToken">Cancels project and installation queries.</param>
    /// <returns>The installation used for both native publication and the test server.</returns>
    internal static async Task<PostgresInstallation> ResolveAsync(PostgresExtensionTestOptions options, CancellationToken cancellationToken)
    {
        if (options.Installation is PostgresInstallation explicitInstallation)
        {
            return explicitInstallation;
        }

        string? commandPath = Environment.GetEnvironmentVariable("ANKUS_TEST_PG_CONFIG");
        if (!string.IsNullOrWhiteSpace(commandPath))
        {
            return await PostgresInstallation.CreateAsync(commandPath, cancellationToken).ConfigureAwait(false);
        }

        string? commandMajor = Environment.GetEnvironmentVariable("ANKUS_TEST_POSTGRES_MAJOR");
        string? hostMajor = ReadHostValue("Ankus.Testing.PostgresMajor");
        int? major = ParseMajor(commandMajor ?? hostMajor,
            commandMajor is null ? "AnkusPostgresMajor" : "ANKUS_TEST_POSTGRES_MAJOR");
        string? hostPath = ReadHostValue("Ankus.Testing.PgConfigPath");
        if (!string.IsNullOrWhiteSpace(hostPath))
        {
            PostgresInstallation installation = await PostgresInstallation.CreateAsync(hostPath, cancellationToken).ConfigureAwait(false);
            RequireMajor(installation, major);
            return installation;
        }

        PostgresProjectSettings settings = await PostgresProjectSettings.ReadAsync(options.ProjectPath, options.Configuration,
            major, cancellationToken).ConfigureAwait(false);
        PostgresInstallation selected = settings.PgConfigPath is null
            ? await PostgresInstallation.DiscoverAsync(settings.PostgresMajor,
                ReadHostValue("Ankus.Testing.HomeDirectory"), cancellationToken).ConfigureAwait(false)
            : await PostgresInstallation.CreateAsync(settings.PgConfigPath, cancellationToken).ConfigureAwait(false);
        RequireMajor(selected, settings.PostgresMajor);
        return selected;
    }

    /// <summary>
    /// Reads the standard .NET runtime configuration without changing process-wide state.
    /// </summary>
    /// <param name="name">The runtime configuration key emitted by the testing package's build targets.</param>
    /// <returns>The configured value, or null when the calling build did not select it.</returns>
    private static string? ReadHostValue(string name)
    {
        string? value = Convert.ToString(AppContext.GetData(name), CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// Validates a selected major without silently reverting to the default.
    /// </summary>
    /// <param name="value">The optional explicit major.</param>
    /// <param name="source">The setting named in an invalid-value diagnostic.</param>
    /// <returns>The selected major, or null when unspecified.</returns>
    private static int? ParseMajor(string? value, string source)
    {
        if (value is null)
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int major) && major is >= 13 and <= 19
            ? major : throw new InvalidOperationException($"{source} must select PostgreSQL 13–19.");
    }

    /// <summary>
    /// Prevents an explicitly selected configuration path from silently changing the requested major.
    /// </summary>
    /// <param name="installation">The installation reported by pg_config.</param>
    /// <param name="major">The selected major, when one was specified.</param>
    private static void RequireMajor(PostgresInstallation installation, int? major)
    {
        if (major is int expected && installation.Version.Major != expected)
        {
            throw new InvalidOperationException($"The build selected PostgreSQL {expected}, but pg_config reports PostgreSQL {installation.Version.Major}.");
        }
    }
}
