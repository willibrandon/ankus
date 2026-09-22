namespace Ankus.Generators;

/// <summary>
/// Copies temporal fields through PostgreSQL's header-defined datum macros, avoiding native struct layout assumptions.
/// </summary>
internal static class NativeTemporalTypes
{
    /// <summary>
    /// Gets the temporal conversions shared by attributed functions and SPI.
    /// </summary>
    internal const string Source = """
        static void
        ankus_read_temporal(Datum datum, AnkusValue *value, Oid type)
        {
            switch (type)
            {
                case DATEOID: value->integral = DatumGetDateADT(datum); break;
                case TIMEOID: value->integral = DatumGetTimeADT(datum); break;
                case TIMESTAMPOID: value->integral = DatumGetTimestamp(datum); break;
                case TIMESTAMPTZOID: value->integral = DatumGetTimestampTz(datum); break;
                case TIMETZOID:
                {
                    TimeTzADT *time = DatumGetTimeTzADTP(datum);
                    value->integral = time->time;
                    value->auxiliary1 = -time->zone;
                    break;
                }

                case INTERVALOID:
                {
                    Interval *interval = DatumGetIntervalP(datum);
        #if PG_VERSION_NUM >= 170000
                    if (INTERVAL_NOT_FINITE(interval))
                    {
                        value->temporal_infinity = INTERVAL_IS_NOBEGIN(interval) ? -1 : 1;
                        break;
                    }

        #endif
                    value->integral = interval->time;
                    value->auxiliary1 = interval->day;
                    value->auxiliary2 = interval->month;
                    break;
                }
            }
        }

        static Datum
        ankus_write_temporal(const AnkusValue *value, Oid type)
        {
            switch (type)
            {
                case DATEOID:
                {
                    int64 days = value->integral;
                    if (days < PG_INT32_MIN || days > PG_INT32_MAX || (!DATE_NOT_FINITE(days) && !IS_VALID_DATE(days)))
                        ereport(ERROR, (errcode(ERRCODE_DATETIME_VALUE_OUT_OF_RANGE), errmsg("date out of range")));
                    return DateADTGetDatum((DateADT) days);
                }

                case TIMESTAMPOID:
                case TIMESTAMPTZOID:
                {
                    int64 timestamp = value->integral;
                    if (!TIMESTAMP_NOT_FINITE(timestamp) && !IS_VALID_TIMESTAMP(timestamp))
                        ereport(ERROR, (errcode(ERRCODE_DATETIME_VALUE_OUT_OF_RANGE), errmsg("timestamp out of range")));
                    return type == TIMESTAMPOID ? TimestampGetDatum(timestamp) : TimestampTzGetDatum(timestamp);
                }

                case TIMEOID:
                case TIMETZOID:
                {
                    TimeTzADT *time;
                    if (value->integral < 0 || value->integral > USECS_PER_DAY)
                        ereport(ERROR, (errcode(ERRCODE_DATETIME_VALUE_OUT_OF_RANGE), errmsg("time out of range")));
                    if (type == TIMEOID)
                        return TimeADTGetDatum(value->integral);
                    if (value->auxiliary1 <= -TZDISP_LIMIT || value->auxiliary1 >= TZDISP_LIMIT)
                        ereport(ERROR, (errcode(ERRCODE_INVALID_TIME_ZONE_DISPLACEMENT_VALUE), errmsg("time zone offset out of range")));
                    time = palloc0(sizeof(TimeTzADT));
                    time->time = value->integral;
                    time->zone = -value->auxiliary1;
                    return TimeTzADTPGetDatum(time);
                }

                case INTERVALOID:
                {
                    Interval *interval = palloc(sizeof(Interval));
                    if (value->temporal_infinity != 0)
                    {
        #if PG_VERSION_NUM >= 170000
                        if (value->temporal_infinity < 0)
                            INTERVAL_NOBEGIN(interval);
                        else
                            INTERVAL_NOEND(interval);
                        return IntervalPGetDatum(interval);
        #else
                        ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED), errmsg("interval infinity requires PostgreSQL 17")));
        #endif
                    }

                    interval->time = value->integral;
                    interval->day = value->auxiliary1;
                    interval->month = value->auxiliary2;
        #if PG_VERSION_NUM >= 170000
                    if (INTERVAL_NOT_FINITE(interval))
                        ereport(ERROR, (errcode(ERRCODE_DATETIME_VALUE_OUT_OF_RANGE),
                            errmsg("finite interval components collide with a PostgreSQL infinity sentinel")));
        #endif
                    return IntervalPGetDatum(interval);
                }

                default:
                    ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED), errmsg("unsupported temporal type")));
            }

            return (Datum) 0;
        }
        """;
}
