using System.Runtime.InteropServices;
using Ankus.Postgres;

namespace Ankus.Examples.CustomScans;

/// <summary>
/// Traces real PostgreSQL sequential scans through a native custom path, plan and executor.
/// </summary>
public static unsafe partial class TraceScan
{
    private static set_rel_pathlist_hook_type s_previous;
    private static nint s_pathMethods;
    private static nint s_scanMethods;
    private static nint s_execMethods;
    private static bool s_installed;
    private static readonly long[] s_counts = new long[8];

    /// <summary>
    /// Enables tracing of sequential paths planned in the current backend.
    /// </summary>
    [PgGucBool("ankus_trace_scan.enabled", false, "Trace sequential scans with a native custom scan")]
    public static partial bool Enabled { get; }

    /// <summary>
    /// Supplies the relation path hook while retaining the exact selected-header signature.
    /// </summary>
    [PgNativeCallback(nameof(SetPaths))]
    private static partial set_rel_pathlist_hook_type Paths { get; }

    /// <summary>
    /// Converts a selected custom path into a copyable native plan.
    /// </summary>
    [PgNativeCallback(nameof(Plan))]
    private static partial CustomPathMethods_PlanCustomPathCallback Planner { get; }

    /// <summary>
    /// Allocates native execution state before PostgreSQL initializes its standard fields.
    /// </summary>
    [PgNativeCallback(nameof(Create))]
    private static partial CustomScanMethods_CreateCustomScanStateCallback Factory { get; }

    /// <summary>
    /// Starts the delegated child plan.
    /// </summary>
    [PgNativeCallback(nameof(Begin))]
    private static partial CustomExecMethods_BeginCustomScanCallback Starter { get; }

    /// <summary>
    /// Executes a tuple through PostgreSQL's scan projection machinery.
    /// </summary>
    [PgNativeCallback(nameof(Execute))]
    private static partial CustomExecMethods_ExecCustomScanCallback Executor { get; }

    /// <summary>
    /// Ends the delegated child plan.
    /// </summary>
    [PgNativeCallback(nameof(End))]
    private static partial CustomExecMethods_EndCustomScanCallback Finisher { get; }

    /// <summary>
    /// Restarts the delegated child with its current parameters.
    /// </summary>
    [PgNativeCallback(nameof(Rescan))]
    private static partial CustomExecMethods_ReScanCustomScanCallback Rescanner { get; }

    /// <summary>
    /// Reports per-node observations through real EXPLAIN output.
    /// </summary>
    [PgNativeCallback(nameof(Explain))]
    private static partial CustomExecMethods_ExplainCustomScanCallback Explainer { get; }

    /// <summary>
    /// Fetches a tuple from the actual child plan.
    /// </summary>
    [PgNativeCallback(nameof(Access))]
    private static partial ExecScanAccessMtd Reader { get; }

    /// <summary>
    /// Accepts a tuple whose child plan already performs its own recheck.
    /// </summary>
    [PgNativeCallback(nameof(Recheck))]
    private static partial ExecScanRecheckMtd Rechecker { get; }

    /// <summary>
    /// Registers backend-lived method tables and chains the previously installed relation hook.
    /// </summary>
    [PgModuleLoad]
    public static void Register()
    {
        if (s_installed)
        {
            return;
        }

        if (s_scanMethods == 0)
        {
            PgMemoryContext owner = PgMemoryContext.Create("Ankus Trace methods", PgMemoryContext.Get(PgMemoryContextKind.Top)!);
            try
            {
                owner.Run(() =>
                {
                    nint name;
                    fixed (byte* text = "Ankus Trace\0"u8)
                    {
                        name = NativeMethods.pstrdup((nint)text);
                    }

                    nint paths = Allocate(new CustomPathMethods { CustomName = name, PlanCustomPath = Planner });
                    nint executor = Allocate(new CustomExecMethods
                    {
                        CustomName = name,
                        BeginCustomScan = Starter,
                        ExecCustomScan = Executor,
                        EndCustomScan = Finisher,
                        ReScanCustomScan = Rescanner,
                        ExplainCustomScan = Explainer,
                        EstimateDSMCustomScan = SharedEstimator,
                        InitializeDSMCustomScan = SharedInitializer,
                        ReInitializeDSMCustomScan = SharedReinitializer,
                        InitializeWorkerCustomScan = WorkerInitializer,
                        ShutdownCustomScan = ShutdownHandler,
                    });
                    nint scans = Allocate(new CustomScanMethods { CustomName = name, CreateCustomScanState = Factory });
                    NativeMethods.RegisterCustomScanMethods(scans);
                    s_pathMethods = paths;
                    s_execMethods = executor;
                    s_scanMethods = scans;
                });
            }
            catch
            {
                if (s_scanMethods == 0)
                {
                    owner.Dispose();
                }

                throw;
            }
        }

        s_previous = NativeGlobals.set_rel_pathlist_hook;
        NativeGlobals.set_rel_pathlist_hook = Paths;
        s_installed = true;
    }

    /// <summary>
    /// Reads backend-local planning, creation, begin, execution, end, rescan, unwind and context cleanup counts.
    /// </summary>
    /// <returns>Eight counters in the documented order, independent of previously returned arrays.</returns>
    [PgFunction]
    public static long[] TraceScanCounts() => [.. s_counts];

    /// <summary>
    /// Clears backend-local observations without changing any registered method or active scan.
    /// </summary>
    [PgFunction]
    public static void TraceScanReset() => Array.Clear(s_counts);

    /// <summary>
    /// Preserves previous hooks and replaces each sequential path with an equally costed tracing path.
    /// </summary>
    private static void SetPaths(nint root, nint relation, uint index, nint entry)
    {
        if (!s_previous.IsNull)
        {
            s_previous.Invoke(root, relation, index, entry);
        }

        if (!Enabled)
        {
            return;
        }

        var rel = (RelOptInfo*)relation;
        WrapPaths(rel->pathlist);
        WrapPaths(rel->partial_pathlist);
    }

    /// <summary>
    /// Retains each original sequential path as a child instead of changing its costs, parameters or qualifications.
    /// </summary>
    private static void WrapPaths(nint paths)
    {
        int count = NativeMethods.list_length(paths);
        for (int index = 0; index < count; index++)
        {
            var cells = (ListCell*)((Ankus.Postgres.List*)paths)->elements;
            nint child = cells[index].ptr_value;
            var original = (Ankus.Postgres.Path*)child;
            if (original->pathtype != NodeTag.T_SeqScan)
            {
                continue;
            }

            var path = (CustomPath*)Allocate(default(CustomPath));
            path->path = *original;
            path->path.type = NodeTag.T_CustomPath;
            path->path.pathtype = NodeTag.T_CustomScan;
            path->flags = original->parallel_aware ? 0U : 1U; // CUSTOMPATH_SUPPORT_BACKWARD_SCAN for ordinary sequential scans.
            path->custom_paths = NativeMethods.lappend(0, child);
            path->methods = s_pathMethods;
            cells[index].ptr_value = (nint)path;
        }
    }

    /// <summary>
    /// Builds one exact CustomScan, leaving qualifications in its real child and copying the child's output shape.
    /// </summary>
    private static nint Plan(nint root, nint relation, nint path, nint targetList, nint clauses, nint children)
    {
        _ = root;
        _ = relation;
        _ = clauses;
        s_counts[0]++;
        var child = (Ankus.Postgres.Plan*)NativeMethods.list_nth(children, 0);
        var plan = (CustomScan*)Allocate(default(CustomScan));
        plan->scan.plan.type = NodeTag.T_CustomScan;
        plan->scan.plan.targetlist = targetList;
        plan->custom_scan_tlist = NativeMethods.copyObjectImpl(child->targetlist);
        plan->custom_plans = children;
        plan->flags = ((CustomPath*)path)->flags;
        plan->methods = s_scanMethods;
        return (nint)plan;
    }

    /// <summary>
    /// Allocates a zeroed extended state with the required tag and executor table and observes its native owner cleanup.
    /// </summary>
    private static nint Create(nint plan)
    {
        _ = plan;
        s_counts[1]++;
        var state = (State*)Allocate(default(State));
        state->_scan.ss.ps.type = NodeTag.T_CustomScanState;
        state->_scan.methods = s_execMethods;
        _ = PgMemoryContext.Current.RegisterResetCallback(static () => s_counts[7]++);
        return (nint)state;
    }

    /// <summary>
    /// Initializes the child and makes it visible to the executor's normal child-plan traversal.
    /// </summary>
    private static void Begin(nint address, nint estate, int flags)
    {
        s_counts[2]++;
        var state = (State*)address;
        var plan = (CustomScan*)state->_scan.ss.ps.plan;
        nint child = NativeMethods.ExecInitNode(NativeMethods.list_nth(plan->custom_plans, 0), estate, flags);
        state->_scan.custom_ps = NativeMethods.lappend(0, child);
    }

    /// <summary>
    /// Applies the native scan projection and always records managed unwind, including when a child raises ERROR.
    /// </summary>
    private static nint Execute(nint address)
    {
        s_counts[3]++;
        var state = (State*)address;
        state->_calls++;
        try
        {
            if (state->_shared != 0)
            {
                var shared = (SharedState*)state->_shared;
                _ = NativeMethods.pg_atomic_fetch_add_u64((nint)(&shared->_calls), 1);
            }

            return NativeMethods.ExecScan(address, Reader, Rechecker);
        }
        finally
        {
            s_counts[6]++;
        }
    }

    /// <summary>
    /// Reads the child in the executor's current direction and records nonempty tuples.
    /// </summary>
    private static nint Access(nint address)
    {
        var state = (State*)address;
        nint slot = NativeMethods.ExecProcNode(NativeMethods.list_nth(state->_scan.custom_ps, 0));
        nint destination = state->_scan.ss.ss_ScanTupleSlot;
        if (slot == 0 || (((TupleTableSlot*)slot)->tts_flags & 2) != 0) // TTS_FLAG_EMPTY
        {
            return NativeMethods.ExecClearTuple(destination);
        }

        state->_rows++;
        if (state->_shared != 0)
        {
            var shared = (SharedState*)state->_shared;
            _ = NativeMethods.pg_atomic_fetch_add_u64((nint)(&shared->_rows), 1);
        }

        return NativeMethods.ExecCopySlot(destination, slot);
    }

    /// <summary>
    /// Fetches the child's EvalPlanQual replacement into the provider's own slot.
    /// </summary>
    private static bool Recheck(nint address, nint slot)
    {
        _ = slot;
        nint replacement = Access(address);
        return (((TupleTableSlot*)replacement)->tts_flags & 2) == 0;
    }

    /// <summary>
    /// Propagates rescans to the child after the executor has updated its parameters.
    /// </summary>
    private static void Rescan(nint address)
    {
        s_counts[5]++;
        var state = (State*)address;
        state->_rescans++;
        NativeMethods.ExecScanReScan(address);
        NativeMethods.ExecReScan(NativeMethods.list_nth(state->_scan.custom_ps, 0));
    }

    /// <summary>
    /// Ends child resources through PostgreSQL's ordinary executor cleanup.
    /// </summary>
    private static void End(nint address)
    {
        s_counts[4]++;
        var state = (State*)address;
        Shutdown(address);
        NativeMethods.ExecEndNode(NativeMethods.list_nth(state->_scan.custom_ps, 0));
    }

    /// <summary>
    /// Emits counters before executor teardown, including zeros for EXPLAIN without ANALYZE.
    /// </summary>
    private static void Explain(nint address, nint ancestors, nint output)
    {
        _ = ancestors;
        var state = (State*)address;
        Property("Trace Rows\0"u8, state->_rows, output);
        Property("Trace Calls\0"u8, state->_calls, output);
        Property("Trace Rescans\0"u8, state->_rescans, output);
        if (((CustomScan*)state->_scan.ss.ps.plan)->scan.plan.parallel_aware)
        {
            Property("Shared Trace Rows\0"u8, checked((long)state->_snapshot._rows), output);
            Property("Shared Trace Calls\0"u8, checked((long)state->_snapshot._calls), output);
            Property("Trace Worker Attachments\0"u8, checked((long)state->_snapshot._workers), output);
            Property("Trace Worker Shutdowns\0"u8, checked((long)state->_snapshot._shutdowns), output);
            Property("Trace DSM Generation\0"u8, checked((long)state->_snapshot._generation), output);
        }
    }

    /// <summary>
    /// Copies a numeric observation through the selected-header EXPLAIN contract.
    /// </summary>
    private static void Property(ReadOnlySpan<byte> name, long value, nint output)
    {
        fixed (byte* label = name)
        {
            NativeMethods.ExplainPropertyInteger((nint)label, 0, value, output);
        }
    }

    /// <summary>
    /// Allocates zeroed native storage in the current PostgreSQL context and transfers it to that context.
    /// </summary>
    private static nint Allocate<T>(T value) where T : unmanaged
    {
        var address = (T*)NativeMethods.palloc0((ulong)sizeof(T));
        *address = value;
        return (nint)address;
    }

    /// <summary>
    /// Embeds the exact selected-header executor state before provider-owned counters.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        /// <summary>
        /// Retains the executor's complete native prefix.
        /// </summary>
        internal CustomScanState _scan;

        /// <summary>
        /// Counts nonempty tuples returned by the child.
        /// </summary>
        internal long _rows;

        /// <summary>
        /// Counts executor entries, including end-of-scan checks.
        /// </summary>
        internal long _calls;

        /// <summary>
        /// Counts explicit rescans of this node.
        /// </summary>
        internal long _rescans;

        /// <summary>
        /// Borrows the current parallel segment only until provider shutdown.
        /// </summary>
        internal nint _shared;

        /// <summary>
        /// Distinguishes a worker attachment from the leader's initialized state.
        /// </summary>
        internal bool _worker;

        /// <summary>
        /// Retains owned observations after the parallel segment is detached.
        /// </summary>
        internal ParallelSnapshot _snapshot;
    }
}
