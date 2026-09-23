using System.ComponentModel;
using System.Text;

namespace Ankus;

/// <summary>
/// Provides generated configuration getters and hook dispatchers with scoped native reads and owned value transport.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static unsafe class NativeGuc
{
    private static readonly UTF8Encoding s_utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    [ThreadStatic]
    private static nint s_read;

    [ThreadStatic]
    private static int s_scopeDepth;

    /// <summary>
    /// Enters a configuration hook's native read capability without enabling transaction-only PostgreSQL APIs.
    /// </summary>
    /// <param name="read">The native read entry point, or zero for an explicitly disabled scope.</param>
    /// <returns>The previous read binding to restore in the matching finally block.</returns>
    public static nint Enter(nint read)
    {
        nint previous = s_read;
        s_read = read;
        s_scopeDepth++;
        return previous;
    }

    /// <summary>
    /// Restores the enclosing configuration hook read capability.
    /// </summary>
    /// <param name="previous">The binding saved by the matching Enter call.</param>
    public static void Exit(nint previous)
    {
        s_read = previous;
        s_scopeDepth--;
    }

    /// <summary>
    /// Reads the current native Boolean backing value, independently of any display hook.
    /// </summary>
    /// <param name="name">The generated setting's qualified name.</param>
    /// <returns>The current Boolean value.</returns>
    public static bool ReadBoolean(string name)
    {
        NativeValue value = Read(name, 0);
        try
        {
            return value.ReadGucScalar() switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidOperationException("A native Boolean configuration value must be zero or one."),
            };
        }
        finally
        {
            value.Release();
        }
    }

    /// <summary>
    /// Reads the current native signed integer backing value.
    /// </summary>
    /// <param name="name">The generated setting's qualified name.</param>
    /// <returns>The current signed integer.</returns>
    public static int ReadInt32(string name) => ReadOrdinal(name, 1);

    /// <summary>
    /// Reads the current native real backing value without losing its floating-point bit representation.
    /// </summary>
    /// <param name="name">The generated setting's qualified name.</param>
    /// <returns>The current floating-point value.</returns>
    public static double ReadDouble(string name)
    {
        NativeValue value = Read(name, 2);
        try
        {
            return BitConverter.Int64BitsToDouble(value.ReadGucScalar());
        }
        finally
        {
            value.Release();
        }
    }

    /// <summary>
    /// Copies the current native string backing value, preserving absent and empty strings.
    /// </summary>
    /// <param name="name">The generated setting's qualified name.</param>
    /// <returns>The owned current string, or null when the native value is absent.</returns>
    public static string? ReadString(string name)
    {
        NativeValue value = Read(name, 3);
        try
        {
            _ = value.ReadGucBuffer(out bool isNull);
            if (isNull)
            {
                return null;
            }

            string text = value.ReadString();
            return ValidateText(text, nameof(value));
        }
        finally
        {
            value.Release();
        }
    }

    /// <summary>
    /// Copies a nonnullable string setting, rejecting an unexpected absent native value.
    /// </summary>
    /// <param name="name">The generated setting's qualified name.</param>
    /// <returns>The owned nonnull current string.</returns>
    public static string ReadRequiredString(string name)
        => ReadString(name) ?? throw new InvalidOperationException("A nonnullable configuration setting has an absent native value.");

    /// <summary>
    /// Reads the dense native enum ordinal for conversion by the generated C# enum mapping.
    /// </summary>
    /// <param name="name">The generated setting's qualified name.</param>
    /// <returns>The current native ordinal.</returns>
    public static int ReadEnum(string name) => ReadOrdinal(name, 4);

    /// <summary>
    /// Copies borrowed native hook extra bytes into immutable managed ownership.
    /// </summary>
    /// <param name="value">The borrowed extra transport; its owner remains responsible for release.</param>
    /// <returns>The owned bytes, including an empty instance, or null for absent extra.</returns>
    public static PgGucExtra? ReadExtra(NativeValue value)
    {
        ReadOnlySpan<byte> bytes = value.ReadGucBuffer(out bool isNull);
        return isNull ? null : new PgGucExtra(bytes);
    }

    /// <summary>
    /// Copies immutable extra bytes into an owned native transport with a matching allocator callback.
    /// </summary>
    /// <param name="extra">The managed bytes, or null for absent extra.</param>
    /// <returns>The owned native result, released by the native hook wrapper after copying.</returns>
    public static NativeValue FromExtra(PgGucExtra? extra)
        => extra is null ? new NativeValue { IsNull = 1 } : NativeValue.FromBytes(extra.AsSpan());

    /// <summary>
    /// Writes owned check-rejection diagnostics without choosing a PostgreSQL reporting severity.
    /// </summary>
    /// <param name="diagnostic">The validated optional diagnostic fields.</param>
    /// <param name="error">The zero-initialized native diagnostic buffer, released by the native caller.</param>
    public static void WriteCheckError(PgGucCheckError diagnostic, NativeCallError* error)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (error is null)
        {
            throw new ArgumentNullException(nameof(error));
        }

        *error = default;
        error->SqlState = NativeError.PackSqlState(diagnostic.SqlState ?? "22023");
        try
        {
            if (diagnostic.Message is string message)
            {
                error->_fields[(int)NativeDiagnosticField.Message] = NativeValue.FromString(message);
            }

            if (diagnostic.Detail is string detail)
            {
                error->_fields[(int)NativeDiagnosticField.Detail] = NativeValue.FromString(detail);
            }

            if (diagnostic.Hint is string hint)
            {
                error->_fields[(int)NativeDiagnosticField.Hint] = NativeValue.FromString(hint);
            }
        }
        catch
        {
            error->Release();
            *error = default;
            throw;
        }
    }

    /// <summary>
    /// Validates text before pinning, allocation, or transfer to PostgreSQL's C-string interfaces.
    /// </summary>
    internal static string? ValidateText(string? value, string parameterName)
    {
        if (value is not null)
        {
            if (value.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException("PostgreSQL configuration text cannot contain a zero character.", parameterName);
            }

            _ = s_utf8.GetByteCount(value);
        }

        return value;
    }

    /// <summary>
    /// Encodes a setting name as a terminated UTF-8 string for either native read route.
    /// </summary>
    internal static byte[] EncodeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ValidateText(name, nameof(name));
        byte[] bytes = new byte[s_utf8.GetByteCount(name) + 1];
        s_utf8.GetBytes(name, bytes);
        return bytes;
    }

    private static int ReadOrdinal(string name, int kind)
    {
        NativeValue value = Read(name, kind);
        try
        {
            return checked((int)value.ReadGucScalar());
        }
        finally
        {
            value.Release();
        }
    }

    private static NativeValue Read(string name, int kind)
    {
        if (s_scopeDepth == 0)
        {
            return NativeBackend.ReadGuc(name, kind);
        }

        if (s_read == 0)
        {
            throw new InvalidOperationException("Configuration reads are unavailable in this callback scope.");
        }

        byte[] bytes = EncodeName(name);
        NativeValue value = default;
        NativeCallError error = default;
        bool transferred = false;
        try
        {
            fixed (byte* text = bytes)
            {
                var read = (delegate* unmanaged[Cdecl]<byte*, int, NativeValue*, NativeCallError*, int>)s_read;
                if (read(text, kind, &value, &error) != 0)
                {
                    throw error.ToException();
                }
            }

            transferred = true;
            return value;
        }
        finally
        {
            if (!transferred)
            {
                value.Release();
            }

            error.Release();
        }
    }
}
