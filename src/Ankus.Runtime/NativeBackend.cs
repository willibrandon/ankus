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
        if (s_execute == 0)
        {
            throw new InvalidOperationException("PostgreSQL APIs can only be used on the active PostgreSQL backend thread.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        if (commandText.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("SQL command text cannot contain a zero character.", nameof(commandText));
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        byte[] sql = new byte[encoding.GetByteCount(commandText) + 1];
        encoding.GetBytes(commandText, sql);
        var execute = (delegate* unmanaged[Cdecl]<byte*, int, NativeSpiParameter*, int, byte, int, byte,
            NativeSpiResult*, NativeCallError*, int>)s_execute;
        NativeCallError error = default;
        NativeSpiResult result = default;
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

            fixed (byte* text = sql)
            fixed (NativeSpiParameter* values = arguments)
            {
                if (execute(text, sql.Length - 1, values, arguments.Length, readOnly ? (byte)1 : (byte)0,
                    limit, (byte)resultMode, &result, &error) != 0)
                {
                    throw error.ToException();
                }
            }

            return result.ToManaged();
        }
        finally
        {
            if (result._release != null)
            {
                result._release(&result);
            }

            foreach (ref NativeSpiParameter parameter in arguments.AsSpan())
            {
                parameter._value.Release();
            }
        }
    }
}
