namespace Ankus.Generators;

/// <summary>
/// Converts PostgreSQL arrays to a pointer-free transport using the scalar datum converters.
/// </summary>
internal static class NativeArrayBridge
{
    /// <summary>
    /// Gets the native array readers and writers, called only behind the PostgreSQL error boundary.
    /// </summary>
    internal const string Source = """
        #include "utils/array.h"
        #include "libpq/pqformat.h"
        #include "miscadmin.h"

        static bool
        ankus_array_element_supported(Oid type)
        {
            switch (type)
            {
                case BOOLOID: case BYTEAOID: case CHAROID: case INT2OID: case INT4OID: case INT8OID:
                case OIDOID: case FLOAT4OID: case FLOAT8OID: case TEXTOID: case VARCHAROID: case BPCHAROID:
                case UUIDOID: case JSONOID: case JSONBOID: case NUMERICOID: case DATEOID: case TIMEOID:
                case TIMETZOID: case TIMESTAMPOID: case TIMESTAMPTZOID: case INTERVALOID:
                case INETOID: case CIDROID:
                    return true;
                default:
                    return false;
            }
        }

        static void
        ankus_read_array(Datum datum, AnkusValue *value, AnkusInputBuffer *owned)
        {
            ArrayType *array = DatumGetArrayTypeP(datum);
            Oid type = ARR_ELEMTYPE(array);
            Oid base_type = getBaseType(type);
            int16 length;
            bool by_value;
            char alignment;
            Datum *elements;
            bool *nulls;
            int count;
            int rank = ARR_NDIM(array);
            StringInfoData buffer;
            if (!ankus_array_element_supported(base_type))
            {
                ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED),
                    errmsg("Array element type OID %u has no Ankus conversion", type)));
            }
            if ((Pointer) array != DatumGetPointer(datum))
            {
                owned->detoasted = (struct varlena *) array;
            }
            get_typlenbyvalalign(type, &length, &by_value, &alignment);
            deconstruct_array(array, type, length, by_value, alignment, &elements, &nulls, &count);
            initStringInfo(&buffer);
            pq_sendint32(&buffer, rank);
            pq_sendint32(&buffer, count);
            pq_sendint32(&buffer, base_type);
            for (int index = 0; index < rank; index++)
            {
                pq_sendint32(&buffer, ARR_DIMS(array)[index]);
                pq_sendint32(&buffer, ARR_LBOUND(array)[index]);
            }
            for (int index = 0; index < count; index++)
            {
                AnkusValue item = {0};
                AnkusInputBuffer item_owned = {0};
                CHECK_FOR_INTERRUPTS();
                item.is_null = nulls[index];
                if (!item.is_null)
                {
                    ankus_read_value(elements[index], base_type, &item, &item_owned);
                }
                pq_sendint64(&buffer, item.integral);
                pq_sendint32(&buffer, item.auxiliary1);
                pq_sendint32(&buffer, item.auxiliary2);
                pq_sendint32(&buffer, item.temporal_infinity);
                pq_sendint32(&buffer, item.is_null);
                pq_sendint32(&buffer, item.length);
                if (item.length != 0)
                {
                    pq_sendbytes(&buffer, (char *) item.data, item.length);
                }
                ankus_free_input(&item_owned);
            }
            if (elements != NULL)
            {
                pfree(elements);
            }
            if (nulls != NULL)
            {
                pfree(nulls);
            }
            owned->serialized = buffer.data;
            value->data = (unsigned char *) buffer.data;
            value->length = buffer.len;
            value->auxiliary1 = -1;
        }

        static Datum
        ankus_write_array(const AnkusValue *value, Oid element_type)
        {
            StringInfoData buffer = {(char *) value->data, value->length, value->length, 0};
            int rank;
            int count;
            int lengths[MAXDIM];
            int bounds[MAXDIM];
            Datum *elements;
            bool *nulls;
            int16 length;
            bool by_value;
            char alignment;
            ArrayType *array;
            if (value->auxiliary1 != -1 || value->length < 12 || value->data == NULL)
            {
                ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Invalid Ankus array header")));
            }
            rank = pq_getmsgint(&buffer, 4);
            count = pq_getmsgint(&buffer, 4);
            if (rank < 0 || rank > MAXDIM || count < 0 ||
                (Oid) pq_getmsgint(&buffer, 4) != element_type || !ankus_array_element_supported(element_type) ||
                (int64) 12 + rank * 8 + (int64) count * 28 > value->length)
            {
                ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Invalid Ankus array shape or element type")));
            }
            for (int index = 0; index < rank; index++)
            {
                lengths[index] = pq_getmsgint(&buffer, 4);
                bounds[index] = pq_getmsgint(&buffer, 4);
            }
            if (ArrayGetNItems(rank, lengths) != count || (count == 0 && rank != 0))
            {
                ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Inconsistent Ankus array dimensions")));
            }
            ArrayCheckBounds(rank, lengths, bounds);
            get_typlenbyvalalign(element_type, &length, &by_value, &alignment);
            elements = palloc(sizeof(Datum) * (Size) Max(count, 1));
            nulls = palloc(sizeof(bool) * (Size) Max(count, 1));
            for (int index = 0; index < count; index++)
            {
                AnkusParameter parameter = {0};
                int is_null;
                const char *data;
                char *terminated = NULL;
                CHECK_FOR_INTERRUPTS();
                parameter.type_oid = element_type;
                parameter.value.integral = pq_getmsgint64(&buffer);
                parameter.value.auxiliary1 = pq_getmsgint(&buffer, 4);
                parameter.value.auxiliary2 = pq_getmsgint(&buffer, 4);
                parameter.value.temporal_infinity = pq_getmsgint(&buffer, 4);
                is_null = pq_getmsgint(&buffer, 4);
                parameter.value.length = pq_getmsgint(&buffer, 4);
                if (is_null < 0 || is_null > 1 || parameter.value.length < 0 ||
                    (is_null && parameter.value.length != 0))
                {
                    ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Invalid Ankus array element")));
                }
                nulls[index] = is_null;
                parameter.value.is_null = is_null;
                data = pq_getmsgbytes(&buffer, parameter.value.length);
                /* These scalar input routines consume C strings rather than length-delimited buffers. */
                if (!is_null && (element_type == JSONOID || element_type == JSONBOID || element_type == NUMERICOID))
                {
                    terminated = pnstrdup(data, parameter.value.length);
                    data = terminated;
                }
                parameter.value.data = (unsigned char *) data;
                elements[index] = ankus_parameter_datum(&parameter);
                if (terminated != NULL)
                {
                    pfree(terminated);
                }
            }
            pq_getmsgend(&buffer);
            array = construct_md_array(elements, nulls, rank, lengths, bounds, element_type, length, by_value, alignment);
            for (int index = 0; index < count; index++)
            {
                if (!by_value && !nulls[index])
                {
                    pfree(DatumGetPointer(elements[index]));
                }
            }
            pfree(elements);
            pfree(nulls);
            return PointerGetDatum(array);
        }

        """;
}
