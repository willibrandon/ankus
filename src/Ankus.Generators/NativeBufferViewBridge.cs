namespace Ankus.Generators;

/// <summary>
/// Borrows packed varlena bytes and owns any detoast or encoding allocations in the selected context.
/// </summary>
internal static class NativeBufferViewBridge
{
    /// <summary>
    /// Gets guarded bytea and text borrowing with exact original datum identity.
    /// </summary>
    internal const string Source = """
        static void
        ankus_buffer_view_operation(AnkusRequest *request, AnkusResult *result, Datum datum, Oid base)
        {
            if (request->scalar_operation == 13)
            {
                if (base != CSTRINGOID)
                {
                    ereport(ERROR, (errcode(ERRCODE_DATATYPE_MISMATCH), errmsg("The borrowed value is not a C string")));
                }

                char *data = DatumGetCString(datum);
                if (data == NULL)
                {
                    ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("A present C string requires native storage")));
                }

                Size length = strlen(data);
                if (length >= MaxAllocSize)
                {
                    ereport(ERROR, (errcode(ERRCODE_PROGRAM_LIMIT_EXCEEDED), errmsg("C string exceeds supported byte capacity")));
                }

                result->text.integral = (int64) (uintptr_t) datum;
                result->processed = (uint64) (uintptr_t) data;
                result->row_count = (int) length;
                return;
            }

            bool is_text = request->scalar_operation == 10;
            if ((!is_text && base != BYTEAOID) ||
                (is_text && base != TEXTOID && base != VARCHAROID && base != BPCHAROID))
                ereport(ERROR, (errcode(ERRCODE_DATATYPE_MISMATCH), errmsg("The borrowed value is not a compatible buffer type")));
            if (DatumGetPointer(datum) == NULL)
                ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("A present buffer requires native storage")));

            MemoryContext owner = ankus_datum_context(request->result_context, request->result_generation);
            MemoryContext previous = MemoryContextSwitchTo(owner);
            struct varlena *flat = pg_detoast_datum_packed((struct varlena *) DatumGetPointer(datum));
            char *data = VARDATA_ANY(flat);
            int length = VARSIZE_ANY_EXHDR(flat);
            if (is_text)
            {
                pg_verify_mbstr(GetDatabaseEncoding(), data, length, false);
                char *converted = pg_server_to_any(data, length, PG_UTF8);
                if (converted != data)
                {
                    data = converted;
                    length = strlen(converted);
                }

                pg_verify_mbstr(PG_UTF8, data, length, false);
            }

            MemoryContextSwitchTo(previous);
            result->text.integral = (int64) (uintptr_t) flat;
            result->processed = (uint64) (uintptr_t) data;
            result->row_count = length;
        }

        """;
}
