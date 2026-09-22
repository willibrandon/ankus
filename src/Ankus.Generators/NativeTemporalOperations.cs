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
        #include "utils/datetime.h"
        #include "miscadmin.h"

        enum AnkusTemporalOperation
        {
            ANKUS_TEMP_PARSE, ANKUS_TEMP_FORMAT, ANKUS_TEMP_ISO, ANKUS_TEMP_ADD, ANKUS_TEMP_SUBTRACT,
            ANKUS_TEMP_MULTIPLY, ANKUS_TEMP_DIVIDE, ANKUS_TEMP_NEGATE, ANKUS_TEMP_TRUNCATE, ANKUS_TEMP_AGE,
            ANKUS_TEMP_ZONE, ANKUS_TEMP_PART, ANKUS_TEMP_JUSTIFY_DAYS, ANKUS_TEMP_JUSTIFY_HOURS,
            ANKUS_TEMP_JUSTIFY, ANKUS_TEMP_COMPARE, ANKUS_TEMP_TRANSACTION, ANKUS_TEMP_STATEMENT,
            ANKUS_TEMP_CLOCK, ANKUS_TEMP_FROM_UNIX, ANKUS_TEMP_TO_DATE, ANKUS_TEMP_TO_TIME,
            ANKUS_TEMP_TO_TIMESTAMP, ANKUS_TEMP_TO_TIMESTAMPTZ, ANKUS_TEMP_MAKE_DATE, ANKUS_TEMP_MAKE_TIME,
            ANKUS_TEMP_EXTRACT, ANKUS_TEMP_MAKE_TIMESTAMP, ANKUS_TEMP_MAKE_TIMESTAMPTZ,
            ANKUS_TEMP_MAKE_INTERVAL, ANKUS_TEMP_TO_TIMETZ, ANKUS_TEMP_ROUND, ANKUS_TEMP_CURRENT_DATE,
            ANKUS_TEMP_CURRENT_TIME, ANKUS_TEMP_LOCAL_TIME, ANKUS_TEMP_CURRENT_TIMESTAMP,
            ANKUS_TEMP_LOCAL_TIMESTAMP, ANKUS_TEMP_ISO_ZONE
        };

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

        static Datum ankus_current_date(PG_FUNCTION_ARGS) { (void) fcinfo; return DateADTGetDatum(GetSQLCurrentDate()); }
        static Datum ankus_current_time(PG_FUNCTION_ARGS) { return TimeTzADTPGetDatum(GetSQLCurrentTime(PG_GETARG_INT32(0))); }
        static Datum ankus_local_time(PG_FUNCTION_ARGS) { return TimeADTGetDatum(GetSQLLocalTime(PG_GETARG_INT32(0))); }
        static Datum ankus_current_timestamp(PG_FUNCTION_ARGS) { return TimestampTzGetDatum(GetSQLCurrentTimestamp(PG_GETARG_INT32(0))); }
        static Datum ankus_local_timestamp(PG_FUNCTION_ARGS) { return TimestampGetDatum(GetSQLLocalTimestamp(PG_GETARG_INT32(0))); }

        static Datum
        ankus_timestamp_round(PG_FUNCTION_ARGS)
        {
            Datum result = timestamp_scale(fcinfo);
            Timestamp value = DatumGetTimestamp(result);
            /* Some server versions do not recheck the finite range after rounding. */
            if (!TIMESTAMP_NOT_FINITE(value) && !IS_VALID_TIMESTAMP(value))
                ereport(ERROR, (errcode(ERRCODE_DATETIME_VALUE_OUT_OF_RANGE), errmsg("timestamp out of range after rounding")));
            return result;
        }

        static Datum
        ankus_timestamp_iso_zone(PG_FUNCTION_ARGS)
        {
            TimestampTz instant = PG_GETARG_TIMESTAMPTZ(0);
            Datum local = DirectFunctionCall2(timestamptz_zone, PG_GETARG_DATUM(1), PG_GETARG_DATUM(0));
            struct pg_tm tm;
            fsec_t fraction;
            int offset;
            char buffer[MAXDATELEN + 1];
            if (TIMESTAMP_NOT_FINITE(instant))
                return CStringGetTextDatum(JsonEncodeDateTime(NULL, PG_GETARG_DATUM(0), TIMESTAMPTZOID, NULL));
            offset = (int) ((instant - DatumGetTimestamp(local)) / USECS_PER_SEC);
            if (timestamp2tm(DatumGetTimestamp(local), NULL, &tm, &fraction, NULL, NULL) != 0)
                ereport(ERROR, (errcode(ERRCODE_DATETIME_VALUE_OUT_OF_RANGE), errmsg("timestamp out of range")));
            tm.tm_isdst = 1;
            EncodeDateTime(&tm, fraction, true, offset, NULL, USE_XSD_DATES, buffer);
            return CStringGetTextDatum(buffer);
        }

        static const AnkusScalarFunction ankus_temporal_functions[] =
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
        #if PG_VERSION_NUM >= 140000
            {ANKUS_TEMP_EXTRACT, extract_date, NUMERICOID, 2, {TEXTOID, DATEOID}},
            {ANKUS_TEMP_EXTRACT, extract_time, NUMERICOID, 2, {TEXTOID, TIMEOID}},
            {ANKUS_TEMP_EXTRACT, extract_timetz, NUMERICOID, 2, {TEXTOID, TIMETZOID}},
            {ANKUS_TEMP_EXTRACT, extract_timestamp, NUMERICOID, 2, {TEXTOID, TIMESTAMPOID}},
            {ANKUS_TEMP_EXTRACT, extract_timestamptz, NUMERICOID, 2, {TEXTOID, TIMESTAMPTZOID}},
            {ANKUS_TEMP_EXTRACT, extract_interval, NUMERICOID, 2, {TEXTOID, INTERVALOID}},
        #endif
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
            {ANKUS_TEMP_MAKE_TIME, make_time, TIMEOID, 3, {INT4OID, INT4OID, FLOAT8OID}},
            {ANKUS_TEMP_MAKE_TIMESTAMP, make_timestamp, TIMESTAMPOID, 6, {INT4OID, INT4OID, INT4OID, INT4OID, INT4OID, FLOAT8OID}},
            {ANKUS_TEMP_MAKE_TIMESTAMPTZ, make_timestamptz, TIMESTAMPTZOID, 6, {INT4OID, INT4OID, INT4OID, INT4OID, INT4OID, FLOAT8OID}},
            {ANKUS_TEMP_MAKE_TIMESTAMPTZ, make_timestamptz_at_timezone, TIMESTAMPTZOID, 7, {INT4OID, INT4OID, INT4OID, INT4OID, INT4OID, FLOAT8OID, TEXTOID}},
            {ANKUS_TEMP_MAKE_INTERVAL, make_interval, INTERVALOID, 7, {INT4OID, INT4OID, INT4OID, INT4OID, INT4OID, INT4OID, FLOAT8OID}},
            {ANKUS_TEMP_TO_TIMETZ, time_timetz, TIMETZOID, 1, {TIMEOID}},
            {ANKUS_TEMP_TO_TIMETZ, timestamptz_timetz, TIMETZOID, 1, {TIMESTAMPTZOID}},
            {ANKUS_TEMP_ROUND, time_scale, TIMEOID, 2, {TIMEOID, INT4OID}},
            {ANKUS_TEMP_ROUND, timetz_scale, TIMETZOID, 2, {TIMETZOID, INT4OID}},
            {ANKUS_TEMP_ROUND, ankus_timestamp_round, TIMESTAMPOID, 2, {TIMESTAMPOID, INT4OID}},
            {ANKUS_TEMP_ROUND, ankus_timestamp_round, TIMESTAMPTZOID, 2, {TIMESTAMPTZOID, INT4OID}},
            {ANKUS_TEMP_CURRENT_DATE, ankus_current_date, DATEOID, 0, {0}},
            {ANKUS_TEMP_CURRENT_TIME, ankus_current_time, TIMETZOID, 1, {INT4OID}},
            {ANKUS_TEMP_LOCAL_TIME, ankus_local_time, TIMEOID, 1, {INT4OID}},
            {ANKUS_TEMP_CURRENT_TIMESTAMP, ankus_current_timestamp, TIMESTAMPTZOID, 1, {INT4OID}},
            {ANKUS_TEMP_LOCAL_TIMESTAMP, ankus_local_timestamp, TIMESTAMPOID, 1, {INT4OID}},
            {ANKUS_TEMP_ISO_ZONE, ankus_timestamp_iso_zone, TEXTOID, 2, {TIMESTAMPTZOID, TEXTOID}}
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
            int operation = request->scalar_operation;
            Oid output = request->scalar_result_oid;
        #if PG_VERSION_NUM < 140000
            if (operation == ANKUS_TEMP_EXTRACT && output == NUMERICOID)
            {
                AnkusResult floating = {0};
                double value;
                Datum numeric;
                AnkusRequest part = *request;
                part.scalar_operation = ANKUS_TEMP_PART;
                part.scalar_result_oid = FLOAT8OID;
                ankus_call_scalar(ankus_temporal_functions, lengthof(ankus_temporal_functions), &part, &floating);
                result->text.is_null = floating.text.is_null;
                if (floating.text.is_null)
                    return;
                memcpy(&value, &floating.text.integral, sizeof(value));
                numeric = DirectFunctionCall3(numeric_in,
                    DirectFunctionCall1(float8out, Float8GetDatum(value)), ObjectIdGetDatum(InvalidOid), Int32GetDatum(-1));
                ankus_result_value(numeric, NUMERICOID, &result->text);
                return;
            }
        #endif
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

            ankus_call_scalar(ankus_temporal_functions, lengthof(ankus_temporal_functions), request, result);
        }
        """;
}
