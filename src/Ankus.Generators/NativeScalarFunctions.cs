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
