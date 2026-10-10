/* Applied only to the emitted memory bridge in the native failure fixture. */
static int completion_diagnostic_fault;

static MemoryContext
completion_create_context(MemoryContext parent, const char *name, Size minimum, Size initial, Size maximum)
{
    if (completion_diagnostic_fault == 1 && strcmp(name, "Ankus cleanup reporting failure") == 0)
    {
        completion_diagnostic_fault = 0;
        ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg("controlled cleanup diagnostic context allocation failure")));
    }

    return AllocSetContextCreateInternal(parent, name, minimum, initial, maximum);
}

static ErrorData *
completion_copy_error_data(void)
{
    if (completion_diagnostic_fault == 2 &&
        strcmp(CurrentMemoryContext->name, "Ankus cleanup reporting failure") == 0)
    {
        completion_diagnostic_fault = 0;
        ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg("controlled cleanup diagnostic copy allocation failure")));
    }

    return ankus_copy_error_data();
}

/* Stage three fails the complete terminal report, as an allocation failure while building it would. */
static void
completion_report(AnkusError *error, int level)
{
    if (completion_diagnostic_fault == 3)
    {
        completion_diagnostic_fault = 0;
        ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg("controlled terminal report construction failure")));
    }

    ankus_report(error, level);
}

/* Preserve the existing allocator/worker wrappers as the ordinary path. */
#undef AllocSetContextCreateInternal
#define AllocSetContextCreateInternal completion_create_context
#define ankus_copy_error_data completion_copy_error_data
#define ankus_report completion_report
