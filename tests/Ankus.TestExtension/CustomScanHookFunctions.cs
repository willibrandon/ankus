using Ankus.Postgres;

namespace Ankus.TestExtension;

/// <summary>
/// Supplies an independent predecessor hook for observing actual trace-provider chaining and native planner arguments.
/// </summary>
public static unsafe partial class CustomScanHookFunctions
{
    /// <summary>
    /// Retains any hook installed before this explicit test probe.
    /// </summary>
    private static set_rel_pathlist_hook_type s_previous;

    /// <summary>
    /// Restricts observations to the caller's real temporary relation.
    /// </summary>
    private static uint s_relation;

    /// <summary>
    /// Prevents accidentally chaining this callback to itself.
    /// </summary>
    private static bool s_installed;

    /// <summary>
    /// Selects one controlled managed or native error for the next matching planner call.
    /// </summary>
    private static int s_failure;

    /// <summary>
    /// Counts matching entries and managed finally execution independently.
    /// </summary>
    private static readonly long[] s_counts = new long[2];

    /// <summary>
    /// Retains the range-table index supplied by PostgreSQL.
    /// </summary>
    private static uint s_index;

    /// <summary>
    /// Records whether every argument points to the same actual planner relation.
    /// </summary>
    private static bool s_arguments;

    /// <summary>
    /// Records whether the predecessor sees the original paths before the trace provider wraps them.
    /// </summary>
    private static bool s_unwrapped;

    /// <summary>
    /// Supplies a typed predecessor callback with the exact selected-header planner signature.
    /// </summary>
    [PgNativeCallback(nameof(SetPaths))]
    private static partial set_rel_pathlist_hook_type Predecessor { get; }

    /// <summary>
    /// Installs the independent predecessor before the standalone trace extension is loaded.
    /// </summary>
    /// <param name="relation">The relation whose planner calls should be observed.</param>
    [PgFunction]
    public static void CustomScanHookInstall(PgRelation relation)
    {
        s_relation = relation.Oid;
        if (!s_installed)
        {
            s_previous = NativeGlobals.set_rel_pathlist_hook;
            NativeGlobals.set_rel_pathlist_hook = Predecessor;
            s_installed = true;
        }

        CustomScanHookConfigure(0);
    }

    /// <summary>
    /// Resets observations and selects a one-shot error without replacing any installed hook.
    /// </summary>
    /// <param name="mode">Zero for ordinary execution, one for a managed error and two for a guarded native error.</param>
    [PgFunction]
    public static void CustomScanHookConfigure(int mode)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mode);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(mode, 2);
        Array.Clear(s_counts);
        s_index = 0;
        s_arguments = true;
        s_unwrapped = true;
        s_failure = mode;
    }

    /// <summary>
    /// Returns owned entry/finally counts, the observed index, exact-argument identity and original-path observations.
    /// </summary>
    /// <returns>Five independent backend-local observations.</returns>
    [PgFunction]
    public static long[] CustomScanHookState() => [s_counts[0], s_counts[1], s_index, s_arguments ? 1 : 0, s_unwrapped ? 1 : 0];

    /// <summary>
    /// Observes real PostgreSQL planner state and preserves the previously installed hook and error boundary.
    /// </summary>
    private static void SetPaths(PlannerInfo* root, RelOptInfo* relation, uint index, RangeTblEntry* entry)
    {
        if (!s_previous.IsNull)
        {
            s_previous.Invoke(root, relation, index, entry);
        }

        if (entry->rtekind != RTEKind.RTE_RELATION || entry->relid != s_relation)
        {
            return;
        }

        s_counts[0]++;
        try
        {
            s_index = index;
            s_arguments &= index > 0 && index < root->simple_rel_array_size && relation->relid == index &&
                root->simple_rel_array[index] == relation && root->simple_rte_array[index] == entry;
            int count = NativeMethods.list_length(relation->pathlist);
            s_unwrapped &= count > 0;
            for (int offset = 0; offset < count; offset++)
            {
                var path = (Ankus.Postgres.Path*)NativeMethods.list_nth(relation->pathlist, offset);
                s_unwrapped &= path->pathtype == NodeTag.T_SeqScan;
            }

            int failure = s_failure;
            s_failure = 0;
            if (failure == 1)
            {
                throw new PgException("P7522", "managed predecessor failure", "predecessor detail", "retry planning");
            }

            if (failure == 2)
            {
                fixed (byte* name = "Ankus missing predecessor\0"u8)
                {
                    _ = NativeMethods.GetCustomScanMethods((sbyte*)name, false);
                }
            }
        }
        finally
        {
            s_counts[1]++;
        }
    }
}
