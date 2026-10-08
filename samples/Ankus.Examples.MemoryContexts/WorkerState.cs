using Ankus.Postgres;

namespace Ankus.Examples.MemoryContexts;

/// <summary>
/// Ports pgrx's background-worker example, which keeps loop state in PostgreSQL's TopMemoryContext.
/// </summary>
/// <remarks>
/// A worker outlives individual transactions and queries. State that must survive every loop iteration belongs
/// to TopMemoryContext, which PostgreSQL never resets during the worker's lifetime.
/// </remarks>
public static class WorkerState
{
    /// <summary>
    /// Registers the worker only when PostgreSQL loads the library through shared_preload_libraries.
    /// </summary>
    [PgModuleLoad]
    public static void Register()
    {
        bool preloading;
        unsafe
        {
            preloading = NativeGlobals.process_shared_preload_libraries_in_progress;
        }

        if (!preloading)
        {
            return;
        }

        PgBackgroundWorker.Register(new("memory_contexts demo worker", "Ankus.Examples.MemoryContexts", "memory_contexts_worker_main")
        {
            // The argument shows where worker-specific configuration such as a queue or partition number belongs.
            Argument = 123,
            RestartDelay = null,
        });
    }

    /// <summary>
    /// Logs one tick per wake-up, counting with native storage owned by TopMemoryContext.
    /// </summary>
    /// <param name="argument">The by-value argument supplied at registration.</param>
    [PgBackgroundWorker(EntryPoint = "memory_contexts_worker_main")]
    public static void Run(nuint argument)
    {
        PgLog.Log($"memory_contexts demo worker starting (arg={argument})");
        using PgMemoryContext top = PgMemoryContext.Get(PgMemoryContextKind.Top)!;

        // TopMemoryContext owns this zeroed counter until the worker process exits.
        PgContextValue<long> counter = top.CreateContextValue(0L);
        while (PgBackgroundWorker.Wait(TimeSpan.FromSeconds(5)))
        {
            long tick = ++counter.Value;
            PgLog.Log($"memory_contexts demo worker tick {tick}");
        }

        PgLog.Log("memory_contexts demo worker exiting");
    }
}
