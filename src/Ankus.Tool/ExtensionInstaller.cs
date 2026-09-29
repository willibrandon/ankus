using System.Runtime.InteropServices;
using Ankus.PgConfig;

namespace Ankus.Tool;

/// <summary>
/// Installs the manifest's native library and control files, placing SQL in the declared PostgreSQL directory.
/// </summary>
internal static class ExtensionInstaller
{
    /// <summary>
    /// Validates the full artifact set and target before copying files, publishing the control file last.
    /// </summary>
    /// <param name="source">The publish directory.</param>
    /// <param name="installation">The target PostgreSQL installation.</param>
    /// <param name="destinationRoot">An optional staging root, analogous to DESTDIR.</param>
    /// <param name="token">Cancels copying between artifacts.</param>
    /// <param name="packageLayout">Whether Windows output uses lib and share/extension relative to the package root.</param>
    /// <returns>The installed file paths.</returns>
    internal static IReadOnlyList<string> Install(string source, PostgresInstallation installation, string? destinationRoot,
        CancellationToken token, bool packageLayout = false)
    {
        source = Path.GetFullPath(source);
        PublishedExtension manifest = PublishedExtension.Read(source);
        if (manifest.PostgresMajor != installation.Version.Major)
        {
            throw new InvalidOperationException(
                $"The extension was built for PostgreSQL {manifest.PostgresMajor}, not {installation.Label}.");
        }

        if (manifest.RuntimeIdentifier != RuntimeInformation.RuntimeIdentifier)
        {
            throw new InvalidOperationException(
                $"The extension targets {manifest.RuntimeIdentifier}, not {RuntimeInformation.RuntimeIdentifier}.");
        }

        string libraryDirectory = StagePath(installation.LibraryDirectory, destinationRoot);
        string extensionDirectory = StagePath(Path.Combine(installation.SharedDirectory, "extension"), destinationRoot);
        string scriptBase = installation.SharedDirectory;
        if (packageLayout && OperatingSystem.IsWindows())
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
            string root = Path.GetFullPath(destinationRoot);
            libraryDirectory = Path.Combine(root, "lib");
            extensionDirectory = Path.Combine(root, "share", "extension");
            scriptBase = Path.Combine(root, "share");
        }

        IReadOnlyList<string> scriptDirectories = GetScriptDirectoryTraversal(manifest.GetScriptDirectory(source, scriptBase));
        string scriptDirectory = scriptDirectories[^1];
        if (packageLayout && OperatingSystem.IsWindows() && !Path.IsPathRooted(manifest.ScriptDirectory ?? "extension"))
        {
            string relative = Path.GetRelativePath(Path.GetFullPath(destinationRoot!), scriptDirectory);
            if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                throw new InvalidOperationException("The SQL directory escapes the portable Windows package root; use install --destdir for a full filesystem layout.");
            }
        }
        else
        {
            scriptDirectories = [.. scriptDirectories.Select(directory => StagePath(directory, destinationRoot))];
            scriptDirectory = scriptDirectories[^1];
        }

        (string Source, string Destination)[] files =
        [
            (Path.Combine(source, manifest.Library), Path.Combine(libraryDirectory, manifest.Library)),
            (Path.Combine(source, "extension", manifest.Sql), Path.Combine(scriptDirectory, manifest.Sql)),
            .. manifest.UpgradeScripts.Select(script =>
                (Path.Combine(source, "extension", script), Path.Combine(scriptDirectory, script))),
            .. manifest.VersionControlFiles.Select(control =>
                (Path.Combine(source, "extension", control), Path.Combine(scriptDirectory, control))),
            (Path.Combine(source, "extension", manifest.Control), Path.Combine(extensionDirectory, manifest.Control)),
        ];
        foreach ((string input, _) in files)
        {
            if (!File.Exists(input))
            {
                throw new FileNotFoundException("The published extension is incomplete.", input);
            }
        }

        foreach (string directory in scriptDirectories)
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
        }

        foreach ((string input, string destination) in files)
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(input, temporary);
                File.Move(temporary, destination, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        return [.. files.Select(static file => file.Destination)];
    }

    private static List<string> GetScriptDirectoryTraversal(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return [Path.GetFullPath(path)];
        }

        string current = Path.GetPathRoot(path)!;
        var directories = new List<string> { current };
        foreach (string component in path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (component == ".")
            {
                continue;
            }

            if (component == "..")
            {
                var directory = new DirectoryInfo(current);
                if (directory.LinkTarget is not null)
                {
                    current = Path.TrimEndingDirectorySeparator(directory.ResolveLinkTarget(returnFinalTarget: true)!.FullName);
                }

                current = Path.GetDirectoryName(current) ?? Path.GetPathRoot(current)!;
            }
            else
            {
                current = Path.Combine(current, component);
            }

            directories.Add(current);
        }

        return directories;
    }

    private static string StagePath(string absolutePath, string? root)
        => root is null ? absolutePath : Path.Combine(Path.GetFullPath(root), absolutePath[Path.GetPathRoot(absolutePath)!.Length..]);
}
