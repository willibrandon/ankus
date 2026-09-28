using Ankus.Postgres;

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
    private static nint CaptureParameters(nint address)
    {
        var path = (Ankus.Postgres.Path*)address;
        var information = (ParamPathInfo*)path->param_info;
        if (information == null)
        {
            return 0;
        }

        nint variables = NativeMethods.pull_vars_of_level(NativeMethods.extract_actual_clauses(information->ppi_clauses, false), 0);
        nint parameters = 0;
        int count = NativeMethods.list_length(variables);
        for (int index = 0; index < count; index++)
        {
            nint expression = NativeMethods.list_nth(variables, index);
            if (((Node*)expression)->type == NodeTag.T_Var &&
                NativeMethods.bms_is_member(((Var*)expression)->varno, information->ppi_req_outer))
            {
                parameters = NativeMethods.list_append_unique(parameters, NativeMethods.copyObjectImpl(expression));
            }
        }

        return parameters == 0 ? 0 : NativeMethods.lappend(NativeMethods.lappend(0, parameters), NativeMethods.makeInteger(0));
    }

    /// <summary>
    /// Uses PostgreSQL's complete append ancestry and returns new private nodes without mutating another path's expressions.
    /// </summary>
    private static nint MapParameters(nint root, nint data, nint child)
    {
        if (data == 0)
        {
            return 0;
        }

        nint expressions = NativeMethods.adjust_appendrel_attrs_multilevel(root, NativeMethods.list_nth(data, 0),
            child, ((RelOptInfo*)child)->top_parent);
        int remaps = ((Integer*)NativeMethods.list_nth(data, 1))->ival;
        return NativeMethods.lappend(NativeMethods.lappend(0, expressions), NativeMethods.makeInteger(checked(remaps + 1)));
    }

    /// <summary>
    /// Deparses adjusted plan expressions through PostgreSQL, including parameter references supplied by ancestor joins.
    /// </summary>
    private static void ExplainParameters(nint address, nint ancestors, nint output)
    {
        var state = (State*)address;
        var plan = (CustomScan*)state->_scan.ss.ps.plan;
        if (plan->custom_exprs == 0)
        {
            return;
        }

        nint context = NativeMethods.set_deparse_context_plan(((ExplainState*)output)->deparse_cxt, (nint)plan, ancestors);
        nint values = 0;
        int count = NativeMethods.list_length(plan->custom_exprs);
        for (int index = 0; index < count; index++)
        {
            values = NativeMethods.lappend(values,
                NativeMethods.deparse_expression(NativeMethods.list_nth(plan->custom_exprs, index), context, true, false));
        }

        fixed (byte* label = "Trace Parameters\0"u8)
        {
            NativeMethods.ExplainPropertyList((nint)label, values, output);
        }

        Property("Trace Parameter Remaps\0"u8, ((Integer*)NativeMethods.list_nth(plan->custom_private, 0))->ival, output);
    }
}
