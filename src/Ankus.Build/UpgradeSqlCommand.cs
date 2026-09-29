using System.Diagnostics;
using System.Text;
using Ankus.PgConfig;

namespace Ankus.Build;

/// <summary>
/// Publishes declared upgrade scripts after validating their names and expanding author tokens.
/// </summary>
internal static class UpgradeSqlCommand
{
    /// <summary>
    /// Publishes the installation SQL and selected upgrades, then commits the complete manifest.
    /// </summary>
    /// <param name="arguments">Artifact directory, publish directory, script list, project directory, and extension version.</param>
    /// <param name="cancellationToken">Cancels script reads and Git inspection.</param>
    /// <returns>A task that completes after the publication is installable.</returns>
    internal static async Task RunAsync(string[] arguments, CancellationToken cancellationToken = default)
    {
        if (arguments.Length != 5)
        {
            throw new ArgumentException("Expected artifact directory, publish directory, upgrade list, project directory, and extension version.", nameof(arguments));
        }

        cancellationToken.ThrowIfCancellationRequested();
        string artifacts = Path.GetFullPath(arguments[0]);
        string output = Path.GetFullPath(arguments[1]);
        PublishedExtension.Invalidate(output);
        PublishedExtension original = PublishedExtension.Read(artifacts);
        string[] scripts = File.ReadAllLines(arguments[2]);
        var manifest = new PublishedExtension(original.PostgresMajor, original.RuntimeIdentifier, original.Library,
            original.Control, original.Sql, [.. scripts.Select(static path => Path.GetFileName(path))]);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        string? gitHash = null;
        foreach (string script in scripts)
        {
            string content = encoding.GetString(await File.ReadAllBytesAsync(script, cancellationToken));
            if (content.Contains("@GIT_HASH@", StringComparison.Ordinal))
            {
                gitHash ??= await ReadGitHashAsync(arguments[3], cancellationToken);
                content = content.Replace("@GIT_HASH@", gitHash, StringComparison.Ordinal);
            }

            content = content.Replace("@EXTENSION_VERSION@", arguments[4], StringComparison.Ordinal);
            contents.Add(Path.GetFileName(script), encoding.GetBytes(content));
        }

        // Read the entire SQL payload before changing any published SQL files.
        foreach (string name in new[] { original.Control, original.Sql })
        {
            contents.Add(name, await File.ReadAllBytesAsync(Path.Combine(artifacts, "extension", name), cancellationToken));
        }

        string extensionDirectory = Path.Combine(output, "extension");
        Directory.CreateDirectory(extensionDirectory);
        foreach ((string name, byte[] content) in contents)
        {
            await File.WriteAllBytesAsync(Path.Combine(extensionDirectory, name), content, cancellationToken);
        }

        manifest.CompletePublish(output);
    }

    private static async Task<string> ReadGitHashAsync(string projectDirectory, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "-C", projectDirectory, "rev-parse", "HEAD" },
            },
        };
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }

        string hash = (await output).Trim();
        string diagnostic = await error;
        if (process.ExitCode != 0 || hash.Length is not (40 or 64) || !hash.All(char.IsAsciiHexDigit))
        {
            throw new InvalidOperationException("Cannot expand @GIT_HASH@ without a committed Git repository: " + diagnostic);
        }

        return hash;
    }
}
