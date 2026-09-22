namespace Ankus.TestExtension;

/// <summary>
/// Exercises guarded SPI calls, recursive dispatch, structured errors, and managed cleanup inside a PostgreSQL backend.
/// </summary>
public static class SpiFunctions
{
    private static int s_finallyCount;

    /// <summary>
    /// Executes SQL in the invoking backend and records managed finally execution.
    /// </summary>
    /// <param name="commandText">The SQL command.</param>
    /// <returns>The affected row count.</returns>
    [PgFunction]
    public static long ExecuteSql(string commandText)
    {
        try
        {
            return Spi.Execute(commandText);
        }
        finally
        {
            s_finallyCount++;
        }
    }

    /// <summary>
    /// Returns the managed cleanup counter for this backend.
    /// </summary>
    /// <returns>The number of completed or failed guarded calls.</returns>
    [PgFunction]
    public static int SpiFinallyCount() => s_finallyCount;

    /// <summary>
    /// Executes a possibly recursive statement and then uses the enclosing callback's restored SPI binding.
    /// </summary>
    /// <param name="commandText">The first SQL command.</param>
    /// <returns>The follow-up statement's row count.</returns>
    [PgFunction]
    public static long ExecuteSqlTwice(string commandText)
    {
        Spi.Execute(commandText);
        return Spi.Execute("SELECT 1");
    }

    /// <summary>
    /// Catches a native SQL error and runs another SPI statement before returning its diagnostics.
    /// </summary>
    /// <param name="commandText">The failing SQL command.</param>
    /// <returns>The error SQLSTATE and successful follow-up row count.</returns>
    [PgFunction]
    public static string CatchSqlError(string commandText)
    {
        try
        {
            Spi.Execute(commandText);
            return "no error";
        }
        catch (PgException exception)
        {
            return exception.SqlState + ":" + Spi.Execute("SELECT 1");
        }
    }

    /// <summary>
    /// Reports extension-authored structured PostgreSQL diagnostics.
    /// </summary>
    /// <param name="sqlState">The SQLSTATE.</param>
    /// <param name="message">The primary diagnostic.</param>
    /// <param name="detail">Additional detail.</param>
    /// <param name="hint">A corrective hint.</param>
    [PgFunction]
    public static void ReportError(string sqlState, string message, string detail, string hint)
        => throw new PgException(sqlState, message, detail, hint);

    /// <summary>
    /// Attempts to use SPI from a thread that does not own the PostgreSQL backend.
    /// </summary>
    /// <returns>The guarded API's rejection message.</returns>
    [PgFunction]
    public static string BackgroundSql()
        => Task.Run(static () =>
        {
            try
            {
                Spi.Execute("SELECT 1");
                return "unexpected success";
            }
            catch (InvalidOperationException exception)
            {
                return exception.Message;
            }
        }).GetAwaiter().GetResult();
}
