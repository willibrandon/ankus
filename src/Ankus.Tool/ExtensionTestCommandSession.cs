using Ankus.PgConfig;

namespace Ankus.Tool;

/// <summary>
/// Owns one test process's fixture storage and stops surviving servers before deleting it.
/// </summary>
/// <param name="installation">The selected server tools used for shutdown.</param>
/// <param name="dataDirectoryBase">An optional parent for this invocation's cluster storage.</param>
internal sealed class ExtensionTestCommandSession(PostgresInstallation installation, string? dataDirectoryBase = null) : IAsyncDisposable
{
    private readonly (string Session, string Data) _directories = CreateDirectories(dataDirectoryBase);

    /// <summary>
    /// Gets the private fixture root. Unix paths stay short enough for PostgreSQL sockets.
    /// </summary>
    internal string DirectoryPath => _directories.Session;

    /// <summary>
    /// Gets the owned cluster root, separate from sockets when the caller selects a data directory.
    /// </summary>
    internal string DataDirectoryPath => _directories.Data;

    /// <summary>
    /// Stops servers left behind by an aborted host and removes only this invocation's storage.
    /// </summary>
    /// <returns>A task completing after server shutdown and directory removal.</returns>
    public async ValueTask DisposeAsync()
    {
        string dataRoot = DataDirectoryPath;
        if (Directory.Exists(dataRoot))
        {
            var enumeration = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (string versionFile in Directory.EnumerateFiles(dataRoot, "PG_VERSION", enumeration))
            {
                string dataDirectory = Path.GetDirectoryName(versionFile)!;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var output = new MemoryStream();
                int status = await ToolProcess.RunAsync(installation.PgCtlPath, ["status", "-D", dataDirectory], timeout.Token,
                    outputStream: output);
                if (status == 0)
                {
                    int stopped = await ToolProcess.RunAsync(installation.PgCtlPath,
                        ["stop", "-D", dataDirectory, "-m", "immediate", "-w", "-t", "25"], timeout.Token, outputStream: output);
                    if (stopped != 0)
                    {
                        status = await ToolProcess.RunAsync(installation.PgCtlPath, ["status", "-D", dataDirectory], timeout.Token,
                            outputStream: output);
                        if (status != 3)
                        {
                            throw new InvalidOperationException($"Test server cleanup failed. Storage retained at '{dataRoot}' and '{DirectoryPath}'.");
                        }
                    }
                }
                else if (status != 3)
                {
                    throw new InvalidOperationException($"Test server status failed ({status}). Storage retained at '{dataRoot}' and '{DirectoryPath}'.");
                }
            }

            Directory.Delete(dataRoot, recursive: true);
        }

        Directory.Delete(DirectoryPath, recursive: true);
    }

    private static (string Session, string Data) CreateDirectories(string? dataDirectoryBase)
    {
        string? parent = dataDirectoryBase is null ? null : Directory.CreateDirectory(Path.GetFullPath(dataDirectoryBase)).FullName;
        string invocation = Guid.NewGuid().ToString("N");
        string session = Directory.CreateDirectory(Path.Combine(OperatingSystem.IsWindows() ? Path.GetTempPath() : "/tmp",
            "ak-test-" + invocation)).FullName;
        try
        {
            string data = Directory.CreateDirectory(parent is null ? Path.Combine(session, "pgdata") :
                Path.Combine(parent, "ak-test-pgdata-" + invocation)).FullName;
            return (session, data);
        }
        catch
        {
            Directory.Delete(session, recursive: true);
            throw;
        }
    }
}
