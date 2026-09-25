namespace Ankus.Generators;

/// <summary>
/// Reads and constructs exact custom ranges through guarded raw scalar bounds.
/// </summary>
internal static class NativeMappedRangeBridge
{
    /// <summary>
    /// Gets range deserialization and canonical construction with checked native ownership.
    /// </summary>
    internal const string Source = """
        static void
        ankus_raw_range(AnkusRequest *request, AnkusResult *result, Datum datum, Oid type)
        {
            Oid subtype = get_range_subtype(type);
            if (!OidIsValid(subtype))
                ereport(ERROR, (errcode(ERRCODE_DATATYPE_MISMATCH), errmsg("Type OID %u is not a range", type)));
            RangeType *range = DatumGetRangeTypeP(datum);
            if (RangeTypeGetOid(range) != type)
                ereport(ERROR, (errcode(ERRCODE_DATATYPE_MISMATCH), errmsg("Range storage does not match its declared type")));
            TypeCacheEntry *cache = lookup_type_cache(type, TYPECACHE_RANGE_INFO);
            RangeBound lower = {0}, upper = {0};
            bool empty;
            range_deserialize(cache, range, &lower, &upper, &empty);
            result->text.integral = empty ? 1 : (lower.infinite ? 8 : lower.inclusive ? 2 : 0) |
                (upper.infinite ? 16 : upper.inclusive ? 4 : 0);
            result->result_type_oid = subtype;
            result->column_count = 1;
            result->row_count = 2;
            result->values = calloc(2, sizeof(AnkusValue));
            if (result->values == NULL)
                ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg("Unable to copy mapped range bounds")));
            RangeBound bounds[2] = {lower, upper};
            for (int index = 0; index < 2; index++)
            {
                bool absent = empty || bounds[index].infinite;
                result->values[index].is_null = absent;
                if (!absent)
                    result->values[index].integral = (int64) (uintptr_t) ankus_copy_raw_datum(bounds[index].val, subtype,
                        request->result_context, request->result_generation);
            }

            if ((Pointer) range != DatumGetPointer(datum))
                pfree(range);
        }

        static void
        ankus_mapped_range(AnkusRequest *request, AnkusResult *result)
        {
            if (request->parameter_count != 3 || request->parameters == NULL || !OidIsValid(request->scalar_result_oid))
                ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("Invalid mapped range request")));
            const AnkusParameter *metadata = &request->parameters[0];
            if (metadata->type_oid != INT4OID || metadata->value.is_null || metadata->value.auxiliary1 != 0 ||
                metadata->value.data != NULL || metadata->value.length != 0 ||
                metadata->value.integral < 0 || metadata->value.integral > 31)
                ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Invalid mapped range flags")));
            int flags = (int) metadata->value.integral;
            if (((flags & 1) != 0 && flags != 1) || (flags & 10) == 10 || (flags & 20) == 20)
                ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Invalid mapped range flags")));
            Oid type = request->scalar_result_oid;
            Oid subtype = get_range_subtype(type);
            if (!OidIsValid(subtype))
                ereport(ERROR, (errcode(ERRCODE_DATATYPE_MISMATCH), errmsg("Type OID %u is not a range", type)));
            bool empty = flags == 1;
            bool absent[2] = {empty || (flags & 8) != 0, empty || (flags & 16) != 0};
            ankus_datum_context(request->result_context, request->result_generation);
            /* Validate both nominal envelopes before a domain constraint can execute user code. */
            for (int index = 0; index < 2; index++)
            {
                const AnkusParameter *parameter = &request->parameters[index + 1];
                const AnkusValue *value = &parameter->value;
                bool raw = value->is_null == 0 && value->auxiliary1 == -5 && value->data != NULL &&
                    value->length == sizeof(AnkusDatumReference);
                bool missing = value->is_null == 1 && value->auxiliary1 == 0 && value->data == NULL && value->length == 0;
                if (parameter->type_oid != subtype || (absent[index] ? !missing : !raw))
                    ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Invalid mapped range bound envelope")));
            }

            RangeBound lower = {0}, upper = {0};
            lower.lower = true;
            lower.infinite = absent[0];
            lower.inclusive = (flags & 2) != 0;
            upper.lower = false;
            upper.infinite = absent[1];
            upper.inclusive = (flags & 4) != 0;
            if (!absent[0])
                lower.val = ankus_parameter_datum(&request->parameters[1]);
            if (!absent[1])
                upper.val = ankus_parameter_datum(&request->parameters[2]);
            if (get_range_subtype(type) != subtype)
                ereport(ERROR, (errcode(ERRCODE_DATATYPE_MISMATCH), errmsg("Mapped range subtype changed during construction")));
            TypeCacheEntry *cache = lookup_type_cache(type, TYPECACHE_RANGE_INFO);
        #if PG_VERSION_NUM >= 160000
            RangeType *range = make_range(cache, &lower, &upper, empty, NULL);
        #else
            RangeType *range = make_range(cache, &lower, &upper, empty);
        #endif
            /* Bounds can belong to caller-owned contexts; serialization does not transfer their ownership. */
            if (get_range_subtype(type) != subtype)
                ereport(ERROR, (errcode(ERRCODE_DATATYPE_MISMATCH), errmsg("Mapped range subtype changed during canonicalization")));
            ankus_datum_context(request->result_context, request->result_generation);
            Datum copy = ankus_copy_raw_datum(RangeTypePGetDatum(range), type,
                request->result_context, request->result_generation);
            result->text.integral = (int64) (uintptr_t) copy;
            result->result_type_oid = type;
        }

        """;
}
