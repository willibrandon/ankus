namespace Ankus.Generators;

/// <summary>
/// Parses geometric input with allowlisted PostgreSQL functions inside guarded subtransactions.
/// </summary>
internal static class NativeGeometryOperations
{
    /// <summary>
    /// Gets geometric text adapters and their exact scalar signatures.
    /// </summary>
    internal const string Source = """
        #define ANKUS_GEOMETRY_PARSE(name, input) \
            static Datum name(PG_FUNCTION_ARGS) { \
                return DirectFunctionCall1(input, CStringGetDatum(TextDatumGetCString(PG_GETARG_DATUM(0)))); \
            }
        ANKUS_GEOMETRY_PARSE(ankus_point_parse, point_in)
        ANKUS_GEOMETRY_PARSE(ankus_lseg_parse, lseg_in)
        ANKUS_GEOMETRY_PARSE(ankus_line_parse, line_in)
        ANKUS_GEOMETRY_PARSE(ankus_box_parse, box_in)
        ANKUS_GEOMETRY_PARSE(ankus_circle_parse, circle_in)
        ANKUS_GEOMETRY_PARSE(ankus_path_parse, path_in)
        ANKUS_GEOMETRY_PARSE(ankus_polygon_parse, poly_in)
        #undef ANKUS_GEOMETRY_PARSE

        static const AnkusScalarFunction ankus_geometry_functions[] = {
            {0, ankus_point_parse, POINTOID, 1, {TEXTOID}},
            {0, ankus_lseg_parse, LSEGOID, 1, {TEXTOID}},
            {0, ankus_line_parse, LINEOID, 1, {TEXTOID}},
            {0, ankus_box_parse, BOXOID, 1, {TEXTOID}},
            {0, ankus_circle_parse, CIRCLEOID, 1, {TEXTOID}},
            {0, ankus_path_parse, PATHOID, 1, {TEXTOID}},
            {0, ankus_polygon_parse, POLYGONOID, 1, {TEXTOID}}
        };

        static void
        ankus_geometry_operation(AnkusRequest *request, AnkusResult *result)
        {
            ankus_call_scalar(ankus_geometry_functions, lengthof(ankus_geometry_functions), request, result);
        }

        """;
}
