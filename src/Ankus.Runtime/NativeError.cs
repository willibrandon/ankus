using System.ComponentModel;
using System.Text;

namespace Ankus;

/// <summary>
/// Copies managed exception diagnostics into a native caller-owned buffer before returning to PostgreSQL's error boundary.
/// This API is used by generated extension dispatchers.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class NativeError
{
    /// <summary>
    /// Writes a bounded, null-terminated UTF-8 message without allowing a secondary managed exception to escape.
    /// The native caller reports PostgreSQL ERROR only after the managed dispatcher has returned.
    /// </summary>
    /// <param name="exception">The caught managed exception.</param>
    /// <param name="destination">The native caller's writable buffer.</param>
    /// <param name="capacity">The buffer size, including the null terminator.</param>
    public static unsafe void Write(Exception exception, byte* destination, int capacity)
    {
        if (destination is null || capacity <= 0)
        {
            return;
        }

        Span<byte> buffer = new(destination, capacity);
        buffer[0] = 0;
        try
        {
            string message = exception.Message;
            Encoding.UTF8.GetEncoder().Convert(message.AsSpan(), buffer[..^1], flush: true, out _, out int written, out _);
            buffer[written] = 0;
        }
        catch
        {
            ReadOnlySpan<byte> fallback = "Managed extension function failed."u8;
            int length = Math.Min(fallback.Length, capacity - 1);
            fallback[..length].CopyTo(buffer);
            buffer[length] = 0;
        }
    }
}
