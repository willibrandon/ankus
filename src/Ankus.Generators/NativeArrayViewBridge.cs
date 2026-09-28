namespace Ankus.Generators;

/// <summary>
/// Borrows PostgreSQL array storage and cursors through the existing native error guard.
/// </summary>
internal static class NativeArrayViewBridge
{
    /// <summary>
    /// Gets array operations that preserve original cells and selected-header layout rules.
    /// </summary>
    internal const string Source = """
        static void
        ankus_array_view_operation(AnkusRequest *request, AnkusResult *result, Datum datum, Oid type)
        {
            Oid element = get_element_type(type);
            if (!OidIsValid(element))
                ereport(ERROR, (errcode(ERRCODE_DATATYPE_MISMATCH), errmsg("The borrowed value is not an array")));
            MemoryContext owner = ankus_datum_context(request->result_context, request->result_generation);
            MemoryContext previous = MemoryContextSwitchTo(owner);
            ArrayType *array = DatumGetArrayTypeP(datum);
            MemoryContextSwitchTo(previous);
            if (ARR_ELEMTYPE(array) != element)
                ereport(ERROR, (errcode(ERRCODE_DATATYPE_MISMATCH), errmsg("Array header element type does not match its declared type")));
            int rank = ARR_NDIM(array);
            if (rank < 0 || rank > MAXDIM)
                ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("Array header has an invalid dimension count")));
            result->result_type_oid = element;
            if (request->scalar_operation == 5)
            {
                int shape[MAXDIM * 2];
                memcpy(shape, ARR_DIMS(array), rank * sizeof(int));
                memcpy(shape + rank, ARR_LBOUND(array), rank * sizeof(int));
                ankus_copy_owned(&result->text, (const unsigned char *) shape, rank * 2 * sizeof(int));
                result->text.integral = (int64) (uintptr_t) array;
                result->row_count = ArrayGetNItems(rank, ARR_DIMS(array));
                result->processed = array_contains_nulls(array);
            }
            else if (request->scalar_operation == 7)
            {
                previous = MemoryContextSwitchTo(owner);
                ArrayIterator iterator = array_create_iterator(array, 0, NULL);
                MemoryContextSwitchTo(previous);
                result->text.integral = (int64) (uintptr_t) iterator;
            }
            else
            {
                Datum cell = (Datum) 0;
                bool is_null = false;
                bool found;
                if (request->scalar_operation == 6)
                {
                    if (request->limit < 0 || request->limit >= ArrayGetNItems(rank, ARR_DIMS(array)))
                        ereport(ERROR, (errcode(ERRCODE_ARRAY_SUBSCRIPT_ERROR), errmsg("Borrowed array index is outside its bounds")));
                    ArrayIterator iterator = array_create_iterator(array, 0, NULL);
                    found = false;
                    for (int index = 0; index <= request->limit; index++)
                        found = array_iterate(iterator, &cell, &is_null);
                    array_free_iterator(iterator);
                }
                else
                {
                    if (request->array_iterator == NULL)
                        ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("A borrowed array iterator is required")));
                    found = array_iterate((ArrayIterator) request->array_iterator, &cell, &is_null);
                }

                result->processed = found;
                result->text.integral = (int64) (uintptr_t) cell;
                result->text.is_null = is_null;
            }
        }

        """;
}
