using Ankus.Testing;

namespace Ankus.IntegrationTests;

/// <summary>
/// Observes an owned postmaster's completed recovery before opening a new backend.
/// </summary>
internal static class CrashRecovery
{
    private static readonly SemaphoreSlim s_crashSlot = new(1, 1);

    /// <summary>
    /// Reserves the host's crash-recovery resource without excluding unrelated integration tests.
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting before an isolated cluster is started.</param>
    /// <returns>A lease released after the cluster and its recovery checks have completed.</returns>
    internal static async Task<IDisposable> ReserveAsync(CancellationToken cancellationToken)
    {
        await s_crashSlot.WaitAsync(cancellationToken);
        return new RecoveryLease();
    }

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

    /// <summary>
    /// Releases an owned crash slot exactly once, including when a test fails or is cancelled.
    /// </summary>
    private sealed class RecoveryLease : IDisposable
    {
        private int _released;

        /// <summary>
        /// Returns capacity after the owning test has disposed its isolated cluster.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                s_crashSlot.Release();
            }
        }
    }
}
