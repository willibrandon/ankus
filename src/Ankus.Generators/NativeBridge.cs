namespace Ankus.Generators;

/// <summary>
/// Defines the native transport ABI and PostgreSQL-owned varlena conversion routines used by generated entry points.
/// </summary>
internal static class NativeBridge
{
    /// <summary>
    /// Gets the C preamble compiled against the selected PostgreSQL installation's headers.
    /// </summary>
    internal const string Source = """
        #include "postgres.h"
        #include "fmgr.h"
        #if PG_VERSION_NUM >= 160000
        #include "varatt.h"
        #endif
        #include "utils/builtins.h"
        #include "catalog/pg_type_d.h"
        #include "utils/uuid.h"
        #include "utils/date.h"
        #include "utils/timestamp.h"
        #include "mb/pg_wchar.h"
        PG_MODULE_MAGIC;

        typedef struct AnkusValue
        {
            int64 integral;
            int32 auxiliary1;
            int32 auxiliary2;
            int32 temporal_infinity;
            unsigned char *data;
            int32 length;
            uint8 is_null;
            void (*release)(void *);
        } AnkusValue;

        typedef struct AnkusInputBuffer
        {
            struct varlena *detoasted;
            char *converted;
            char *serialized;
        } AnkusInputBuffer;

        """;

    /// <summary>
    /// Gets native input detoasting, encoding conversion, and temporary-buffer cleanup helpers.
    /// </summary>
    internal const string ReadBuffers = """

        static inline void
        ankus_read_buffer(Datum datum, AnkusValue *value, AnkusInputBuffer *owned, bool is_text)
        {
            struct varlena *original = (struct varlena *) DatumGetPointer(datum);
            struct varlena *unpacked = pg_detoast_datum_packed(original);
            if (unpacked != original)
            {
                owned->detoasted = unpacked;
            }

            value->data = (unsigned char *) VARDATA_ANY(unpacked);
            value->length = VARSIZE_ANY_EXHDR(unpacked);
            if (is_text)
            {
                char *converted = pg_server_to_any((const char *) value->data, value->length, PG_UTF8);
                if (converted != (char *) value->data)
                {
                    owned->converted = converted;
                    value->data = (unsigned char *) converted;
                    value->length = strlen(converted);
                }
            }
        }

        static inline void
        ankus_free_input(AnkusInputBuffer *owned)
        {
            if (owned->converted != NULL)
            {
                pfree(owned->converted);
            }

            if (owned->detoasted != NULL)
            {
                pfree(owned->detoasted);
            }

            if (owned->serialized != NULL)
            {
                pfree(owned->serialized);
            }
        }

        """;

    /// <summary>
    /// Gets native output encoding and PostgreSQL memory-context allocation helpers.
    /// </summary>
    internal const string WriteBuffer = """

        static inline Datum
        ankus_write_buffer(const AnkusValue *value, bool is_text)
        {
            const char *data = (const char *) value->data;
            int length = value->length;
            char *converted = NULL;
            struct varlena *result;
            if (is_text)
            {
                converted = pg_any_to_server(data, length, PG_UTF8);
                if (converted != data)
                {
                    data = converted;
                    length = strlen(converted);
                }
            }

            result = (struct varlena *) palloc((Size) length + VARHDRSZ);
            SET_VARSIZE(result, length + VARHDRSZ);
            memcpy(VARDATA(result), data, length);
            if (converted != NULL && converted != (char *) value->data)
            {
                pfree(converted);
            }

            return PointerGetDatum(result);
        }

        """;
}
