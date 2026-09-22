namespace Ankus.Generators;

/// <summary>
/// Converts buffered scalar datums while keeping PostgreSQL parsing, detoasting, and allocation in native frames.
/// </summary>
internal static class NativeExtendedTypes
{
    /// <summary>
    /// Gets buffered datum conversion helpers shared by generated functions and SPI.
    /// </summary>
    internal const string Source = """
        #include "utils/fmgrprotos.h"
        #include "libpq/pqcomm.h"
        #include "utils/inet.h"
        #include "libpq/pqformat.h"

        static void
        ankus_read_typed_buffer(Datum datum, AnkusValue *value, AnkusInputBuffer *owned, Oid type)
        {
            if (type == UUIDOID)
            {
                value->data = DatumGetUUIDP(datum)->data;
                value->length = UUID_LEN;
            }
            else if (ankus_geometry_io(type, false) != NULL)
            {
                ankus_read_geometry(datum, value, owned, type);
            }
            else if (type == INETOID || type == CIDROID)
            {
                bytea *wire = DatumGetByteaP(DirectFunctionCall1(type == INETOID ? inet_send : cidr_send, datum));
                owned->serialized = (char *) wire;
                value->data = (unsigned char *) VARDATA(wire);
                value->length = VARSIZE(wire) - VARHDRSZ;
                value->data[0] = value->data[0] == PGSQL_AF_INET ? 4 : 6;
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

            if (ankus_geometry_io(type, true) != NULL)
            {
                return ankus_write_geometry(value, type);
            }

            if (type == INETOID || type == CIDROID)
            {
                char bytes[20];
                StringInfoData buffer;
                Datum result;
                if ((value->length != 8 && value->length != 20) || value->data == NULL ||
                    value->data[0] != (value->length == 8 ? 4 : 6) ||
                    value->data[2] != (type == CIDROID ? 1 : 0) || value->data[3] != value->length - 4)
                    ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Invalid network transport header")));
                memcpy(bytes, value->data, value->length);
                bytes[0] = bytes[0] == 4 ? PGSQL_AF_INET : PGSQL_AF_INET6;
                buffer.data = bytes;
                buffer.len = value->length;
                buffer.maxlen = sizeof(bytes);
                buffer.cursor = 0;
                result = DirectFunctionCall1(type == INETOID ? inet_recv : cidr_recv, PointerGetDatum(&buffer));
                pq_getmsgend(&buffer);
                return result;
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
