using System.Runtime.InteropServices;
using Ankus.PgConfig;
using Ankus.Testing;

namespace Ankus.IntegrationTests;

/// <summary>
/// Resolves local PostgreSQL and publishes the sample for the test host's native runtime identifier.
/// </summary>
internal static class IntegrationEnvironment
{
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
        PostgresInstallation installation = await PostgresInstallation.DiscoverAsync(cancellationToken);
        string nativePath = NativeOutputDirectory.Replace("\\", "/", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal);
        char pathSeparator = OperatingSystem.IsWindows() ? ';' : ':';
        return new PostgresTestClusterOptions
        {
            Installation = installation,
            DataDirectoryBase = Path.Combine(RepositoryRoot, "artifacts", "test-pgdata"),
            LogDirectory = Path.Combine(RepositoryRoot, "artifacts", "test-logs"),
            StartupTimeout = TimeSpan.FromSeconds(60),
            PostgreSqlConfiguration =
            [
                $"extension_control_path = '{nativePath}'",
                $"dynamic_library_path = '{nativePath}{pathSeparator}$libdir'",
            ],
        };
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
        return await PublishExtensionAsync("samples", "Ankus.Examples.Hello", cancellationToken);
    }

    private static async Task<string> PublishExtensionAsync(string directory, string name, CancellationToken cancellationToken)
    {
        string project = Path.Combine(RepositoryRoot, directory, name, name + ".csproj");
        string output = NativeOutputDirectory;
        await ProcessRunner.RunCheckedAsync(
            "dotnet",
            ["publish", project, "--configuration", "Release", "--runtime", RuntimeInformation.RuntimeIdentifier,
                "--self-contained", "true", "--output", output],
            new Dictionary<string, string?>(),
            cancellationToken);

        string extension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        return RequireFile(Path.Combine(output, name + extension));
    }

    private static string RequireFile(string path)
        => File.Exists(path) ? path : throw new FileNotFoundException("The Native AOT extension was not published.", path);
}
