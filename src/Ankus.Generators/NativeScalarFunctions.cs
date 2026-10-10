namespace Ankus.Generators;

/// <summary>
/// Validates and invokes allowlisted scalar signatures inside the native guard.
/// </summary>
internal static class NativeScalarFunctions
{
    /// <summary>
    /// Gets the typed scalar function table contract and invocation helper.
    /// </summary>
    internal const string Source = """
        #if PG_VERSION_NUM >= 160000
        #include "nodes/miscnodes.h"

        /* Set while TryParse converts a built-in type. PostgreSQL 16 input functions then
         * report invalid text here instead of raising, so no subtransaction is needed. */
        static ErrorSaveContext *ankus_soft_input;
        /* Whether the ERROR being handled reports such a soft failure. The input function
         * returned normally, so it left nothing that needs rollback. */
        static bool ankus_soft_input_raised;
        #endif

        /* Calls a built-in input function. During TryParse on PostgreSQL 16 and later, a soft
         * failure is raised here, at a point where the input function has released everything,
         * so the guard can recover it without a subtransaction. */
        static Datum
        ankus_input_call(PGFunction input, char *text, Oid io_parameter, int32 typmod)
        {
        #if PG_VERSION_NUM >= 160000
            if (ankus_soft_input != NULL)
            {
                Datum result;
                if (DirectInputFunctionCallSafe(input, text, io_parameter, typmod, (Node *) ankus_soft_input, &result))
                    return result;
                ankus_soft_input_raised = true;
                /* Raise the saved report as the ERROR the input function would have raised. */
                ankus_soft_input->error_data->elevel = ERROR;
                ThrowErrorData(ankus_soft_input->error_data);
            }
        #endif
            return DirectFunctionCall3(input, CStringGetDatum(text), ObjectIdGetDatum(io_parameter), Int32GetDatum(typmod));
        }

        /* Calls an input function by OID, with the same soft-failure handling. */
        static Datum
        ankus_oid_input_call(Oid function, char *text, Oid io_parameter, int32 typmod)
        {
        #if PG_VERSION_NUM >= 160000
            if (ankus_soft_input != NULL)
            {
                FmgrInfo input;
                Datum result;
                fmgr_info(function, &input);
                if (InputFunctionCallSafe(&input, text, io_parameter, typmod, (Node *) ankus_soft_input, &result))
                    return result;
                ankus_soft_input_raised = true;
                /* Raise the saved report as the ERROR the input function would have raised. */
                ankus_soft_input->error_data->elevel = ERROR;
                ThrowErrorData(ankus_soft_input->error_data);
            }
        #endif
            return OidInputFunctionCall(function, text, io_parameter, typmod);
        }

        typedef struct AnkusScalarFunction
        {
            int operation;
            PGFunction function;
            Oid result_type;
            int argument_count;
            Oid argument_types[7];
        } AnkusScalarFunction;

        static void
        ankus_scalar_result(AnkusRequest *request, AnkusResult *result, Datum datum, bool is_null, Oid type)
        {
            result->text.is_null = is_null;
            if (request->result_context != 0)
            {
                ankus_datum_context(request->result_context, request->result_generation);
                result->result_type_oid = type;
                if (!is_null)
                    result->text.integral = (int64) (uintptr_t) ankus_copy_raw_datum(datum, type,
                        request->result_context, request->result_generation);
            }
            else if (!is_null)
                ankus_result_value(datum, type, &result->text);
        }

        static void
        ankus_call_scalar(const AnkusScalarFunction *functions, Size count, AnkusRequest *request, AnkusResult *result)
        {
            for (Size index = 0; index < count; index++)
            {
                const AnkusScalarFunction *entry = &functions[index];
                bool match = entry->operation == request->scalar_operation && entry->result_type == request->scalar_result_oid &&
                    entry->argument_count == request->parameter_count;
                if (match)
                {
                    for (int argument = 0; argument < entry->argument_count; argument++)
                        match = match && entry->argument_types[argument] == request->parameters[argument].type_oid &&
                            !request->parameters[argument].value.is_null;
                }

                if (match)
                {
                    if (request->result_context != 0)
                        ankus_datum_context(request->result_context, request->result_generation);
                    LOCAL_FCINFO(call, 7);
                    FmgrInfo info;
                    Datum datum;
                    memset(&info, 0, sizeof(info));
                    info.fn_addr = entry->function;
                    info.fn_nargs = entry->argument_count;
                    info.fn_mcxt = CurrentMemoryContext;
                    InitFunctionCallInfoData(*call, &info, entry->argument_count, InvalidOid, NULL, NULL);
                    for (int argument = 0; argument < entry->argument_count; argument++)
                    {
                        call->args[argument].value = ankus_parameter_datum(&request->parameters[argument]);
                        call->args[argument].isnull = false;
                    }

                    datum = FunctionCallInvoke(call);
                    ankus_scalar_result(request, result, datum, call->isnull, entry->result_type);
                    return;
                }
            }

            ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED), errmsg("unsupported scalar operation signature")));
        }
        """;
}
