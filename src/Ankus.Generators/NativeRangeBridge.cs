namespace Ankus.Generators;

/// <summary>
/// Serializes built-in range bounds through the existing scalar ABI and PostgreSQL range type cache.
/// </summary>
internal static class NativeRangeBridge
{
    /// <summary>
    /// Gets pointer-free range transport, detoast ownership and version-aware canonical construction.
    /// </summary>
    internal const string Source = """
        #include "utils/rangetypes.h"
        #include "utils/typcache.h"

        static Oid
        ankus_range_subtype(Oid type)
        {
            switch (type)
            {
                case INT4RANGEOID: return INT4OID;
                case INT8RANGEOID: return INT8OID;
                case NUMRANGEOID: return NUMERICOID;
                case DATERANGEOID: return DATEOID;
                case TSRANGEOID: return TIMESTAMPOID;
                case TSTZRANGEOID: return TIMESTAMPTZOID;
                default: return InvalidOid;
            }
        }

        static void
        ankus_send_range_bound(StringInfo buffer, Datum datum, Oid subtype)
        {
            AnkusValue bound = {0};
            AnkusInputBuffer owned = {0};
            ankus_read_value(datum, subtype, &bound, &owned);
            pq_sendint64(buffer, bound.integral);
            pq_sendint32(buffer, bound.auxiliary1);
            pq_sendint32(buffer, bound.auxiliary2);
            pq_sendint32(buffer, bound.temporal_infinity);
            pq_sendint32(buffer, 0);
            pq_sendint32(buffer, bound.length);
            if (bound.length != 0)
                pq_sendbytes(buffer, (char *) bound.data, bound.length);
            ankus_free_input(&owned);
        }

        static void
        ankus_read_range(Datum datum, AnkusValue *value, AnkusInputBuffer *owned)
        {
            RangeType *range = DatumGetRangeTypeP(datum);
            Oid type = RangeTypeGetOid(range);
            Oid subtype = ankus_range_subtype(type);
            TypeCacheEntry *cache;
            RangeBound lower, upper;
            bool empty;
            int flags;
            StringInfoData buffer;
            if (!OidIsValid(subtype))
                ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED), errmsg("Unsupported range type OID %u", type)));
            if ((Pointer) range != DatumGetPointer(datum))
                owned->detoasted = (struct varlena *) range;
            cache = lookup_type_cache(type, TYPECACHE_RANGE_INFO);
            range_deserialize(cache, range, &lower, &upper, &empty);
            flags = empty ? 1 : (lower.infinite ? 8 : lower.inclusive ? 2 : 0) |
                (upper.infinite ? 16 : upper.inclusive ? 4 : 0);
            initStringInfo(&buffer);
            pq_sendint32(&buffer, type);
            pq_sendint32(&buffer, flags);
            if (!empty && !lower.infinite)
                ankus_send_range_bound(&buffer, lower.val, subtype);
            if (!empty && !upper.infinite)
                ankus_send_range_bound(&buffer, upper.val, subtype);
            owned->serialized = buffer.data;
            value->data = (unsigned char *) buffer.data;
            value->length = buffer.len;
            value->auxiliary1 = -2;
        }

        static Datum
        ankus_receive_range_bound(StringInfo buffer, Oid subtype)
        {
            AnkusParameter parameter = {0};
            const char *data;
            char *terminated = NULL;
            Datum result;
            parameter.type_oid = subtype;
            parameter.value.integral = pq_getmsgint64(buffer);
            if (subtype == INT4OID && (parameter.value.integral < PG_INT32_MIN || parameter.value.integral > PG_INT32_MAX))
                ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Integer range bound out of range")));
            parameter.value.auxiliary1 = pq_getmsgint(buffer, 4);
            parameter.value.auxiliary2 = pq_getmsgint(buffer, 4);
            parameter.value.temporal_infinity = pq_getmsgint(buffer, 4);
            if (pq_getmsgint(buffer, 4) != 0)
                ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Range bound cannot be NULL")));
            parameter.value.length = pq_getmsgint(buffer, 4);
            if (parameter.value.length < 0 || (subtype != NUMERICOID && parameter.value.length != 0))
                ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Invalid range bound length")));
            data = pq_getmsgbytes(buffer, parameter.value.length);
            if (subtype == NUMERICOID)
            {
                if (memchr(data, 0, parameter.value.length) != NULL)
                    ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("NUL in numeric range bound")));
                terminated = pnstrdup(data, parameter.value.length);
                data = terminated;
            }

            parameter.value.data = (unsigned char *) data;
            result = ankus_parameter_datum(&parameter);
            if (terminated != NULL)
                pfree(terminated);
            return result;
        }

        static Datum
        ankus_write_range(const AnkusValue *value, Oid type)
        {
            StringInfoData buffer = {(char *) value->data, value->length, value->length, 0};
            Oid subtype = ankus_range_subtype(type);
            TypeCacheEntry *cache;
            RangeBound lower = {0}, upper = {0};
            RangeType *range;
            int flags;
            bool empty;
            if (!OidIsValid(subtype) || value->auxiliary1 != -2 || value->length < 8 || value->data == NULL ||
                (Oid) pq_getmsgint(&buffer, 4) != type)
                ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Invalid Ankus range header")));
            flags = pq_getmsgint(&buffer, 4);
            if ((flags & ~31) != 0 || ((flags & 1) != 0 && flags != 1) ||
                (flags & 10) == 10 || (flags & 20) == 20)
                ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Invalid Ankus range flags")));
            empty = flags == 1;
            lower.lower = true;
            lower.infinite = (flags & 8) != 0;
            lower.inclusive = (flags & 2) != 0;
            upper.lower = false;
            upper.infinite = (flags & 16) != 0;
            upper.inclusive = (flags & 4) != 0;
            if (!empty && !lower.infinite)
                lower.val = ankus_receive_range_bound(&buffer, subtype);
            if (!empty && !upper.infinite)
                upper.val = ankus_receive_range_bound(&buffer, subtype);
            pq_getmsgend(&buffer);
            cache = lookup_type_cache(type, TYPECACHE_RANGE_INFO);
        #if PG_VERSION_NUM >= 160000
            range = make_range(cache, &lower, &upper, empty, NULL);
        #else
            range = make_range(cache, &lower, &upper, empty);
        #endif
            if (!cache->rngelemtype->typbyval)
            {
                if (!empty && !lower.infinite)
                    pfree(DatumGetPointer(lower.val));
                if (!empty && !upper.infinite)
                    pfree(DatumGetPointer(upper.val));
            }

            return RangeTypePGetDatum(range);
        }

        """;
}
