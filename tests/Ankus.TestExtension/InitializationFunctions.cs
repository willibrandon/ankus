namespace Ankus.TestExtension;

/// <summary>
/// Exercises backend initialization, error recovery, recursive loading, and managed state lifetime.
/// </summary>
public static class InitializationFunctions
{
    private static int s_attempts;
    private static int s_finallyCount;
    private static string? s_mode;
    private static int s_answer;

    /// <summary>
    /// Initializes this backend according to a session-local test setting.
    /// </summary>
    [PgInitialize]
    public static void Initialize()
    {
        s_attempts++;
        try
        {
            s_mode = Spi.ExecuteScalar<string?>("SELECT current_setting('ankus_test.initialization', true)") ?? "default";
            switch (s_mode)
            {
                case "managed-error":
                    throw new InvalidOperationException("Managed initialization failed.");
                case "startup-error":
                    throw new InvalidOperationException("Startup initialization failed.");
                case "postgres-error":
                    throw new PgException("22023", new string('x', 4096) + " initialisation 🐘",
                        "Owned initialization détail", "Change the initialization mode and retry.");
                case "spi-error":
                    try
                    {
                        Spi.Execute("SELECT 1 / 0");
                    }
                    catch (PgException exception) when (exception.SqlState == "22012")
                    {
                        s_mode = "spi-recovered";
                    }

                    break;
                case "recursive":
                    Spi.Execute("LOAD 'Ankus.TestExtension'");
                    break;
                case "recursive-caught":
                    try
                    {
                        Spi.Execute("LOAD 'Ankus.TestExtension'");
                    }
                    catch (PgException exception) when (exception.SqlState == "55000")
                    {
                        s_mode = "recursive-recovered";
                    }

                    break;
                case "nested":
                    Spi.Execute("LOAD 'Ankus.Examples.Initialization'");
                    break;
                case "sql-rollback":
                    Spi.Execute("INSERT INTO initialization_probe VALUES (99)");
                    throw new PgException("P0001", "Rollback initialization SQL.");
            }

            s_answer = Spi.ExecuteScalar<int>("SELECT 42");
        }
        finally
        {
            s_finallyCount++;
            if (s_mode == "startup-error")
            {
                PgLog.Write(PgLogLevel.Warning, "Startup initialization finally completed.");
            }
        }
    }

    /// <summary>
    /// Reads backend-owned initialization state after PostgreSQL has loaded the library.
    /// </summary>
    /// <returns>The attempt count, completed finally count, selected mode, and guarded SQL result.</returns>
    [PgFunction]
    public static string InitializationState() => $"{s_attempts}|{s_finallyCount}|{s_mode}|{s_answer}";
}
