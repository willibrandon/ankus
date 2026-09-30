using Ankus.PgConfig;

namespace Ankus.Tool;

/// <summary>
/// Owns one test process's fixture storage and stops surviving servers before deleting it.
/// </summary>
/// <param name="installation">The selected server tools used for shutdown.</param>
internal sealed class ExtensionTestCommandSession(PostgresInstallation installation) : IAsyncDisposable
{
    /// <summary>
    /// Gets the private fixture root. Unix paths stay short enough for PostgreSQL sockets.
    /// </summary>
    internal string DirectoryPath { get; } = Directory.CreateDirectory(Path.Combine(
        OperatingSystem.IsWindows() ? Path.GetTempPath() : "/tmp", "ak-test-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>
    /// Stops servers left behind by an aborted host and removes only this invocation's storage.
    /// </summary>
    /// <returns>A task completing after server shutdown and directory removal.</returns>
    public async ValueTask DisposeAsync()
    {
        string dataRoot = Path.Combine(DirectoryPath, "pgdata");
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
                            throw new InvalidOperationException($"Test server cleanup failed. Storage retained at '{DirectoryPath}'.");
                        }
                    }
                }
                else if (status != 3)
                {
                    throw new InvalidOperationException($"Test server status failed ({status}). Storage retained at '{DirectoryPath}'.");
                }
            }
        }

        Directory.Delete(DirectoryPath, recursive: true);
    }
}
