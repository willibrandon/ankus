using Ankus.Testing;

namespace Ankus.IntegrationTests;

/// <summary>
/// Observes an owned postmaster's completed recovery before opening a new backend.
/// </summary>
internal static class CrashRecovery
{
    /// <summary>
    /// Waits for readiness after the most recent reinitialization, without repeatedly spawning rejected backends.
    /// </summary>
    /// <param name="cluster">The isolated server whose recovery is under test.</param>
    /// <param name="cancellationToken">The existing recovery deadline.</param>
    /// <returns>The log snapshot proving reinitialization and subsequent readiness.</returns>
    internal static async Task<string> WaitAsync(PostgresTestCluster cluster, CancellationToken cancellationToken)
    {
        const string Restart = "all server processes terminated; reinitializing";
        const string Ready = "database system is ready to accept connections";
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string log = cluster.ReadServerLog();
            int restart = log.LastIndexOf(Restart, StringComparison.Ordinal);
            if (restart >= 0 && log.IndexOf(Ready, restart + Restart.Length, StringComparison.Ordinal) >= 0)
            {
                return log;
            }

            await Task.Delay(500, cancellationToken);
        }
    }
}
