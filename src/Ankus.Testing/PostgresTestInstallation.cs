using Ankus.PgConfig;

namespace Ankus.Testing;

/// <summary>
/// Owns a relocatable copy of a PostgreSQL installation for isolated extension tests.
/// </summary>
public sealed class PostgresTestInstallation : IAsyncDisposable
{
    private int _disposed;

    private PostgresTestInstallation(PostgresInstallation installation, string rootDirectory)
    {
        Installation = installation;
        RootDirectory = rootDirectory;
    }

    /// <summary>
    /// Gets the relocated PostgreSQL installation.
    /// </summary>
    public PostgresInstallation Installation { get; }

    /// <summary>
    /// Gets the staging root owned by this instance.
    /// </summary>
    public string RootDirectory { get; }

    /// <summary>
    /// Copies PostgreSQL into a relocatable staging root without changing the source installation.
    /// </summary>
    /// <param name="installation">The source PostgreSQL installation.</param>
    /// <param name="rootDirectory">An empty directory to own as the staging root.</param>
    /// <param name="cancellationToken">Cancels file staging and PostgreSQL inspection.</param>
    /// <returns>The staged installation owner.</returns>
    public static async Task<PostgresTestInstallation> StageAsync(
        PostgresInstallation installation,
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        string root = Path.GetFullPath(rootDirectory);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new IOException($"The PostgreSQL staging directory is not empty: {root}");
        }

        Directory.CreateDirectory(root);
        try
        {
            string[] directories =
            [
                installation.BinDirectory,
                installation.LibraryDirectory,
                installation.SharedDirectory,
                installation.IncludeDirectory,
                installation.ServerIncludeDirectory,
            ];
            StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            foreach (string source in directories.Distinct(comparer))
            {
                CopyDirectory(source, StagePath(source, root), cancellationToken);
            }

            string stagedPgConfig = StagePath(installation.PgConfigPath, root);
            PostgresInstallation staged = await PostgresInstallation.CreateAsync(stagedPgConfig, cancellationToken)
                .ConfigureAwait(false);
            if (staged.Version.Major != installation.Version.Major)
            {
                throw new InvalidOperationException("The staged PostgreSQL installation changed major version.");
            }

            return new PostgresTestInstallation(staged, root);
        }
        catch
        {
            Directory.Delete(root, recursive: true);
            throw;
        }
    }

    /// <summary>
    /// Copies published extension control and SQL files into this isolated installation.
    /// Native libraries can remain in the publish directory and be selected with <c>dynamic_library_path</c>.
    /// </summary>
    /// <param name="publishDirectory">The Ankus publish directory.</param>
    public void InstallExtensionFiles(string publishDirectory)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(publishDirectory);
        string source = Path.Combine(Path.GetFullPath(publishDirectory), "extension");
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"The published extension directory was not found: {source}");
        }

        string[] files =
        [
            .. Directory.EnumerateFiles(source)
                .Where(static path => Path.GetExtension(path) is ".control" or ".sql"),
        ];
        if (!files.Any(static path => Path.GetExtension(path) == ".control")
            || !files.Any(static path => Path.GetExtension(path) == ".sql"))
        {
            throw new InvalidOperationException("The publish directory does not contain extension control and SQL files.");
        }

        string destination = Path.Combine(Installation.SharedDirectory, "extension");
        Directory.CreateDirectory(destination);
        foreach (string file in files)
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
    }

    /// <summary>
    /// Deletes the staged installation.
    /// </summary>
    /// <returns>A completed task after synchronous file cleanup.</returns>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && Directory.Exists(RootDirectory))
        {
            Directory.Delete(RootDirectory, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private static string StagePath(string absolutePath, string rootDirectory)
    {
        string fullPath = Path.GetFullPath(absolutePath);
        string pathRoot = Path.GetPathRoot(fullPath)
            ?? throw new InvalidOperationException($"PostgreSQL returned a path without a root: {fullPath}");
        return Path.Combine(rootDirectory, fullPath[pathRoot.Length..]);
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"The PostgreSQL installation directory was not found: {source}");
        }

        Directory.CreateDirectory(destination);
        foreach (string sourceDirectory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = Path.GetRelativePath(source, sourceDirectory);
            Directory.CreateDirectory(Path.Combine(destination, relativePath));
        }

        foreach (string sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = Path.GetRelativePath(source, sourceFile);
            string destinationFile = Path.Combine(destination, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, overwrite: true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(destinationFile, File.GetUnixFileMode(sourceFile));
            }
        }
    }
}
