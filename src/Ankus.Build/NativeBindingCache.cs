using System.Security.Cryptography;
using System.Text.Json;

namespace Ankus.Build;

/// <summary>
/// Shares immutable verified binding artifacts while serializing producers across processes.
/// </summary>
internal static class NativeBindingCache
{
    private const string ManifestName = "manifest.json";

    /// <summary>
    /// Returns a verified entry or publishes a complete replacement after its producer succeeds.
    /// </summary>
    /// <param name="root">The dedicated Ankus cache directory.</param>
    /// <param name="key">The SHA-256 identity of all explicitly selected inputs.</param>
    /// <param name="produce">Writes deliverable files in staging and returns additional compiler input files.</param>
    /// <param name="cancellationToken">Cancels lock acquisition, production or verification.</param>
    /// <returns>Ownership of the verified entry until its consumer finishes reading the artifacts.</returns>
    internal static async Task<NativeBindingCacheLease> GetAsync(string root, string key,
        Func<string, CancellationToken, Task<IReadOnlyList<NativeBindingCacheFile>>> produce, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(produce);
        if (key.Length != 64 || key.Any(static value => !char.IsAsciiHexDigitUpper(value)))
        {
            throw new ArgumentException("A binding cache key must be an uppercase SHA-256 identity.", nameof(key));
        }

        cancellationToken.ThrowIfCancellationRequested();
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        // Never remove lock files: deleting an unlocked name races another process opening it.
        FileStream ownership = await LockAsync(Path.Combine(root, key + ".lock"), cancellationToken);
        try
        {
            string entry = await PopulateAsync(root, key, produce, cancellationToken);
            return new(entry, ownership);
        }
        catch
        {
            await ownership.DisposeAsync();
            throw;
        }
    }

    private static async Task<string> PopulateAsync(string root, string key,
        Func<string, CancellationToken, Task<IReadOnlyList<NativeBindingCacheFile>>> produce, CancellationToken cancellationToken)
    {
        string entry = Path.Combine(root, key);
        if (await ValidAsync(entry, key, cancellationToken))
        {
            return entry;
        }

        string stage = Path.Combine(root, key + ".stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            IReadOnlyList<NativeBindingCacheFile> inputs = await produce(stage, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            string[] files = [.. Directory.GetFiles(stage).Order(StringComparer.Ordinal)];
            if (files.Length == 0 || Directory.GetDirectories(stage).Length != 0 || files.Any(static file => Path.GetFileName(file) == ManifestName))
            {
                throw new InvalidOperationException("A binding cache producer must supply nonempty flat artifact files.");
            }

            var artifacts = new List<NativeBindingCacheFile>();
            foreach (string file in files)
            {
                artifacts.Add(new(Path.GetFileName(file), await HashAsync(file, cancellationToken)));
            }

            NativeBindingCacheFile[] dependencies = [.. inputs.Distinct().OrderBy(static input => input.Path, StringComparer.Ordinal)];
            if (dependencies.Any(static input => !Path.IsPathFullyQualified(input.Path)) ||
                !await MatchFilesAsync(dependencies, cancellationToken))
            {
                throw new IOException("A binding compiler input changed during production.");
            }

            var manifest = new NativeBindingCacheManifest(key, artifacts, dependencies);
            await File.WriteAllTextAsync(Path.Combine(stage, ManifestName), JsonSerializer.Serialize(manifest), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!await ValidAsync(stage, key, cancellationToken))
            {
                throw new IOException("Binding cache artifacts changed before publication.");
            }

            string? previous = null;
            if (Directory.Exists(entry))
            {
                previous = Path.Combine(root, key + ".replaced-" + Guid.NewGuid().ToString("N"));
                Directory.Move(entry, previous);
            }

            try
            {
                Directory.Move(stage, entry);
            }
            catch
            {
                if (previous is not null)
                {
                    Directory.Move(previous, entry);
                }

                throw;
            }

            if (previous is not null)
            {
                await NativeBuildDirectory.DeleteAsync(previous);
            }

            return entry;
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                await NativeBuildDirectory.DeleteAsync(stage);
            }
        }
    }

    /// <summary>
    /// Computes content identity without trusting file timestamps.
    /// </summary>
    internal static async Task<string> HashAsync(string file, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    /// <summary>
    /// Observes file content with bounded concurrent reads while preserving the caller's deterministic order.
    /// </summary>
    internal static async Task<NativeBindingCacheFile[]> SnapshotAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        string[] files = [.. paths];
        var snapshot = new NativeBindingCacheFile[files.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, files.Length), new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = cancellationToken,
        }, async (index, token) => snapshot[index] = new(files[index], await HashAsync(files[index], token)));
        return snapshot;
    }

    private static async Task<bool> MatchFilesAsync(IEnumerable<NativeBindingCacheFile> files, CancellationToken cancellationToken)
    {
        int changed = 0;
        await Parallel.ForEachAsync(files, new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = cancellationToken,
        }, async (file, token) =>
        {
            if (!await MatchesAsync(file.Path, file.Hash, allowSymbolicLink: true, token))
            {
                Interlocked.Exchange(ref changed, 1);
            }
        });
        return changed == 0;
    }

    private static async Task<FileStream> LockAsync(string path, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 ||
                error.HResult == (OperatingSystem.IsMacOS() ? 35 : 11))
            {
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    private static async Task<bool> ValidAsync(string directory, string key, CancellationToken cancellationToken)
    {
        string file = Path.Combine(directory, ManifestName);
        if (!File.Exists(file))
        {
            return false;
        }

        NativeBindingCacheManifest? manifest;
        try
        {
            if (new FileInfo(file).Length > 8 * 1024 * 1024)
            {
                return false;
            }

            await using FileStream stream = File.OpenRead(file);
            manifest = await JsonSerializer.DeserializeAsync<NativeBindingCacheManifest>(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return false;
        }

        if (manifest is null || manifest.Key != key || manifest.Artifacts is not { Count: > 0 and <= 100 } ||
            manifest.Dependencies is null || manifest.Dependencies.Count > 100_000)
        {
            return false;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (NativeBindingCacheFile artifact in manifest.Artifacts)
        {
            if (artifact is null || string.IsNullOrEmpty(artifact.Path) || artifact.Path != Path.GetFileName(artifact.Path) ||
                artifact.Path is "." or ".." or ManifestName || !names.Add(artifact.Path) ||
                !await MatchesAsync(Path.Combine(directory, artifact.Path), artifact.Hash, allowSymbolicLink: false, cancellationToken))
            {
                return false;
            }
        }

        foreach (NativeBindingCacheFile dependency in manifest.Dependencies)
        {
            if (dependency is null || string.IsNullOrEmpty(dependency.Path) || !Path.IsPathFullyQualified(dependency.Path) ||
                string.IsNullOrEmpty(dependency.Hash))
            {
                return false;
            }
        }

        return Directory.GetDirectories(directory).Length == 0 && Directory.GetFiles(directory).Length == names.Count + 1 &&
            await MatchFilesAsync(manifest.Dependencies, cancellationToken);
    }

    private static async Task<bool> MatchesAsync(string path, string hash, bool allowSymbolicLink, CancellationToken cancellationToken)
        => File.Exists(path) && (allowSymbolicLink || !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) &&
            await HashAsync(path, cancellationToken) == hash;
}

/// <summary>
/// Keeps a verified cache entry stable until its consumer has copied the required artifacts.
/// </summary>
/// <param name="directory">The verified artifact directory.</param>
/// <param name="ownership">Exclusive cross-process ownership retained until disposal.</param>
internal sealed class NativeBindingCacheLease(string directory, FileStream ownership) : IAsyncDisposable
{
    /// <summary>
    /// Gets the verified directory held by this lease.
    /// </summary>
    internal string Directory { get; } = directory;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ownership.DisposeAsync();
}

/// <summary>
/// Pins one artifact or compiler dependency to exact file content.
/// </summary>
/// <param name="Path">An artifact filename or absolute compiler input path.</param>
/// <param name="Hash">The SHA-256 file content identity.</param>
internal sealed record NativeBindingCacheFile(string Path, string Hash);

/// <summary>
/// Records a completely produced binding cache entry and all additional observed compiler inputs.
/// </summary>
/// <param name="Key">The explicit input identity that selected this entry.</param>
/// <param name="Artifacts">The immutable deliverable files.</param>
/// <param name="Dependencies">Additional compiler input files whose content must still match.</param>
internal sealed record NativeBindingCacheManifest(string Key, IReadOnlyList<NativeBindingCacheFile> Artifacts,
    IReadOnlyList<NativeBindingCacheFile> Dependencies);
