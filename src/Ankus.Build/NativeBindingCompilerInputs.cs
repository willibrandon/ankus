namespace Ankus.Build;

/// <summary>
/// Observes the exact compiler inputs selected by MSBuild with bounded asynchronous content reads.
/// </summary>
internal static class NativeBindingCompilerInputs
{
    /// <summary>
    /// Hashes each selected file once without trusting timestamps or omitting analyzer dependencies.
    /// </summary>
    /// <param name="inventory">The file containing MSBuild's absolute compiler input paths.</param>
    /// <param name="cancellationToken">Cancels inventory and content reads.</param>
    /// <returns>Content identities in deterministic path order.</returns>
    internal static async Task<NativeBindingCacheFile[]> ReadAsync(string inventory, CancellationToken cancellationToken)
    {
        string[] paths = await File.ReadAllLinesAsync(inventory, cancellationToken);
        if (paths.Length == 0 || paths.Any(static path => !Path.IsPathFullyQualified(path)))
        {
            throw new FormatException("The binding compiler input snapshot is invalid.");
        }

        return await NativeBindingCache.SnapshotAsync(paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), cancellationToken);
    }
}
