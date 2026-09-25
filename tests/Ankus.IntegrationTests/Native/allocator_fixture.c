#include "postgres.h"
#include "fmgr.h"
#include "funcapi.h"
#include "lib/stringinfo.h"
#include "catalog/pg_type_d.h"
#include "executor/executor.h"
#include "utils/builtins.h"
#include "utils/lsyscache.h"
#include "utils/memutils.h"
#include "utils/tuplestore.h"

PG_MODULE_MAGIC;

/* Native access deliberately uses the selected header's layout, never a managed copy. */
PG_FUNCTION_INFO_V1(ankus_test_stringinfo_cursor);
PGDLLEXPORT Datum
ankus_test_stringinfo_cursor(PG_FUNCTION_ARGS)
{
    StringInfo buffer = (StringInfo) (intptr_t) PG_GETARG_INT64(0);
    int value = PG_GETARG_INT32(1);
    if (value >= 0)
        buffer->cursor = value;
    PG_RETURN_INT32(buffer->cursor);
}

PG_FUNCTION_INFO_V1(ankus_test_stringinfo_borrow);
PGDLLEXPORT Datum
ankus_test_stringinfo_borrow(PG_FUNCTION_ARGS)
{
    int mode = PG_GETARG_INT32(1);
    StringInfoData buffer;
    char readonly_data[] = {65, 0, (char) 255, 127};
    if (mode == 0 || mode == 4)
    {
        initStringInfo(&buffer);
        appendBinaryStringInfo(&buffer, "abc", 3);
        buffer.cursor = 7;
        if (mode == 4)
            buffer.data[buffer.len] = '?';
    }
    else
    {
        /* A deliberately unterminated view, or a zero-length NULL data address. */
        buffer.data = mode == 2 ? NULL : readonly_data;
        buffer.len = mode == 2 ? 0 : 3;
        buffer.maxlen = 0;
        buffer.cursor = 0;
        if (mode == 3)
        {
            readonly_data[1] = 'b';
            readonly_data[2] = 'c';
        }
    }

    FmgrInfo function;
    LOCAL_FCINFO(call, 2);
    fmgr_info(PG_GETARG_OID(0), &function);
    InitFunctionCallInfoData(*call, &function, 2, InvalidOid, NULL, NULL);
    call->args[0].isnull = false;
    call->args[0].value = PointerGetDatum(&buffer);
    call->args[1].isnull = false;
    call->args[1].value = Int32GetDatum(mode);
    Datum result = FunctionCallInvoke(call);
    if (call->isnull)
        elog(ERROR, "StringInfo borrowing unexpectedly returned NULL");
    if (mode == 0 || mode == 4)
    {
        if (buffer.cursor != 7 || (mode == 0 && (buffer.len != 4 || memcmp(buffer.data, "a\021c*\0", 5) != 0)) ||
            (mode == 4 && (buffer.len != 3 || memcmp(buffer.data, "abc\0", 4) != 0)))
            elog(ERROR, "StringInfo borrowed mutation changed native length, cursor, bytes or terminator");
        pfree(buffer.data);
    }
    else if (readonly_data[0] != 65 || readonly_data[1] != (mode == 3 ? 'b' : 0) ||
        (unsigned char) readonly_data[2] != (mode == 3 ? 'c' : 255) || readonly_data[3] != 127)
        elog(ERROR, "StringInfo readonly borrowing modified caller-owned storage");

    return result;
}

PG_FUNCTION_INFO_V1(ankus_test_function_address);
PGDLLEXPORT Datum
ankus_test_function_address(PG_FUNCTION_ARGS)
{
    FmgrInfo function;
    fmgr_info(PG_GETARG_OID(0), &function);
    PG_RETURN_INT64((int64) (intptr_t) function.fn_addr);
}

PG_FUNCTION_INFO_V1(ankus_test_nullable_sum);
PGDLLEXPORT Datum
ankus_test_nullable_sum(PG_FUNCTION_ARGS)
{
    if (fcinfo->flinfo != NULL || fcinfo->context != NULL || fcinfo->resultinfo != NULL)
        ereport(ERROR, (errmsg("Direct native call unexpectedly supplied catalog execution fields")));
    int32 total = 0;
    for (int index = 0; index < PG_NARGS(); index++)
    {
        if (PG_ARGISNULL(index))
            PG_RETURN_NULL();
        total += PG_GETARG_INT32(index);
    }

    PG_RETURN_INT32(total);
}

PG_FUNCTION_INFO_V1(ankus_test_internal_invoke);
PGDLLEXPORT Datum
ankus_test_internal_invoke(PG_FUNCTION_ARGS)
{
    FmgrInfo function;
    LOCAL_FCINFO(call, 1);
    int32 mode = PG_GETARG_INT32(1);
    int64 stored = 41;
    fmgr_info(PG_GETARG_OID(0), &function);
    InitFunctionCallInfoData(*call, &function, 1, InvalidOid, NULL, NULL);
    call->args[0].isnull = mode == 0;
    call->args[0].value = mode == 2 ? PointerGetDatum(&stored) : (Datum) 0;
    Datum result = FunctionCallInvoke(call);
    if (mode == 2 && stored != 42)
        ereport(ERROR, (errmsg("managed internal callback did not update the native pointee")));
    if (call->isnull)
        PG_RETURN_NULL();

    return result;
}

/* Read each opaque state through its managed consumer while its result owner is live. */
static void
internal_fixture_check_row(FmgrInfo *reader, Datum value, bool is_null, int row)
{
    LOCAL_FCINFO(call, 1);
    if (row > 3 || is_null != (row == 3))
        ereport(ERROR, (errmsg("internal set returned an unexpected row or NULL flag")));
    InitFunctionCallInfoData(*call, reader, 1, InvalidOid, NULL, NULL);
    call->args[0].value = value;
    call->args[0].isnull = is_null;
    Datum result = FunctionCallInvoke(call);
    if (call->isnull != is_null || (!is_null && DatumGetInt64(result) != (row == 1 ? 42 : 40)))
        ereport(ERROR, (errmsg("internal set state did not retain its expected value")));
}

PG_FUNCTION_INFO_V1(ankus_test_internal_set_invoke);
PGDLLEXPORT Datum
ankus_test_internal_set_invoke(PG_FUNCTION_ARGS)
{
    MemoryContext previous = CurrentMemoryContext;
    EState *estate = CreateExecutorState();
    bool materialize = PG_GETARG_BOOL(2);
    bool early = PG_GETARG_BOOL(3);
    volatile int rows = 0;
    PG_TRY();
    {
        MemoryContextSwitchTo(estate->es_query_cxt);
        FmgrInfo function;
        FmgrInfo reader;
        ReturnSetInfo info = {0};
        LOCAL_FCINFO(call, 1);
        fmgr_info(PG_GETARG_OID(0), &function);
        fmgr_info(PG_GETARG_OID(1), &reader);
        bool table = get_func_rettype(function.fn_oid) == RECORDOID;
        info.type = T_ReturnSetInfo;
        info.econtext = GetPerTupleExprContext(estate);
        info.allowedModes = materialize ? SFRM_Materialize : SFRM_ValuePerCall;
        InitFunctionCallInfoData(*call, &function, 1, InvalidOid, NULL, (Node *) &info);
        call->args[0].isnull = true;
        if (materialize)
        {
            (void) FunctionCallInvoke(call);
            if (info.returnMode != SFRM_Materialize || info.setResult == NULL || info.setDesc == NULL)
                ereport(ERROR, (errmsg("internal set did not materialize its rows")));
            TupleTableSlot *slot = MakeSingleTupleTableSlot(info.setDesc, &TTSOpsMinimalTuple);
            while (tuplestore_gettupleslot(info.setResult, true, false, slot))
            {
                bool is_null;
                Datum value = slot_getattr(slot, 1, &is_null);
                internal_fixture_check_row(&reader, value, is_null, rows);
                if (table)
                {
                    value = slot_getattr(slot, 2, &is_null);
                    if (is_null || DatumGetInt32(value) != rows + 1)
                        ereport(ERROR, (errmsg("internal TABLE position was incorrect")));
                }

                rows++;
                ExecClearTuple(slot);
                if (early)
                    break;
            }

            ExecDropSingleTupleTableSlot(slot);
            tuplestore_end(info.setResult);
        }
        else
        {
            for (;;)
            {
                Datum value = FunctionCallInvoke(call);
                if (info.isDone == ExprEndResult)
                    break;
                bool is_null = call->isnull;
                if (table)
                {
                    HeapTupleHeader tuple = DatumGetHeapTupleHeader(value);
                    Datum position = GetAttributeByNum(tuple, 2, &is_null);
                    if (is_null || DatumGetInt32(position) != rows + 1)
                        ereport(ERROR, (errmsg("internal TABLE position was incorrect")));
                    value = GetAttributeByNum(tuple, 1, &is_null);
                }

                internal_fixture_check_row(&reader, value, is_null, rows);
                rows++;
                if (early)
                    break;
            }
        }
    }
    PG_FINALLY();
    {
        MemoryContextSwitchTo(previous);
        FreeExecutorState(estate);
    }
    PG_END_TRY();
    PG_RETURN_INT32(rows);
}

static MemoryContext fixture_parent = NULL;
static MemoryContextCallback fixture_cleanup;

/* The callback lives outside the transaction whose deletion clears this pointer. */
static void
allocator_fixture_cleanup(void *argument)
{
    (void) argument;
    fixture_parent = NULL;
}

PG_FUNCTION_INFO_V1(ankus_test_allocator_create);
PGDLLEXPORT Datum
ankus_test_allocator_create(PG_FUNCTION_ARGS)
{
    int kind = PG_GETARG_INT32(0);
    Oid callback = PG_GETARG_OID(1);
    char *name = text_to_cstring(PG_GETARG_TEXT_PP(2));
    MemoryContext caller = CurrentMemoryContext;
    MemoryContext child;
    FmgrInfo function;
    Datum result = (Datum) 0;
    LOCAL_FCINFO(call, 0);

    /* Resolve catalog entries and load the managed library before selecting Slab. */
    fmgr_info(callback, &function);
    InitFunctionCallInfoData(*call, &function, 0, InvalidOid, NULL, NULL);
    if (fixture_parent != NULL)
    {
        MemoryContextDelete(fixture_parent);
    }

    fixture_parent = AllocSetContextCreate(TopTransactionContext,
        "Ankus allocator fixture", ALLOCSET_SMALL_SIZES);
    fixture_cleanup.func = allocator_fixture_cleanup;
    fixture_cleanup.arg = NULL;
    MemoryContextRegisterResetCallback(fixture_parent, &fixture_cleanup);
    name = MemoryContextStrdup(fixture_parent, name);
    switch (kind)
    {
        case 0:
            child = SlabContextCreate(fixture_parent, name, 8192, 64);
            break;
        case 1:
#if PG_VERSION_NUM >= 150000
            child = GenerationContextCreate(fixture_parent, name, ALLOCSET_DEFAULT_SIZES);
#else
            child = GenerationContextCreate(fixture_parent, name, 8192);
#endif
            break;
        case 2:
#if PG_VERSION_NUM >= 170000
            child = BumpContextCreate(fixture_parent, name, ALLOCSET_DEFAULT_SIZES);
            break;
#else
            ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED), errmsg("Bump requires PostgreSQL 17 or later")));
#endif
        default:
            ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("unknown test allocator kind")));
    }

    PG_TRY();
    {
        MemoryContextSwitchTo(child);
        result = FunctionCallInvoke(call);
    }
    PG_FINALLY();
    {
        MemoryContextSwitchTo(caller);
    }
    PG_END_TRY();

    if (call->isnull)
    {
        ereport(ERROR, (errcode(ERRCODE_NULL_VALUE_NOT_ALLOWED), errmsg("allocator capture returned NULL")));
    }

    return result;
}

PG_FUNCTION_INFO_V1(ankus_test_allocator_delete);
PGDLLEXPORT Datum
ankus_test_allocator_delete(PG_FUNCTION_ARGS)
{
    (void) fcinfo;
    if (fixture_parent != NULL)
    {
        MemoryContextDelete(fixture_parent);
    }

    PG_RETURN_VOID();
}

PG_FUNCTION_INFO_V1(ankus_test_allocator_flags);
PGDLLEXPORT Datum
ankus_test_allocator_flags(PG_FUNCTION_ARGS)
{
    int flags = 0;
    (void) fcinfo;
#ifdef USE_ASSERT_CHECKING
    flags |= 1;
#endif
#ifdef MEMORY_CONTEXT_CHECKING
    flags |= 2;
#endif
    PG_RETURN_INT32(flags);
}
