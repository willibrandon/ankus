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
    /// <param name="diagnosticsToStandardError">Whether to stream the child's stdout to stderr.</param>
    /// <param name="outputStream">An optional destination for captured stdout.</param>
    /// <param name="postgresClient">Whether to clear inherited PostgreSQL connection settings.</param>
    /// <param name="workingDirectory">An optional child working directory.</param>
    /// <param name="environment">Optional child-only environment overrides, applied after PostgreSQL connection isolation.</param>
    /// <returns>The process exit code.</returns>
    internal static async Task<int> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken token,
        bool diagnosticsToStandardError = false, Stream? outputStream = null, bool postgresClient = false,
        string? workingDirectory = null, IReadOnlyDictionary<string, string?>? environment = null)
    {
        token.ThrowIfCancellationRequested();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = diagnosticsToStandardError || outputStream is not null,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            },
        };
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (postgresClient)
        {
            foreach (string key in process.StartInfo.Environment.Keys.Where(static key => key.StartsWith("PG", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                process.StartInfo.Environment.Remove(key);
            }

            process.StartInfo.Environment["PGCLIENTENCODING"] = "UTF8";
        }

        if (environment is not null)
        {
            foreach ((string name, string? value) in environment)
            {
                process.StartInfo.Environment[name] = value;
            }
        }

        process.Start();
        try
        {
            Task output = process.StartInfo.RedirectStandardOutput
                ? process.StandardOutput.BaseStream.CopyToAsync(outputStream ?? Console.OpenStandardError(), token)
                : Task.CompletedTask;
            await Task.WhenAll(output, process.WaitForExitAsync(token));
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
