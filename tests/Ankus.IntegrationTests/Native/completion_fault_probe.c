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

Datum
ankus_test_completion_reporting_allocation_fault(PG_FUNCTION_ARGS)
{
    int stage = PG_GETARG_INT32(0);
    if (stage != 1 && stage != 2)
    {
        ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("unknown completion reporting allocation stage")));
    }

    ankus_memory_completion_ensure();
    MemoryContext owner = AllocSetContextCreate(TopTransactionContext, "completion diagnostic fault", ALLOCSET_SMALL_SIZES);
    MemoryContextCallback *callback = MemoryContextAlloc(owner, sizeof(MemoryContextCallback));
    callback->func = completion_fault_callback;
    callback->arg = NULL;
    MemoryContextRegisterResetCallback(owner, callback);
    completion_diagnostic_fault = stage;
    PG_RETURN_INT32(42);
}
