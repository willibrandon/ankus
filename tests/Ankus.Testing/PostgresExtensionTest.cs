using System.Globalization;
using System.Runtime.InteropServices;
using Ankus.PgConfig;
using Npgsql;

namespace Ankus.Testing;

/// <summary>
/// Publishes an extension project, loads it into an isolated PostgreSQL 18+ cluster, and owns both lifetimes.
/// Ordinary test initialization can use this fixture without environment variables or wrapper commands.
/// </summary>
public sealed class PostgresExtensionTest : IAsyncDisposable
{
    private readonly string _publishDirectory;
    private readonly object _disposeLock = new();
    private Task? _disposeTask;

    private PostgresExtensionTest(PostgresTestCluster cluster, string publishDirectory)
    {
        Cluster = cluster;
        _publishDirectory = publishDirectory;
    }

    /// <summary>Gets the running cluster, with the extension installed in the public schema.</summary>
    public PostgresTestCluster Cluster { get; }

    /// <summary>
    /// Publishes for the host architecture using the selected installation's headers, starts a fresh cluster,
    /// and executes CREATE EXTENSION. Build and startup failures fail initialization rather than skip tests.
    /// </summary>
    /// <param name="projectPath">The extension project file.</param>
    /// <param name="installation">The PostgreSQL installation, or null to discover PostgreSQL 18.</param>
    /// <param name="cancellationToken">Cancels discovery, publication, or startup.</param>
    /// <returns>The fixture to dispose after all tests finish.</returns>
    /// <remarks>
    /// Uses PostgreSQL 18's per-cluster extension search path to avoid changing the shared installation.
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

        installation ??= await PostgresInstallation.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        if (installation.Version.Major < 18)
        {
            throw new NotSupportedException("Isolated extension publication requires PostgreSQL 18 or later (extension_control_path). Use PostgresTestCluster with an explicitly staged installation for earlier versions.");
        }

        string root = Path.GetDirectoryName(projectPath)!;
        string invocation = Guid.NewGuid().ToString("N");
        string output = Path.Combine(root, "bin", "ankus-test-publish", invocation);
        string logs = Path.Combine(root, "bin", "ankus-test-logs");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(logs);
        PostgresTestCluster? cluster = null;
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
            cluster = await PostgresTestCluster.StartAsync(new PostgresTestClusterOptions
            {
                Installation = installation,
                DataDirectoryBase = Path.Combine(root, "bin", "ankus-test-pgdata"),
                LogDirectory = logs,
                PostgreSqlConfiguration =
                [
                    $"extension_control_path = '{searchPath}{separator}$system'",
                    $"dynamic_library_path = '{searchPath}{separator}$libdir'",
                ],
            }, cancellationToken).ConfigureAwait(false);
            await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            string name = Path.GetFileNameWithoutExtension(manifest.Control);
            await using var command = new NpgsqlCommand("CREATE EXTENSION \"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"", connection);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return new PostgresExtensionTest(cluster, output);
        }
        catch
        {
            if (cluster is not null)
            {
                await cluster.DisposeAsync().ConfigureAwait(false);
            }

            Directory.Delete(output, recursive: true);
            throw;
        }
    }

    /// <summary>Stops the backend before deleting the temporary published library. Logs remain available.</summary>
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
        if (Directory.Exists(_publishDirectory))
        {
            Directory.Delete(_publishDirectory, recursive: true);
        }
    }

    private static string EscapeProperty(string value)
        => string.Concat(value.Select(static c => c is '%' or ';' or ',' or '$' or '@' or '(' or ')' or '\'' or '*' or '?' or '"'
            ? "%" + ((int)c).ToString("X2", CultureInfo.InvariantCulture) : c.ToString()));
}
