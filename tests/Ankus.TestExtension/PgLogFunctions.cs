namespace Ankus.TestExtension;

/// <summary>
/// Exercises PostgreSQL logging and managed unwinding through ordinary extension functions.
/// </summary>
public static class PgLogFunctions
{
    /// <summary>
    /// Reports literal text and returns normally for nonterminal levels.
    /// </summary>
    /// <param name="level">The managed severity.</param>
    /// <param name="message">The primary text.</param>
    /// <returns>The backend value after logging.</returns>
    [PgFunction]
    public static int LogMessage(int level, string message)
    {
        PgLog.Write((PgLogLevel)level, message);
        return Spi.ExecuteScalar<int>("SELECT 42");
    }

    /// <summary>
    /// Tests active server and client reporting thresholds.
    /// </summary>
    /// <param name="level">The managed severity.</param>
    /// <returns>Whether the level is enabled.</returns>
    [PgFunction]
    public static bool LogEnabled(int level) => PgLog.IsEnabled((PgLogLevel)level);

    /// <summary>
    /// Reports all structured fields through a scoped connection, retaining ownership of the session.
    /// </summary>
    /// <param name="message">The primary text.</param>
    /// <returns>The result from the existing session after reporting.</returns>
    [PgFunction]
    public static int LogDiagnostic(string message)
        => Spi.Connect(session =>
        {
            PgLog.Write(PgLogLevel.Warning, new PgDiagnostic(message)
            {
                SqlState = "01P01",
                Detail = "client détail",
                DetailLog = "server-only détail",
                Hint = "retry 🐘",
                Context = "managed context",
                SchemaName = "schéma",
                TableName = "t",
                ColumnName = "c",
                DataTypeName = "custom_type",
                ConstraintName = "constraint_name",
                Position = 3,
                InternalPosition = 2,
                InternalQuery = "SELECT 1",
                File = "logging.cs",
                Line = 42,
                Routine = "LogDiagnostic",
            });
            return session.ExecuteScalar<int>("SELECT 42");
        });

    /// <summary>
    /// Catches an ERROR before the native boundary and proves normal SPI work remains possible.
    /// </summary>
    /// <returns>The structured error and continued query result.</returns>
    [PgFunction]
    public static string LogCatchError()
    {
        try
        {
            PgLog.Write(PgLogLevel.Error, new PgDiagnostic("caught error") { SqlState = "22023", Detail = "detail", Hint = "hint" });
        }
        catch (PgException error)
        {
            return error.SqlState + "|" + error.Message + "|" + error.Detail + "|" + error.Hint + "|" + Spi.ExecuteScalar<int>("SELECT 42");
        }

        throw new InvalidOperationException("ERROR returned normally.");
    }

    /// <summary>
    /// Reports a terminal message after proving managed finally blocks execute.
    /// </summary>
    /// <param name="level">ERROR, FATAL, or PANIC.</param>
    /// <param name="marker">The unique primary message.</param>
    [PgFunction]
    public static void LogTerminal(int level, string marker)
    {
        try
        {
            Spi.Connect(session =>
            {
                session.Execute("INSERT INTO log_rollback VALUES (99)");
                PgLog.Write((PgLogLevel)level, new PgDiagnostic(marker) { SqlState = "P0001", Detail = "terminal detail" });
            });
        }
        finally
        {
            PgLog.Write(PgLogLevel.Warning, "finally " + marker);
        }
    }

    /// <summary>
    /// Attempts logging on a managed worker thread.
    /// </summary>
    /// <returns>The rejection message.</returns>
    [PgFunction]
    public static string LogWorker()
        => Task.Run(() =>
        {
            try
            {
                PgLog.Write(PgLogLevel.Notice, "worker");
                return "unexpected success";
            }
            catch (InvalidOperationException error)
            {
                return error.Message;
            }
        }).GetAwaiter().GetResult();

    /// <summary>
    /// Exercises managed validation and native encoding failures before continuing on the same session.
    /// </summary>
    /// <param name="mode">The invalid input case.</param>
    /// <returns>The diagnostic and the continued query value.</returns>
    [PgFunction]
    public static string LogInvalid(int mode)
    {
        try
        {
            switch (mode)
            {
                case 0: PgLog.Write((PgLogLevel)(-1), "bad"); break;
                case 1: PgLog.IsEnabled((PgLogLevel)13); break;
                case 2: PgLog.Write(PgLogLevel.Warning, new PgDiagnostic("bad") { SqlState = "bad" }); break;
                case 3: PgLog.Write(PgLogLevel.Error, new PgDiagnostic("bad") { SqlState = "00000" }); break;
                case 4: PgLog.Write(PgLogLevel.Warning, "bad\0message"); break;
                case 5: PgLog.Write(PgLogLevel.Warning, "\uD800"); break;
                case 6: PgLog.Write(PgLogLevel.Warning, new PgDiagnostic("bad") { Detail = "\uD800" }); break;
                case 7: PgLog.Write(PgLogLevel.Warning, (PgDiagnostic)null!); break;
                case 8: PgLog.Write(PgLogLevel.Warning, "🐘"); break;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }
        catch (Exception error)
        {
            string kind = error is PgException postgres ? postgres.SqlState : error.GetType().Name;
            return kind + "|" + Spi.ExecuteScalar<int>("SELECT 42");
        }

        return "unexpected success";
    }

    /// <summary>
    /// Measures operation-context retention after repeated structured reports.
    /// </summary>
    /// <returns>The number of extra subtransaction contexts.</returns>
    [PgFunction]
    public static long LogContextGrowth()
    {
        const string count = "SELECT count(*) FROM pg_backend_memory_contexts WHERE name = 'CurTransactionContext'";
        var diagnostic = new PgDiagnostic(new string('x', 10000)) { File = "logging.cs", Routine = "repeat" };
        PgLog.Write(PgLogLevel.ServerOnly, diagnostic);
        long before = Spi.ExecuteScalar<long>(count);
        for (int index = 0; index < 100; index++)
        {
            PgLog.Write(PgLogLevel.ServerOnly, diagnostic);
        }

        return Spi.ExecuteScalar<long>(count) - before;
    }
}
