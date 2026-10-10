using System.Text.RegularExpressions;

namespace Ankus.Build;

/// <summary>
/// The parts of pgrx 0.19.3's bindgen configuration that select which header declarations become bindings.
/// </summary>
internal static partial class NativeBindingHeaderRules
{
    /// <summary>
    /// Determines whether pgrx blocklists a function, which then contributes no binding and no referenced types.
    /// </summary>
    /// <param name="name">The C function name.</param>
    /// <param name="major">The PostgreSQL major, since some blocklist entries apply only to some majors.</param>
    /// <returns>Whether the function is blocklisted.</returns>
    internal static bool IsBlocklistedFunction(string name, int major)
        => BlocklistedFunction().IsMatch(name) ||
            major >= 19 && TransactionIdComparison().IsMatch(name) ||
            // pgrx builds with its C shim, so before PostgreSQL 16 these macros have no function to bind.
            major < 16 && name is "BufferGetBlock" or "BufferGetPage";

    /// <summary>
    /// Determines whether pgrx blocklists a variable or constant, such as header-tracking metadata.
    /// </summary>
    /// <param name="name">The C identifier.</param>
    /// <returns>Whether it is blocklisted.</returns>
    internal static bool IsBlocklistedVariable(string name)
        => name is "CONFIGURE_ARGS" or "ERROR" || HeaderMetadata().IsMatch(name) || HeaderGuard().IsMatch(name);

    /// <summary>
    /// Determines whether pgrx tells bindgen not to parse a macro at all, which keeps it from defining later macros too.
    /// </summary>
    /// <param name="name">The macro name.</param>
    /// <returns>Whether bindgen ignores the macro.</returns>
    internal static bool IsIgnoredMacro(string name)
        => name is "FP_INFINITE" or "FP_NAN" or "FP_NORMAL" or "FP_SUBNORMAL" or "FP_ZERO" or "IPPORT_RESERVED" or "M_E" or "M_LOG2E" or
            "M_LOG10E" or "M_LN2" or "M_LN10" or "M_PI" or "M_PI_2" or "M_PI_4" or "M_1_PI" or "M_2_PI" or "M_SQRT2" or "M_SQRT1_2" or
            "M_2_SQRTPI";

    [GeneratedRegex(@"\A(?:pg_re_throw|err(?:start|code|msg|detail|context_msg|hint|finish)|heap_getattr|BufferIsLocal|" +
        @"GetMemoryChunkContext|GETSTRUCT|MAXALIGN|MemoryContextIsValid|MemoryContextSwitchTo|TYPEALIGN|TransactionIdIsNormal|" +
        @"expression_tree_walker|get_pg_major_minor_version_string|get_pg_major_version_num|get_pg_major_version_string|" +
        @"get_pg_version_string|heap_tuple_get_struct|planstate_tree_walker|query_or_expression_tree_walker|query_tree_walker|" +
        @"range_table_entry_walker|range_table_walker|raw_expression_tree_walker|type_is_array|varsize_any|" +
        @"PageValidateSpecialPointer|PageIsValid|IsQueryIdEnabled|am_tablesync_worker|am_sequencesync_worker|" +
        @"am_leader_apply_worker|am_parallel_apply_worker|get_logical_worker_type|ERROR)\z")]
    private static partial Regex BlocklistedFunction();

    [GeneratedRegex(@"\ATransactionId(?:Precedes|PrecedesOrEquals|Follows|FollowsOrEquals)\z")]
    private static partial Regex TransactionIdComparison();

    [GeneratedRegex(@"\A_*(?:HAVE|have)_.*\z")]
    private static partial Regex HeaderMetadata();

    [GeneratedRegex(@"\A_[A-Z_]+_H\z")]
    private static partial Regex HeaderGuard();
}
