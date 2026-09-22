using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Ankus;

/// <summary>
/// Carries bounded UTF-8 diagnostics across a guarded native call without transferring ownership of error-context memory.
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
    /// Contains the null-terminated primary error message.
    /// </summary>
    public fixed byte Message[2048];

    /// <summary>
    /// Contains null-terminated additional error detail.
    /// </summary>
    public fixed byte Detail[2048];

    /// <summary>
    /// Contains a null-terminated corrective hint.
    /// </summary>
    public fixed byte Hint[1024];

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

        fixed (byte* message = Message, detail = Detail, hint = Hint)
        {
            return new PgException(new string(state), Read(message, 2048), Read(detail, 2048), Read(hint, 1024));
        }
    }

    private static string Read(byte* buffer, int capacity)
    {
        ReadOnlySpan<byte> bytes = new(buffer, capacity);
        int end = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]);
    }
}
