namespace Ankus.Generators;

/// <summary>Validates and invokes allowlisted scalar signatures inside the native guard.</summary>
internal static class NativeScalarFunctions
{
    /// <summary>Gets the typed scalar function table contract and invocation helper.</summary>
    internal const string Source = """
        typedef struct AnkusScalarFunction
        {
            int operation;
            PGFunction function;
            Oid result_type;
            int argument_count;
            Oid argument_types[3];
        } AnkusScalarFunction;

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
                    LOCAL_FCINFO(call, 3);
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
                    result->text.is_null = call->isnull;
                    if (!call->isnull)
                        ankus_result_value(datum, entry->result_type, &result->text);
                    return;
                }
            }
            ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED), errmsg("unsupported scalar operation signature")));
        }
        """;
}
