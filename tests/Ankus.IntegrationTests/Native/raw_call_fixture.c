/* Appended to actual selected-header call bodies by NativeRawCallFixtureCompiler. */
#include "fmgr.h"
#include "miscadmin.h"
#include "utils/memutils.h"

PG_MODULE_MAGIC;
PG_FUNCTION_INFO_V1(ankus_test_raw_call_address);
PG_FUNCTION_INFO_V1(ankus_test_raw_call_holdoffs);
PG_FUNCTION_INFO_V1(ankus_test_raw_call_error);
PG_FUNCTION_INFO_V1(ankus_test_raw_call_control);

static bool raw_holdoffs_saved = false;
static uint32 raw_interrupt_holdoff;
static uint32 raw_cancel_holdoff;
static MemoryContext raw_memory_context;

PGDLLEXPORT Datum
ankus_test_raw_call_address(PG_FUNCTION_ARGS)
{
    void *address;
    switch (PG_GETARG_INT32(0))
    {
        case 0: address = (void *) ankus_native_call_FullTransactionIdFromU64; break;
        case 1: address = (void *) ankus_native_call_pg_strtoint32; break;
        case 2: address = (void *) ankus_native_call_OidFunctionCall1Coll; break;
        case 3: address = (void *) ankus_native_call_ProcessInterrupts; break;
        case 4: address = (void *) ankus_native_call_pfree; break;
        default: ereport(ERROR, (errmsg("unknown raw call fixture body"))); address = NULL; break;
    }

    PG_RETURN_INT64((int64) (intptr_t) address);
}

PGDLLEXPORT Datum
ankus_test_raw_call_holdoffs(PG_FUNCTION_ARGS)
{
    (void) fcinfo;
    PG_RETURN_INT64((int64) (((uint64) InterruptHoldoffCount << 32) | (uint32) QueryCancelHoldoffCount));
}

PGDLLEXPORT Datum
ankus_test_raw_call_error(PG_FUNCTION_ARGS)
{
    int value = PG_GETARG_INT32(0);
    if (value == 0)
    {
        MemoryContextSwitchTo(TopMemoryContext);
        HOLD_INTERRUPTS();
        HOLD_CANCEL_INTERRUPTS();
        ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE),
            errmsg("raw call failure"), errdetail("owned native detail"), errhint("retry with a valid value")));
    }

    PG_RETURN_INT32(value + 17);
}

PGDLLEXPORT Datum
ankus_test_raw_call_control(PG_FUNCTION_ARGS)
{
    int mode = PG_GETARG_INT32(0);
    if (mode == 1)
    {
        raw_interrupt_holdoff = InterruptHoldoffCount;
        raw_cancel_holdoff = QueryCancelHoldoffCount;
        raw_holdoffs_saved = true;
        HOLD_INTERRUPTS();
        HOLD_CANCEL_INTERRUPTS();
    }
    else if (mode == -1 && raw_holdoffs_saved)
    {
        InterruptHoldoffCount = raw_interrupt_holdoff;
        QueryCancelHoldoffCount = raw_cancel_holdoff;
        raw_holdoffs_saved = false;
    }
    else if (mode == 2)
    {
        raw_memory_context = MemoryContextSwitchTo(TopMemoryContext);
    }
    else if (mode == -2 && raw_memory_context != NULL)
    {
        MemoryContextSwitchTo(raw_memory_context);
        raw_memory_context = NULL;
    }

    PG_RETURN_INT64((int64) (((uint64) InterruptHoldoffCount << 32) | (uint32) QueryCancelHoldoffCount));
}
