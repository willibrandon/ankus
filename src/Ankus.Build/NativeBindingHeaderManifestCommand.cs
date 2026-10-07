using System.Globalization;
using Ankus.PgConfig;

namespace Ankus.Build;

/// <summary>
/// Generates or checks one PostgreSQL header manifest from an installed server.
/// </summary>
internal static class NativeBindingHeaderManifestCommand
{
    /// <summary>
    /// Generates or checks the selected header manifest.
    /// </summary>
    /// <param name="arguments">The PostgreSQL major, pg_config path, output path, and optional --check flag.</param>
    /// <param name="cancellationToken">Cancels PostgreSQL installation discovery and output writes.</param>
    internal static async Task RunAsync(string[] arguments, CancellationToken cancellationToken = default)
    {
        if (arguments.Length is < 3 or > 4 || arguments.Length == 4 && arguments[3] != "--check" ||
            !int.TryParse(arguments[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) || major is < 13 or > 19)
        {
            throw new ArgumentException(
                "Expected binding-header-manifest <major 13-19> <pg_config> <output-path> [--check].",
                nameof(arguments));
        }

        PostgresInstallation installation = await PostgresInstallation.CreateAsync(arguments[1], cancellationToken).ConfigureAwait(false);
        if (installation.Version.Major != major)
        {
            throw new InvalidOperationException(
                $"Expected PostgreSQL {major}, but '{installation.PgConfigPath}' is {installation.Label}.");
        }

        string path = Path.GetFullPath(arguments[2]);
        string content = NativeBindingHeaderManifest.Generate(installation.ServerIncludeDirectory);
        bool check = arguments.Length == 4;
        if (File.Exists(path) && await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false) == content)
        {
            Console.WriteLine($"PG{major}: header manifest is current.");
            return;
        }

        if (check)
        {
            throw new InvalidOperationException($"Native header manifest is stale: {path}");
        }

        string? directory = Path.GetDirectoryName(path);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"PG{major}: generated header manifest from {installation.Label}.");
    }
}
