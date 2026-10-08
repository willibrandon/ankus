using System.Runtime.InteropServices;
using Ankus.Postgres;

namespace Ankus.Examples.Threads;

/// <summary>
/// Ports pgrx's <c>pgthread</c> parse-analysis hook, which rejects the exact query text <c>SELECT 1;</c>.
/// </summary>
/// <remarks>
/// PostgreSQL invokes the hook synchronously on the backend thread for every statement it analyzes, including
/// statements run through SPI. The managed handler can therefore call PostgreSQL directly.
/// </remarks>
public static unsafe partial class ParseAnalysisHook
{
    private static post_parse_analyze_hook_type s_previous;
    private static bool s_installed;

    /// <summary>
    /// Supplies the hook with the selected PostgreSQL headers' exact signature.
    /// </summary>
    [PgNativeCallback(nameof(Analyze))]
    private static partial post_parse_analyze_hook_type Hook { get; }

    /// <summary>
    /// Installs the hook once per backend, saving the previously installed hook for chaining.
    /// </summary>
    [PgModuleLoad]
    public static void Register()
    {
        if (s_installed)
        {
            return;
        }

        post_parse_analyze_hook_type hook = Hook;
        s_previous = NativeGlobals.post_parse_analyze_hook;
        NativeGlobals.post_parse_analyze_hook = hook;
        s_installed = true;
    }

#if ANKUS_PG13
    private static void Analyze(ParseState* state, Query* query)
    {
        Inspect(state);
        if (!s_previous.IsNull)
        {
            s_previous.Invoke(state, query);
        }
    }
#else
    private static void Analyze(ParseState* state, Query* query, JumbleState* jumble)
    {
        Inspect(state);
        if (!s_previous.IsNull)
        {
            s_previous.Invoke(state, query, jumble);
        }
    }
#endif

    /// <summary>
    /// Allocates in the current memory context and rejects one exact query text.
    /// </summary>
    private static void Inspect(ParseState* state)
    {
        // Like pgrx, allocate one byte to show that the hook calls PostgreSQL on the backend thread.
        _ = NativeMethods.palloc(1);
        if (state->p_sourcetext != null &&
            MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)state->p_sourcetext).SequenceEqual("SELECT 1;"u8))
        {
            PgLog.Error("oh no");
        }
    }
}
