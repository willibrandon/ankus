using System.Globalization;
using System.Runtime.InteropServices;
using Ankus.PgConfig;
using Npgsql;

namespace Ankus.Testing;

/// <summary>
/// Publishes an extension project, loads it into an isolated PostgreSQL cluster, and owns both lifetimes.
/// Ordinary test initialization can use this fixture without environment variables or wrapper commands.
/// </summary>
public sealed class PostgresExtensionTest : IAsyncDisposable
{
    private readonly string _publishDirectory;
    private readonly string _dataDirectoryBase;
    private readonly PostgresTestInstallation? _stagedInstallation;
    private readonly object _disposeLock = new();
    private Task? _disposeTask;

    private PostgresExtensionTest(
        PostgresTestCluster cluster,
        string publishDirectory,
        string dataDirectoryBase,
        PostgresTestInstallation? stagedInstallation)
    {
        Cluster = cluster;
        _publishDirectory = publishDirectory;
        _dataDirectoryBase = dataDirectoryBase;
        _stagedInstallation = stagedInstallation;
    }

    /// <summary>
    /// Gets the running cluster, with the extension installed in the public schema.
    /// </summary>
    public PostgresTestCluster Cluster { get; }

    /// <summary>
    /// Publishes for the host architecture using the selected installation's headers, starts a fresh cluster,
    /// and executes CREATE EXTENSION. Build and startup failures fail initialization rather than skip tests.
    /// </summary>
    /// <param name="projectPath">The extension project file.</param>
    /// <param name="installation">
    /// The PostgreSQL installation, or null to use <c>ANKUS_TEST_PG_CONFIG</c> when set and otherwise discover PostgreSQL 18.
    /// </param>
    /// <param name="cancellationToken">Cancels discovery, publication, or startup.</param>
    /// <returns>The fixture to dispose after all tests finish.</returns>
    /// <remarks>
    /// PostgreSQL 18 and later use a per-cluster extension search path. Earlier versions run from an isolated,
    /// relocatable copy of the selected installation.
    /// Server logs and build logs remain in the project's bin/ankus-test-logs directory.
    /// </remarks>
    public static async Task<PostgresExtensionTest> StartAsync(string projectPath,
        PostgresInstallation? installation = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        projectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(projectPath) || !projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("The extension project was not found.", projectPath);
        }

        if (installation is null)
        {
            string? testPgConfig = Environment.GetEnvironmentVariable("ANKUS_TEST_PG_CONFIG");
            installation = string.IsNullOrWhiteSpace(testPgConfig)
                ? await PostgresInstallation.DiscoverAsync(cancellationToken).ConfigureAwait(false)
                : await PostgresInstallation.CreateAsync(testPgConfig, cancellationToken).ConfigureAwait(false);
        }

        string root = Path.GetDirectoryName(projectPath)!;
        string invocation = Guid.NewGuid().ToString("N");
        string output = Path.Combine(root, "bin", "ankus-test-publish", invocation);
        string logs = Path.Combine(root, "bin", "ankus-test-logs");
        string dataDirectoryBase = Path.Combine(Path.GetTempPath(), "ankus-test-pgdata-" + invocation);
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(logs);
        PostgresTestCluster? cluster = null;
        PostgresTestInstallation? stagedInstallation = null;
        try
        {
            await ProcessRunner.RunCheckedAsync("dotnet",
                ["publish", projectPath, "-c", "Release", "-r", RuntimeInformation.RuntimeIdentifier, "-o", output,
                    "-p:AnkusPostgresMajor=" + installation.Version.Major.ToString(CultureInfo.InvariantCulture),
                    "-p:AnkusPgConfigPath=" + EscapeProperty(installation.PgConfigPath),
                    "-bl:" + Path.Combine(logs, invocation + ".binlog")],
                new Dictionary<string, string?>(), cancellationToken, workingDirectory: root).ConfigureAwait(false);
            PublishedExtension manifest = PublishedExtension.Read(output);
            if (manifest.PostgresMajor != installation.Version.Major || manifest.RuntimeIdentifier != RuntimeInformation.RuntimeIdentifier)
            {
                throw new InvalidOperationException("The published extension does not match the selected PostgreSQL target.");
            }

            string searchPath = output.Replace("\\", "/", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal);
            char separator = OperatingSystem.IsWindows() ? ';' : ':';
            PostgresInstallation clusterInstallation = installation;
            List<string> configuration = [$"dynamic_library_path = '{searchPath}{separator}$libdir'"];
            if (installation.Version.Major >= 18)
            {
                configuration.Insert(0, $"extension_control_path = '{searchPath}{separator}$system'");
            }
            else
            {
                string stageRoot = Path.Combine(Path.GetTempPath(), "ankus-test-postgresql-" + invocation);
                stagedInstallation = await PostgresTestInstallation.StageAsync(installation, stageRoot, cancellationToken)
                    .ConfigureAwait(false);
                stagedInstallation.InstallExtensionFiles(output);
                clusterInstallation = stagedInstallation.Installation;
            }

            cluster = await PostgresTestCluster.StartAsync(new PostgresTestClusterOptions
            {
                Installation = clusterInstallation,
                DataDirectoryBase = dataDirectoryBase,
                LogDirectory = logs,
                PostgreSqlConfiguration = configuration,
            }, cancellationToken).ConfigureAwait(false);
            await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            string name = Path.GetFileNameWithoutExtension(manifest.Control);
            await using var command = new NpgsqlCommand("CREATE EXTENSION \"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"", connection);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return new PostgresExtensionTest(cluster, output, dataDirectoryBase, stagedInstallation);
        }
        catch
        {
            if (cluster is not null)
            {
                await cluster.DisposeAsync().ConfigureAwait(false);
            }

            if (stagedInstallation is not null)
            {
                await stagedInstallation.DisposeAsync().ConfigureAwait(false);
            }

            DeleteDirectory(dataDirectoryBase);
            Directory.Delete(output, recursive: true);
            throw;
        }
    }

    /// <summary>
    /// Stops the backend before deleting the temporary published library. Logs remain available.
    /// </summary>
    /// <returns>A task completing after shutdown and file cleanup.</returns>
    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            if (_disposeTask is null || _disposeTask.IsFaulted)
            {
                _disposeTask = DisposeCoreAsync();
            }

            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await Cluster.DisposeAsync().ConfigureAwait(false);
        if (_stagedInstallation is not null)
        {
            await _stagedInstallation.DisposeAsync().ConfigureAwait(false);
        }

        DeleteDirectory(_dataDirectoryBase);
        if (Directory.Exists(_publishDirectory))
        {
            Directory.Delete(_publishDirectory, recursive: true);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string EscapeProperty(string value)
        => string.Concat(value.Select(static c => c is '%' or ';' or ',' or '$' or '@' or '(' or ')' or '\'' or '*' or '?' or '"'
            ? "%" + ((int)c).ToString("X2", CultureInfo.InvariantCulture) : c.ToString()));
}
