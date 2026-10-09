using System.Globalization;

namespace Ankus.Build;

/// <summary>
/// Writes validated primary control metadata from a managed extension without executing or publishing it.
/// </summary>
internal static class ExtensionControlCommand
{
    /// <summary>
    /// Reads generated assembly metadata and atomically writes the effective primary control file.
    /// </summary>
    /// <param name="arguments">
    /// Assembly, output, PostgreSQL major, extension name, version, library, optional author-file path and optional
    /// versioned-library mode.
    /// </param>
    internal static async Task RunAsync(string[] arguments)
    {
        if (arguments.Length is not (7 or 8))
        {
            throw new ArgumentException("Expected assembly, control output, PostgreSQL major, name, version, library, author control path " +
                "and optional versioned-library mode.");
        }

        bool versionedLibrary = arguments.Length == 8 && VersionedLibrary.ParseMode(arguments[7]);
        if (versionedLibrary)
        {
            _ = VersionedLibrary.GetBaseName(arguments[5], arguments[4]);
        }

        int major = int.Parse(arguments[2], CultureInfo.InvariantCulture);
        ArgumentOutOfRangeException.ThrowIfLessThan(major, 13);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(major, 19);
        string output = Path.GetFullPath(arguments[1]);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(output, Path.GetFullPath(arguments[0]), comparison) ||
            (arguments[6].Length != 0 && string.Equals(output, Path.GetFullPath(arguments[6]), comparison)))
        {
            throw new ArgumentException("The control output must differ from the managed assembly and author control paths.");
        }

        ExtensionManifest manifest = ExtensionManifest.Read(arguments[0]);
        string? authored = arguments[6].Length == 0 ? null : await File.ReadAllTextAsync(arguments[6]);
        IReadOnlyDictionary<string, string> package = ExtensionPackage.Create(arguments[3], arguments[4], arguments[5],
            manifest.Sql, manifest.Relocatable, authored, major, versionedLibrary);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, package[arguments[3] + ".control"]);
            File.Move(temporary, output, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
