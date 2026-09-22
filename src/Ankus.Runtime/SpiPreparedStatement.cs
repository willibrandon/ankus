namespace Ankus;

/// <summary>
/// Owns a reusable PostgreSQL SPI plan. Execution and disposal require the owning backend thread.
/// Use a using declaration for local statements; explicitly dispose cached statements when replacing them.
/// </summary>
public sealed class SpiPreparedStatement : IDisposable
{
    private readonly nint _backend;
    private readonly uint[] _parameterTypes;
    private int _activeExecutions;

    /// <summary>
    /// Creates managed ownership before the native plan is allocated.
    /// </summary>
    /// <param name="commandText">The prepared command text.</param>
    /// <param name="parameterTypes">The positional PostgreSQL type OIDs.</param>
    /// <param name="backend">The owning native backend binding.</param>
    internal SpiPreparedStatement(string commandText, uint[] parameterTypes, nint backend)
    {
        CommandText = commandText;
        _parameterTypes = parameterTypes;
        _backend = backend;
    }

    /// <summary>
    /// Gets the SQL text used to prepare this statement.
    /// </summary>
    public string CommandText { get; }

    /// <summary>
    /// Gets the number of positional parameters required by this statement.
    /// </summary>
    public int ParameterCount => _parameterTypes.Length;

    /// <summary>
    /// Gets or sets the native plan handle. Zero means no native plan is owned.
    /// </summary>
    internal nint Handle { get; set; }

    /// <summary>
    /// Executes the statement and returns the final command's processed-row count.
    /// </summary>
    /// <param name="parameters">Values whose PostgreSQL types must match the declared parameter types.</param>
    /// <returns>The processed-row count.</returns>
    public long Execute(params ReadOnlySpan<SpiParameter> parameters)
        => Run(parameters, readOnly: false, limit: 0, SpiResultMode.None).RowsAffected;

    /// <summary>
    /// Executes the statement and materializes its result rows and metadata.
    /// </summary>
    /// <param name="parameters">Values matching the declared parameter types.</param>
    /// <returns>The managed result.</returns>
    public SpiResult Query(params ReadOnlySpan<SpiParameter> parameters)
        => Query(readOnly: false, limit: 0, parameters);

    /// <summary>
    /// Executes the statement with explicit read-only mode and row limit.
    /// </summary>
    /// <param name="readOnly">Whether PostgreSQL should use read-only SPI execution.</param>
    /// <param name="limit">The maximum returned rows, or zero for no limit.</param>
    /// <param name="parameters">Values matching the declared parameter types.</param>
    /// <returns>The managed result.</returns>
    public SpiResult Query(bool readOnly, int limit, params ReadOnlySpan<SpiParameter> parameters)
        => Run(parameters, readOnly, limit, SpiResultMode.All);

    /// <summary>
    /// Reads the first cell without limiting command execution or applying implicit type conversions.
    /// SQL NULL or an absent cell requires a nullable value type or reference type.
    /// </summary>
    /// <typeparam name="T">The expected managed result type.</typeparam>
    /// <param name="parameters">Values matching the declared parameter types.</param>
    /// <returns>The scalar value.</returns>
    public T ExecuteScalar<T>(params ReadOnlySpan<SpiParameter> parameters)
    {
        SpiResult result = Run(parameters, readOnly: false, limit: 0, SpiResultMode.Scalar);
        return result.Count == 0 || result.Columns.Count == 0 ? SpiRow.Convert<T>(null) : result[0].Get<T>(0);
    }

    /// <summary>
    /// Frees the native plan through a guarded backend call. Repeated disposal is harmless.
    /// PostgreSQL APIs cannot run on the finalizer thread, so this type requires explicit disposal.
    /// </summary>
    public void Dispose()
    {
        if (Handle == 0)
        {
            return;
        }

        NativeBackend.CheckAccess(_backend);
        if (_activeExecutions != 0)
        {
            throw new InvalidOperationException("A prepared statement cannot be disposed while it is executing.");
        }

        nint plan = Handle;
        try
        {
            NativeBackend.FreePlan(ref plan);
        }
        finally
        {
            Handle = plan;
        }
    }

    private SpiResult Run(ReadOnlySpan<SpiParameter> parameters, bool readOnly, int limit, SpiResultMode resultMode)
    {
        ObjectDisposedException.ThrowIf(Handle == 0, this);
        NativeBackend.CheckAccess(_backend);
        if (parameters.Length != _parameterTypes.Length)
        {
            throw new ArgumentException($"The statement requires {_parameterTypes.Length} parameters, but received {parameters.Length}.",
                nameof(parameters));
        }

        for (int index = 0; index < parameters.Length; index++)
        {
            if (parameters[index].TypeOid != _parameterTypes[index])
            {
                throw new ArgumentException($"Parameter {index + 1} must have PostgreSQL type OID {_parameterTypes[index]}.",
                    nameof(parameters));
            }
        }

        _activeExecutions++;
        try
        {
            return NativeBackend.RunPlan(Handle, parameters, readOnly, limit, resultMode);
        }
        finally
        {
            _activeExecutions--;
        }
    }
}
