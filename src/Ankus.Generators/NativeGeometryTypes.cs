namespace Ankus.Generators;

/// <summary>
/// Converts geometric datums using PostgreSQL binary protocols and header-derived empty collection storage.
/// </summary>
internal static class NativeGeometryTypes
{
    /// <summary>
    /// Gets native send/receive adapters, framing checks and allocator-matched input ownership.
    /// </summary>
    internal const string Source = """
        #include "utils/geo_decls.h"
        #include "utils/memutils.h"
        #include "utils/fmgrprotos.h"
        #include "libpq/pqformat.h"

        static PGFunction
        ankus_geometry_io(Oid type, bool receive)
        {
            switch (type)
            {
                case POINTOID: return receive ? point_recv : point_send;
                case LSEGOID: return receive ? lseg_recv : lseg_send;
                case LINEOID: return receive ? line_recv : line_send;
                case BOXOID: return receive ? box_recv : box_send;
                case CIRCLEOID: return receive ? circle_recv : circle_send;
                case PATHOID: return receive ? path_recv : path_send;
                case POLYGONOID: return receive ? poly_recv : poly_send;
                default: return NULL;
            }
        }

        static void
        ankus_read_geometry(Datum datum, AnkusValue *value, AnkusInputBuffer *owned, Oid type)
        {
            bytea *wire;
            if (type == PATHOID || type == POLYGONOID)
            {
                struct varlena *original = (struct varlena *) DatumGetPointer(datum);
                struct varlena *unpacked = pg_detoast_datum(original);
                if (unpacked != original)
                    owned->detoasted = unpacked;
                datum = PointerGetDatum(unpacked);
            }

            wire = DatumGetByteaP(DirectFunctionCall1(ankus_geometry_io(type, false), datum));
            owned->serialized = (char *) wire;
            value->data = (unsigned char *) VARDATA(wire);
            value->length = VARSIZE(wire) - VARHDRSZ;
        }

        static Datum
        ankus_write_geometry(const AnkusValue *value, Oid type)
        {
            StringInfoData buffer;
            Datum result;
            int expected = type == POINTOID ? 16 : type == LSEGOID || type == BOXOID ? 32 : 24;
            bool variable = type == PATHOID || type == POLYGONOID;
            if (value->data == NULL || value->length < 0 || (!variable && value->length != expected))
                ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Invalid geometric transport length")));
            buffer.data = (char *) value->data;
            buffer.len = value->length;
            buffer.maxlen = value->length;
            buffer.cursor = 0;
            if (variable)
            {
                int closed = type == PATHOID ? pq_getmsgbyte(&buffer) : 0;
                int32 count = pq_getmsgint(&buffer, 4);
                Size header = type == PATHOID ? offsetof(PATH, p) : offsetof(POLYGON, p);
                if (closed > 1 || count < 0 || (Size) count > (MaxAllocSize - header) / sizeof(Point) ||
                    (int64) count * 16 != buffer.len - buffer.cursor)
                    ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Invalid geometric point count or closure flag")));
                if (count == 0)
                {
                    /* pgrx supports empty owned geometries, unlike the SQL text and binary input functions. */
                    struct varlena *empty = palloc0(header);
                    SET_VARSIZE(empty, header);
                    if (type == PATHOID)
                        ((PATH *) empty)->closed = closed;
                    return PointerGetDatum(empty);
                }

                buffer.cursor = 0;
            }

            result = DirectFunctionCall1(ankus_geometry_io(type, true), PointerGetDatum(&buffer));
            pq_getmsgend(&buffer);
            return result;
        }

        """;
}
