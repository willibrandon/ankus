#include "nodes/makefuncs.h"

/* Compare exact C-string storage and exercise a null address with isnull still false. */
PGDLLEXPORT Datum ankus_test_cstring_argument(PG_FUNCTION_ARGS);
PG_FUNCTION_INFO_V1(ankus_test_cstring_argument);
PGDLLEXPORT Datum
ankus_test_cstring_argument(PG_FUNCTION_ARGS)
{
    Oid callback = PG_GETARG_OID(0);
    unsigned char bytes[] = {0x01, 0x80, 0xff, 0};
    char *storage = PG_GETARG_BOOL(1) ? NULL : (char *) bytes;
    FmgrInfo function;
    LOCAL_FCINFO(call, 2);
    fmgr_info(callback, &function);
    Const *input = makeConst(CSTRINGOID, -1, InvalidOid, -2, CStringGetDatum(storage), false, false);
    Const *address = makeConst(INT8OID, -1, InvalidOid, sizeof(int64),
        Int64GetDatum((int64) (uintptr_t) storage), false, FLOAT8PASSBYVAL);
    function.fn_expr = (Node *) makeFuncExpr(callback, BOOLOID,
        list_make2(input, address), InvalidOid, InvalidOid, COERCE_EXPLICIT_CALL);
    InitFunctionCallInfoData(*call, &function, 2, InvalidOid, NULL, NULL);
    call->args[0].value = input->constvalue;
    call->args[1].value = address->constvalue;
    call->args[0].isnull = false;
    call->args[1].isnull = false;
    Datum result = FunctionCallInvoke(call);
    if (call->isnull)
    {
        ereport(ERROR, (errcode(ERRCODE_NULL_VALUE_NOT_ALLOWED), errmsg("C-string observation returned NULL")));
    }

    return result;
}

/* Capture physical argument addresses before any generated managed conversion. */
static Datum
ankus_test_call_buffer(Oid callback, Datum value, Oid type)
{
    void *storage = DatumGetPointer(value);
    const char *kind = VARATT_IS_EXTERNAL(storage) ? "external" :
        VARATT_IS_COMPRESSED(storage) ? "compressed" : VARATT_IS_SHORT(storage) ? "short" : "flat";
    char *payload = VARATT_IS_EXTERNAL(storage) || VARATT_IS_COMPRESSED(storage) ? NULL : VARDATA_ANY(storage);
    FmgrInfo function;
    LOCAL_FCINFO(call, 4);
    fmgr_info(callback, &function);
    Const *input = makeConst(type, -1, InvalidOid, -1, value, false, false);
    Const *address = makeConst(INT8OID, -1, InvalidOid, sizeof(int64),
        Int64GetDatum((int64) (uintptr_t) storage), false, FLOAT8PASSBYVAL);
    Const *bytes = makeConst(INT8OID, -1, InvalidOid, sizeof(int64),
        Int64GetDatum((int64) (uintptr_t) payload), false, FLOAT8PASSBYVAL);
    Const *form = makeConst(TEXTOID, -1, InvalidOid, -1, CStringGetTextDatum(kind), false, false);
    function.fn_expr = (Node *) makeFuncExpr(callback, TEXTARRAYOID,
        list_make4(input, address, bytes, form), InvalidOid, InvalidOid, COERCE_EXPLICIT_CALL);
    InitFunctionCallInfoData(*call, &function, 4, InvalidOid, NULL, NULL);
    call->args[0].value = value;
    call->args[1].value = address->constvalue;
    call->args[2].value = bytes->constvalue;
    call->args[3].value = form->constvalue;
    for (int index = 0; index < 4; index++)
    {
        call->args[index].isnull = false;
    }

    Datum result = FunctionCallInvoke(call);
    if (call->isnull)
    {
        ereport(ERROR, (errcode(ERRCODE_NULL_VALUE_NOT_ALLOWED), errmsg("Buffer address observation returned NULL")));
    }

    return result;
}

PGDLLEXPORT Datum ankus_test_buffer_argument(PG_FUNCTION_ARGS);
PG_FUNCTION_INFO_V1(ankus_test_buffer_argument);
PGDLLEXPORT Datum
ankus_test_buffer_argument(PG_FUNCTION_ARGS)
{
    return ankus_test_call_buffer(PG_GETARG_OID(0), PG_GETARG_DATUM(1), get_fn_expr_argtype(fcinfo->flinfo, 1));
}

/* Deliberately malformed text tests guarded validation before any managed string conversion. */
PGDLLEXPORT Datum ankus_test_buffer_invalid_text(PG_FUNCTION_ARGS);
PG_FUNCTION_INFO_V1(ankus_test_buffer_invalid_text);
PGDLLEXPORT Datum
ankus_test_buffer_invalid_text(PG_FUNCTION_ARGS)
{
    int byte_value = PG_GETARG_INT32(1);
    if (byte_value < 0 || byte_value > 255)
    {
        ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("The fixture requires one exact byte")));
    }

    text *value = palloc(VARHDRSZ + 1);
    SET_VARSIZE(value, VARHDRSZ + 1);
    VARDATA(value)[0] = (char) byte_value;
    return ankus_test_call_buffer(PG_GETARG_OID(0), PointerGetDatum(value), TEXTOID);
}
