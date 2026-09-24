using System.Runtime.InteropServices;
using Ankus.PgConfig;
using Ankus.Testing;

namespace Ankus.IntegrationTests;

/// <summary>
/// Resolves local PostgreSQL and publishes the sample for the test host's native runtime identifier.
/// </summary>
internal static class IntegrationEnvironment
{
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
        string? pgConfig = Environment.GetEnvironmentVariable("ANKUS_TEST_PG_CONFIG");
        PostgresInstallation installation = string.IsNullOrWhiteSpace(pgConfig)
            ? await PostgresInstallation.DiscoverAsync(cancellationToken)
            : await PostgresInstallation.CreateAsync(pgConfig, cancellationToken);
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
        await PublishExtensionAsync("tests", "Ankus.TestExtension", cancellationToken);
        await PublishExtensionAsync("samples", "Ankus.Examples.Enums", cancellationToken);
        await PublishExtensionAsync("samples", "Ankus.Examples.Composites", cancellationToken);
        await PublishExtensionAsync("samples", "Ankus.Examples.Operators", cancellationToken);
        await PublishExtensionAsync("samples", "Ankus.Examples.Sets", cancellationToken);
        await PublishExtensionAsync("samples", "Ankus.Examples.Triggers", cancellationToken);
        await PublishExtensionAsync("samples", "Ankus.Examples.EventTriggers", cancellationToken);
        await PublishExtensionAsync("samples", "Ankus.Examples.Aggregates", cancellationToken);
        await PublishExtensionAsync("samples", "Ankus.Examples.Initialization", cancellationToken);
        await PublishExtensionAsync("samples", "Ankus.Examples.Configuration", cancellationToken);
        await PublishExtensionAsync("tests", "Ankus.GucOnlyExtension", cancellationToken);
        await PublishExtensionAsync("tests", "Ankus.GucPrefixExtension", cancellationToken);
        await PublishExtensionAsync("tests", "Ankus.GucUnicodePrefixExtension", cancellationToken);
        await PublishExtensionAsync("tests", "Ankus.GucHooksExtension", cancellationToken);
        await PublishExtensionAsync("tests", "Ankus.PreloadExtension", cancellationToken);
        await PublishExtensionAsync("tests", "Ankus.GucAssignExtension", cancellationToken);
        await PublishExtensionAsync("tests", "Ankus.GucShowExtension", cancellationToken);
        return await PublishExtensionAsync("samples", "Ankus.Examples.Hello", cancellationToken);
    }

    private static async Task<string> PublishExtensionAsync(string directory, string name, CancellationToken cancellationToken)
    {
        string project = Path.Combine(RepositoryRoot, directory, name, name + ".csproj");
        string output = NativeOutputDirectory;
        List<string> arguments = ["publish", project, "--configuration", "Release", "--runtime", RuntimeInformation.RuntimeIdentifier,
            "--self-contained", "true", "--output", output];
        string? pgConfig = Environment.GetEnvironmentVariable("ANKUS_TEST_PG_CONFIG");
        if (!string.IsNullOrWhiteSpace(pgConfig))
        {
            arguments.Add("-p:AnkusPgConfigPath=" + pgConfig);
        }

        await ProcessRunner.RunCheckedAsync(
            "dotnet",
            arguments,
            new Dictionary<string, string?>(),
            cancellationToken);

        string extension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        return RequireFile(Path.Combine(output, name + extension));
    }

    private static string RequireFile(string path)
        => File.Exists(path) ? path : throw new FileNotFoundException("The Native AOT extension was not published.", path);
}
