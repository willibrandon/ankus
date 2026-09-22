namespace Ankus.Generators;

/// <summary>
/// Calls allowlisted built-in range operations within the guarded scalar dispatch infrastructure.
/// </summary>
internal static class NativeRangeOperations
{
    /// <summary>
    /// Gets range input/output, identity canonicalization and exact operation signatures for six range families.
    /// </summary>
    internal const string Source = """
        static Datum ankus_range_identity(PG_FUNCTION_ARGS) { PG_RETURN_DATUM(PG_GETARG_DATUM(0)); }

        #define ANKUS_RANGE_FUNCTIONS(type, subtype) \
            {1, ankus_range_identity, type, 1, {type}}, \
            {3, range_contains_elem, BOOLOID, 2, {type, subtype}}, \
            {4, range_contains, BOOLOID, 2, {type, type}}, \
            {5, range_overlaps, BOOLOID, 2, {type, type}}, \
            {6, range_adjacent, BOOLOID, 2, {type, type}}, \
            {7, range_union, type, 2, {type, type}}, \
            {8, range_intersect, type, 2, {type, type}}, \
            {9, range_minus, type, 2, {type, type}}, \
            {10, range_merge, type, 2, {type, type}}
        static const AnkusScalarFunction ankus_range_functions[] = {
            ANKUS_RANGE_FUNCTIONS(INT4RANGEOID, INT4OID),
            ANKUS_RANGE_FUNCTIONS(INT8RANGEOID, INT8OID),
            ANKUS_RANGE_FUNCTIONS(NUMRANGEOID, NUMERICOID),
            ANKUS_RANGE_FUNCTIONS(DATERANGEOID, DATEOID),
            ANKUS_RANGE_FUNCTIONS(TSRANGEOID, TIMESTAMPOID),
            ANKUS_RANGE_FUNCTIONS(TSTZRANGEOID, TIMESTAMPTZOID)
        };
        #undef ANKUS_RANGE_FUNCTIONS

        static void
        ankus_range_operation(AnkusRequest *request, AnkusResult *result)
        {
            int operation = request->scalar_operation;
            Oid output = request->scalar_result_oid;
            if ((operation == 0 || operation == 2) && request->parameter_count == 1 &&
                !request->parameters[0].value.is_null)
            {
                const AnkusParameter *argument = &request->parameters[0];
                Oid function;
                Datum datum;
                if (operation == 0 && OidIsValid(ankus_range_subtype(output)) && argument->type_oid == TEXTOID)
                {
                    Oid parameter;
                    getTypeInputInfo(output, &function, &parameter);
                    datum = OidInputFunctionCall(function, TextDatumGetCString(ankus_parameter_datum(argument)), parameter, -1);
                    ankus_result_value(datum, output, &result->text);
                    return;
                }
                if (operation == 2 && output == TEXTOID && OidIsValid(ankus_range_subtype(argument->type_oid)))
                {
                    bool variable;
                    char *text;
                    getTypeOutputInfo(argument->type_oid, &function, &variable);
                    text = OidOutputFunctionCall(function, ankus_parameter_datum(argument));
                    datum = CStringGetTextDatum(text);
                    ankus_result_value(datum, TEXTOID, &result->text);
                    return;
                }
            }
            ankus_call_scalar(ankus_range_functions, lengthof(ankus_range_functions), request, result);
        }

        """;
}
