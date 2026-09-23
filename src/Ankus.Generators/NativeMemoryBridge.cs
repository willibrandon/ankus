namespace Ankus.Generators;

/// <summary>
/// Emits the independent PostgreSQL memory-context capability and its checked native registry.
/// </summary>
internal static class NativeMemoryBridge
{
    /// <summary>
    /// Gets the request envelope, monotonic context/allocation registry, and guarded allocator operations.
    /// </summary>
    internal const string Source = """
        #include <stdint.h>
        #include <stdlib.h>
        #include <string.h>
        #include "utils/memutils.h"
        #include "utils/palloc.h"
        #include "nodes/memnodes.h"

        typedef enum AnkusMemoryOperation
        {
            ANKUS_MEMORY_CURRENT = 1,
            ANKUS_MEMORY_PREDEFINED = 2,
            ANKUS_MEMORY_CREATE = 3,
            ANKUS_MEMORY_PARENT = 4,
            ANKUS_MEMORY_NAME = 5,
            ANKUS_MEMORY_RESET = 6,
            ANKUS_MEMORY_RESET_ONLY = 7,
            ANKUS_MEMORY_RESET_CHILDREN = 8,
            ANKUS_MEMORY_DELETE = 9,
            ANKUS_MEMORY_SWITCH = 10,
            ANKUS_MEMORY_ALLOCATE = 11,
            ANKUS_MEMORY_REALLOCATE = 12,
            ANKUS_MEMORY_FREE = 13,
            ANKUS_MEMORY_READ = 14,
            ANKUS_MEMORY_WRITE = 15,
            ANKUS_MEMORY_CLEAR = 16,
            ANKUS_MEMORY_OWNER = 17,
            ANKUS_MEMORY_EMPTY = 18,
            ANKUS_MEMORY_STATISTICS = 19
        } AnkusMemoryOperation;

        typedef struct AnkusMemoryRequest
        {
            int32 operation;
            int32 flags;
            intptr_t context;
            intptr_t other;
            intptr_t pointer;
            intptr_t data;
            uintptr_t length;
            uintptr_t alignment;
            intptr_t value;
        } AnkusMemoryRequest;

        typedef struct AnkusMemoryResult
        {
            intptr_t context;
            intptr_t pointer;
            intptr_t data;
            uintptr_t length;
            intptr_t value;
        } AnkusMemoryResult;

        typedef struct AnkusMemoryApi AnkusMemoryApi;
        typedef int (*AnkusMemoryInvoke)(AnkusMemoryApi *, AnkusMemoryRequest *, AnkusMemoryResult *, AnkusError *);

        struct AnkusMemoryApi
        {
            intptr_t provider;
            MemoryContext current;
            AnkusMemoryInvoke invoke;
        };

        static int ankus_memory_invoke(AnkusMemoryApi *, AnkusMemoryRequest *, AnkusMemoryResult *, AnkusError *);
        static char ankus_memory_provider;

        static void
        ankus_memory_initialize(AnkusMemoryApi *memory)
        {
            memset(memory, 0, sizeof(*memory));
            memory->provider = (intptr_t) &ankus_memory_provider;
            memory->current = CurrentMemoryContext;
            memory->invoke = ankus_memory_invoke;
        }

        typedef struct AnkusMemoryAllocation AnkusMemoryAllocation;
        typedef struct AnkusMemoryContext AnkusMemoryContext;

        struct AnkusMemoryAllocation
        {
            uint64 id;
            void *pointer;
            Size size;
            uint64 context_id;
            AnkusMemoryAllocation *next;
        };

        struct AnkusMemoryContext
        {
            uint64 id;
            MemoryContext context;
            uint64 parent_id;
            uint64 generation;
            bool alive;
            bool retain_reset;
            bool callback_pending;
            char *owned_name;
            MemoryContextCallback callback;
            AnkusMemoryContext *next;
        };

        static AnkusMemoryContext *ankus_memory_contexts;
        static AnkusMemoryAllocation *ankus_memory_allocations;
        static uint64 ankus_memory_next_context = 1;
        static uint64 ankus_memory_next_allocation = 1;

        static AnkusMemoryContext *
        ankus_memory_context_by_id(uint64 id)
        {
            for (AnkusMemoryContext *entry = ankus_memory_contexts; entry != NULL; entry = entry->next)
            {
                if (entry->id == id && entry->alive)
                {
                    return entry;
                }
            }

            return NULL;
        }

        static AnkusMemoryContext *
        ankus_memory_context_by_pointer(MemoryContext context)
        {
            for (AnkusMemoryContext *entry = ankus_memory_contexts; entry != NULL; entry = entry->next)
            {
                if (entry->alive && entry->context == context)
                {
                    return entry;
                }
            }

            return NULL;
        }

        static void
        ankus_memory_remove_allocations(uint64 context_id)
        {
            AnkusMemoryAllocation **slot = &ankus_memory_allocations;
            while (*slot != NULL)
            {
                AnkusMemoryAllocation *allocation = *slot;
                if (allocation->context_id == context_id)
                {
                    *slot = allocation->next;
                    free(allocation);
                    continue;
                }

                slot = &allocation->next;
            }
        }

        static AnkusMemoryAllocation *
        ankus_memory_allocation_by_id(uint64 id)
        {
            for (AnkusMemoryAllocation *allocation = ankus_memory_allocations; allocation != NULL; allocation = allocation->next)
            {
                if (allocation->id == id && allocation->pointer != NULL && allocation->context_id != 0)
                {
                    AnkusMemoryContext *context = ankus_memory_context_by_id(allocation->context_id);
                    if (context != NULL)
                    {
                        return allocation;
                    }
                }
            }

            return NULL;
        }

        static void
        ankus_memory_remove_context(AnkusMemoryContext *target)
        {
            AnkusMemoryContext **slot = &ankus_memory_contexts;
            while (*slot != NULL)
            {
                if (*slot == target)
                {
                    *slot = target->next;
                    if (target->owned_name != NULL)
                    {
                        MemoryContextSetIdentifier(target->context, NULL);
                        free(target->owned_name);
                    }

                    free(target);
                    return;
                }

                slot = &(*slot)->next;
            }
        }

        static void
        ankus_memory_mark_dead(AnkusMemoryContext *entry)
        {
            if (entry == NULL || !entry->alive)
            {
                return;
            }

            entry->alive = false;
            ankus_memory_remove_allocations(entry->id);
            entry->callback_pending = false;
        }

        static void
        ankus_memory_reset_callback(void *argument)
        {
            AnkusMemoryContext *entry = (AnkusMemoryContext *) argument;
            if (entry == NULL)
            {
                return;
            }

            ankus_memory_remove_allocations(entry->id);
            if (entry->retain_reset)
            {
                entry->callback_pending = false;
                entry->generation++;
                return;
            }

            ankus_memory_mark_dead(entry);
            ankus_memory_remove_context(entry);
        }

        static void
        ankus_memory_free_allocation(AnkusMemoryAllocation *allocation)
        {
            AnkusMemoryAllocation **slot = &ankus_memory_allocations;
            while (*slot != NULL)
            {
                if (*slot == allocation)
                {
                    *slot = allocation->next;
                    free(allocation);
                    return;
                }

                slot = &(*slot)->next;
            }
        }

        static AnkusMemoryContext *
        ankus_memory_register_context(MemoryContext context)
        {
            if (context == NULL)
            {
                return NULL;
            }

            AnkusMemoryContext *existing = ankus_memory_context_by_pointer(context);
            if (existing != NULL)
            {
                return existing;
            }

            AnkusMemoryContext *entry = (AnkusMemoryContext *) calloc(1, sizeof(AnkusMemoryContext));
            if (entry == NULL)
            {
                ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg("out of memory registering an Ankus memory context")));
            }

            entry->id = ankus_memory_next_context++;
            if (entry->id == 0)
            {
                free(entry);
                ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg("Ankus memory context identity space is exhausted")));
            }

            entry->context = context;
            entry->generation = 1;
            entry->alive = true;
            entry->callback.func = ankus_memory_reset_callback;
            entry->callback.arg = entry;
            entry->callback.next = NULL;
            entry->next = ankus_memory_contexts;
            ankus_memory_contexts = entry;
            MemoryContextRegisterResetCallback(context, &entry->callback);
            entry->callback_pending = true;
            return entry;
        }

        static AnkusMemoryContext *
        ankus_memory_context_from_request(AnkusMemoryRequest *request)
        {
            AnkusMemoryContext *entry = ankus_memory_context_by_id((uint64) request->context);
            if (entry == NULL)
            {
                ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                    errmsg("the PostgreSQL memory context handle is stale or belongs to another backend")));
            }

            return entry;
        }

        static uint64
        ankus_memory_context_id(MemoryContext context)
        {
            AnkusMemoryContext *entry = ankus_memory_register_context(context);
            return entry == NULL ? 0 : entry->id;
        }

        static void
        ankus_memory_check_delete(AnkusMemoryContext *entry)
        {
            if (entry->context == TopMemoryContext || entry->context->parent == NULL)
            {
                ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                    errmsg("top-level PostgreSQL memory contexts cannot be deleted by an extension")));
            }

            for (MemoryContext current = CurrentMemoryContext; current != NULL; current = MemoryContextGetParent(current))
            {
                if (current == entry->context)
                {
                    ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                        errmsg("the current memory context or one of its ancestors cannot be deleted")));
                }
            }
        }

        static void
        ankus_memory_register_name(AnkusMemoryContext *entry, const char *name, Size length)
        {
            char *server_name = pg_any_to_server(name, (int) length, PG_UTF8);
            Size server_length = strlen(server_name);
            char *identifier = (char *) malloc(server_length + 1);
            if (identifier == NULL)
            {
                ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg("out of memory naming an Ankus memory context")));
            }

            memcpy(identifier, server_name, server_length + 1);
            if (server_name != name)
            {
                pfree(server_name);
            }

            entry->owned_name = identifier;
            MemoryContextSetIdentifier(entry->context, identifier);
        }

        static void
        ankus_memory_execute(AnkusMemoryApi *api, AnkusMemoryRequest *request, AnkusMemoryResult *result)
        {
            memset(result, 0, sizeof(*result));
            switch ((AnkusMemoryOperation) request->operation)
            {
                case ANKUS_MEMORY_CURRENT:
                {
                    AnkusMemoryContext *entry = ankus_memory_register_context(CurrentMemoryContext);
                    result->context = (intptr_t) entry->id;
                    break;
                }
                case ANKUS_MEMORY_PREDEFINED:
                {
                    MemoryContext context = NULL;
                    switch ((int) request->value)
                    {
                        case 0: context = CurrentMemoryContext; break;
                        case 1: context = TopMemoryContext; break;
                        case 2: context = PortalContext; break;
                        case 3: context = ErrorContext; break;
                        case 4: context = PostmasterContext; break;
                        case 5: context = CacheMemoryContext; break;
                        case 6: context = MessageContext; break;
                        case 7: context = TopTransactionContext; break;
                        case 8: context = CurTransactionContext; break;
                        default: ereport(ERROR, (errmsg("invalid predefined PostgreSQL memory context")));
                    }

                    if (context != NULL)
                    {
                        result->context = (intptr_t) ankus_memory_context_id(context);
                    }

                    break;
                }
                case ANKUS_MEMORY_CREATE:
                {
                    MemoryContext parent = request->context == 0 ? CurrentMemoryContext :
                        ankus_memory_context_from_request(request)->context;
                    MemoryContext context = AllocSetContextCreate(parent, "Ankus memory context", ALLOCSET_DEFAULT_SIZES);
                    MemoryContext caller = CurrentMemoryContext;
                    PG_TRY();
                    {
                        MemoryContextSwitchTo(context);
                        AnkusMemoryContext *entry = ankus_memory_register_context(context);
                        ankus_memory_register_name(entry, (const char *) request->data, (Size) request->length);
                        result->context = (intptr_t) entry->id;
                        MemoryContextSwitchTo(caller);
                    }
                    PG_CATCH();
                    {
                        MemoryContextSwitchTo(caller);
                        MemoryContextDelete(context);
                        PG_RE_THROW();
                    }
                    PG_END_TRY();
                    break;
                }
                case ANKUS_MEMORY_PARENT:
                {
                    AnkusMemoryContext *entry = ankus_memory_context_from_request(request);
                    result->context = (intptr_t) ankus_memory_context_id(MemoryContextGetParent(entry->context));
                    break;
                }
                case ANKUS_MEMORY_NAME:
                {
                    AnkusMemoryContext *entry = ankus_memory_context_from_request(request);
                    const char *name = entry->context->ident == NULL ? entry->context->name : entry->context->ident;
                    char *utf8 = pg_server_to_any(name, strlen(name), PG_UTF8);
                    Size length = strlen(utf8);
                    if (request->data != 0)
                    {
                        if (request->length < length)
                        {
                            ereport(ERROR, (errcode(ERRCODE_DATA_EXCEPTION), errmsg("the context identifier buffer is too small")));
                        }

                        memcpy((void *) request->data, utf8, length);
                    }

                    result->length = length;
                    if (utf8 != name)
                    {
                        pfree(utf8);
                    }

                    break;
                }
                case ANKUS_MEMORY_RESET:
                case ANKUS_MEMORY_RESET_ONLY:
                {
                    AnkusMemoryContext *entry = ankus_memory_context_from_request(request);
                    for (MemoryContext protected = api->current; protected != NULL; protected = MemoryContextGetParent(protected))
                    {
                        if (protected == entry->context)
                        {
                            ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                                errmsg("an active native callback memory context or its ancestor cannot be reset")));
                        }
                    }

                    if (request->operation == ANKUS_MEMORY_RESET && CurrentMemoryContext != entry->context)
                    {
                        ankus_memory_check_delete(entry);
                    }

                    entry->retain_reset = true;
                    PG_TRY();
                    {
                        if (request->operation == ANKUS_MEMORY_RESET)
                        {
                            MemoryContextReset(entry->context);
                        }
                        else
                        {
                            MemoryContextResetOnly(entry->context);
                        }
                    }
                    PG_FINALLY();
                    {
                        entry->retain_reset = false;
                        if (!entry->callback_pending)
                        {
                            MemoryContextRegisterResetCallback(entry->context, &entry->callback);
                            entry->callback_pending = true;
                        }
                    }
                    PG_END_TRY();

                    break;
                }
                case ANKUS_MEMORY_RESET_CHILDREN:
                {
                    AnkusMemoryContext *entry = ankus_memory_context_from_request(request);
                    for (MemoryContext protected = api->current; protected != NULL; protected = MemoryContextGetParent(protected))
                    {
                        if (protected != api->current && protected == entry->context)
                        {
                            ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                                errmsg("children containing an active native callback memory context cannot be reset")));
                        }
                    }

                    for (AnkusMemoryContext *child = ankus_memory_contexts; child != NULL; child = child->next)
                    {
                        for (MemoryContext parent = MemoryContextGetParent(child->context); parent != NULL;
                             parent = MemoryContextGetParent(parent))
                        {
                            if (parent == entry->context)
                            {
                                child->retain_reset = true;
                                break;
                            }
                        }
                    }

                    PG_TRY();
                    {
                        MemoryContextResetChildren(entry->context);
                    }
                    PG_FINALLY();
                    {
                        for (AnkusMemoryContext *child = ankus_memory_contexts; child != NULL; child = child->next)
                        {
                            if (child->retain_reset)
                            {
                                child->retain_reset = false;
                                if (!child->callback_pending)
                                {
                                    MemoryContextRegisterResetCallback(child->context, &child->callback);
                                    child->callback_pending = true;
                                }
                            }
                        }
                    }
                    PG_END_TRY();
                    break;
                }
                case ANKUS_MEMORY_DELETE:
                {
                    AnkusMemoryContext *entry = ankus_memory_context_by_id((uint64) request->context);
                    if (entry != NULL)
                    {
                        ankus_memory_check_delete(entry);
                        for (MemoryContext protected = api->current; protected != NULL; protected = MemoryContextGetParent(protected))
                        {
                            if (protected == entry->context)
                            {
                                ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                                    errmsg("an active native callback memory context or its ancestor cannot be deleted")));
                            }
                        }

                        MemoryContextDelete(entry->context);
                    }
                    break;
                }
                case ANKUS_MEMORY_SWITCH:
                {
                    AnkusMemoryContext *target = ankus_memory_context_from_request(request);
                    MemoryContext previous = MemoryContextSwitchTo(target->context);
                    result->context = (intptr_t) ankus_memory_context_id(previous);
                    break;
                }
                case ANKUS_MEMORY_ALLOCATE:
                {
                    AnkusMemoryContext *entry = ankus_memory_context_from_request(request);
                    int flags = (request->flags & 1) != 0 ? MCXT_ALLOC_ZERO : 0;
                    if ((request->flags & 2) != 0)
                    {
                        flags |= MCXT_ALLOC_NO_OOM;
                    }

                    if ((uint64) request->length > (uint64) SIZE_MAX)
                    {
                        ereport(ERROR, (errmsg("PostgreSQL memory allocation size is too large")));
                    }

                    void *pointer = MemoryContextAllocExtended(entry->context, (Size) request->length, flags);
                    if (pointer == NULL)
                    {
                        break;
                    }

                    AnkusMemoryAllocation *allocation = (AnkusMemoryAllocation *) calloc(1, sizeof(AnkusMemoryAllocation));
                    if (allocation == NULL)
                    {
                        pfree(pointer);
                        ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg("out of memory registering an Ankus allocation")));
                    }

                    allocation->id = ankus_memory_next_allocation++;
                    if (allocation->id == 0)
                    {
                        free(allocation);
                        pfree(pointer);
                        ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg("Ankus allocation identity space is exhausted")));
                    }

                    allocation->pointer = pointer;
                    allocation->size = (Size) request->length;
                    allocation->context_id = entry->id;
                    allocation->next = ankus_memory_allocations;
                    ankus_memory_allocations = allocation;
                    result->context = (intptr_t) entry->id;
                    result->pointer = (intptr_t) allocation->id;
                    result->length = request->length;
                    break;
                }
                case ANKUS_MEMORY_REALLOCATE:
                {
                    AnkusMemoryAllocation *allocation = ankus_memory_allocation_by_id((uint64) request->context);
                    if (allocation == NULL)
                    {
                        ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                            errmsg("the PostgreSQL allocation handle is stale")));
                    }

                    allocation->pointer = repalloc(allocation->pointer, (Size) request->length);
                    allocation->size = (Size) request->length;
                    result->context = (intptr_t) allocation->id;
                    result->length = request->length;
                    break;
                }
                case ANKUS_MEMORY_FREE:
                {
                    AnkusMemoryAllocation *allocation = ankus_memory_allocation_by_id((uint64) request->context);
                    if (allocation != NULL)
                    {
                        pfree(allocation->pointer);
                        allocation->pointer = NULL;
                        allocation->context_id = 0;
                        ankus_memory_free_allocation(allocation);
                    }

                    break;
                }
                case ANKUS_MEMORY_READ:
                case ANKUS_MEMORY_WRITE:
                case ANKUS_MEMORY_CLEAR:
                {
                    AnkusMemoryAllocation *allocation = ankus_memory_allocation_by_id((uint64) request->context);
                    if (allocation == NULL)
                    {
                        ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                            errmsg("the PostgreSQL allocation handle is stale")));
                    }

                    uint64 offset = (uint64) request->value;
                    if (offset > (uint64) allocation->size || (uint64) request->length >
                        (uint64) allocation->size - offset)
                    {
                        ereport(ERROR, (errcode(ERRCODE_DATA_EXCEPTION), errmsg("the PostgreSQL allocation range is outside its chunk")));
                    }

                    unsigned char *destination = (unsigned char *) allocation->pointer + offset;
                    if (request->operation == ANKUS_MEMORY_READ)
                    {
                        if (request->data == 0 && request->length != 0)
                        {
                            ereport(ERROR, (errmsg("the managed memory destination is null")));
                        }

                        if (request->data != 0)
                        {
                            memcpy((void *) request->data, destination, (Size) request->length);
                        }

                        result->pointer = (intptr_t) allocation->pointer;
                    }
                    else if (request->operation == ANKUS_MEMORY_WRITE)
                    {
                        if (request->data == 0 && request->length != 0)
                        {
                            ereport(ERROR, (errmsg("the managed memory source is null")));
                        }

                        if (request->data != 0)
                        {
                            memcpy(destination, (const void *) request->data, (Size) request->length);
                        }
                    }
                    else
                    {
                        memset(destination, 0, (Size) request->length);
                    }

                    result->length = request->length;
                    break;
                }
                case ANKUS_MEMORY_OWNER:
                {
                    AnkusMemoryAllocation *allocation = ankus_memory_allocation_by_id((uint64) request->context);
                    if (allocation == NULL)
                    {
                        ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                            errmsg("the PostgreSQL allocation handle is stale")));
                    }

                    result->context = (intptr_t) ankus_memory_context_id(GetMemoryChunkContext(allocation->pointer));
                    break;
                }
                case ANKUS_MEMORY_EMPTY:
                {
                    AnkusMemoryContext *entry = ankus_memory_context_from_request(request);
                    result->value = MemoryContextIsEmpty(entry->context) ? 1 : 0;
                    break;
                }
                case ANKUS_MEMORY_STATISTICS:
                {
                    AnkusMemoryContext *entry = ankus_memory_context_from_request(request);
                    result->length = MemoryContextMemAllocated(entry->context, true);
                    break;
                }
                default:
                    ereport(ERROR, (errmsg("unknown Ankus memory operation")));
            }
        }

        static int
        ankus_memory_invoke(AnkusMemoryApi *api, AnkusMemoryRequest *request,
            AnkusMemoryResult *result, AnkusError *error)
        {
            MemoryContext caller = CurrentMemoryContext;
            memset(error, 0, sizeof(*error));
            int status = 0;
            PG_TRY();
            {
                ankus_memory_execute(api, request, result);
            }
            PG_CATCH();
            {
                MemoryContext diagnostic = NULL;
                MemoryContextSwitchTo(caller);
                PG_TRY();
                {
                    diagnostic = AllocSetContextCreate(caller, "Ankus memory diagnostics", ALLOCSET_SMALL_SIZES);
                    MemoryContextSwitchTo(diagnostic);
                    ErrorData *data = ankus_copy_error_data();
                    FlushErrorState();
                    ankus_capture_error(data, error);
                    ankus_free_error_data(data);
                    MemoryContextSwitchTo(caller);
                    MemoryContextDelete(diagnostic);
                    status = 1;
                }
                PG_CATCH();
                {
                    MemoryContextSwitchTo(caller);
                    FlushErrorState();
                    ereport(FATAL, (errmsg("Unable to recover PostgreSQL state after a guarded memory failure")));
                }
                PG_END_TRY();
            }
            PG_END_TRY();
            return status;
        }

        """;
}
