namespace Ankus.Generators;

/// <summary>
/// Calls allowlisted range operations with catalog-validated type identities inside the native guard.
/// </summary>
internal static class NativeRangeOperations
{
    /// <summary>
    /// Gets range input/output, metadata, construction and exact allowlisted operation signatures.
    /// </summary>
    internal const string Source = """
        static Datum ankus_range_identity(PG_FUNCTION_ARGS) { PG_RETURN_DATUM(PG_GETARG_DATUM(0)); }

        static PGFunction
        ankus_range_function(int operation)
        {
            switch (operation)
            {
                case 1: return ankus_range_identity;
                case 3: return range_contains_elem;
                case 4: return range_contains;
                case 5: return range_overlaps;
                case 6: return range_adjacent;
                case 7: return range_union;
                case 8: return range_intersect;
                case 9: return range_minus;
                case 10: return range_merge;
                default: return NULL;
            }
        }

        static void
        ankus_range_operation(AnkusRequest *request, AnkusResult *result)
        {
            int operation = request->scalar_operation;
            Oid output = request->scalar_result_oid;
            if (operation == 11)
            {
                ankus_mapped_range(request, result);
                return;
            }

            if (operation == 12 && output == OIDOID && request->parameter_count == 1 && request->parameters != NULL &&
                request->parameters[0].type_oid == OIDOID && !request->parameters[0].value.is_null)
            {
                Oid type = DatumGetObjectId(ankus_parameter_datum(&request->parameters[0]));
                Oid subtype = get_range_subtype(type);
                if (!OidIsValid(subtype))
                    ereport(ERROR, (errcode(ERRCODE_DATATYPE_MISMATCH), errmsg("Type OID %u is not a range", type)));
                result->text.integral = subtype;
                return;
            }

            if ((operation == 0 || operation == 2) && request->parameter_count == 1 &&
                request->parameters != NULL && !request->parameters[0].value.is_null)
            {
                const AnkusParameter *argument = &request->parameters[0];
                Oid function;
                Datum datum;
                if (operation == 0 && OidIsValid(get_range_subtype(output)) && argument->type_oid == TEXTOID)
                {
                    Oid parameter;
                    if (request->result_context != 0)
                        ankus_datum_context(request->result_context, request->result_generation);
                    getTypeInputInfo(output, &function, &parameter);
                    datum = OidInputFunctionCall(function, TextDatumGetCString(ankus_parameter_datum(argument)), parameter, -1);
                    ankus_scalar_result(request, result, datum, false, output);
                    return;
                }

                if (operation == 2 && output == TEXTOID && OidIsValid(get_range_subtype(argument->type_oid)))
                {
                    bool variable;
                    char *text;
                    getTypeOutputInfo(argument->type_oid, &function, &variable);
                    text = OidOutputFunctionCall(function, ankus_parameter_datum(argument));
                    datum = CStringGetTextDatum(text);
                    ankus_scalar_result(request, result, datum, false, TEXTOID);
                    return;
                }
            }

            PGFunction function = ankus_range_function(operation);
            if (function != NULL && request->parameter_count >= 1 && request->parameters != NULL)
            {
                Oid type = request->parameters[0].type_oid;
                Oid subtype = get_range_subtype(type);
                if (OidIsValid(subtype))
                {
                    AnkusScalarFunction entry = {operation, function,
                        operation >= 3 && operation <= 6 ? BOOLOID : type,
                        operation == 1 ? 1 : 2, {type, operation == 3 ? subtype : type}};
                    ankus_call_scalar(&entry, 1, request, result);
                    return;
                }
            }

            ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED), errmsg("unsupported range operation signature")));
        }

        """;
}
