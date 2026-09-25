namespace Ankus.Generators;

/// <summary>
/// Copies generated base-type payloads and converts their C string and binary protocol boundaries.
/// </summary>
internal static class NativeCustomTypeBridge
{
    /// <summary>
    /// Gets guarded storage and I/O conversion helpers.
    /// </summary>
    internal const string Source = """
        static bool ankus_custom_type_supported(Oid type);

        static void
        ankus_read_custom(Datum datum, Oid type, AnkusValue *value, AnkusInputBuffer *owned)
        {
            if (get_typtype(type) != TYPTYPE_BASE || get_typlen(type) != -1)
                ereport(ERROR, (errcode(ERRCODE_DATATYPE_MISMATCH), errmsg("PostgreSQL type OID %u has no generated base-type codec", type)));
            ankus_read_buffer(datum, value, owned, false);
            value->auxiliary1 = -7;
            value->auxiliary2 = (int32) type;
        }

        static Datum
        ankus_write_custom(const AnkusValue *value, Oid type)
        {
            if (value->auxiliary1 != -7 || (Oid) value->auxiliary2 != type ||
                value->length < 0 || (value->length != 0 && value->data == NULL) || get_typlen(type) != -1)
                ereport(ERROR, (errcode(ERRCODE_DATATYPE_MISMATCH), errmsg("Custom PostgreSQL type payload does not match its declared type")));
            return ankus_write_buffer(value, false);
        }

        """;

    /// <summary>
    /// Gets text I/O helpers emitted only for extensions declaring custom base types.
    /// </summary>
    internal const string TextSource = """
        static Oid
        ankus_declared_argument_type(FunctionCallInfo call, int index)
        {
            Oid *types = NULL;
            int count = 0;
            (void) get_func_signature(call->flinfo->fn_oid, &types, &count);
            if (index < 0 || index >= count)
                ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("Missing declared function argument")));
            Oid type = types[index];
            pfree(types);
            return getBaseType(type);
        }

        static void
        ankus_read_cstring(Datum datum, AnkusValue *value, AnkusInputBuffer *owned)
        {
            char *text = DatumGetCString(datum);
            /* InputFunctionCall passes a null pointer with args[0].isnull false for NULL fields. */
            if (text == NULL)
            {
                value->is_null = true;
                return;
            }

            char *utf8 = pg_server_to_any(text, strlen(text), PG_UTF8);
            if (utf8 != text)
                owned->converted = utf8;
            value->data = (unsigned char *) utf8;
            value->length = strlen(utf8);
        }

        static Datum
        ankus_write_cstring(const AnkusValue *value)
        {
            char *terminated = pnstrdup((char *) value->data, value->length);
            char *text = pg_any_to_server(terminated, value->length, PG_UTF8);
            if (text != terminated)
                pfree(terminated);
            return CStringGetDatum(text);
        }

        """;

    /// <summary>
    /// Gets the binary receive adapter emitted only when the protocol is requested.
    /// </summary>
    internal const string BinarySource = """
        static void
        ankus_read_type_receive(Datum datum, Oid type, AnkusValue *value)
        {
            StringInfo buffer = (StringInfo) DatumGetPointer(datum);
            if (buffer == NULL || buffer->cursor < 0 || buffer->cursor > buffer->len)
                ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Invalid custom type binary input")));
            value->length = buffer->len - buffer->cursor;
            value->data = (unsigned char *) pq_getmsgbytes(buffer, value->length);
            value->auxiliary1 = -7;
            value->auxiliary2 = (int32) type;
        }

        """;
}
