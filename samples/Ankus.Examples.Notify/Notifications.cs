using System.Text;
using Ankus.Postgres;

namespace Ankus.Examples.Notify;

/// <summary>
/// Wraps PostgreSQL's asynchronous notification interface in <c>commands/async.h</c>: <c>NOTIFY</c>, <c>LISTEN</c>
/// and <c>UNLISTEN</c>.
/// </summary>
/// <remarks>
/// Every operation belongs to the current transaction. PostgreSQL queues notifications and applies subscription changes
/// when that transaction commits, and discards them when it rolls back. The wrappers reject a call that has no active
/// transaction, such as one from a commit or rollback callback, before entering PostgreSQL: the native functions would
/// otherwise record work in transaction memory that is being released. PostgreSQL reports empty or over-length channels
/// and payloads as errors with SQLSTATE <c>22023</c>; lengths are measured in server-encoded bytes.
/// </remarks>
public static class Notifications
{
    /// <summary>
    /// Encodes managed text strictly; an unpaired surrogate cannot be represented in any PostgreSQL encoding.
    /// </summary>
    private static readonly UTF8Encoding s_strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Queues a notification on <paramref name="channel"/> with <paramref name="payload"/>, delivered at commit.
    /// </summary>
    /// <param name="channel">The channel name; PostgreSQL limits it to 63 bytes.</param>
    /// <param name="payload">The payload; PostgreSQL limits it to 7999 bytes with the default 8 kB block size.</param>
    /// <exception cref="ArgumentException">Either argument contains a NUL character or an unpaired surrogate.</exception>
    /// <exception cref="InvalidOperationException">No transaction is in progress.</exception>
    /// <exception cref="PgException">PostgreSQL rejected the channel, the payload or the notification queue.</exception>
    public static void Notify(string channel, string payload)
    {
        Validate(channel, nameof(channel));
        Validate(payload, nameof(payload));
        RequireTransaction();
        unsafe
        {
            sbyte* nativeChannel = ToServerString(channel);
            sbyte* nativePayload = ToServerString(payload);
            // Async_Notify copies both strings into transaction memory before returning.
            NativeMethods.Async_Notify(nativeChannel, nativePayload);
            NativeMethods.pfree(nativePayload);
            NativeMethods.pfree(nativeChannel);
        }
    }

    /// <summary>
    /// Subscribes this session to <paramref name="channel"/> when the current transaction commits.
    /// </summary>
    /// <param name="channel">The channel name.</param>
    /// <exception cref="ArgumentException">The channel contains a NUL character or an unpaired surrogate.</exception>
    /// <exception cref="InvalidOperationException">No transaction is in progress.</exception>
    public static void Listen(string channel)
    {
        Validate(channel, nameof(channel));
        RequireTransaction();
        unsafe
        {
            sbyte* nativeChannel = ToServerString(channel);
            NativeMethods.Async_Listen(nativeChannel);
            NativeMethods.pfree(nativeChannel);
        }
    }

    /// <summary>
    /// Unsubscribes this session from <paramref name="channel"/> when the current transaction commits.
    /// </summary>
    /// <param name="channel">The channel name.</param>
    /// <exception cref="ArgumentException">The channel contains a NUL character or an unpaired surrogate.</exception>
    /// <exception cref="InvalidOperationException">No transaction is in progress.</exception>
    public static void Unlisten(string channel)
    {
        Validate(channel, nameof(channel));
        RequireTransaction();
        unsafe
        {
            sbyte* nativeChannel = ToServerString(channel);
            NativeMethods.Async_Unlisten(nativeChannel);
            NativeMethods.pfree(nativeChannel);
        }
    }

    /// <summary>
    /// Unsubscribes this session from every channel when the current transaction commits, like <c>UNLISTEN *</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">No transaction is in progress.</exception>
    public static void UnlistenAll()
    {
        RequireTransaction();
        unsafe
        {
            NativeMethods.Async_UnlistenAll();
        }
    }

    /// <summary>
    /// Rejects text that PostgreSQL's NUL-terminated C strings would silently truncate.
    /// </summary>
    /// <param name="value">The managed text.</param>
    /// <param name="parameterName">The rejected argument's name.</param>
    private static void Validate(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        int position = value.IndexOf('\0', StringComparison.Ordinal);
        if (position >= 0)
        {
            throw new ArgumentException($"A NUL character at position {position} would truncate the PostgreSQL string.", parameterName);
        }
    }

    /// <summary>
    /// Rejects calls made while no transaction is in progress, including commit and rollback callbacks.
    /// </summary>
    private static void RequireTransaction()
    {
        bool active;
        unsafe
        {
            active = NativeMethods.IsTransactionState();
        }

        if (!active)
        {
            throw new InvalidOperationException("LISTEN, NOTIFY and UNLISTEN require a transaction in progress.");
        }
    }

    /// <summary>
    /// Copies managed text into a NUL-terminated string in the server encoding.
    /// </summary>
    /// <param name="value">Validated text without NUL characters.</param>
    /// <returns>A string allocated in the current memory context; the caller may release it with <c>pfree</c>.</returns>
    private static unsafe sbyte* ToServerString(string value)
    {
        byte[] utf8 = s_strictUtf8.GetBytes(value);
        byte* buffer = (byte*)NativeMethods.palloc((ulong)utf8.Length + 1);
        utf8.CopyTo(new Span<byte>(buffer, utf8.Length));
        buffer[utf8.Length] = 0;
        // Returns the same buffer after validation when the server encoding is UTF-8, otherwise a converted copy.
        sbyte* converted = NativeMethods.pg_any_to_server((sbyte*)buffer, utf8.Length, (int)pg_enc.PG_UTF8);
        if (converted != (sbyte*)buffer)
        {
            NativeMethods.pfree(buffer);
        }

        return converted;
    }
}
