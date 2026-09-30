using System.Runtime.InteropServices;

namespace Ankus.Tool;

/// <summary>
/// Cancels command preparation while allowing an interactive SQL client to handle its own Ctrl+C.
/// </summary>
internal sealed class InteractiveCommandCancellation : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private PosixSignalRegistration? _termination;
    private bool _enabled;
    private int _interactiveClient;

    /// <summary>
    /// Gets cancellation for preparation and noninteractive execution.
    /// </summary>
    internal CancellationToken Token => _cancellation.Token;

    /// <summary>
    /// Installs handlers for run, connect and test, allowing command-owned cleanup after cancellation.
    /// </summary>
    internal void Enable()
    {
        Console.CancelKeyPress += HandleInterrupt;
        _enabled = true;
        if (!OperatingSystem.IsWindows())
        {
            _termination = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                _cancellation.Cancel();
            });
        }
    }

    /// <summary>
    /// Runs a SQL client in the same terminal, leaving query interruption to that client.
    /// </summary>
    /// <param name="executable">The psql or pgcli executable.</param>
    /// <param name="arguments">Client arguments without shell parsing.</param>
    /// <param name="token">Cancels termination and noninteractive execution.</param>
    /// <returns>The client exit code.</returns>
    internal async Task<int> RunClientAsync(string executable, string[] arguments, CancellationToken token)
    {
        Volatile.Write(ref _interactiveClient, Console.IsInputRedirected ? 0 : 1);
        try
        {
            return await ToolProcess.RunAsync(executable, arguments, token, postgresClient: true);
        }
        finally
        {
            Volatile.Write(ref _interactiveClient, 0);
        }
    }

    /// <summary>
    /// Removes handlers after the client and command have exited.
    /// </summary>
    public void Dispose()
    {
        if (_enabled)
        {
            Console.CancelKeyPress -= HandleInterrupt;
        }

        _termination?.Dispose();
        _cancellation.Dispose();
    }

    private void HandleInterrupt(object? sender, ConsoleCancelEventArgs arguments)
    {
        arguments.Cancel = true;
        if (Volatile.Read(ref _interactiveClient) == 0)
        {
            _cancellation.Cancel();
        }
    }
}
