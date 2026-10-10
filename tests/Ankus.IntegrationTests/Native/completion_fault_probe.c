/* Tests actual emitted completion reporting after commit/preparation becomes durable. */
static void
completion_fault_callback(void *argument)
{
    (void) argument;
    AnkusError error = {0};
    error.sqlstate = ERRCODE_INVALID_PARAMETER_VALUE;
    strlcpy(error.message, "callback café", sizeof(error.message));
    ankus_report_completion_cleanup(&error);
    ankus_release_error(&error);
}

PGDLLEXPORT Datum ankus_test_completion_reporting_allocation_fault(PG_FUNCTION_ARGS);
PG_FUNCTION_INFO_V1(ankus_test_completion_reporting_allocation_fault);
PGDLLEXPORT Datum ankus_test_completion_reporting_allocation_remaining(PG_FUNCTION_ARGS);
PG_FUNCTION_INFO_V1(ankus_test_completion_reporting_allocation_remaining);

Datum
ankus_test_completion_reporting_allocation_fault(PG_FUNCTION_ARGS)
{
    int stage = PG_GETARG_INT32(0);
    if (stage != 1 && stage != 2)
    {
        ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("unknown completion reporting allocation stage")));
    }

    MemoryContext owner = AllocSetContextCreate(TopTransactionContext, "completion diagnostic fault", ALLOCSET_SMALL_SIZES);
    MemoryContextCallback *callback = MemoryContextAlloc(owner, sizeof(MemoryContextCallback));
    callback->func = completion_fault_callback;
    callback->arg = NULL;
    MemoryContextRegisterResetCallback(owner, callback);
    completion_diagnostic_fault = stage;
    PG_RETURN_INT32(42);
}

Datum
ankus_test_completion_reporting_allocation_remaining(PG_FUNCTION_ARGS)
{
    (void) fcinfo;
    PG_RETURN_INT32(completion_diagnostic_fault);
}

/* Requests FATAL cleanup reporting at completion with the complete report armed to fail. */
static void
completion_terminal_callback(void *argument)
{
    (void) argument;
    AnkusError error = {0};
    error.sqlstate = MAKE_SQLSTATE('P', '7', '8', '0', '8');
    error.report_level = 12;
    strlcpy(error.message, "terminal fallback report", sizeof(error.message));
    completion_diagnostic_fault = 3;
    ankus_report_completion_cleanup(&error);
    ankus_release_error(&error);
}

PGDLLEXPORT Datum ankus_test_completion_terminal_fault(PG_FUNCTION_ARGS);
PG_FUNCTION_INFO_V1(ankus_test_completion_terminal_fault);

Datum
ankus_test_completion_terminal_fault(PG_FUNCTION_ARGS)
{
    (void) fcinfo;
    /* Tracks whether completion wrote a commit record, as every Ankus memory API user does. */
    ankus_memory_completion_ensure();
    MemoryContext owner = AllocSetContextCreate(TopTransactionContext, "completion terminal fault", ALLOCSET_SMALL_SIZES);
    MemoryContextCallback *callback = MemoryContextAlloc(owner, sizeof(MemoryContextCallback));
    callback->func = completion_terminal_callback;
    callback->arg = NULL;
    MemoryContextRegisterResetCallback(owner, callback);
    PG_RETURN_INT32(42);
}
