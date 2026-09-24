namespace Ankus.Generators;

/// <summary>
/// Captures PostgreSQL function-call metadata and independent raw argument copies.
/// </summary>
internal static class NativeFunctionBridge
{
    /// <summary>
    /// Gets function-call capture executed inside the native error and subtransaction guard.
    /// </summary>
    internal const string Source = """
        static void
        ankus_function_context(AnkusRequest *request, AnkusResult *result)
        {
            FunctionCallInfo call = request->function_call;
            if (call == NULL || call->flinfo == NULL || call->nargs < 0)
                ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("Invalid function-call context")));

            ankus_datum_context(request->result_context, request->result_generation);
            result->function_oid = call->flinfo->fn_oid;
            result->result_type_oid = get_fn_expr_rettype(call->flinfo);
            result->collation_oid = call->fncollation;
            Oid *declared_types = NULL;
            int declared_count = 0;
            Oid declared_result = get_func_signature(result->function_oid, &declared_types, &declared_count);
            if (!OidIsValid(result->result_type_oid))
                result->result_type_oid = declared_result;
            result->column_count = call->nargs;
            result->row_count = 1;
            if (call->nargs == 0)
                return;

            result->columns = calloc(call->nargs, sizeof(AnkusColumn));
            result->values = calloc(call->nargs, sizeof(AnkusValue));
            if (result->columns == NULL || result->values == NULL)
                ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg("out of memory copying function arguments")));

            for (int index = 0; index < call->nargs; index++)
            {
                Oid type = get_fn_expr_argtype(call->flinfo, index);
                if (!OidIsValid(type) && index < declared_count)
                    type = declared_types[index];
                if (!OidIsValid(type))
                    ereport(ERROR, (errcode(ERRCODE_INDETERMINATE_DATATYPE), errmsg("Cannot determine function argument %d's type", index + 1)));
                result->columns[index].type_oid = type;
                result->values[index].is_null = call->args[index].isnull;
                if (!call->args[index].isnull)
                    result->values[index].integral = (int64) (uintptr_t) ankus_copy_raw_datum(call->args[index].value,
                        type, request->result_context, request->result_generation);
            }
        }
        """;
}
