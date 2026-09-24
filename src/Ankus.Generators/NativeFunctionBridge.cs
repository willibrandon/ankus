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
        typedef struct AnkusFunctionSite
        {
            MemoryContextCallback reset;
            FmgrInfo *function;
            MemoryContext owner;
            Oid function_oid;
            intptr_t identity;
            struct AnkusFunctionSite *next;
        } AnkusFunctionSite;

        static AnkusFunctionSite *ankus_function_sites;
        static uintptr_t ankus_next_function_site = 1;

        static void
        ankus_function_site_reset(void *argument)
        {
            AnkusFunctionSite *site = argument;
            AnkusFunctionSite **slot = &ankus_function_sites;
            while (*slot != site)
                slot = &(*slot)->next;
            *slot = site->next;
            free(site);
        }

        static void
        ankus_function_site(FmgrInfo *function, AnkusResult *result)
        {
            AnkusMemoryContext *owner = ankus_memory_register_context(function->fn_mcxt);
            result->function_memory = (intptr_t) owner->id;
            result->function_generation = owner->generation;
            for (AnkusFunctionSite *site = ankus_function_sites; site != NULL; site = site->next)
            {
                if (site->function == function && site->owner == function->fn_mcxt && site->function_oid == function->fn_oid)
                {
                    result->function_site = site->identity;
                    return;
                }
            }

            if (ankus_next_function_site > INTPTR_MAX)
                ereport(ERROR, (errcode(ERRCODE_PROGRAM_LIMIT_EXCEEDED), errmsg("Ankus function-site identities exhausted")));
            AnkusFunctionSite *site = calloc(1, sizeof(*site));
            if (site == NULL)
                ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg("out of memory registering a function site")));
            site->function = function;
            site->owner = function->fn_mcxt;
            site->function_oid = function->fn_oid;
            site->identity = (intptr_t) ankus_next_function_site++;
            site->reset.func = ankus_function_site_reset;
            site->reset.arg = site;
            site->next = ankus_function_sites;
            ankus_function_sites = site;
            MemoryContextRegisterResetCallback(site->owner, &site->reset);
            result->function_site = site->identity;
        }

        static void
        ankus_function_context(AnkusRequest *request, AnkusResult *result)
        {
            FunctionCallInfo call = request->function_call;
            if (call == NULL || call->flinfo == NULL || call->nargs < 0)
                ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("Invalid function-call context")));

            ankus_datum_context(request->result_context, request->result_generation);
            result->function_oid = call->flinfo->fn_oid;
            result->collation_oid = call->fncollation;
            /* fn_extra remains available to PostgreSQL's set-function machinery. */
            ankus_function_site(call->flinfo, result);
            Oid *declared_types = NULL;
            int declared_count = 0;
            Oid declared_result = get_func_signature(result->function_oid, &declared_types, &declared_count);
            result->result_type_oid = IsPolymorphicType(declared_result) ? get_fn_expr_rettype(call->flinfo) : declared_result;
            /* Type input/receive calls include extra native slots beyond their SQL declaration. */
            int count = Min(call->nargs, declared_count);
            result->column_count = count;
            result->row_count = 1;
            if (count == 0)
                return;

            result->columns = calloc(count, sizeof(AnkusColumn));
            result->values = calloc(count, sizeof(AnkusValue));
            if (result->columns == NULL || result->values == NULL)
                ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg("out of memory copying function arguments")));

            for (int index = 0; index < count; index++)
            {
                Oid type = get_fn_expr_argtype(call->flinfo, index);
                if (!OidIsValid(type))
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
