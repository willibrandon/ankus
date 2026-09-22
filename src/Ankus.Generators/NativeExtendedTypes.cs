namespace Ankus.Generators;

/// <summary>
/// Converts UUID, JSON/JSONB, and numeric buffers while keeping PostgreSQL parsing, detoasting, and allocation in native frames.
/// </summary>
internal static class NativeExtendedTypes
{
    /// <summary>
    /// Gets buffered datum conversion helpers shared by generated functions and SPI.
    /// </summary>
    internal const string Source = """
        #include "utils/fmgrprotos.h"

        static void
        ankus_read_typed_buffer(Datum datum, AnkusValue *value, AnkusInputBuffer *owned, Oid type)
        {
            if (type == UUIDOID)
            {
                value->data = DatumGetUUIDP(datum)->data;
                value->length = UUID_LEN;
            }
            else if (type == JSONBOID || type == NUMERICOID)
            {
                struct varlena *original = (struct varlena *) DatumGetPointer(datum);
                struct varlena *unpacked = pg_detoast_datum(original);
                char *converted;
                if (unpacked != original)
                {
                    owned->detoasted = unpacked;
                }
                owned->serialized = DatumGetCString(DirectFunctionCall1(type == JSONBOID ? jsonb_out : numeric_out,
                    PointerGetDatum(unpacked)));
                converted = pg_server_to_any(owned->serialized, strlen(owned->serialized), PG_UTF8);
                if (converted != owned->serialized)
                {
                    owned->converted = converted;
                }
                value->data = (unsigned char *) converted;
                value->length = strlen(converted);
            }
            else
            {
                ankus_read_buffer(datum, value, owned, type != BYTEAOID);
            }
        }

        static Datum
        ankus_write_typed_buffer(const AnkusValue *value, Oid type)
        {
            if (type == UUIDOID)
            {
                pg_uuid_t *uuid;
                if (value->length != UUID_LEN)
                {
                    ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Invalid UUID transport length")));
                }
                uuid = palloc(sizeof(pg_uuid_t));
                memcpy(uuid->data, value->data, UUID_LEN);
                return UUIDPGetDatum(uuid);
            }
            if (type == JSONOID || type == JSONBOID || type == NUMERICOID)
            {
                char *text = pg_any_to_server((char *) value->data, value->length, PG_UTF8);
                Datum result = type == NUMERICOID
                    ? DirectFunctionCall3(numeric_in, CStringGetDatum(text), ObjectIdGetDatum(InvalidOid), Int32GetDatum(-1))
                    : type == JSONOID
                    ? DirectFunctionCall1(json_in, CStringGetDatum(text))
                    : DirectFunctionCall1(jsonb_in, CStringGetDatum(text));
                if (text != (char *) value->data)
                {
                    pfree(text);
                }
                return result;
            }
            return ankus_write_buffer(value, type != BYTEAOID);
        }

        """;
}
