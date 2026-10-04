using System.Globalization;
using System.Runtime.InteropServices;
using Ankus.Postgres;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises the generated reporting-hook global and typed callbacks over PostgreSQL-owned error data.
/// </summary>
[PgSchema("emit_log_values")]
public static partial class NativeLogHookFunctions
{
    private static emit_log_hook_type s_previous;
    private static bool s_installed;
    private static bool s_reject;
    private static bool s_nested;
    private static bool s_rejectNested;
    private static string s_marker = string.Empty;
    private static string[] s_snapshot = [];
    private static readonly List<int> s_order = [];
    private static readonly List<string> s_messages = [];

    /// <summary>
    /// Supplies the outer reporting callback through the selected native signature.
    /// </summary>
    [PgNativeCallback(nameof(OuterLog))]
    private static partial emit_log_hook_type Outer { get; }

    /// <summary>
    /// Supplies an independent inner callback of the same reporting signature.
    /// </summary>
    [PgNativeCallback(nameof(InnerLog))]
    private static partial emit_log_hook_type Inner { get; }

    /// <summary>
    /// Installs an explicitly chained pair without losing the previous native hook.
    /// </summary>
    /// <param name="marker">The exact message observed by this backend-local probe.</param>
    /// <param name="mode">Capture, reject, nested capture, or nested rejection.</param>
    /// <returns>Whether both callback addresses and the generated global retain their exact values.</returns>
    [PgFunction]
    public static bool NativeLogHookInstall(string marker, int mode)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mode, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(mode, 3);
        s_marker = marker;
        s_reject = mode == 1;
        s_nested = mode >= 2;
        s_rejectNested = mode == 3;
        s_snapshot = [];
        s_order.Clear();
        s_messages.Clear();
        unsafe
        {
            if (!s_installed)
            {
                s_previous = NativeGlobals.emit_log_hook;
                NativeGlobals.emit_log_hook = Outer;
                s_installed = true;
            }

            return !Outer.IsNull && !Inner.IsNull && Outer.DangerousGetAddress() != Inner.DangerousGetAddress() &&
                NativeGlobals.emit_log_hook.DangerousGetAddress() == Outer.DangerousGetAddress();
        }
    }

    /// <summary>
    /// Emits structured literal text through PostgreSQL's real reporter after installing the callback chain.
    /// </summary>
    /// <returns>The ordinary query value after a successful nonterminal report.</returns>
    [PgFunction]
    public static int NativeLogHookReport()
    {
        PgLog.Notice(new PgDiagnostic(s_marker)
        {
            SqlState = "01000",
            Detail = "owned hook detail",
            Hint = "owned hook hint",
        });
        return 42;
    }

    /// <summary>
    /// Returns diagnostic copies after PostgreSQL has released the report's native buffers.
    /// </summary>
    /// <returns>Severity, SQLSTATE bits, exact fields and client/server routing observed by the hook.</returns>
    [PgFunction]
    public static string[] NativeLogHookSnapshot() => s_snapshot;

    /// <summary>
    /// Returns the actual nested callback order after success or failure.
    /// </summary>
    /// <returns>The ordered callback entries and unwind markers.</returns>
    [PgFunction]
    public static int[] NativeLogHookOrder() => [.. s_order];

    /// <summary>
    /// Returns owned messages from every actual entry into the managed outer callback.
    /// </summary>
    /// <returns>The messages observed before invoking the inner callback.</returns>
    [PgFunction]
    public static string[] NativeLogHookMessages() => [.. s_messages];

    /// <summary>
    /// Restores the saved native hook only while this probe still owns the installed head.
    /// </summary>
    /// <returns>Whether the original global value was restored exactly.</returns>
    [PgFunction]
    public static bool NativeLogHookRestore()
    {
        unsafe
        {
            if (s_installed)
            {
                if (NativeGlobals.emit_log_hook.DangerousGetAddress() != Outer.DangerousGetAddress())
                {
                    return false;
                }

                NativeGlobals.emit_log_hook = s_previous;
                s_installed = false;
            }

            return NativeGlobals.emit_log_hook.DangerousGetAddress() == s_previous.DangerousGetAddress();
        }
    }

    /// <summary>
    /// Enters the inner native callback and records managed unwinding before any backend error is raised.
    /// </summary>
    /// <param name="data">The reporter's borrowed diagnostic storage, valid only during this callback.</param>
    private static unsafe void OuterLog(ErrorData* data)
    {
        string message = Read(data->message);
        s_messages.Add(message);
        int entry = message == s_marker ? 1 : message == s_marker + " nested" ? 5 : 0;
        if (entry != 0)
        {
            s_order.Add(entry);
        }

        try
        {
            Inner.Invoke(data);
        }
        finally
        {
            if (entry != 0)
            {
                s_order.Add(entry + 3);
            }
        }
    }

    /// <summary>
    /// Copies selected-header diagnostic fields and chains to the previous hook synchronously.
    /// </summary>
    /// <param name="data">The reporter's current diagnostic storage.</param>
    private static unsafe void InnerLog(ErrorData* data)
    {
        string message = Read(data->message);
        bool matching = message == s_marker;
        bool nested = message == s_marker + " nested";
        if (matching)
        {
            s_order.Add(2);
            s_snapshot =
            [
                data->elevel.ToString(CultureInfo.InvariantCulture),
                data->sqlerrcode.ToString(CultureInfo.InvariantCulture),
                Read(data->message), Read(data->detail), Read(data->hint),
                data->output_to_server.ToString(), data->output_to_client.ToString(),
            ];
            if (s_reject)
            {
                s_reject = false;
                throw new PgException("P7520", "managed log hook failure", "owned callback detail", "retry report");
            }

            if (s_nested)
            {
                s_nested = false;
                PgLog.Notice(new PgDiagnostic(s_marker + " nested")
                {
                    SqlState = "01000",
                    Detail = "owned nested detail",
                    Hint = "owned nested hint",
                });
            }
        }
        else if (nested)
        {
            s_order.Add(6);
            if (s_rejectNested)
            {
                s_rejectNested = false;
                throw new PgException("P7522", "nested managed log hook failure", "owned nested failure detail", "retry nested report");
            }
        }

        if (!s_previous.IsNull)
        {
            s_previous.Invoke(data);
        }

        if (matching || nested)
        {
            s_order.Add(matching ? 3 : 7);
        }
    }

    /// <summary>
    /// Copies a nullable native UTF-8 field while the UTF-8 test backend still owns its storage.
    /// </summary>
    /// <param name="value">A field within the current reporter's diagnostic.</param>
    /// <returns>An independently owned managed string.</returns>
    private static unsafe string Read(sbyte* value) => Marshal.PtrToStringUTF8((nint)value) ?? string.Empty;
}
