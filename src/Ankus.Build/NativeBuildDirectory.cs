namespace Ankus.Build;

/// <summary>
/// Removes owned native build staging after its tools and diagnostic streams have finished.
/// </summary>
internal static class NativeBuildDirectory
{
    /// <summary>
    /// Allows bounded Windows file-release retries while preserving persistent access and ownership failures.
    /// </summary>
    /// <param name="directory">The caller-owned directory whose child processes have already been joined.</param>
    internal static async Task DeleteAsync(string directory)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (Exception error) when (OperatingSystem.IsWindows() && attempt < 19 &&
                error is IOException or UnauthorizedAccessException && (error.HResult & 0xFFFF) is 5 or 32 or 33 or 145)
            {
                // Access denied, sharing/lock violation, or a directory containing a locked file.
                // Owned cleanup must finish even after the operation's cancellation token expires.
                await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None);
            }
        }
    }
}
