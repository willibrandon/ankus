namespace Ankus.Generators;

/// <summary>
/// Constructs exact mapped arrays from checked raw cells without canonical element conversion.
/// </summary>
internal static class NativeMappedArrayBridge
{
    /// <summary>
    /// Gets the eager array constructor, called only inside the existing native error guard.
    /// </summary>
    internal const string Source = """
        static void
        ankus_mapped_array(AnkusRequest *request, AnkusResult *result)
        {
            if (request->scalar_operation != 0 || request->parameter_count < 1 || request->parameters == NULL ||
                !OidIsValid(request->scalar_result_oid))
                ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("Invalid mapped array request")));

            const AnkusParameter *metadata = &request->parameters[0];
            const AnkusValue *shape = &metadata->value;
            if (metadata->type_oid != BYTEAOID || shape->is_null || shape->auxiliary1 != 0 ||
                shape->data == NULL || shape->length < 12 || shape->length > 12 + MAXDIM * 8)
                ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Invalid mapped array shape header")));

            StringInfoData buffer = {(char *) shape->data, shape->length, shape->length, 0};
            int rank = pq_getmsgint(&buffer, 4);
            int count = pq_getmsgint(&buffer, 4);
            Oid element = (Oid) pq_getmsgint(&buffer, 4);
            Oid array_type = request->scalar_result_oid;
            int lengths[MAXDIM];
            int bounds[MAXDIM];
            if (rank < 0 || rank > MAXDIM || count < 0 || count != request->parameter_count - 1 ||
                shape->length != 12 + rank * 8 || (Size) count > MaxArraySize ||
                !OidIsValid(element) || get_array_type(element) != array_type || get_element_type(array_type) != element)
                ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Invalid mapped array shape or type identity")));

            for (int index = 0; index < rank; index++)
            {
                lengths[index] = pq_getmsgint(&buffer, 4);
                bounds[index] = pq_getmsgint(&buffer, 4);
            }

            pq_getmsgend(&buffer);
            if (ArrayGetNItems(rank, lengths) != count || (count == 0 && rank != 0))
                ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Inconsistent mapped array dimensions")));
            ArrayCheckBounds(rank, lengths, bounds);
            ankus_datum_context(request->result_context, request->result_generation);

            /* Validate every nominal envelope before any domain constraint can run user code. */
            for (int index = 0; index < count; index++)
            {
                const AnkusParameter *parameter = &request->parameters[index + 1];
                const AnkusValue *value = &parameter->value;
                bool raw = value->auxiliary1 == -5 && value->data != NULL && value->length == sizeof(AnkusDatumReference);
                bool absent = value->is_null == 1 && value->auxiliary1 == 0 && value->data == NULL && value->length == 0;
                if (parameter->type_oid != element || value->is_null > 1 || (!raw && !absent))
                    ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Invalid mapped array element envelope")));
            }

            int16 length;
            bool by_value;
            char alignment;
            get_typlenbyvalalign(element, &length, &by_value, &alignment);
            Datum *values = palloc(sizeof(Datum) * (Size) Max(count, 1));
            bool *nulls = palloc(sizeof(bool) * (Size) Max(count, 1));
            for (int index = 0; index < count; index++)
            {
                const AnkusParameter *parameter = &request->parameters[index + 1];
                CHECK_FOR_INTERRUPTS();
                nulls[index] = parameter->value.is_null != 0;
                values[index] = ankus_parameter_datum(parameter);
            }

            ArrayType *array = construct_md_array(values, nulls, rank, lengths, bounds, element, length, by_value, alignment);
            /* Domain constraints may execute SQL or invalidate a caller-selected destination. */
            if (get_array_type(element) != array_type || get_element_type(array_type) != element)
                ereport(ERROR, (errcode(ERRCODE_DATATYPE_MISMATCH), errmsg("Mapped array type changed during construction")));
            ankus_datum_context(request->result_context, request->result_generation);
            Datum copy = ankus_copy_raw_datum(PointerGetDatum(array), array_type,
                request->result_context, request->result_generation);
            result->text.integral = (int64) (uintptr_t) copy;
            result->result_type_oid = array_type;
        }

        """;
}
