namespace Ankus;

/// <summary>
/// Provides a scoped SPI connection during a synchronous Spi.Connect callback.
/// Operations require the owning backend callback and the innermost active session.
/// </summary>
public sealed class SpiSession
{
    private readonly nint _backend;
    private readonly int _callbackDepth;

    /// <summary>
    /// Creates a session owner before the native connection is opened.
    /// </summary>
    /// <param name="backend">The native binding.</param>
    /// <param name="callbackDepth">The owning managed dispatcher depth.</param>
    internal SpiSession(nint backend, int callbackDepth)
    {
        _backend = backend;
        _callbackDepth = callbackDepth;
    }

    /// <summary>
    /// Gets or sets the native session identity. Zero denotes an inactive scope.
    /// </summary>
    internal long Identity { get; set; }

    /// <summary>
    /// Plans one SQL statement within this session and returns its owned JSON EXPLAIN output.
    /// </summary>
    /// <param name="commandText">The SQL statement.</param>
    /// <param name="parameters">The typed positional parameters.</param>
    /// <returns>The JSON query plan.</returns>
    public PgJson Explain(string commandText, params ReadOnlySpan<SpiParameter> parameters)
    {
        CheckAccess();
        return NativeBackend.Explain(commandText, parameters, this);
    }

    /// <summary>
    /// Executes commands within this session and returns the final command's processed-row count.
    /// </summary>
    /// <param name="commandText">The SQL command text.</param>
    /// <param name="parameters">The typed positional parameters.</param>
    /// <returns>The processed-row count.</returns>
    public long Execute(string commandText, params ReadOnlySpan<SpiParameter> parameters)
    {
        CheckAccess();
        return NativeBackend.Run(commandText, parameters, readOnly: false, limit: 0, SpiResultMode.None, this).RowsAffected;
    }

    /// <summary>
    /// Executes a query and returns owned rows and metadata that remain valid after the session ends.
    /// </summary>
    /// <param name="commandText">The SQL query.</param>
    /// <param name="parameters">The typed positional parameters.</param>
    /// <returns>The independently owned managed result.</returns>
    public SpiResult Query(string commandText, params ReadOnlySpan<SpiParameter> parameters)
        => Query(commandText, readOnly: false, limit: 0, parameters);

    /// <summary>
    /// Executes a query with explicit read-only mode and row limit.
    /// </summary>
    /// <param name="commandText">The SQL query.</param>
    /// <param name="readOnly">Whether to use PostgreSQL's read-only SPI execution mode.</param>
    /// <param name="limit">The maximum returned rows, or zero for no limit.</param>
    /// <param name="parameters">The typed positional parameters.</param>
    /// <returns>The independently owned managed result.</returns>
    public SpiResult Query(string commandText, bool readOnly, int limit, params ReadOnlySpan<SpiParameter> parameters)
    {
        CheckAccess();
        return NativeBackend.Run(commandText, parameters, readOnly, limit, SpiResultMode.All, this);
    }

    /// <summary>
    /// Reads the first result cell without limiting a command's write effects.
    /// </summary>
    /// <typeparam name="T">The expected managed result type.</typeparam>
    /// <param name="commandText">The SQL command.</param>
    /// <param name="parameters">The typed positional parameters.</param>
    /// <returns>The scalar value.</returns>
    public T ExecuteScalar<T>(string commandText, params ReadOnlySpan<SpiParameter> parameters)
    {
        CheckAccess();
        SpiResult result = NativeBackend.Run(commandText, parameters, readOnly: false, limit: 0, SpiResultMode.Scalar, this);
        return result.Count == 0 || result.Columns.Count == 0 ? SpiRow.Convert<T>(null) : result[0].Get<T>(0);
    }

    /// <summary>
    /// Prepares a statement owned by this session. Call Keep on the statement to retain it beyond the callback.
    /// </summary>
    /// <param name="commandText">The SQL command text.</param>
    /// <param name="parameterTypes">The declared CLR parameter types.</param>
    /// <returns>A session-bound prepared statement.</returns>
    public SpiPreparedStatement Prepare(string commandText, params ReadOnlySpan<Type> parameterTypes)
    {
        CheckAccess();
        return NativeBackend.Prepare(commandText, parameterTypes, this);
    }

    /// <summary>
    /// Opens a transaction-bound cursor whose portal remains valid after this session ends.
    /// </summary>
    /// <param name="commandText">The SQL query.</param>
    /// <param name="parameters">The typed positional parameters.</param>
    /// <returns>An owned cursor.</returns>
    public SpiCursor OpenCursor(string commandText, params ReadOnlySpan<SpiParameter> parameters)
        => OpenCursor(commandText, readOnly: false, parameters);

    /// <summary>
    /// Opens a cursor with explicit read-only execution mode.
    /// </summary>
    /// <param name="commandText">The SQL query.</param>
    /// <param name="readOnly">Whether to use read-only execution.</param>
    /// <param name="parameters">The typed positional parameters.</param>
    /// <returns>An owned transaction-bound cursor.</returns>
    public SpiCursor OpenCursor(string commandText, bool readOnly, params ReadOnlySpan<SpiParameter> parameters)
    {
        CheckAccess();
        return NativeBackend.OpenCursor(commandText, parameters, readOnly, this);
    }

    /// <summary>
    /// Rejects expired, worker-thread, nested-session, and reentrant-callback access before entering native code.
    /// </summary>
    internal void CheckAccess()
    {
        ObjectDisposedException.ThrowIf(Identity == 0, this);
        NativeBackend.CheckAccess(_backend);
        NativeBackend.CheckSession(this, _callbackDepth);
    }
}
