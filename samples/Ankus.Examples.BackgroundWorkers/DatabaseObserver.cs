namespace Ankus.Examples.BackgroundWorkers;

/// <summary>
/// Observes database metadata in an independent PostgreSQL worker and publishes shared counters.
/// </summary>
public static class DatabaseObserver
{
    private static readonly PgAtomic<int> s_process = new("ankus_background_workers.process");
    private static readonly PgAtomic<long> s_databases = new("ankus_background_workers.databases");
    private static readonly PgAtomic<long> s_observations = new("ankus_background_workers.observations");

    /// <summary>
    /// Registers the worker and its shared values when PostgreSQL preloads the library.
    /// </summary>
    [PgModuleLoad]
    public static void Register()
    {
        PgSharedMemory.Initialize(s_process);
        PgSharedMemory.Initialize(s_databases);
        PgSharedMemory.Initialize(s_observations);
        PgBackgroundWorker.Register(new("Ankus database observer", "Ankus.Examples.BackgroundWorkers", nameof(Run))
        {
            DatabaseAccess = true,
            Argument = 42,
            RestartDelay = TimeSpan.FromSeconds(5),
        });
    }

    /// <summary>
    /// Connects once, commits each observation separately and responds to reload and termination signals.
    /// </summary>
    /// <param name="argument">The registration's by-value identifier.</param>
    [PgBackgroundWorker]
    public static void Run(nuint argument)
    {
        PgBackgroundWorker.Connect("postgres");
        s_process.Exchange(Environment.ProcessId);
        PgLog.Write(PgLogLevel.Log, $"Database observer {argument} started.");
        do
        {
            if ((PgBackgroundWorker.ConsumeSignals(PgBackgroundWorkerSignals.Reload) & PgBackgroundWorkerSignals.Reload) != 0)
            {
                PgBackgroundWorker.ReloadConfiguration();
            }

            long databases = PgBackgroundWorker.RunTransaction(() => Spi.ExecuteScalar<long>("SELECT count(*) FROM pg_database"));
            s_databases.Exchange(databases);
            s_observations.Add(1);
        }
        while (PgBackgroundWorker.Wait(TimeSpan.FromSeconds(10)));

        s_process.Exchange(0);
        PgLog.Write(PgLogLevel.Log, $"Database observer {argument} stopped.");
    }

    /// <summary>
    /// Reads the running worker's process identifier, or zero before startup and after graceful shutdown.
    /// </summary>
    /// <returns>The independently running PostgreSQL process identifier.</returns>
    [PgFunction]
    public static int ObserverProcess() => s_process.Value;

    /// <summary>
    /// Reads the database count from the worker's most recent committed query.
    /// </summary>
    /// <returns>The observed count.</returns>
    [PgFunction]
    public static long ObservedDatabases() => s_databases.Value;

    /// <summary>
    /// Reads the number of completed worker observations since shared-memory initialization.
    /// </summary>
    /// <returns>The cumulative observation count.</returns>
    [PgFunction]
    public static long CompletedObservations() => s_observations.Value;
}
