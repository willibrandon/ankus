namespace Ankus;

/// <summary>
/// Executes SQL inside the current PostgreSQL backend through the Server Programming Interface.
/// </summary>
public static class Spi
{
    /// <summary>
    /// Executes SQL and returns the rows processed by its final statement.
    /// The call uses an internal subtransaction: success retains changes in the enclosing transaction;
    /// failure rolls back this call's changes before throwing a managed PostgreSQL error.
    /// </summary>
    /// <param name="commandText">The SQL commands to execute.</param>
    /// <returns>The number of rows processed by the final statement.</returns>
    /// <exception cref="PgException">PostgreSQL rejected the command.</exception>
    /// <exception cref="InvalidOperationException">The caller is not on the active PostgreSQL backend thread.</exception>
    public static long Execute(string commandText) => NativeBackend.Execute(commandText);
}
