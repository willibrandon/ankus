using System.Diagnostics;

namespace Ankus.Testing;

/// <summary>
/// Executes native PostgreSQL tools directly without a command shell and applies
/// a caller-provided process environment consistently across platforms.
/// </summary>
internal static class ProcessRunner
{
    /// <summary>
    /// Executes a process, captures both output streams, and terminates its process
    /// tree if cancellation interrupts the invocation.
    /// </summary>
    /// <param name="fileName">The executable to run.</param>
    /// <param name="arguments">Individual command-line arguments.</param>
    /// <param name="environment">Environment variables to add or replace.</param>
    /// <param name="cancellationToken">Cancels and terminates the process.</param>
    /// <param name="captureOutput">Whether to redirect output rather than inherit the host's handles.</param>
    /// <returns>The captured process result.</returns>
    internal static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken,
        bool captureOutput = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        foreach ((string key, string? value) in environment)
        {
            process.StartInfo.Environment[key] = value;
        }

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start '{fileName}'.");
        }

        Task<string> standardOutput = captureOutput
            ? process.StandardOutput.ReadToEndAsync(CancellationToken.None) : Task.FromResult(string.Empty);
        Task<string> standardError = captureOutput
            ? process.StandardError.ReadToEndAsync(CancellationToken.None) : Task.FromResult(string.Empty);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The process exited between cancellation and termination.
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);

            throw;
        }

        return new ProcessResult(
            process.ExitCode,
            await standardOutput.ConfigureAwait(false),
            await standardError.ConfigureAwait(false));
    }

    /// <summary>
    /// Executes a process and throws a detailed exception when it exits
    /// unsuccessfully.
    /// </summary>
    /// <param name="fileName">The executable to run.</param>
    /// <param name="arguments">Individual command-line arguments.</param>
    /// <param name="environment">Environment variables to add or replace.</param>
    /// <param name="cancellationToken">Cancels and terminates the process.</param>
    /// <param name="captureOutput">Whether to redirect output rather than inherit the host's handles.</param>
    /// <returns>The successful process result.</returns>
    internal static async Task<ProcessResult> RunCheckedAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken,
        bool captureOutput = true)
    {
        ProcessResult result = await RunAsync(
            fileName,
            arguments,
            environment,
            cancellationToken,
            captureOutput).ConfigureAwait(false);
        result.EnsureSuccess(fileName, arguments);
        return result;
    }
}
