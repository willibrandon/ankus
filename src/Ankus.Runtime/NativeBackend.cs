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
    private static nint t_execute;

    /// <summary>
    /// Enters a native callback scope, preserving the previous binding for recursive SPI calls.
    /// </summary>
    /// <param name="execute">The native guarded SPI entry point.</param>
    /// <returns>The previous callback binding, restored by generated code in a finally block.</returns>
    public static nint Enter(nint execute)
    {
        nint previous = t_execute;
        t_execute = execute;
        return previous;
    }

    /// <summary>
    /// Restores the enclosing native callback scope.
    /// </summary>
    /// <param name="previous">The binding saved on entry.</param>
    public static void Exit(nint previous) => t_execute = previous;

    /// <summary>
    /// Executes SQL through a native guard that catches PostgreSQL errors and recovers its internal subtransaction.
    /// </summary>
    /// <param name="commandText">The SQL command text.</param>
    /// <returns>The number of rows processed by the final statement.</returns>
    internal static long Execute(string commandText)
    {
        if (t_execute == 0)
        {
            throw new InvalidOperationException("PostgreSQL APIs can only be used on the active PostgreSQL backend thread.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);
        if (commandText.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("SQL command text cannot contain a zero character.", nameof(commandText));
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        byte[] sql = new byte[encoding.GetByteCount(commandText) + 1];
        encoding.GetBytes(commandText, sql);
        var execute = (delegate* unmanaged[Cdecl]<byte*, int, long*, NativeCallError*, int>)t_execute;
        NativeCallError error = default;
        long rows = 0;
        fixed (byte* text = sql)
        {
            if (execute(text, sql.Length - 1, &rows, &error) != 0)
            {
                throw error.ToException();
            }
        }

        return rows;
    }
}
