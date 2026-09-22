namespace Ankus.Generators;

/// <summary>
/// Calls allowlisted PostgreSQL temporal routines entirely within the native guard and its disposable memory context.
/// </summary>
internal static class NativeTemporalOperations
{
    /// <summary>
    /// Gets the typed native function table and invocation code.
    /// </summary>
    internal const string Source = """
        #include "utils/fmgrprotos.h"
        #include "utils/json.h"

        enum AnkusTemporalOperation
        {
            ANKUS_TEMP_PARSE, ANKUS_TEMP_FORMAT, ANKUS_TEMP_ISO, ANKUS_TEMP_ADD, ANKUS_TEMP_SUBTRACT,
            ANKUS_TEMP_MULTIPLY, ANKUS_TEMP_DIVIDE, ANKUS_TEMP_NEGATE, ANKUS_TEMP_TRUNCATE, ANKUS_TEMP_AGE,
            ANKUS_TEMP_ZONE, ANKUS_TEMP_PART, ANKUS_TEMP_JUSTIFY_DAYS, ANKUS_TEMP_JUSTIFY_HOURS,
            ANKUS_TEMP_JUSTIFY, ANKUS_TEMP_COMPARE, ANKUS_TEMP_TRANSACTION, ANKUS_TEMP_STATEMENT,
            ANKUS_TEMP_CLOCK, ANKUS_TEMP_FROM_UNIX, ANKUS_TEMP_TO_DATE, ANKUS_TEMP_TO_TIME,
            ANKUS_TEMP_TO_TIMESTAMP, ANKUS_TEMP_TO_TIMESTAMPTZ, ANKUS_TEMP_MAKE_DATE, ANKUS_TEMP_MAKE_TIME
        };

        typedef struct AnkusTemporalFunction
        {
            int operation;
            PGFunction function;
            Oid result_type;
            int argument_count;
            Oid argument_types[3];
        } AnkusTemporalFunction;

        static Datum
        ankus_date_part(PG_FUNCTION_ARGS)
        {
        #if PG_VERSION_NUM >= 140000
            Datum numeric = extract_date(fcinfo);
            if (fcinfo->isnull)
                return (Datum) 0;
            return DirectFunctionCall1(numeric_float8, numeric);
        #else
            fcinfo->args[1].value = DirectFunctionCall1(date_timestamp, PG_GETARG_DATUM(1));
            return timestamp_part(fcinfo);
        #endif
        }

        static const AnkusTemporalFunction ankus_temporal_functions[] =
        {
            {ANKUS_TEMP_ADD, date_pli, DATEOID, 2, {DATEOID, INT4OID}},
            {ANKUS_TEMP_SUBTRACT, date_mii, DATEOID, 2, {DATEOID, INT4OID}},
            {ANKUS_TEMP_SUBTRACT, date_mi, INT4OID, 2, {DATEOID, DATEOID}},
            {ANKUS_TEMP_ADD, date_pl_interval, TIMESTAMPOID, 2, {DATEOID, INTERVALOID}},
            {ANKUS_TEMP_SUBTRACT, date_mi_interval, TIMESTAMPOID, 2, {DATEOID, INTERVALOID}},
            {ANKUS_TEMP_ADD, datetime_timestamp, TIMESTAMPOID, 2, {DATEOID, TIMEOID}},
            {ANKUS_TEMP_ADD, datetimetz_timestamptz, TIMESTAMPTZOID, 2, {DATEOID, TIMETZOID}},
            {ANKUS_TEMP_ADD, time_pl_interval, TIMEOID, 2, {TIMEOID, INTERVALOID}},
            {ANKUS_TEMP_SUBTRACT, time_mi_interval, TIMEOID, 2, {TIMEOID, INTERVALOID}},
            {ANKUS_TEMP_SUBTRACT, time_mi_time, INTERVALOID, 2, {TIMEOID, TIMEOID}},
            {ANKUS_TEMP_ADD, timetz_pl_interval, TIMETZOID, 2, {TIMETZOID, INTERVALOID}},
            {ANKUS_TEMP_SUBTRACT, timetz_mi_interval, TIMETZOID, 2, {TIMETZOID, INTERVALOID}},
            {ANKUS_TEMP_ADD, timestamp_pl_interval, TIMESTAMPOID, 2, {TIMESTAMPOID, INTERVALOID}},
            {ANKUS_TEMP_SUBTRACT, timestamp_mi_interval, TIMESTAMPOID, 2, {TIMESTAMPOID, INTERVALOID}},
            {ANKUS_TEMP_SUBTRACT, timestamp_mi, INTERVALOID, 2, {TIMESTAMPOID, TIMESTAMPOID}},
            {ANKUS_TEMP_ADD, timestamptz_pl_interval, TIMESTAMPTZOID, 2, {TIMESTAMPTZOID, INTERVALOID}},
            {ANKUS_TEMP_SUBTRACT, timestamptz_mi_interval, TIMESTAMPTZOID, 2, {TIMESTAMPTZOID, INTERVALOID}},
            {ANKUS_TEMP_SUBTRACT, timestamp_mi, INTERVALOID, 2, {TIMESTAMPTZOID, TIMESTAMPTZOID}},
            {ANKUS_TEMP_ADD, interval_pl, INTERVALOID, 2, {INTERVALOID, INTERVALOID}},
            {ANKUS_TEMP_SUBTRACT, interval_mi, INTERVALOID, 2, {INTERVALOID, INTERVALOID}},
            {ANKUS_TEMP_MULTIPLY, interval_mul, INTERVALOID, 2, {INTERVALOID, FLOAT8OID}},
            {ANKUS_TEMP_DIVIDE, interval_div, INTERVALOID, 2, {INTERVALOID, FLOAT8OID}},
            {ANKUS_TEMP_NEGATE, interval_um, INTERVALOID, 1, {INTERVALOID}},
            {ANKUS_TEMP_TRUNCATE, timestamp_trunc, TIMESTAMPOID, 2, {TEXTOID, TIMESTAMPOID}},
            {ANKUS_TEMP_TRUNCATE, timestamptz_trunc, TIMESTAMPTZOID, 2, {TEXTOID, TIMESTAMPTZOID}},
            {ANKUS_TEMP_TRUNCATE, timestamptz_trunc_zone, TIMESTAMPTZOID, 3, {TEXTOID, TIMESTAMPTZOID, TEXTOID}},
            {ANKUS_TEMP_TRUNCATE, interval_trunc, INTERVALOID, 2, {TEXTOID, INTERVALOID}},
            {ANKUS_TEMP_AGE, timestamp_age, INTERVALOID, 2, {TIMESTAMPOID, TIMESTAMPOID}},
            {ANKUS_TEMP_AGE, timestamptz_age, INTERVALOID, 2, {TIMESTAMPTZOID, TIMESTAMPTZOID}},
            {ANKUS_TEMP_ZONE, timestamp_zone, TIMESTAMPTZOID, 2, {TEXTOID, TIMESTAMPOID}},
            {ANKUS_TEMP_ZONE, timestamptz_zone, TIMESTAMPOID, 2, {TEXTOID, TIMESTAMPTZOID}},
            {ANKUS_TEMP_ZONE, timetz_zone, TIMETZOID, 2, {TEXTOID, TIMETZOID}},
            {ANKUS_TEMP_PART, timestamp_part, FLOAT8OID, 2, {TEXTOID, TIMESTAMPOID}},
            {ANKUS_TEMP_PART, ankus_date_part, FLOAT8OID, 2, {TEXTOID, DATEOID}},
            {ANKUS_TEMP_PART, timestamptz_part, FLOAT8OID, 2, {TEXTOID, TIMESTAMPTZOID}},
            {ANKUS_TEMP_PART, time_part, FLOAT8OID, 2, {TEXTOID, TIMEOID}},
            {ANKUS_TEMP_PART, timetz_part, FLOAT8OID, 2, {TEXTOID, TIMETZOID}},
            {ANKUS_TEMP_PART, interval_part, FLOAT8OID, 2, {TEXTOID, INTERVALOID}},
            {ANKUS_TEMP_JUSTIFY_DAYS, interval_justify_days, INTERVALOID, 1, {INTERVALOID}},
            {ANKUS_TEMP_JUSTIFY_HOURS, interval_justify_hours, INTERVALOID, 1, {INTERVALOID}},
            {ANKUS_TEMP_JUSTIFY, interval_justify_interval, INTERVALOID, 1, {INTERVALOID}},
            {ANKUS_TEMP_COMPARE, interval_cmp, INT4OID, 2, {INTERVALOID, INTERVALOID}},
            {ANKUS_TEMP_TRANSACTION, now, TIMESTAMPTZOID, 0, {0}},
            {ANKUS_TEMP_STATEMENT, statement_timestamp, TIMESTAMPTZOID, 0, {0}},
            {ANKUS_TEMP_CLOCK, clock_timestamp, TIMESTAMPTZOID, 0, {0}},
            {ANKUS_TEMP_FROM_UNIX, float8_timestamptz, TIMESTAMPTZOID, 1, {FLOAT8OID}},
            {ANKUS_TEMP_TO_DATE, timestamp_date, DATEOID, 1, {TIMESTAMPOID}},
            {ANKUS_TEMP_TO_DATE, timestamptz_date, DATEOID, 1, {TIMESTAMPTZOID}},
            {ANKUS_TEMP_TO_TIME, timestamp_time, TIMEOID, 1, {TIMESTAMPOID}},
            {ANKUS_TEMP_TO_TIME, timestamptz_time, TIMEOID, 1, {TIMESTAMPTZOID}},
            {ANKUS_TEMP_TO_TIME, timetz_time, TIMEOID, 1, {TIMETZOID}},
            {ANKUS_TEMP_TO_TIMESTAMP, date_timestamp, TIMESTAMPOID, 1, {DATEOID}},
            {ANKUS_TEMP_TO_TIMESTAMP, timestamptz_timestamp, TIMESTAMPOID, 1, {TIMESTAMPTZOID}},
            {ANKUS_TEMP_TO_TIMESTAMPTZ, date_timestamptz, TIMESTAMPTZOID, 1, {DATEOID}},
            {ANKUS_TEMP_TO_TIMESTAMPTZ, timestamp_timestamptz, TIMESTAMPTZOID, 1, {TIMESTAMPOID}},
            {ANKUS_TEMP_MAKE_DATE, make_date, DATEOID, 3, {INT4OID, INT4OID, INT4OID}},
            {ANKUS_TEMP_MAKE_TIME, make_time, TIMEOID, 3, {INT4OID, INT4OID, FLOAT8OID}}
        };

        static bool
        ankus_is_temporal_type(Oid type)
        {
            return type == DATEOID || type == TIMEOID || type == TIMETZOID ||
                type == TIMESTAMPOID || type == TIMESTAMPTZOID || type == INTERVALOID;
        }

        static void
        ankus_temporal_operation(AnkusRequest *request, AnkusResult *result)
        {
            int operation = request->temporal_operation;
            Oid output = request->temporal_result_oid;
            if (operation == ANKUS_TEMP_PARSE || operation == ANKUS_TEMP_FORMAT || operation == ANKUS_TEMP_ISO)
            {
                Oid function;
                Oid io_parameter;
                bool variable;
                Datum datum;
                const AnkusParameter *argument;
                if (request->parameter_count != 1 || request->parameters[0].value.is_null)
                    ereport(ERROR, (errmsg("temporal text conversion requires one non-null argument")));
                argument = &request->parameters[0];
                if (operation == ANKUS_TEMP_PARSE)
                {
                    char *input;
                    if (argument->type_oid != TEXTOID || !ankus_is_temporal_type(output))
                        ereport(ERROR, (errmsg("invalid temporal parse signature")));
                    input = TextDatumGetCString(ankus_parameter_datum(argument));
                    getTypeInputInfo(output, &function, &io_parameter);
                    datum = OidInputFunctionCall(function, input, io_parameter, -1);
                }
                else
                {
                    char *text;
                    if (!ankus_is_temporal_type(argument->type_oid) || output != TEXTOID)
                        ereport(ERROR, (errmsg("invalid temporal format signature")));
                    datum = ankus_parameter_datum(argument);
                    if (operation == ANKUS_TEMP_ISO && argument->type_oid != INTERVALOID)
                        text = JsonEncodeDateTime(NULL, datum, argument->type_oid, NULL);
                    else
                    {
                        getTypeOutputInfo(argument->type_oid, &function, &variable);
                        text = OidOutputFunctionCall(function, datum);
                    }
                    datum = CStringGetTextDatum(text);
                }
                ankus_result_value(datum, output, &result->text);
                return;
            }

            for (Size index = 0; index < lengthof(ankus_temporal_functions); index++)
            {
                const AnkusTemporalFunction *entry = &ankus_temporal_functions[index];
                bool match = entry->operation == operation && entry->result_type == output &&
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
                        ankus_result_value(datum, output, &result->text);
                    return;
                }
            }
            ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED), errmsg("unsupported temporal operation signature")));
        }
        """;
}
