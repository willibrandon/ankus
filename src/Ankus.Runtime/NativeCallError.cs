using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Ankus;

/// <summary>
/// Carries owned UTF-8 diagnostics and a bounded emergency message across the guarded native boundary.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeCallError
{
    /// <summary>
    /// Contains PostgreSQL's packed SQLSTATE representation.
    /// </summary>
    public int SqlState;

    /// <summary>
    /// Contains a bounded, null-terminated emergency message used if full diagnostic transport fails.
    /// </summary>
    public fixed byte Message[2048];

    /// <summary>
    /// Contains the client query's one-based character position.
    /// </summary>
    internal int _position;

    /// <summary>
    /// Contains the internal query's one-based character position.
    /// </summary>
    internal int _internalPosition;

    /// <summary>
    /// Contains the original source line number.
    /// </summary>
    internal int _line;

    /// <summary>
    /// Contains routing and completeness flags for native errors.
    /// </summary>
    internal NativeErrorFlags _flags;

    /// <summary>
    /// Contains optional full diagnostic strings with allocator-specific release callbacks.
    /// </summary>
    internal NativeErrorFields _fields;

    /// <summary>
    /// Copies native diagnostics into a managed exception before the native buffer expires.
    /// </summary>
    /// <returns>A managed PostgreSQL exception.</returns>
    internal PgException ToException()
    {
        Span<char> state = stackalloc char[5];
        for (int index = 0; index < state.Length; index++)
        {
            state[index] = (char)(((SqlState >> (index * 6)) & 0x3F) + '0');
        }

        fixed (byte* message = Message)
        {
            return new PgException(new string(state), Read(NativeDiagnosticField.Message) ?? Read(message, 2048),
                Read(NativeDiagnosticField.Detail), Read(NativeDiagnosticField.Hint))
            {
                Context = Read(NativeDiagnosticField.Context),
                SchemaName = Read(NativeDiagnosticField.Schema),
                TableName = Read(NativeDiagnosticField.Table),
                ColumnName = Read(NativeDiagnosticField.Column),
                DataTypeName = Read(NativeDiagnosticField.DataType),
                ConstraintName = Read(NativeDiagnosticField.Constraint),
                InternalQuery = Read(NativeDiagnosticField.InternalQuery),
                File = Read(NativeDiagnosticField.File),
                Routine = Read(NativeDiagnosticField.Routine),
                DetailLog = Read(NativeDiagnosticField.DetailLog),
                Backtrace = Read(NativeDiagnosticField.Backtrace),
                Position = _position,
                InternalPosition = _internalPosition,
                Line = _line,
                NativeFlags = _flags,
            };
        }
    }

    /// <summary>
    /// Releases every owned diagnostic with its originating allocator, including partially populated errors.
    /// </summary>
    internal void Release()
    {
        for (int index = 0; index < NativeErrorFields.Length; index++)
        {
            _fields[index].Release();
        }
    }

    private readonly string? Read(NativeDiagnosticField field) => _fields[(int)field].ReadOptionalString();

    private static string Read(byte* buffer, int capacity)
    {
        ReadOnlySpan<byte> bytes = new(buffer, capacity);
        int end = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]);
    }
}
