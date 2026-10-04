namespace Ankus.Tool;

/// <summary>
/// Presents regression inputs through an ASCII-only path to PostgreSQL's Windows test driver.
/// </summary>
internal sealed class WindowsRegressionWorkspace : IDisposable
{
    private static readonly string[] s_outputFiles = ["regression.out", "regression.diffs"];

    private WindowsRegressionWorkspace(string directoryPath)
    {
        DirectoryPath = directoryPath;
    }

    /// <summary>
    /// Gets the temporary directory passed to the native regression driver.
    /// </summary>
    internal string DirectoryPath { get; }

    /// <summary>
    /// Creates a Windows workspace only when the authored suite path contains characters outside ASCII.
    /// </summary>
    internal static WindowsRegressionWorkspace? Create(string source)
    {
        if (!OperatingSystem.IsWindows() || source.All(char.IsAscii))
        {
            return null;
        }

        string target = CreateAsciiTemporaryDirectory();
        var workspace = new WindowsRegressionWorkspace(target);
        try
        {
            CopyDirectory(Path.Combine(source, "sql"), Path.Combine(target, "sql"));
            CopyDirectory(Path.Combine(source, "expected"), Path.Combine(target, "expected"));
            string resultMap = Path.Combine(source, "resultmap");
            if (File.Exists(resultMap))
            {
                File.Copy(resultMap, Path.Combine(target, "resultmap"));
            }

            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Copies native results back to their authored locations before the workspace is removed.
    /// </summary>
    internal void CopyOutputsTo(string destination)
    {
        CopyDirectory(Path.Combine(DirectoryPath, "results"), Path.Combine(destination, "results"));
        foreach (string name in s_outputFiles)
        {
            string source = Path.Combine(DirectoryPath, name);
            string target = Path.Combine(destination, name);
            if (File.Exists(source))
            {
                File.Copy(source, target, overwrite: true);
            }
            else
            {
                File.Delete(target);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }

    private static string CreateAsciiTemporaryDirectory()
    {
        string[] roots = [Path.GetTempPath(), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)];
        var failures = new List<Exception>();
        foreach (string root in roots.Where(static path => path.Length != 0 && path.All(char.IsAscii)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string path = Path.Combine(root, "ankus-regress-" + Guid.NewGuid().ToString("N"));
            try
            {
                return Directory.CreateDirectory(path).FullName;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                failures.Add(error);
            }
        }

        throw new AggregateException("PostgreSQL's Windows regression driver requires a writable ASCII-only temporary path. Set TEMP to such a directory.", failures);
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
