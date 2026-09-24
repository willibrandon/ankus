using System.Globalization;
using System.Runtime.InteropServices;
using Ankus.PgConfig;
using Ankus.Testing;

namespace Ankus.IntegrationTests;

/// <summary>
/// Resolves local PostgreSQL and publishes the sample for the test host's native runtime identifier.
/// </summary>
internal static class IntegrationEnvironment
{
    private static PostgresInstallation? s_installation;
    private static PostgresTestInstallation? s_stagedInstallation;

    /// <summary>
    /// Gets the repository root from the test binary's build location.
    /// </summary>
    internal static string RepositoryRoot
    {
        get
        {
            for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Ankus.slnx")))
                {
                    return directory.FullName;
                }
            }

            throw new DirectoryNotFoundException("Run these integration tests from the Ankus repository build output.");
        }
    }

    /// <summary>
    /// Gets the host-native publish directory containing the sample library and its extension installation files.
    /// </summary>
    internal static string NativeOutputDirectory
        => Path.Combine(RepositoryRoot, "artifacts", "native", RuntimeInformation.RuntimeIdentifier);

    /// <summary>
    /// Creates cluster settings using an automatically discovered local installation.
    /// </summary>
    /// <param name="cancellationToken">Cancels PostgreSQL discovery.</param>
    /// <returns>The cluster settings for this environment.</returns>
    internal static async Task<PostgresTestClusterOptions> CreateOptionsAsync(CancellationToken cancellationToken)
    {
        PostgresInstallation installation = await GetInstallationAsync(cancellationToken);
        string nativePath = NativeOutputDirectory.Replace("\\", "/", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal);
        char pathSeparator = OperatingSystem.IsWindows() ? ';' : ':';
        List<string> configuration = [$"dynamic_library_path = '{nativePath}{pathSeparator}$libdir'"];
        if (installation.Version.Major >= 18)
        {
            configuration.Insert(0, $"extension_control_path = '{nativePath}'");
        }
        else
        {
            if (s_stagedInstallation is null)
            {
                string stageRoot = Path.Combine(RepositoryRoot, "artifacts", "test-postgresql", Guid.NewGuid().ToString("N"));
                s_stagedInstallation = await PostgresTestInstallation.StageAsync(installation, stageRoot, cancellationToken);
                s_stagedInstallation.InstallExtensionFiles(NativeOutputDirectory);
            }

            installation = s_stagedInstallation.Installation;
        }

        return new PostgresTestClusterOptions
        {
            Installation = installation,
            DataDirectoryBase = Path.Combine(RepositoryRoot, "artifacts", "test-pgdata"),
            LogDirectory = Path.Combine(RepositoryRoot, "artifacts", "test-logs"),
            StartupTimeout = TimeSpan.FromSeconds(60),
            PostgreSqlConfiguration = configuration,
        };
    }

    /// <summary>
    /// Removes a staged pre-18 PostgreSQL installation after every cluster has stopped.
    /// </summary>
    internal static async Task CleanupAsync()
    {
        if (s_stagedInstallation is not null)
        {
            await s_stagedInstallation.DisposeAsync();
            s_stagedInstallation = null;
        }
    }

    /// <summary>
    /// Publishes a host-native sample library as part of ordinary test initialization.
    /// </summary>
    /// <param name="cancellationToken">Cancels the Native AOT build.</param>
    /// <returns>The absolute path to the extension library.</returns>
    internal static async Task<string> PublishSampleAsync(CancellationToken cancellationToken)
    {
        PostgresInstallation installation = await GetInstallationAsync(cancellationToken);
        await PublishExtensionAsync("tests", "Ankus.TestExtension", installation, cancellationToken);
        await PublishExtensionAsync("samples", "Ankus.Examples.Enums", installation, cancellationToken);
        await PublishExtensionAsync("samples", "Ankus.Examples.Composites", installation, cancellationToken);
        await PublishExtensionAsync("samples", "Ankus.Examples.Operators", installation, cancellationToken);
        await PublishExtensionAsync("samples", "Ankus.Examples.Sets", installation, cancellationToken);
        await PublishExtensionAsync("samples", "Ankus.Examples.Triggers", installation, cancellationToken);
        await PublishExtensionAsync("samples", "Ankus.Examples.EventTriggers", installation, cancellationToken);
        await PublishExtensionAsync("samples", "Ankus.Examples.Aggregates", installation, cancellationToken);
        await PublishExtensionAsync("samples", "Ankus.Examples.Initialization", installation, cancellationToken);
        await PublishExtensionAsync("samples", "Ankus.Examples.Configuration", installation, cancellationToken);
        await PublishExtensionAsync("tests", "Ankus.GucOnlyExtension", installation, cancellationToken);
        await PublishExtensionAsync("tests", "Ankus.GucPrefixExtension", installation, cancellationToken);
        await PublishExtensionAsync("tests", "Ankus.GucUnicodePrefixExtension", installation, cancellationToken);
        await PublishExtensionAsync("tests", "Ankus.GucHooksExtension", installation, cancellationToken);
        await PublishExtensionAsync("tests", "Ankus.PreloadExtension", installation, cancellationToken);
        await PublishExtensionAsync("tests", "Ankus.GucAssignExtension", installation, cancellationToken);
        await PublishExtensionAsync("tests", "Ankus.GucShowExtension", installation, cancellationToken);
        return await PublishExtensionAsync("samples", "Ankus.Examples.Hello", installation, cancellationToken);
    }

    private static async Task<string> PublishExtensionAsync(string directory, string name,
        PostgresInstallation installation, CancellationToken cancellationToken)
    {
        string project = Path.Combine(RepositoryRoot, directory, name, name + ".csproj");
        string output = NativeOutputDirectory;
        List<string> arguments = ["publish", project, "--configuration", "Release", "--runtime", RuntimeInformation.RuntimeIdentifier,
            "--self-contained", "true", "--output", output,
            "-p:AnkusPostgresMajor=" + installation.Version.Major.ToString(CultureInfo.InvariantCulture),
            "-p:AnkusPgConfigPath=" + installation.PgConfigPath];

        await ProcessRunner.RunCheckedAsync(
            "dotnet",
            arguments,
            new Dictionary<string, string?>(),
            cancellationToken);

        string extension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        return RequireFile(Path.Combine(output, name + extension));
    }

    private static async Task<PostgresInstallation> GetInstallationAsync(CancellationToken cancellationToken)
    {
        if (s_installation is not null)
        {
            return s_installation;
        }

        string? pgConfig = Environment.GetEnvironmentVariable("ANKUS_TEST_PG_CONFIG");
        PostgresInstallation installation = string.IsNullOrWhiteSpace(pgConfig)
            ? await PostgresInstallation.DiscoverAsync(cancellationToken)
            : await PostgresInstallation.CreateAsync(pgConfig, cancellationToken);
        s_installation = installation;
        return installation;
    }

    private static string RequireFile(string path)
        => File.Exists(path) ? path : throw new FileNotFoundException("The Native AOT extension was not published.", path);
}
