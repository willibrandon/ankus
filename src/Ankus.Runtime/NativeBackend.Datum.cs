namespace Ankus;

/// <summary>
/// Executes raw-result and datum operations through the active backend guard.
/// </summary>
public static unsafe partial class NativeBackend
{
    /// <summary>
    /// Captures exact array shape and raw cells without requiring managed element mappings.
    /// </summary>
    /// <param name="datum">The live array datum.</param>
    /// <returns>The element identity, shape, bounds, and nullable cells.</returns>
    internal static (uint Element, int[] Dimensions, int[] LowerBounds, PgAnyElement?[] Values) ReadPolymorphicArray(PgDatum datum)
        => RunDatum(datum, 3, datum.Lifetime, result =>
        {
            byte[] shape = result._text.ReadBytes();
            ReadOnlySpan<int> dimensionsAndBounds = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(shape);
            int rank = dimensionsAndBounds.Length / 2;
            var values = new PgAnyElement?[result._rowCount];
            for (int index = 0; index < values.Length; index++)
            {
                NativeValue cell = result._values[index];
                if (cell.IsNull == 0)
                {
                    values[index] = new PgAnyElement(new PgDatum(unchecked((nuint)cell.Integral), result._resultTypeOid, false, datum.Lifetime));
                }
            }

            return (result._resultTypeOid, dimensionsAndBounds[..rank].ToArray(), dimensionsAndBounds[rank..].ToArray(), values);
        });

    /// <summary>
    /// Converts a raw datum into a managed SPI value or a wrapper sharing the original lifetime.
    /// </summary>
    /// <typeparam name="T">The desired managed type.</typeparam>
    /// <param name="value">The checked datum.</param>
    /// <returns>The managed copy or checked polymorphic wrapper.</returns>
    internal static T ReadDatum<T>(PgDatum value)
    {
        if (PgDatumRegistry.Find(typeof(T)) is { } mapping)
        {
            return SpiRow.Convert<T>(mapping.Read(value));
        }

        PgDatumRegistry.RejectOrdinaryResult<T>();
        if (typeof(T) == typeof(PgInternal))
        {
            value.Lifetime.Validate();
            if (value.TypeOid != 2281)
            {
                throw new InvalidCastException("The datum is not PostgreSQL internal state.");
            }

            return value.IsNull ? default! : (T)(object)new PgInternal(value);
        }

        if (PgPolymorphic.Is<T>())
        {
            return PgPolymorphic.Read<T>(value);
        }

        return RunDatum(value, 0, null, static result =>
        {
            uint typeOid = checked((uint)result._rowsAffected);
            object? converted = result._text.IsEnum && result._text.IsNull == 0
                ? PgEnumRegistry.FindOid(typeOid).FromLabel(result._text.ReadString())
                : SpiType.FromNative(result._text, typeOid);
            return SpiRow.Convert<T>(converted);
        });
    }

    /// <summary>
    /// Copies a datum's PostgreSQL output text into managed storage.
    /// </summary>
    /// <param name="value">The checked datum.</param>
    /// <returns>The server-formatted text, or null.</returns>
    internal static string? FormatDatum(PgDatum value)
        => RunDatum(value, 1, null, static result => result._text.IsNull != 0 ? null : result._text.ReadString());

    /// <summary>
    /// Clones a datum into an independently selected native lifetime.
    /// </summary>
    /// <param name="value">The source datum.</param>
    /// <param name="lifetime">The checked destination.</param>
    /// <returns>The copied datum.</returns>
    internal static PgDatum CopyDatum(PgDatum value, PgDatumLifetime lifetime)
        => RunDatum(value, 2, lifetime, result => new PgDatum(unchecked((nuint)result._text.Integral),
            value.TypeOid, result._text.IsNull != 0, lifetime));

    /// <summary>
    /// Executes raw SQL with result storage owned independently of the SPI connection.
    /// </summary>
    /// <param name="commandText">The SQL commands.</param>
    /// <param name="parameters">Bound values.</param>
    /// <param name="readOnly">The SPI snapshot mode.</param>
    /// <param name="limit">The row limit, or zero for no limit.</param>
    /// <param name="session">The optional active SPI session.</param>
    /// <param name="resultMode">The columns and rows to capture without changing execution limits.</param>
    /// <returns>The disposable native result.</returns>
    internal static SpiRawResult RunRaw(string commandText, ReadOnlySpan<SpiParameter> parameters, bool readOnly,
        int limit, SpiSession? session = null, SpiResultMode resultMode = SpiResultMode.All)
    {
        CheckAccess();
        byte[] sql = EncodeCommand(commandText);
        fixed (byte* text = sql)
        {
            NativeSpiRequest request = new()
            {
                _command = text,
                _commandLength = sql.Length - 1,
                _readOnly = readOnly ? (byte)1 : (byte)0,
                _limit = limit,
                _resultMode = resultMode,
                _sessionId = session?.Identity ?? 0,
            };
            return RunRawRequest(request, parameters);
        }
    }

    /// <summary>
    /// Executes a prepared statement with context-owned raw results.
    /// </summary>
    /// <param name="plan">The live native plan.</param>
    /// <param name="parameters">Bound values.</param>
    /// <param name="readOnly">The SPI snapshot mode.</param>
    /// <param name="limit">The row limit.</param>
    /// <param name="session">The plan's optional owning session.</param>
    /// <param name="resultMode">The columns and rows to capture without changing execution limits.</param>
    /// <returns>The disposable native result.</returns>
    internal static SpiRawResult RunRawPlan(nint plan, ReadOnlySpan<SpiParameter> parameters, bool readOnly,
        int limit, SpiSession? session, SpiResultMode resultMode = SpiResultMode.All)
        => RunRawRequest(new NativeSpiRequest
        {
            _operation = SpiOperation.ExecutePlan,
            _plan = plan,
            _readOnly = readOnly ? (byte)1 : (byte)0,
            _limit = limit,
            _resultMode = resultMode,
            _sessionId = session?.Identity ?? 0,
        }, parameters);

    /// <summary>
    /// Fetches a cursor batch into independently owned native storage.
    /// </summary>
    /// <param name="identity">The native cursor identity.</param>
    /// <param name="count">The number of rows to fetch.</param>
    /// <param name="forward">Whether to fetch forward rather than backward.</param>
    /// <returns>The owned raw batch.</returns>
    internal static SpiRawResult FetchRawCursor(long identity, int count, bool forward)
        => RunRawRequest(new NativeSpiRequest
        {
            _operation = SpiOperation.FetchCursor,
            _cursorId = identity,
            _limit = count,
            _resultMode = SpiResultMode.All,
            _forward = forward ? (byte)1 : (byte)0,
        }, []);

    /// <summary>
    /// Invokes a datum operation and releases its result even when managed conversion fails.
    /// </summary>
    /// <typeparam name="T">The managed result type.</typeparam>
    /// <param name="value">The checked native input.</param>
    /// <param name="operation">The datum operation.</param>
    /// <param name="destination">The optional copy destination.</param>
    /// <param name="convert">The synchronous owned-result conversion.</param>
    /// <returns>The converted result.</returns>
    private static T RunDatum<T>(PgDatum value, int operation, PgDatumLifetime? destination, Func<NativeSpiResult, T> convert)
    {
        CheckAccess();
        destination?.Validate();
        NativeSpiRequest request = new()
        {
            _operation = SpiOperation.Datum,
            _scalarOperation = operation,
            _resultContext = destination?.ContextId ?? 0,
            _resultGeneration = destination?.Generation ?? 0,
        };
        NativeSpiResult result = default;
        try
        {
            InvokeParameters(&request, [SpiParameter.Create(value)], &result);
            return convert(result);
        }
        finally
        {
            ReleaseResult(&result);
        }
    }

    /// <summary>
    /// Creates native storage before execution and transfers ownership only after managed rows are built.
    /// </summary>
    /// <param name="request">The SQL or prepared-plan request.</param>
    /// <param name="parameters">The bound values.</param>
    /// <returns>The owned raw result.</returns>
    private static SpiRawResult RunRawRequest(NativeSpiRequest request, ReadOnlySpan<SpiParameter> parameters)
    {
        CheckAccess();
        ArgumentOutOfRangeException.ThrowIfNegative(request._limit);
        // The callback context outlives SPI sessions and their internal subtransactions.
        PgMemoryContext context = PgMemoryContext.Create("Ankus raw SPI result", PgMemoryContext.Callback);
        NativeSpiResult result = default;
        try
        {
            var lifetime = new PgDatumLifetime(context);
            request._resultContext = lifetime.ContextId;
            request._resultGeneration = lifetime.Generation;
            InvokeParameters(&request, parameters, &result);
            var columns = new SpiColumn[result._columnCount];
            for (int index = 0; index < columns.Length; index++)
            {
                columns[index] = new SpiColumn(result._columns[index]._name.ReadString(), result._columns[index]._typeOid);
            }

            IReadOnlyList<SpiColumn> metadata = Array.AsReadOnly(columns);
            var rows = new SpiRawRow[result._rowCount];
            for (int row = 0; row < rows.Length; row++)
            {
                var values = new PgDatum[columns.Length];
                for (int column = 0; column < values.Length; column++)
                {
                    NativeValue value = result._values[row * values.Length + column];
                    values[column] = new PgDatum(unchecked((nuint)value.Integral), columns[column].TypeOid, value.IsNull != 0, lifetime);
                }

                rows[row] = new SpiRawRow(values, metadata);
            }

            return new SpiRawResult(context, metadata, rows, result._rowsAffected);
        }
        catch (Exception primary)
        {
            PgResultCleanup.Dispose(context, primary);
            throw;
        }
        finally
        {
            ReleaseResult(&result);
        }
    }
}
