using Ankus.Postgres;

namespace Ankus.Examples.Hooks;

/// <summary>
/// Ports pgrx's hooks example: a safety catch that rejects DELETE without WHERE and TRUNCATE by non-superusers.
/// </summary>
/// <remarks>
/// Each hook saves the previously installed hook and chains to it, falling back to PostgreSQL's standard
/// implementation where one exists. A failed library load can be retried, so every hook records its own
/// installation immediately: a retry must never save this extension's own hook as the previous hook.
/// </remarks>
public static unsafe partial class SafetyHooks
{
    private static ExecutorRun_hook_type s_previousExecutorRun;
    private static post_parse_analyze_hook_type s_previousPostParseAnalyze;
    private static ProcessUtility_hook_type s_previousProcessUtility;
    private static bool s_executorRunInstalled;
    private static bool s_postParseAnalyzeInstalled;
    private static bool s_processUtilityInstalled;

    /// <summary>
    /// Supplies the executor-run hook with the selected PostgreSQL headers' exact signature.
    /// </summary>
    [PgNativeCallback(nameof(ExecutorRun))]
    private static partial ExecutorRun_hook_type ExecutorRunHook { get; }

    /// <summary>
    /// Supplies the parse-analysis hook with the selected PostgreSQL headers' exact signature.
    /// </summary>
    [PgNativeCallback(nameof(PostParseAnalyze))]
    private static partial post_parse_analyze_hook_type PostParseAnalyzeHook { get; }

    /// <summary>
    /// Supplies the utility hook with the selected PostgreSQL headers' exact signature.
    /// </summary>
    [PgNativeCallback(nameof(ProcessUtility))]
    private static partial ProcessUtility_hook_type ProcessUtilityHook { get; }

    /// <summary>
    /// Installs each hook at most once per process when PostgreSQL loads the library.
    /// </summary>
    [PgModuleLoad]
    public static void Register()
    {
        if (!s_executorRunInstalled)
        {
            ExecutorRun_hook_type hook = ExecutorRunHook;
            s_previousExecutorRun = NativeGlobals.ExecutorRun_hook;
            NativeGlobals.ExecutorRun_hook = hook;
            s_executorRunInstalled = true;
        }

        if (!s_postParseAnalyzeInstalled)
        {
            post_parse_analyze_hook_type hook = PostParseAnalyzeHook;
            s_previousPostParseAnalyze = NativeGlobals.post_parse_analyze_hook;
            NativeGlobals.post_parse_analyze_hook = hook;
            s_postParseAnalyzeInstalled = true;
        }

        if (!s_processUtilityInstalled)
        {
            ProcessUtility_hook_type hook = ProcessUtilityHook;
            s_previousProcessUtility = NativeGlobals.ProcessUtility_hook;
            NativeGlobals.ProcessUtility_hook = hook;
            s_processUtilityInstalled = true;
        }
    }

    /// <summary>
    /// Rejects a DELETE whose query has no WHERE qualification.
    /// </summary>
    private static void DeleteMustHaveAWhere(Query* query)
    {
        // A DELETE query always has a join tree; its quals are null without a WHERE clause.
        if (query->commandType == CmdType.CMD_DELETE && query->jointree->quals == null)
        {
            PgLog.Error("DELETE queries must have a WHERE clause");
        }
    }

    /// <summary>
    /// Rejects TRUNCATE unless the current user is a superuser.
    /// </summary>
    private static void OnlySuperusersCanTruncate(PlannedStmt* statement)
    {
        if (statement->utilityStmt != null && statement->utilityStmt->type == NodeTag.T_TruncateStmt &&
            !NativeMethods.superuser())
        {
            PgLog.Error("Only superusers can truncate");
        }
    }

#if ANKUS_PG13 || ANKUS_PG14 || ANKUS_PG15 || ANKUS_PG16 || ANKUS_PG17
    private static void ExecutorRun(QueryDesc* query, ScanDirection direction, ulong count, bool executeOnce)
    {
        if (s_previousExecutorRun.IsNull)
        {
            NativeMethods.standard_ExecutorRun(query, direction, count, executeOnce);
        }
        else
        {
            s_previousExecutorRun.Invoke(query, direction, count, executeOnce);
        }
    }
#else
    private static void ExecutorRun(QueryDesc* query, ScanDirection direction, ulong count)
    {
        if (s_previousExecutorRun.IsNull)
        {
            NativeMethods.standard_ExecutorRun(query, direction, count);
        }
        else
        {
            s_previousExecutorRun.Invoke(query, direction, count);
        }
    }
#endif

#if ANKUS_PG13
    private static void PostParseAnalyze(ParseState* state, Query* query)
    {
        DeleteMustHaveAWhere(query);
        if (!s_previousPostParseAnalyze.IsNull)
        {
            s_previousPostParseAnalyze.Invoke(state, query);
        }
    }

    private static void ProcessUtility(PlannedStmt* statement, sbyte* queryString, ProcessUtilityContext context,
        ParamListInfoData* parameters, QueryEnvironment* environment, _DestReceiver* destination, QueryCompletion* completion)
    {
        OnlySuperusersCanTruncate(statement);
        if (s_previousProcessUtility.IsNull)
        {
            NativeMethods.standard_ProcessUtility(statement, queryString, context, parameters, environment, destination, completion);
        }
        else
        {
            s_previousProcessUtility.Invoke(statement, queryString, context, parameters, environment, destination, completion);
        }
    }
#else
    private static void PostParseAnalyze(ParseState* state, Query* query, JumbleState* jumble)
    {
        DeleteMustHaveAWhere(query);
        if (!s_previousPostParseAnalyze.IsNull)
        {
            s_previousPostParseAnalyze.Invoke(state, query, jumble);
        }
    }

    private static void ProcessUtility(PlannedStmt* statement, sbyte* queryString, bool readOnlyTree, ProcessUtilityContext context,
        ParamListInfoData* parameters, QueryEnvironment* environment, _DestReceiver* destination, QueryCompletion* completion)
    {
        OnlySuperusersCanTruncate(statement);
        if (s_previousProcessUtility.IsNull)
        {
            NativeMethods.standard_ProcessUtility(statement, queryString, readOnlyTree, context, parameters, environment,
                destination, completion);
        }
        else
        {
            s_previousProcessUtility.Invoke(statement, queryString, readOnlyTree, context, parameters, environment,
                destination, completion);
        }
    }
#endif
}
