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
    private static readonly SemaphoreSlim s_stagingLock = new(1, 1);
    private static bool s_nativeExtensionFilesInstalled;
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
            installation = await PrepareExtensionInstallationAsync(NativeOutputDirectory, cancellationToken);
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
    /// Makes extension control files visible to PostgreSQL releases that predate extension_control_path.
    /// </summary>
    /// <param name="publishDirectory">The publish directory containing extension control and SQL files.</param>
    /// <param name="cancellationToken">Cancels PostgreSQL staging.</param>
    /// <returns>The source installation for PostgreSQL 18 or later; otherwise an isolated staged installation.</returns>
    internal static async Task<PostgresInstallation> PrepareExtensionInstallationAsync(
        string publishDirectory,
        CancellationToken cancellationToken)
    {
        PostgresInstallation installation = await GetInstallationAsync(cancellationToken);
        if (installation.Version.Major >= 18)
        {
            return installation;
        }

        await s_stagingLock.WaitAsync(cancellationToken);
        try
        {
            if (s_stagedInstallation is null)
            {
                string stageRoot = Path.Combine(RepositoryRoot, "artifacts", "test-postgresql", Guid.NewGuid().ToString("N"));
                s_stagedInstallation = await PostgresTestInstallation.StageAsync(installation, stageRoot, cancellationToken);
            }

            string source = Path.GetFullPath(publishDirectory);
            string nativeSource = Path.GetFullPath(NativeOutputDirectory);
            bool isNativeOutput = string.Equals(source, nativeSource,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            if (!isNativeOutput || !s_nativeExtensionFilesInstalled)
            {
                s_stagedInstallation.InstallExtensionFiles(publishDirectory);
                s_nativeExtensionFilesInstalled |= isNativeOutput;
            }

            return s_stagedInstallation.Installation;
        }
        finally
        {
            s_stagingLock.Release();
        }
    }

    /// <summary>
    /// Removes a staged pre-18 PostgreSQL installation after every cluster has stopped.
    /// </summary>
    internal static async Task CleanupAsync()
    {
        await s_stagingLock.WaitAsync();
        try
        {
            if (s_stagedInstallation is not null)
            {
                await s_stagedInstallation.DisposeAsync();
                s_stagedInstallation = null;
                s_nativeExtensionFilesInstalled = false;
            }
        }
        finally
        {
            s_stagingLock.Release();
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
        (string Directory, string Name)[] extensions =
        [
            ("tests", "Ankus.TestExtension"),
            ("samples", "Ankus.Examples.Enums"),
            ("samples", "Ankus.Examples.Composites"),
            ("samples", "Ankus.Examples.Operators"),
            ("samples", "Ankus.Examples.Sets"),
            ("samples", "Ankus.Examples.Triggers"),
            ("samples", "Ankus.Examples.EventTriggers"),
            ("samples", "Ankus.Examples.Aggregates"),
            ("samples", "Ankus.Examples.Initialization"),
            ("samples", "Ankus.Examples.Configuration"),
            ("tests", "Ankus.GucOnlyExtension"),
            ("tests", "Ankus.GucPrefixExtension"),
            ("tests", "Ankus.GucUnicodePrefixExtension"),
            ("tests", "Ankus.GucHooksExtension"),
            ("tests", "Ankus.PreloadExtension"),
            ("tests", "Ankus.GucAssignExtension"),
            ("tests", "Ankus.GucShowExtension"),
            ("samples", "Ankus.Examples.Hello"),
        ];
        string publishRoot = Path.Combine(RepositoryRoot, "artifacts", "native-publish", RuntimeInformation.RuntimeIdentifier);
        if (Directory.Exists(publishRoot))
        {
            Directory.Delete(publishRoot, true);
        }

        Directory.CreateDirectory(publishRoot);
        (string Directory, string Name) first = extensions[0];
        await PublishExtensionAsync(first.Directory, first.Name, Path.Combine(publishRoot, first.Name),
            installation, buildProjectReferences: true, cancellationToken);
        ParallelOptions options = new()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 3),
        };
        await Parallel.ForEachAsync(extensions.AsMemory(1).ToArray(), options, async (extension, token) =>
        {
            await PublishExtensionAsync(extension.Directory, extension.Name, Path.Combine(publishRoot, extension.Name),
                installation, buildProjectReferences: false, token);
        });

        if (Directory.Exists(NativeOutputDirectory))
        {
            Directory.Delete(NativeOutputDirectory, true);
        }

        foreach ((string Directory, string Name) extension in extensions)
        {
            CopyDirectory(Path.Combine(publishRoot, extension.Name), NativeOutputDirectory);
        }

        Directory.Delete(publishRoot, true);
        string libraryExtension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        return RequireFile(Path.Combine(NativeOutputDirectory, "Ankus.Examples.Hello" + libraryExtension));
    }

    private static async Task PublishExtensionAsync(string directory, string name, string output,
        PostgresInstallation installation, bool buildProjectReferences, CancellationToken cancellationToken)
    {
        string project = Path.Combine(RepositoryRoot, directory, name, name + ".csproj");
        List<string> arguments = ["publish", project, "--configuration", "Release", "--runtime", RuntimeInformation.RuntimeIdentifier,
            "--self-contained", "true", "--output", output,
            "-p:BuildProjectReferences=" + buildProjectReferences.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
            "-p:AnkusPostgresMajor=" + installation.Version.Major.ToString(CultureInfo.InvariantCulture),
            "-p:AnkusPgConfigPath=" + installation.PgConfigPath];

        await ProcessRunner.RunCheckedAsync(
            "dotnet",
            arguments,
            new Dictionary<string, string?>(),
            cancellationToken);

        string extension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        RequireFile(Path.Combine(output, name + extension));
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string destinationFile = Path.Combine(destination, Path.GetRelativePath(source, sourceFile));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, true);
        }
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
