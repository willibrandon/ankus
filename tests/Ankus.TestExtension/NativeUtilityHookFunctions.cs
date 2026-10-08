using System.Globalization;
using Ankus.Postgres;

namespace Ankus.TestExtension;

/// <summary>
/// Runs read-only SQL from PostgreSQL's utility hook, where statements such as SET execute without an active snapshot.
/// </summary>
[PgSchema("utility_hook_values")]
public static partial class NativeUtilityHookFunctions
{
    private static ProcessUtility_hook_type s_previous;
    private static bool s_installed;
    private static int s_mode;
    private static int s_calls;
    private static string s_result = string.Empty;

    /// <summary>
    /// Supplies the utility callback through the selected header's exact signature.
    /// </summary>
    [PgNativeCallback(nameof(Run))]
    private static partial ProcessUtility_hook_type Hook { get; }

    /// <summary>
    /// Installs the chained hook and arms one observation of the next utility statement.
    /// </summary>
    /// <param name="mode">No SQL, a read-only scalar query, or a read-only cursor, run for the next SET statement.</param>
    /// <returns>Whether the generated global contains the hook's exact callback address.</returns>
    [PgFunction]
    public static bool NativeUtilityHookInstall(int mode)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mode, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(mode, 2);
        s_mode = mode;
        s_calls = 0;
        s_result = "armed";
        unsafe
        {
            if (!s_installed)
            {
                s_previous = NativeGlobals.ProcessUtility_hook;
                NativeGlobals.ProcessUtility_hook = Hook;
                s_installed = true;
            }

            return NativeGlobals.ProcessUtility_hook.DangerousGetAddress() == Hook.DangerousGetAddress();
        }
    }

    /// <summary>
    /// Reports the armed observation and the number of SET statements seen since installation.
    /// </summary>
    /// <returns>The observation followed by the SET statement count.</returns>
    [PgFunction]
    public static string NativeUtilityHookResult() => s_result + "|" + s_calls.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Restores the previous utility hook.
    /// </summary>
    /// <returns>Whether the previous hook is installed again.</returns>
    [PgFunction]
    public static bool NativeUtilityHookRestore()
    {
        unsafe
        {
            if (s_installed)
            {
                NativeGlobals.ProcessUtility_hook = s_previous;
                s_installed = false;
            }

            return NativeGlobals.ProcessUtility_hook.DangerousGetAddress() == s_previous.DangerousGetAddress();
        }
    }

#if ANKUS_PG13
    private static unsafe void Run(PlannedStmt* statement, sbyte* query, ProcessUtilityContext context, ParamListInfoData* parameters,
        QueryEnvironment* environment, _DestReceiver* destination, QueryCompletion* completion)
    {
        Observe(statement);
        if (s_previous.IsNull)
        {
            NativeMethods.standard_ProcessUtility(statement, query, context, parameters, environment, destination, completion);
        }
        else
        {
            s_previous.Invoke(statement, query, context, parameters, environment, destination, completion);
        }
    }
#else
    private static unsafe void Run(PlannedStmt* statement, sbyte* query, bool readOnlyTree, ProcessUtilityContext context,
        ParamListInfoData* parameters, QueryEnvironment* environment, _DestReceiver* destination, QueryCompletion* completion)
    {
        Observe(statement);
        if (s_previous.IsNull)
        {
            NativeMethods.standard_ProcessUtility(statement, query, readOnlyTree, context, parameters, environment, destination, completion);
        }
        else
        {
            s_previous.Invoke(statement, query, readOnlyTree, context, parameters, environment, destination, completion);
        }
    }
#endif

    /// <summary>
    /// Performs the armed observation once, before PostgreSQL executes a SET statement.
    /// </summary>
    /// <remarks>
    /// Other utility statements, including BEGIN, pass through unchanged so SQL cannot precede transaction configuration.
    /// </remarks>
    /// <param name="statement">The utility statement PostgreSQL is about to execute.</param>
    private static unsafe void Observe(PlannedStmt* statement)
    {
        if (statement->utilityStmt == null || statement->utilityStmt->type != NodeTag.T_VariableSetStmt)
        {
            return;
        }

        s_calls++;
        int mode = s_mode;
        s_mode = -1;
        if (mode == 1)
        {
            s_result = Spi.ExecuteScalar<int>("SELECT 6 * 7").ToString(CultureInfo.InvariantCulture);
        }
        else if (mode == 2)
        {
            using SpiCursor cursor = Spi.OpenCursor("SELECT value FROM generate_series(1, 3) AS value");
            SpiResult rows = cursor.Fetch(10);
            s_result = string.Join(",", Enumerable.Range(0, rows.Count).Select(index => rows[index].Get<int>(0)));
        }
        else if (mode == 0)
        {
            s_result = "none";
        }
    }
}
