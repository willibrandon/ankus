
#undef calloc
#undef free
#undef MemoryContextAllocExtended

static void
fault_require(bool condition, const char *message)
{
    if (!condition)
    {
        elog(ERROR, "registry fault fixture: %s", message);
    }
}

static int
fault_published_count(void)
{
    int count = 0;
    for (AnkusMemoryAllocation *allocation = ankus_memory_allocations; allocation != NULL; allocation = allocation->next)
    {
        count++;
    }

    return count;
}

static AnkusMemoryResult
fault_invoke_success(AnkusMemoryApi *api, AnkusMemoryOperation operation, intptr_t context,
    intptr_t data, uintptr_t length)
{
    AnkusMemoryRequest request = {0};
    AnkusMemoryResult result = {0};
    AnkusError error = {0};
    request.operation = operation;
    request.context = context;
    request.data = data;
    request.length = length;
    int status = ankus_memory_invoke(api, &request, &result, &error);
    if (status != 0)
    {
        ankus_report(&error, ERROR);
    }

    ankus_release_error(&error);
    return result;
}

static int64
fault_read(AnkusMemoryApi *api, intptr_t allocation)
{
    int64 value = 0;
    fault_invoke_success(api, ANKUS_MEMORY_READ, allocation, (intptr_t) &value, sizeof(value));
    return value;
}

PG_FUNCTION_INFO_V1(ankus_test_allocator_registry_fault);
PGDLLEXPORT Datum
ankus_test_allocator_registry_fault(PG_FUNCTION_ARGS)
{
    int mode = PG_GETARG_INT32(0);
    fault_require(mode >= 1 && mode <= 6, "unknown failure scenario");
    fault_require(fault_live_records == 0 && ankus_memory_allocations == NULL && ankus_memory_contexts == NULL,
        "a prior invocation retained registry storage");
    MemoryContext caller = CurrentMemoryContext;
    MemoryContext root = AllocSetContextCreate(caller, "Ankus registry fault root", ALLOCSET_SMALL_SIZES);
    volatile uint64 saved_next = ankus_memory_next_allocation;
    char *volatile report = NULL;
    PG_TRY();
    {
        /* Bump payload remains live while every fault is exercised, including
         * Slab ERROR and raw AllocSet adoption failures. */
        MemoryContext bump = BumpContextCreate(root, "Ankus registry fault control",
            ALLOCSET_SMALL_MINSIZE, ALLOCSET_SMALL_INITSIZE, ALLOCSET_SMALL_MAXSIZE);
        fault_owner = mode == 3 ? SlabContextCreate(root, "Ankus registry fault slab", 8192, 64) :
            mode >= 5 ? AllocSetContextCreate(root, "Ankus registry fault adoption", ALLOCSET_SMALL_SIZES) : bump;
        AnkusMemoryApi api;
        ankus_memory_initialize(&api);
        uint64 bump_id = ankus_memory_context_id(bump);
        uint64 owner_id = ankus_memory_context_id(fault_owner);
        AnkusMemoryResult control = fault_invoke_success(&api, ANKUS_MEMORY_ALLOCATE, (intptr_t) bump_id, 0, 64);
        int64 control_value = 1193046;
        fault_invoke_success(&api, ANKUS_MEMORY_WRITE, control.pointer, (intptr_t) &control_value, sizeof(control_value));
        void *raw = mode >= 5 ? MemoryContextAlloc(fault_owner, 64) : NULL;
        int64 retry_value = 7654321;
        if (raw != NULL)
        {
            memcpy(raw, &retry_value, sizeof(retry_value));
        }

        AnkusMemoryRequest request = {0};
        AnkusMemoryResult result = {0};
        AnkusError error = {0};
        request.operation = mode >= 5 ? ANKUS_MEMORY_ADOPT : ANKUS_MEMORY_ALLOCATE;
        request.context = (intptr_t) owner_id;
        request.pointer = (intptr_t) raw;
        request.length = mode == 3 ? 63 : 64;
        request.flags = mode == 4 ? 2 : 0;
        int before_live = fault_live_records;
        int before_published = fault_published_count();
        int before_allocated = fault_successful_reservations;
        int before_freed = fault_released_reservations;
        saved_next = ankus_memory_next_allocation;
        fault_storage_calls = 0;
        fault_monitor_storage = true;
        fault_fail_calloc = mode == 1 || mode == 5;
        fault_return_null = mode == 4;
        if (mode == 2 || mode == 6)
        {
            ankus_memory_next_allocation = 0;
        }

        int status = ankus_memory_invoke(&api, &request, &result, &error);
        fault_monitor_storage = false;
        if (mode == 2 || mode == 6)
        {
            ankus_memory_next_allocation = saved_next;
        }

        fault_require(!fault_fail_calloc && !fault_return_null, "the requested failure boundary was not reached");
        fault_require(result.context == 0 && result.pointer == 0 && result.length == 0,
            "a failed acquisition published a result handle");
        int allocated = fault_successful_reservations - before_allocated;
        int freed = fault_released_reservations - before_freed;
        int live_delta = fault_live_records - before_live;
        int published_delta = fault_published_count() - before_published;
        int64 observed_control = fault_read(&api, control.pointer);
        int64 observed_raw = 0;
        if (raw != NULL)
        {
            memcpy(&observed_raw, raw, sizeof(observed_raw));
            fault_require(GetMemoryChunkContext(raw) == fault_owner, "failed adoption changed raw ownership");
        }

        fault_require(fault_invoke_success(&api, ANKUS_MEMORY_OWNER, control.pointer, 0, 0).context == (intptr_t) bump_id,
            "the live Bump allocation lost its registered owner");
        char sqlstate[6];
        strlcpy(sqlstate, status == 0 ? "00000" : unpack_sql_state(error.sqlstate), sizeof(sqlstate));
        char message[sizeof(error.message)];
        strlcpy(message, error.message, sizeof(message));
        ankus_release_error(&error);

        request.length = 64;
        fault_require(ankus_memory_invoke(&api, &request, &result, &error) == 0 && result.pointer != 0 && result.length == 64,
            "retry did not publish the expected allocation");
        ankus_release_error(&error);
        fault_require(fault_published_count() == before_published + 1, "retry did not publish exactly one record");
        fault_invoke_success(&api, ANKUS_MEMORY_WRITE, result.pointer, (intptr_t) &retry_value, sizeof(retry_value));
        int64 observed_retry = fault_read(&api, result.pointer);
        if (mode == 3 || mode >= 5)
        {
            fault_invoke_success(&api, ANKUS_MEMORY_FREE, result.pointer, 0, 0);
            fault_require(fault_published_count() == before_published, "free after retry retained its registry record");
        }

        report = psprintf("%d|%s|%s|%d|%d|%d|%d|%d|" INT64_FORMAT "|" INT64_FORMAT "|" INT64_FORMAT,
            status, sqlstate, message, allocated, freed, fault_storage_calls, live_delta, published_delta,
            observed_control, observed_raw, observed_retry);
    }
    PG_FINALLY();
    {
        fault_fail_calloc = false;
        fault_return_null = false;
        fault_monitor_storage = false;
        fault_owner = NULL;
        if (ankus_memory_next_allocation == 0)
        {
            ankus_memory_next_allocation = saved_next;
        }

        MemoryContextSwitchTo(caller);
        MemoryContextDelete(root);
    }
    PG_END_TRY();
    fault_require(fault_live_records == 0 && ankus_memory_allocations == NULL && ankus_memory_contexts == NULL,
        "context deletion retained registry storage");
    /* These emitted helpers are used by other bridge sections, which this
     * bounded module deliberately does not compile. */
    (void) ankus_log_level;
    (void) ankus_log_enabled;
    PG_RETURN_TEXT_P(cstring_to_text(report));
}
