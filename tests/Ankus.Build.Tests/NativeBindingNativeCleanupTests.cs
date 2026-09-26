namespace Ankus.Build.Tests;

public sealed partial class NativeBindingNativeTests
{
    /// <summary>
    /// Releases an open file before Windows cleanup retries, and removes the entire owned directory.
    /// </summary>
    [TestMethod]
    public async Task NativeProbeCleanupWaitsForReleasedFile()
    {
        string directory = Directory.CreateTempSubdirectory("ankus-probe-cleanup-").FullName;
        string file = Path.Combine(directory, "held.bin");
        Task? cleanup = null;
        try
        {
            using (FileStream held = File.Open(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
            {
                cleanup = DeleteDirectoryAsync(directory);
                if (OperatingSystem.IsWindows()) { Assert.IsFalse(cleanup.IsCompleted, "Windows cannot delete a file whose handle denies delete sharing."); }
            }

            await cleanup;
            Assert.IsFalse(Directory.Exists(directory));
        }
        finally
        {
            if (cleanup is not null) { await cleanup; }
            else { await DeleteDirectoryAsync(directory); }
        }
    }

    /// <summary>
    /// Persistent access denial remains a failure, with the file intact for explicit cleanup.
    /// </summary>
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task NativeProbeCleanupDoesNotHideAccessDenial()
    {
        string directory = Directory.CreateTempSubdirectory("ankus-probe-cleanup-").FullName;
        string file = Path.Combine(directory, "readonly.bin");
        try
        {
            await File.WriteAllTextAsync(file, "retained", context.CancellationToken);
            File.SetAttributes(file, FileAttributes.ReadOnly);
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => DeleteDirectoryAsync(directory));
            Assert.AreEqual("retained", await File.ReadAllTextAsync(file, context.CancellationToken));
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);
            await DeleteDirectoryAsync(directory);
        }

        Assert.IsFalse(Directory.Exists(directory));
    }

    /// <summary>
    /// Missing directories are programming errors rather than transient file locks.
    /// </summary>
    [TestMethod]
    public async Task NativeProbeCleanupRejectsMissingDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ankus-probe-missing-" + Guid.NewGuid().ToString("N"));
        await Assert.ThrowsExactlyAsync<DirectoryNotFoundException>(() => DeleteDirectoryAsync(directory));
    }

    /// <summary>
    /// Removes owned probe artifacts after exit, allowing bounded retries for Windows file-release races.
    /// </summary>
    private static async Task DeleteDirectoryAsync(string directory)
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
                // Access denied, sharing/lock violation, or a directory still containing a locked file.
                // Cleanup must finish even if the test's own cancellation token has already expired.
                await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None);
            }
        }
    }
}
