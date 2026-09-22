namespace Ankus.Generators;

/// <summary>Invokes PostgreSQL's numeric arithmetic and typmod routines through the native guard.</summary>
internal static class NativeNumericOperations
{
    /// <summary>Gets numeric operation signatures and native adapters.</summary>
    internal const string Source = """
        #include "utils/array.h"

        enum AnkusNumericOperation
        {
            ANKUS_NUM_PARSE, ANKUS_NUM_ADD, ANKUS_NUM_SUBTRACT, ANKUS_NUM_MULTIPLY, ANKUS_NUM_DIVIDE,
            ANKUS_NUM_REMAINDER, ANKUS_NUM_NEGATE, ANKUS_NUM_ABS, ANKUS_NUM_ROUND, ANKUS_NUM_TRUNCATE,
            ANKUS_NUM_CEILING, ANKUS_NUM_FLOOR, ANKUS_NUM_SQRT, ANKUS_NUM_EXP, ANKUS_NUM_LOG,
            ANKUS_NUM_LOG_BASE, ANKUS_NUM_POWER, ANKUS_NUM_GCD, ANKUS_NUM_LCM,
            ANKUS_NUM_FROM_DOUBLE, ANKUS_NUM_TO_DOUBLE, ANKUS_NUM_RESCALE,
            ANKUS_NUM_FROM_SINGLE, ANKUS_NUM_TO_SINGLE, ANKUS_NUM_TO_INT16, ANKUS_NUM_TO_INT32, ANKUS_NUM_TO_INT64
        };

        static Datum
        ankus_numeric_parse(PG_FUNCTION_ARGS)
        {
            return DirectFunctionCall3(numeric_in, CStringGetDatum(TextDatumGetCString(PG_GETARG_DATUM(0))),
                ObjectIdGetDatum(InvalidOid), Int32GetDatum(-1));
        }

        static Datum
        ankus_numeric_rescale(PG_FUNCTION_ARGS)
        {
            Datum parts[2] = {DirectFunctionCall1(int4out, PG_GETARG_DATUM(1)),
                DirectFunctionCall1(int4out, PG_GETARG_DATUM(2))};
            ArrayType *array = construct_array(parts, 2, CSTRINGOID, -2, false, TYPALIGN_CHAR);
            Datum typmod = DirectFunctionCall1(numerictypmodin, PointerGetDatum(array));
            return DirectFunctionCall2(numeric, PG_GETARG_DATUM(0), typmod);
        }

        static const AnkusScalarFunction ankus_numeric_functions[] =
        {
            {ANKUS_NUM_PARSE, ankus_numeric_parse, NUMERICOID, 1, {TEXTOID}},
            {ANKUS_NUM_ADD, numeric_add, NUMERICOID, 2, {NUMERICOID, NUMERICOID}},
            {ANKUS_NUM_SUBTRACT, numeric_sub, NUMERICOID, 2, {NUMERICOID, NUMERICOID}},
            {ANKUS_NUM_MULTIPLY, numeric_mul, NUMERICOID, 2, {NUMERICOID, NUMERICOID}},
            {ANKUS_NUM_DIVIDE, numeric_div, NUMERICOID, 2, {NUMERICOID, NUMERICOID}},
            {ANKUS_NUM_REMAINDER, numeric_mod, NUMERICOID, 2, {NUMERICOID, NUMERICOID}},
            {ANKUS_NUM_NEGATE, numeric_uminus, NUMERICOID, 1, {NUMERICOID}},
            {ANKUS_NUM_ABS, numeric_abs, NUMERICOID, 1, {NUMERICOID}},
            {ANKUS_NUM_ROUND, numeric_round, NUMERICOID, 2, {NUMERICOID, INT4OID}},
            {ANKUS_NUM_TRUNCATE, numeric_trunc, NUMERICOID, 2, {NUMERICOID, INT4OID}},
            {ANKUS_NUM_CEILING, numeric_ceil, NUMERICOID, 1, {NUMERICOID}},
            {ANKUS_NUM_FLOOR, numeric_floor, NUMERICOID, 1, {NUMERICOID}},
            {ANKUS_NUM_SQRT, numeric_sqrt, NUMERICOID, 1, {NUMERICOID}},
            {ANKUS_NUM_EXP, numeric_exp, NUMERICOID, 1, {NUMERICOID}},
            {ANKUS_NUM_LOG, numeric_ln, NUMERICOID, 1, {NUMERICOID}},
            {ANKUS_NUM_LOG_BASE, numeric_log, NUMERICOID, 2, {NUMERICOID, NUMERICOID}},
            {ANKUS_NUM_POWER, numeric_power, NUMERICOID, 2, {NUMERICOID, NUMERICOID}},
            {ANKUS_NUM_GCD, numeric_gcd, NUMERICOID, 2, {NUMERICOID, NUMERICOID}},
            {ANKUS_NUM_LCM, numeric_lcm, NUMERICOID, 2, {NUMERICOID, NUMERICOID}},
            {ANKUS_NUM_FROM_DOUBLE, float8_numeric, NUMERICOID, 1, {FLOAT8OID}},
            {ANKUS_NUM_TO_DOUBLE, numeric_float8, FLOAT8OID, 1, {NUMERICOID}},
            {ANKUS_NUM_RESCALE, ankus_numeric_rescale, NUMERICOID, 3, {NUMERICOID, INT4OID, INT4OID}},
            {ANKUS_NUM_FROM_SINGLE, float4_numeric, NUMERICOID, 1, {FLOAT4OID}},
            {ANKUS_NUM_TO_SINGLE, numeric_float4, FLOAT4OID, 1, {NUMERICOID}},
            {ANKUS_NUM_TO_INT16, numeric_int2, INT2OID, 1, {NUMERICOID}},
            {ANKUS_NUM_TO_INT32, numeric_int4, INT4OID, 1, {NUMERICOID}},
            {ANKUS_NUM_TO_INT64, numeric_int8, INT8OID, 1, {NUMERICOID}}
        };

        static void
        ankus_numeric_operation(AnkusRequest *request, AnkusResult *result)
        {
            ankus_call_scalar(ankus_numeric_functions, lengthof(ankus_numeric_functions), request, result);
        }
        """;
}
