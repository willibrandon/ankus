namespace Ankus.IntegrationTests;

/// <summary>
/// Owns immutable packaged probes reused by cases that vary only their backend operations.
/// </summary>
public sealed partial class ToolCommandTests
{
    private static CancellationTokenSource? s_publicationLifetime;
    private static Lazy<Task<string>>? s_workerCancellationPublication;
    private static Lazy<Task<string>>? s_lwLockInterruptPublication;

    /// <summary>
    /// Retains one binary per probe while every consuming case owns a separate PostgreSQL server.
    /// </summary>
    private static void InitializeSharedPublications()
    {
        s_publicationLifetime = new CancellationTokenSource();
        CancellationToken token = s_publicationLifetime.Token;
        s_workerCancellationPublication = new(() => PublishPackageConsumerInDirectoryAsync(CreateSharedDirectory(),
            "WorkerCancellation", "ankus_worker_cancellation", WorkerCancellationSource, token));
        s_lwLockInterruptPublication = new(() => PublishPackageConsumerInDirectoryAsync(CreateSharedDirectory(),
            "LwLockInterrupts", "ankus_lwlock_interrupts", LwLockInterruptSource, token));
    }

    /// <summary>
    /// Cancels and joins fixture-owned compilation before class cleanup removes its output.
    /// </summary>
    private static async Task CleanupSharedPublicationsAsync()
    {
        if (s_publicationLifetime is null)
        {
            return;
        }

        await s_publicationLifetime.CancelAsync();
        Lazy<Task<string>>?[] publications = [s_workerCancellationPublication, s_lwLockInterruptPublication];
        foreach (Lazy<Task<string>>? publication in publications)
        {
            if (publication is { IsValueCreated: true })
            {
                // Consumers observe publication failures. Cleanup must still join the
                // producer when a test cancellation stopped waiting for its result.
                await ((Task)publication.Value).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }

        s_publicationLifetime.Dispose();
        s_publicationLifetime = null;
        s_workerCancellationPublication = null;
        s_lwLockInterruptPublication = null;
    }

    /// <summary>
    /// Lets one case cancel its wait without cancelling another case's shared compilation.
    /// </summary>
    private static Task<string> GetSharedPublicationAsync(Lazy<Task<string>>? publication, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (publication is null)
        {
            throw new InvalidOperationException("The packaged probe fixture is not initialized.");
        }

        return publication.Value.WaitAsync(token);
    }
}
