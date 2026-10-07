using Ankus.Testing;

namespace Ankus.IntegrationTests;

/// <summary>
/// Limits build-intensive child processes without blocking unrelated tool tests.
/// </summary>
internal static class PackageProcessRunner
{
    private static readonly SemaphoreSlim s_slots = new(
        IntegrationEnvironment.PackageTestConcurrency,
        IntegrationEnvironment.PackageTestConcurrency);

    /// <summary>
    /// Runs a child process while reserving capacity only for build-intensive commands.
    /// </summary>
    internal static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken,
        bool captureOutput = true,
        string? workingDirectory = null)
    {
        bool reserve = RequiresSlot(fileName, arguments);
        if (reserve)
        {
            await s_slots.WaitAsync(cancellationToken);
        }

        try
        {
            return await ProcessRunner.RunAsync(
                fileName,
                arguments,
                environment,
                cancellationToken,
                captureOutput,
                workingDirectory);
        }
        finally
        {
            if (reserve)
            {
                s_slots.Release();
            }
        }
    }

    /// <summary>
    /// Runs a child process and requires a successful exit code.
    /// </summary>
    internal static async Task<ProcessResult> RunCheckedAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken,
        bool captureOutput = true,
        string? workingDirectory = null)
    {
        ProcessResult result = await RunAsync(
            fileName,
            arguments,
            environment,
            cancellationToken,
            captureOutput,
            workingDirectory);
        result.EnsureSuccess(fileName, arguments);
        return result;
    }

    private static bool RequiresSlot(string fileName, IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return false;
        }

        string executable = Path.GetFileNameWithoutExtension(fileName);
        string command = arguments[0];
        return string.Equals(executable, "dotnet", StringComparison.OrdinalIgnoreCase)
            ? command is "build" or "msbuild" or "pack" or "publish" or "restore" or "run" or "test" or "tool"
            : string.Equals(executable, "ankus", StringComparison.OrdinalIgnoreCase) &&
                command is "bench" or "build" or "install" or "package" or "publish" or "regress" or "run" or "schema" or "test" or "upgrade";
    }
}
