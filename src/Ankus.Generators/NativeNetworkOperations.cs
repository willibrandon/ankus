namespace Ankus.Generators;

/// <summary>
/// Parses network text inside the native subtransaction and error boundary.
/// </summary>
internal static class NativeNetworkOperations
{
    /// <summary>
    /// Gets allowlisted inet/cidr input adapters and their scalar signatures.
    /// </summary>
    internal const string Source = """
        static Datum
        ankus_inet_parse(PG_FUNCTION_ARGS)
        {
            return DirectFunctionCall1(inet_in, CStringGetDatum(TextDatumGetCString(PG_GETARG_DATUM(0))));
        }

        static Datum
        ankus_cidr_parse(PG_FUNCTION_ARGS)
        {
            return DirectFunctionCall1(cidr_in, CStringGetDatum(TextDatumGetCString(PG_GETARG_DATUM(0))));
        }

        static const AnkusScalarFunction ankus_network_functions[] = {
            {0, ankus_inet_parse, INETOID, 1, {TEXTOID}},
            {0, ankus_cidr_parse, CIDROID, 1, {TEXTOID}}
        };

        static void
        ankus_network_operation(AnkusRequest *request, AnkusResult *result)
        {
            ankus_call_scalar(ankus_network_functions, lengthof(ankus_network_functions), request, result);
        }

        """;
}
