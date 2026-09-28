#include "nodes/makefuncs.h"
#include "utils/expandeddatum.h"

/* Pass the original physical Datum to managed code without the ordinary owned raw argument conversion. */
PG_FUNCTION_INFO_V1(ankus_test_array_storage);
PGDLLEXPORT Datum
ankus_test_array_storage(PG_FUNCTION_ARGS)
{
    Oid callback = PG_GETARG_OID(0);
    Datum value = PG_GETARG_DATUM(1);
    Oid type = get_fn_expr_argtype(fcinfo->flinfo, 1);
    if (PG_GETARG_INT32(2) == 1)
    {
        value = expand_array(value, CurrentMemoryContext, NULL);
    }

    void *storage = DatumGetPointer(value);
    const char *kind = VARATT_IS_EXTERNAL_EXPANDED(storage) ? "expanded" :
        VARATT_IS_EXTERNAL(storage) ? "external" : VARATT_IS_COMPRESSED(storage) ? "compressed" :
        VARATT_IS_SHORT(storage) ? "short" : "flat";
    return OidFunctionCall3(callback, Int64GetDatum((int64) (uintptr_t) value),
        ObjectIdGetDatum(type), CStringGetTextDatum(kind));
}

/* Observe generated scalar borrowing against its caller's real flat array address. */
static Datum
ankus_test_array_address(FunctionCallInfo fcinfo, int mode)
{
    Oid callback = PG_GETARG_OID(0);
    ArrayType *array = PG_GETARG_ARRAYTYPE_P(1);
    if (mode == 2)
    {
        int rank = ARR_NDIM(array);
        int count = ArrayGetNItems(rank, ARR_DIMS(array));
        Size bytes = VARSIZE(array) - ARR_DATA_OFFSET(array);
        Size offset = ARR_OVERHEAD_WITHNULLS(rank, count);
        ArrayType *with_bitmap = palloc0(offset + bytes);
        memcpy(with_bitmap, array, ARR_OVERHEAD_NONULLS(rank));
        SET_VARSIZE(with_bitmap, offset + bytes);
        with_bitmap->dataoffset = (int32) offset;
        unsigned char *bitmap = ARR_NULLBITMAP(with_bitmap);
        if (bitmap == NULL)
        {
            ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("The slice witness requires a NULL bitmap")));
        }

        memset(bitmap, 0xff, (count + 7) / 8);
        memcpy(ARR_DATA_PTR(with_bitmap), ARR_DATA_PTR(array), bytes);
        array = with_bitmap;
    }

    Oid type = get_fn_expr_argtype(fcinfo->flinfo, 1);
    FmgrInfo function;
    LOCAL_FCINFO(call, 2);
    fmgr_info(callback, &function);
    Const *input = makeConst(type, -1, InvalidOid, -1, PointerGetDatum(array), false, false);
    Const *address = makeConst(INT8OID, -1, InvalidOid, sizeof(int64),
        Int64GetDatum((int64) (uintptr_t) (mode != 0 ? ARR_DATA_PTR(array) : (char *) array)), false, FLOAT8PASSBYVAL);
    function.fn_expr = (Node *) makeFuncExpr(callback, BOOLOID, list_make2(input, address),
        InvalidOid, InvalidOid, COERCE_EXPLICIT_CALL);
    InitFunctionCallInfoData(*call, &function, 2, InvalidOid, NULL, NULL);
    call->args[0].value = PointerGetDatum(array);
    call->args[0].isnull = false;
    call->args[1].value = address->constvalue;
    call->args[1].isnull = false;
    Datum result = FunctionCallInvoke(call);
    if (call->isnull)
    {
        ereport(ERROR, (errcode(ERRCODE_NULL_VALUE_NOT_ALLOWED), errmsg("Array address observation returned NULL")));
    }

    return result;
}

PG_FUNCTION_INFO_V1(ankus_test_array_argument);
PGDLLEXPORT Datum
ankus_test_array_argument(PG_FUNCTION_ARGS)
{
    return ankus_test_array_address(fcinfo, 0);
}

/* Observe the native payload before the generated conversion borrows it. */
PG_FUNCTION_INFO_V1(ankus_test_array_slice_argument);
PGDLLEXPORT Datum
ankus_test_array_slice_argument(PG_FUNCTION_ARGS)
{
    return ankus_test_array_address(fcinfo, 1);
}

/* A bitmap with every cell present is different from an array containing NULLs. */
PG_FUNCTION_INFO_V1(ankus_test_array_slice_bitmap);
PGDLLEXPORT Datum
ankus_test_array_slice_bitmap(PG_FUNCTION_ARGS)
{
    return ankus_test_array_address(fcinfo, 2);
}

/* Only called for a new detoast allocation, never an interior tuple address. */
PG_FUNCTION_INFO_V1(ankus_test_array_owner);
PGDLLEXPORT Datum
ankus_test_array_owner(PG_FUNCTION_ARGS)
{
    void *storage = (void *) (uintptr_t) PG_GETARG_INT64(0);
    MemoryContext owner = GetMemoryChunkContext(storage);
    PG_RETURN_TEXT_P(cstring_to_text(owner->ident == NULL ? owner->name : owner->ident));
}
