/* Appended to actual selected-header call bodies by NativeRawCallFixtureCompiler. */
#include "fmgr.h"
#include "miscadmin.h"
#include "storage/lwlock.h"
#include "utils/memutils.h"
#include "utils/builtins.h"

PG_MODULE_MAGIC;
PG_FUNCTION_INFO_V1(ankus_test_raw_call_address);
PG_FUNCTION_INFO_V1(ankus_test_raw_call_holdoffs);
PG_FUNCTION_INFO_V1(ankus_test_raw_call_error);
PG_FUNCTION_INFO_V1(ankus_test_raw_call_control);
PG_FUNCTION_INFO_V1(ankus_test_raw_call_lock_held);
PG_FUNCTION_INFO_V1(ankus_test_log_arm);
PG_FUNCTION_INFO_V1(ankus_test_log_holdoff);

static bool raw_holdoffs_saved = false;
static uint32 raw_interrupt_holdoff;
static uint32 raw_cancel_holdoff;
static MemoryContext raw_memory_context;
static LWLock raw_error_lock;
static bool raw_error_lock_initialized;
static emit_log_hook_type log_previous_hook;
static char log_marker[128];
static int log_mode;
static uint32 log_observed_holdoff;

/* A backend-local, one-report probe exercises the actual PostgreSQL reporter. */
static void
log_probe(ErrorData *data)
{
    if (log_previous_hook != NULL)
    {
        log_previous_hook(data);
    }

    if (data->message != NULL && strcmp(data->message, log_marker) == 0)
    {
        emit_log_hook = log_previous_hook;
        log_observed_holdoff = InterruptHoldoffCount;
        if (log_mode == 1)
        {
            QueryCancelPending = true;
            InterruptPending = true;
        }
        else if (log_mode == 2)
        {
            ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE),
                errmsg("native report hook failure")));
        }
    }
}

PGDLLEXPORT Datum
ankus_test_log_arm(PG_FUNCTION_ARGS)
{
    char *marker = text_to_cstring(PG_GETARG_TEXT_PP(0));
    if (strlen(marker) >= sizeof(log_marker) || emit_log_hook == log_probe)
    {
        ereport(ERROR, (errmsg("invalid or already armed report probe")));
    }

    strlcpy(log_marker, marker, sizeof(log_marker));
    pfree(marker);
    log_mode = PG_GETARG_INT32(1);
    log_observed_holdoff = 0;
    log_previous_hook = emit_log_hook;
    emit_log_hook = log_probe;
    PG_RETURN_VOID();
}

PGDLLEXPORT Datum
ankus_test_log_holdoff(PG_FUNCTION_ARGS)
{
    (void) fcinfo;
    PG_RETURN_INT64(log_observed_holdoff);
}

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
        if (!raw_error_lock_initialized)
        {
            int tranche;
#if PG_VERSION_NUM >= 190000
            tranche = LWLockNewTrancheId("ankus raw error fixture");
#else
            tranche = LWLockNewTrancheId();
            LWLockRegisterTranche(tranche, "ankus raw error fixture");
#endif
            LWLockInitialize(&raw_error_lock, tranche);
            raw_error_lock_initialized = true;
        }

        LWLockAcquire(&raw_error_lock, LW_EXCLUSIVE);
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

PGDLLEXPORT Datum
ankus_test_raw_call_lock_held(PG_FUNCTION_ARGS)
{
    (void) fcinfo;
    PG_RETURN_BOOL(raw_error_lock_initialized && LWLockHeldByMe(&raw_error_lock));
}
