using Ankus.Postgres;
using NativeList = Ankus.Postgres.List;

namespace Ankus.Examples.CustomScans;

/// <summary>
/// Retains copyable outer-parameter expressions and maps them to the chosen partition child.
/// </summary>
public static unsafe partial class TraceScan
{
    /// <summary>
    /// Translates provider-owned path expressions when PostgreSQL selects an outer partition.
    /// </summary>
    [PgNativeCallback(nameof(MapParameters))]
    private static partial CustomPathMethods_ReparameterizeCustomPathByChildCallback ParameterMapper { get; }

    /// <summary>
    /// Keeps distinct direct outer variables for diagnostics without retaining or evaluating their enclosing clauses.
    /// </summary>
    private static NativeList* CaptureParameters(Ankus.Postgres.Path* path)
    {
        ParamPathInfo* information = path->param_info;
        if (information == null)
        {
            return null;
        }

        NativeList* variables = NativeMethods.pull_vars_of_level((Node*)NativeMethods.extract_actual_clauses(information->ppi_clauses, false), 0);
        NativeList* parameters = null;
        int count = NativeMethods.list_length(variables);
        for (int index = 0; index < count; index++)
        {
            var expression = (Node*)NativeMethods.list_nth(variables, index);
            if (expression->type != NodeTag.T_Var)
            {
                continue;
            }

#if ANKUS_PG13 || ANKUS_PG14
            int relationIndex = checked((int)((Var*)expression)->varno);
#else
            int relationIndex = ((Var*)expression)->varno;
#endif
            if (NativeMethods.bms_is_member(relationIndex, information->ppi_req_outer))
            {
                parameters = NativeMethods.list_append_unique(parameters, NativeMethods.copyObjectImpl(expression));
            }
        }

        return parameters == null ? null : NativeMethods.lappend(NativeMethods.lappend(null, parameters), NativeMethods.makeInteger(0));
    }

    /// <summary>
    /// Uses PostgreSQL's complete append ancestry and returns new private nodes without mutating another path's expressions.
    /// </summary>
    private static NativeList* MapParameters(PlannerInfo* root, NativeList* data, RelOptInfo* child)
    {
        if (data == null)
        {
            return null;
        }

#if ANKUS_PG13 || ANKUS_PG14 || ANKUS_PG15
        Node* expressions = NativeMethods.adjust_appendrel_attrs_multilevel(root, (Node*)NativeMethods.list_nth(data, 0),
            child->relids, child->top_parent_relids);
#else
        Node* expressions = NativeMethods.adjust_appendrel_attrs_multilevel(root, (Node*)NativeMethods.list_nth(data, 0),
            child, child->top_parent);
#endif
        int remaps = ReadParameterRemaps(NativeMethods.list_nth(data, 1));
        return NativeMethods.lappend(NativeMethods.lappend(null, expressions), NativeMethods.makeInteger(checked(remaps + 1)));
    }

    /// <summary>
    /// Reads the counter from the selected server's integer value-node representation.
    /// </summary>
    private static int ReadParameterRemaps(void* address)
    {
#if ANKUS_PG13 || ANKUS_PG14
        return ((Value*)address)->val.ival;
#else
        return ((Integer*)address)->ival;
#endif
    }

    /// <summary>
    /// Deparses adjusted plan expressions through PostgreSQL, including parameter references supplied by ancestor joins.
    /// </summary>
    private static void ExplainParameters(CustomScanState* address, NativeList* ancestors, ExplainState* output)
    {
        var state = (State*)address;
        var plan = (CustomScan*)state->_scan.ss.ps.plan;
        if (plan->custom_exprs == null)
        {
            return;
        }

        NativeList* context = NativeMethods.set_deparse_context_plan(output->deparse_cxt, &plan->scan.plan, ancestors);
        NativeList* values = null;
        int count = NativeMethods.list_length(plan->custom_exprs);
        for (int index = 0; index < count; index++)
        {
            values = NativeMethods.lappend(values,
                NativeMethods.deparse_expression((Node*)NativeMethods.list_nth(plan->custom_exprs, index), context, true, false));
        }

        fixed (byte* label = "Trace Parameters\0"u8)
        {
            NativeMethods.ExplainPropertyList((sbyte*)label, values, output);
        }

        Property("Trace Parameter Remaps\0"u8, ReadParameterRemaps(NativeMethods.list_nth(plan->custom_private, 0)), output);
    }
}
