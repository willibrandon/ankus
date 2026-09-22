using System.ComponentModel;
using System.Text;

namespace Ankus;

/// <summary>
/// Binds guarded PostgreSQL entry points to the current backend thread for the duration of generated managed dispatch.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static unsafe class NativeBackend
{
    [ThreadStatic]
    private static nint s_execute;

    /// <summary>
    /// Enters a native callback scope, preserving the previous binding for recursive SPI calls.
    /// </summary>
    /// <param name="execute">The native guarded SPI entry point.</param>
    /// <returns>The previous callback binding, restored by generated code in a finally block.</returns>
    public static nint Enter(nint execute)
    {
        nint previous = s_execute;
        s_execute = execute;
        return previous;
    }

    /// <summary>
    /// Restores the enclosing native callback scope.
    /// </summary>
    /// <param name="previous">The binding saved on entry.</param>
    public static void Exit(nint previous) => s_execute = previous;

    /// <summary>
    /// Executes SQL through the native guard and copies requested result data before releasing native allocations.
    /// </summary>
    /// <param name="commandText">The SQL command text.</param>
    /// <param name="parameters">The positional parameters.</param>
    /// <param name="readOnly">Whether to use a read-only SPI snapshot.</param>
    /// <param name="limit">The maximum returned rows, or zero for no limit.</param>
    /// <param name="resultMode">The result materialization mode.</param>
    /// <returns>The managed query result.</returns>
    internal static SpiResult Run(
        string commandText, ReadOnlySpan<SpiParameter> parameters, bool readOnly, int limit, SpiResultMode resultMode)
    {
        CheckAccess();
        byte[] sql = EncodeCommand(commandText);
        fixed (byte* text = sql)
        {
            var request = new NativeSpiRequest
            {
                _command = text,
                _commandLength = sql.Length - 1,
                _readOnly = readOnly ? (byte)1 : (byte)0,
                _limit = limit,
                _resultMode = resultMode,
            };
            return RunRequest(request, parameters);
        }
    }

    /// <summary>
    /// Creates a retained native plan after validating its command and managed parameter type identities.
    /// </summary>
    /// <param name="commandText">The SQL text to prepare.</param>
    /// <param name="parameterTypes">The declared CLR parameter types.</param>
    /// <returns>The owned prepared statement.</returns>
    internal static SpiPreparedStatement Prepare(string commandText, ReadOnlySpan<Type> parameterTypes)
    {
        CheckAccess();
        byte[] sql = EncodeCommand(commandText);
        var types = new uint[parameterTypes.Length];
        var arguments = new NativeSpiParameter[parameterTypes.Length];
        for (int index = 0; index < types.Length; index++)
        {
            types[index] = SpiType.GetOid(parameterTypes[index]);
            arguments[index]._typeOid = types[index];
        }

        var statement = new SpiPreparedStatement(commandText, types, s_execute);
        fixed (byte* text = sql)
        fixed (NativeSpiParameter* values = arguments)
        {
            var request = new NativeSpiRequest
            {
                _operation = SpiOperation.Prepare,
                _command = text,
                _commandLength = sql.Length - 1,
                _parameters = values,
                _parameterCount = arguments.Length,
            };
            NativeSpiResult result = default;
            Invoke(&request, &result);
            statement.Handle = request._plan;
        }

        return statement;
    }

    /// <summary>
    /// Verifies that PostgreSQL is accessible through the active callback and optional owning binding.
    /// </summary>
    /// <param name="owner">The required native binding, or zero to accept any active binding.</param>
    internal static void CheckAccess(nint owner = 0)
    {
        if (s_execute == 0 || (owner != 0 && s_execute != owner))
        {
            throw new InvalidOperationException("PostgreSQL APIs can only be used on the active PostgreSQL backend thread.");
        }
    }

    /// <summary>
    /// Executes a retained plan through the common guarded parameter/result path.
    /// </summary>
    /// <param name="plan">The owned native plan.</param>
    /// <param name="parameters">The positional values.</param>
    /// <param name="readOnly">Whether to use read-only execution.</param>
    /// <param name="limit">The maximum returned rows, or zero for no limit.</param>
    /// <param name="resultMode">The materialization mode.</param>
    /// <returns>The managed result.</returns>
    internal static SpiResult RunPlan(
        nint plan, ReadOnlySpan<SpiParameter> parameters, bool readOnly, int limit, SpiResultMode resultMode)
        => RunRequest(new NativeSpiRequest
        {
            _operation = SpiOperation.ExecutePlan,
            _plan = plan,
            _readOnly = readOnly ? (byte)1 : (byte)0,
            _limit = limit,
            _resultMode = resultMode,
        }, parameters);

    /// <summary>
    /// Frees a plan and clears the handle when the native operation consumes it, including on subsequent errors.
    /// </summary>
    /// <param name="plan">The owned handle, updated to reflect native ownership.</param>
    internal static void FreePlan(ref nint plan)
    {
        var request = new NativeSpiRequest { _operation = SpiOperation.FreePlan, _plan = plan };
        NativeSpiResult result = default;
        try
        {
            Invoke(&request, &result);
        }
        finally
        {
            plan = request._plan;
        }
    }

    /// <summary>
    /// Opens a cursor from UTF-8 SQL and borrowed typed parameters.
    /// </summary>
    /// <param name="commandText">The SQL command.</param>
    /// <param name="parameters">The bound parameters.</param>
    /// <param name="readOnly">Whether to use read-only execution.</param>
    /// <returns>The owned managed cursor.</returns>
    internal static SpiCursor OpenCursor(string commandText, ReadOnlySpan<SpiParameter> parameters, bool readOnly)
    {
        CheckAccess();
        byte[] sql = EncodeCommand(commandText);
        fixed (byte* text = sql)
        {
            return CreateCursor(new NativeSpiRequest
            {
                _operation = SpiOperation.OpenCursor,
                _command = text,
                _commandLength = sql.Length - 1,
                _readOnly = readOnly ? (byte)1 : (byte)0,
            }, parameters);
        }
    }

    /// <summary>
    /// Opens a cursor from a retained plan without transferring plan ownership.
    /// </summary>
    /// <param name="plan">The prepared plan handle.</param>
    /// <param name="parameters">The bound parameters.</param>
    /// <param name="readOnly">Whether to use read-only execution.</param>
    /// <returns>The owned managed cursor.</returns>
    internal static SpiCursor OpenPlanCursor(nint plan, ReadOnlySpan<SpiParameter> parameters, bool readOnly)
        => CreateCursor(new NativeSpiRequest
        {
            _operation = SpiOperation.OpenPlanCursor,
            _plan = plan,
            _readOnly = readOnly ? (byte)1 : (byte)0,
        }, parameters);

    /// <summary>
    /// Resolves an existing portal by its exact name and returns a managed owner.
    /// </summary>
    /// <param name="name">The PostgreSQL portal name.</param>
    /// <returns>The owned cursor.</returns>
    internal static SpiCursor FindCursor(string name)
    {
        CheckAccess();
        ArgumentNullException.ThrowIfNull(name);
        if (name.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("A cursor name cannot contain a zero character.", nameof(name));
        }

        byte[] encoded = EncodeUtf8(name);
        fixed (byte* text = encoded)
        {
            return CreateCursor(new NativeSpiRequest
            {
                _operation = SpiOperation.FindCursor,
                _command = text,
                _commandLength = encoded.Length - 1,
            }, []);
        }
    }

    /// <summary>
    /// Fetches and materializes the next cursor batch after native identity validation.
    /// </summary>
    /// <param name="identity">The native cursor identity.</param>
    /// <param name="count">The requested row count.</param>
    /// <param name="forward">Whether to fetch forward rather than backward.</param>
    /// <returns>The managed result batch.</returns>
    internal static SpiResult FetchCursor(long identity, int count, bool forward)
        => RunRequest(new NativeSpiRequest
        {
            _operation = SpiOperation.FetchCursor,
            _cursorId = identity,
            _limit = count,
            _resultMode = SpiResultMode.All,
            _forward = forward ? (byte)1 : (byte)0,
        }, []);

    /// <summary>
    /// Closes a portal if its native identity remains live.
    /// </summary>
    /// <param name="identity">The cursor identity.</param>
    internal static void CloseCursor(long identity)
    {
        CheckAccess();
        var request = new NativeSpiRequest { _operation = SpiOperation.CloseCursor, _cursorId = identity };
        NativeSpiResult result = default;
        Invoke(&request, &result);
    }

    private static SpiCursor CreateCursor(NativeSpiRequest request, ReadOnlySpan<SpiParameter> parameters)
    {
        CheckAccess();
        var cursor = new SpiCursor(s_execute);
        NativeSpiResult result = default;
        try
        {
            InvokeParameters(&request, parameters, &result);
            cursor.Identity = result._cursorId;
            cursor.Name = result._cursorName.ReadString();
            return cursor;
        }
        catch
        {
            cursor.Dispose();
            throw;
        }
        finally
        {
            ReleaseResult(&result);
        }
    }

    private static SpiResult RunRequest(NativeSpiRequest request, ReadOnlySpan<SpiParameter> parameters)
    {
        CheckAccess();
        int limit = request._limit;
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        NativeSpiResult result = default;
        try
        {
            InvokeParameters(&request, parameters, &result);
            return result.ToManaged();
        }
        finally
        {
            ReleaseResult(&result);
        }
    }

    private static void InvokeParameters(NativeSpiRequest* request, ReadOnlySpan<SpiParameter> parameters, NativeSpiResult* result)
    {
        var arguments = new NativeSpiParameter[parameters.Length];
        try
        {
            for (int index = 0; index < parameters.Length; index++)
            {
                if (parameters[index].TypeOid == 0)
                {
                    throw new ArgumentException("SPI parameters must be created with an explicit managed type.", nameof(parameters));
                }

                arguments[index]._typeOid = parameters[index].TypeOid;
                arguments[index]._value = SpiType.ToNative(parameters[index].Value);
            }

            fixed (NativeSpiParameter* values = arguments)
            {
                request->_parameters = values;
                request->_parameterCount = arguments.Length;
                Invoke(request, result);
            }
        }
        finally
        {
            foreach (ref NativeSpiParameter parameter in arguments.AsSpan())
            {
                parameter._value.Release();
            }
        }
    }

    private static void ReleaseResult(NativeSpiResult* result)
    {
        if (result->_release != null)
        {
            result->_release(result);
        }
    }

    private static void Invoke(NativeSpiRequest* request, NativeSpiResult* result)
    {
        var execute = (delegate* unmanaged[Cdecl]<NativeSpiRequest*, NativeSpiResult*, NativeCallError*, int>)s_execute;
        NativeCallError error = default;
        try
        {
            if (execute(request, result, &error) != 0)
            {
                throw error.ToException();
            }
        }
        finally
        {
            error.Release();
        }
    }

    private static byte[] EncodeCommand(string commandText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);
        if (commandText.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("SQL command text cannot contain a zero character.", nameof(commandText));
        }

        return EncodeUtf8(commandText);
    }

    private static byte[] EncodeUtf8(string text)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        byte[] sql = new byte[encoding.GetByteCount(text) + 1];
        encoding.GetBytes(text, sql);
        return sql;
    }
}
