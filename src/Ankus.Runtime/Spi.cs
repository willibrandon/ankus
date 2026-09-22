namespace Ankus;

/// <summary>
/// Executes SQL inside the current PostgreSQL backend through the Server Programming Interface.
/// </summary>
public static class Spi
{
    /// <summary>
    /// Quotes a single SQL identifier using PostgreSQL's keyword rules and quote_all_identifiers setting.
    /// </summary>
    /// <param name="identifier">One identifier; dots are part of its name.</param>
    /// <returns>The quoted identifier or the original spelling when quoting is unnecessary.</returns>
    public static string QuoteIdentifier(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        return NativeBackend.Quote(SpiOperation.QuoteIdentifier, SpiParameter.Create(identifier));
    }

    /// <summary>
    /// Quotes a schema or other qualifier and an identifier independently, joining them with a dot.
    /// </summary>
    /// <param name="qualifier">The qualifier, or null to omit it.</param>
    /// <param name="identifier">The identifier.</param>
    /// <returns>The qualified SQL name.</returns>
    public static string QuoteQualifiedIdentifier(string? qualifier, string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        return NativeBackend.Quote(SpiOperation.QuoteQualifiedIdentifier, SpiParameter.Create(qualifier), SpiParameter.Create(identifier));
    }

    /// <summary>
    /// Quotes a text literal with escaping that is valid regardless of standard_conforming_strings.
    /// </summary>
    /// <param name="value">The literal text.</param>
    /// <returns>The quoted SQL literal.</returns>
    public static string QuoteLiteral(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return NativeBackend.Quote(SpiOperation.QuoteLiteral, SpiParameter.Create(value));
    }

    /// <summary>
    /// Plans one SQL statement and returns PostgreSQL's JSON EXPLAIN output. The statement is not executed by EXPLAIN ANALYZE.
    /// </summary>
    /// <param name="commandText">The statement to explain.</param>
    /// <param name="parameters">The typed positional parameters.</param>
    /// <returns>The owned JSON query plan.</returns>
    public static PgJson Explain(string commandText, params ReadOnlySpan<SpiParameter> parameters)
        => NativeBackend.Explain(commandText, parameters);

    /// <summary>
    /// Runs a synchronous callback using one scoped SPI connection. Session-bound plans expire when the callback exits.
    /// </summary>
    /// <param name="action">The synchronous backend-thread callback.</param>
    public static void Connect(Action<SpiSession> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        NativeBackend.Connect(session =>
        {
            action(session);
            return 0;
        });
    }

    /// <summary>
    /// Runs a synchronous callback using one scoped SPI connection and returns its managed result.
    /// </summary>
    /// <typeparam name="TResult">The callback result type.</typeparam>
    /// <param name="action">The synchronous backend-thread callback. It must not start asynchronous session operations.</param>
    /// <returns>The callback result, whose owned rows and retained plans can outlive the session.</returns>
    public static TResult Connect<TResult>(Func<SpiSession, TResult> action) => NativeBackend.Connect(action);

    /// <summary>
    /// Opens a transaction-bound cursor with optional typed positional parameters.
    /// </summary>
    /// <param name="commandText">One SQL command that returns rows.</param>
    /// <param name="parameters">The positional parameter values.</param>
    /// <returns>An owned cursor that should be disposed or detached.</returns>
    public static SpiCursor OpenCursor(string commandText, params ReadOnlySpan<SpiParameter> parameters)
        => OpenCursor(commandText, readOnly: false, parameters);

    /// <summary>
    /// Opens a cursor with explicit read-only SPI execution mode.
    /// </summary>
    /// <param name="commandText">One SQL command that returns rows.</param>
    /// <param name="readOnly">Whether PostgreSQL should use read-only execution.</param>
    /// <param name="parameters">The positional parameter values.</param>
    /// <returns>An owned transaction-bound cursor.</returns>
    public static SpiCursor OpenCursor(string commandText, bool readOnly, params ReadOnlySpan<SpiParameter> parameters)
        => NativeBackend.OpenCursor(commandText, parameters, readOnly);

    /// <summary>
    /// Takes ownership of an existing PostgreSQL cursor by its exact portal name.
    /// </summary>
    /// <param name="name">The portal name, including names returned by SpiCursor.Detach.</param>
    /// <returns>An owned cursor.</returns>
    public static SpiCursor FindCursor(string name) => NativeBackend.FindCursor(name);

    /// <summary>
    /// Prepares a reusable statement with explicitly declared managed parameter types.
    /// The statement survives SPI calls and transaction boundaries until disposed on the owning backend thread.
    /// </summary>
    /// <param name="commandText">The SQL commands to prepare.</param>
    /// <param name="parameterTypes">The declared CLR types of $1, $2, and subsequent parameters.</param>
    /// <returns>A statement that must be disposed from an extension callback on its owning backend.</returns>
    public static SpiPreparedStatement Prepare(string commandText, params ReadOnlySpan<Type> parameterTypes)
        => NativeBackend.Prepare(commandText, parameterTypes);

    /// <summary>
    /// Executes SQL and returns the rows processed by its final statement.
    /// The call uses an internal subtransaction: success retains changes in the enclosing transaction;
    /// failure rolls back this call's changes before throwing a managed PostgreSQL error.
    /// </summary>
    /// <param name="commandText">The SQL commands to execute.</param>
    /// <param name="parameters">Typed positional parameters referenced as $1, $2, and so on.</param>
    /// <returns>The number of rows processed by the final statement.</returns>
    /// <exception cref="PgException">PostgreSQL rejected the command.</exception>
    /// <exception cref="InvalidOperationException">The caller is not on the active PostgreSQL backend thread.</exception>
    public static long Execute(string commandText, params ReadOnlySpan<SpiParameter> parameters)
        => NativeBackend.Run(commandText, parameters, readOnly: false, limit: 0, SpiResultMode.None).RowsAffected;

    /// <summary>
    /// Executes SQL and copies all result rows and metadata into managed objects.
    /// </summary>
    /// <param name="commandText">The SQL command.</param>
    /// <param name="parameters">Typed positional parameters.</param>
    /// <returns>The materialized result.</returns>
    public static SpiResult Query(string commandText, params ReadOnlySpan<SpiParameter> parameters)
        => Query(commandText, readOnly: false, limit: 0, parameters);

    /// <summary>
    /// Executes SQL with explicit snapshot mode and a row limit, copying result data into managed memory.
    /// </summary>
    /// <param name="commandText">The SQL command.</param>
    /// <param name="readOnly">Whether PostgreSQL should use read-only SPI execution.</param>
    /// <param name="limit">The maximum returned rows, or zero for no limit.</param>
    /// <param name="parameters">Typed positional parameters.</param>
    /// <returns>The materialized result.</returns>
    public static SpiResult Query(string commandText, bool readOnly, int limit, params ReadOnlySpan<SpiParameter> parameters)
        => NativeBackend.Run(commandText, parameters, readOnly, limit, SpiResultMode.All);

    /// <summary>
    /// Reads the first column of the first result row without implicit type conversion.
    /// SQL NULL and an empty result return null for nullable or reference types and reject non-nullable value types.
    /// </summary>
    /// <typeparam name="T">The expected managed result type.</typeparam>
    /// <param name="commandText">The SQL command.</param>
    /// <param name="parameters">Typed positional parameters.</param>
    /// <returns>The scalar result.</returns>
    public static T ExecuteScalar<T>(string commandText, params ReadOnlySpan<SpiParameter> parameters)
    {
        SpiResult result = NativeBackend.Run(commandText, parameters, readOnly: false, limit: 0, SpiResultMode.Scalar);
        return result.Count == 0 || result.Columns.Count == 0 ? SpiRow.Convert<T>(null) : result[0].Get<T>(0);
    }
}
