using System.Runtime.InteropServices;
using Ankus.PgConfig;

namespace Ankus.Tool;

/// <summary>
/// Installs only the manifest's native library, versioned SQL, and control file.
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
    /// <returns>The installed file paths.</returns>
    internal static IReadOnlyList<string> Install(string source, PostgresInstallation installation, string? destinationRoot,
        CancellationToken token)
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
        (string Source, string Destination)[] files =
        [
            (Path.Combine(source, manifest.Library), Path.Combine(libraryDirectory, manifest.Library)),
            (Path.Combine(source, "extension", manifest.Sql), Path.Combine(extensionDirectory, manifest.Sql)),
            (Path.Combine(source, "extension", manifest.Control), Path.Combine(extensionDirectory, manifest.Control)),
        ];
        foreach ((string input, _) in files)
        {
            if (!File.Exists(input))
            {
                throw new FileNotFoundException("The published extension is incomplete.", input);
            }
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

    private static string StagePath(string absolutePath, string? root)
        => root is null ? absolutePath : Path.Combine(Path.GetFullPath(root), absolutePath[Path.GetPathRoot(absolutePath)!.Length..]);
}
