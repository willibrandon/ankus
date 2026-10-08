using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Observes PostgreSQL errors inside the backend that raised them.
/// </summary>
/// <remarks>
/// pgrx-compatible samples report errors with SQLSTATE <c>XX000</c>, and Npgsql closes a connection after any internal
/// (<c>XX</c> class) error. Trapping the error in PL/pgSQL rolls back its subtransaction, keeps the same backend for
/// recovery checks and still exposes the exact SQLSTATE and message.
/// </remarks>
internal static class ErrorTrap
{
    /// <summary>
    /// Creates the session-local <c>pg_temp.trap(statement)</c> function.
    /// </summary>
    /// <param name="connection">The session that will run trapped statements.</param>
    /// <param name="transaction">The active transaction, if any.</param>
    /// <param name="token">Cancels the command.</param>
    /// <returns>A task that completes after creation.</returns>
    internal static async Task InstallAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("""
            CREATE FUNCTION pg_temp.trap(statement text) RETURNS text LANGUAGE plpgsql AS $$
            BEGIN
                EXECUTE statement;
                RETURN 'completed';
            EXCEPTION WHEN OTHERS THEN
                RETURN SQLSTATE || ': ' || SQLERRM;
            END
            $$
            """, connection, transaction);
        await command.ExecuteNonQueryAsync(token);
    }

    /// <summary>
    /// Executes one statement through PL/pgSQL and reports its outcome.
    /// </summary>
    /// <param name="connection">A session with the trap function installed.</param>
    /// <param name="transaction">The active transaction, if any.</param>
    /// <param name="statement">The single SQL statement to execute.</param>
    /// <param name="token">Cancels the command.</param>
    /// <returns><c>completed</c>, or the SQLSTATE and primary message separated by a colon and space.</returns>
    internal static async Task<string> RunAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string statement,
        CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT pg_temp.trap($1)", connection, transaction);
        command.Parameters.AddWithValue(statement);
        return Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
    }
}
