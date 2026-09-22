using System.Diagnostics;

namespace Ankus.Tool;

/// <summary>
/// Executes child tools without shell parsing and preserves their exit codes.
/// </summary>
internal static class ToolProcess
{
    /// <summary>
    /// Runs a tool with inherited output and terminates its complete process tree on cancellation.
    /// </summary>
    /// <param name="executable">The executable name or path.</param>
    /// <param name="arguments">Individual command-line arguments.</param>
    /// <param name="token">Cancels process execution.</param>
    /// <returns>The process exit code.</returns>
    internal static async Task<int> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false } };
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        try
        {
            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The process exited while cancellation was delivered.
            }

            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        return process.ExitCode;
    }
}
