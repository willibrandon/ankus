namespace Ankus.Generators;

/// <summary>
/// Emits the independent PostgreSQL memory-context capability and its checked native registry.
/// </summary>
internal static class NativeMemoryBridge
{
    /// <summary>
    /// Gets native cleanup bindings shared by the memory and SPI guards.
    /// </summary>
    internal const string CleanupBinding = """
        static MemoryContext ankus_memory_cleanup_context;
        static bool ankus_memory_error_cleanup;
        """;

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
        #if PG_VERSION_NUM >= 160000
        #include "utils/memutils_internal.h"
        #include "utils/memutils_memorychunk.h"
        #endif

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
            ANKUS_MEMORY_STATISTICS = 19,
            ANKUS_MEMORY_REGISTER_CALLBACK = 20,
            ANKUS_MEMORY_CANCEL_CALLBACK = 21,
            ANKUS_MEMORY_DETACH = 22,
            ANKUS_MEMORY_ADOPT = 23
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

        typedef struct AnkusMemoryContextSizes
        {
            uintptr_t minimum;
            uintptr_t initial;
            uintptr_t maximum;
        } AnkusMemoryContextSizes;

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
        typedef struct AnkusMemoryProtection AnkusMemoryProtection;

        struct AnkusMemoryProtection
        {
            MemoryContext owner;
            bool teardown;
            AnkusMemoryProtection *previous;
        };

        static AnkusMemoryProtection *ankus_memory_protection;

        static void
        ankus_memory_protect(AnkusMemoryProtection *scope, MemoryContext owner, bool teardown)
        {
            scope->owner = owner;
            scope->teardown = teardown;
            scope->previous = ankus_memory_protection;
            ankus_memory_protection = scope;
        }

        static bool
        ankus_memory_contains(MemoryContext ancestor, MemoryContext context)
        {
            for (MemoryContext current = context; current != NULL; current = MemoryContextGetParent(current))
            {
                if (current == ancestor)
                {
                    return true;
                }
            }

            return false;
        }

        static void
        ankus_memory_check_protection(MemoryContext context, bool creation)
        {
            for (AnkusMemoryProtection *scope = ankus_memory_protection; scope != NULL; scope = scope->previous)
            {
                if ((!creation || scope->teardown) &&
                    (ankus_memory_contains(context, scope->owner) ||
                        (scope->teardown && ankus_memory_contains(scope->owner, context))))
                {
                    ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                        errmsg("the memory context overlaps an active native callback or cleanup operation")));
                }
            }
        }

        typedef int (*AnkusMemoryCallbackFunction)(intptr_t, AnkusMemoryApi *, AnkusError *);

        typedef struct AnkusMemoryCallback
        {
            MemoryContextCallback callback;
            MemoryContext owner;
            MemoryContext cleanup_context;
            uint64 context_id;
            intptr_t handle;
            AnkusMemoryCallbackFunction invoke;
            struct AnkusMemoryCallback *next;
        } AnkusMemoryCallback;

        static AnkusMemoryCallback *ankus_memory_callbacks;

        static void
        ankus_memory_callback(void *argument)
        {
            AnkusMemoryCallback *record = argument;
            AnkusMemoryCallback **slot = &ankus_memory_callbacks;
            while (*slot != record)
            {
                slot = &(*slot)->next;
            }

            *slot = record->next;
            intptr_t handle = record->handle;
            AnkusMemoryCallbackFunction invoke = record->invoke;
            MemoryContext owner = record->owner;
            MemoryContext cleanup_context = record->cleanup_context;
            free(record);
            if (handle == 0)
            {
                return;
            }

            AnkusMemoryApi memory = {0};
            AnkusMemoryProtection scope = {0};
            AnkusError error = {0};
            int status;
            MemoryContext previous_cleanup = ankus_memory_cleanup_context;
            bool previous_error_cleanup = ankus_memory_error_cleanup;
            ankus_memory_cleanup_context = cleanup_context;
            ankus_memory_error_cleanup = previous_error_cleanup || ankus_memory_contains(ErrorContext, owner);
            ankus_memory_initialize(&memory);
            ankus_memory_protect(&scope, owner, true);
            PG_TRY();
            {
                status = invoke(handle, &memory, &error);
            }
            PG_FINALLY();
            {
                ankus_memory_cleanup_context = previous_cleanup;
                ankus_memory_error_cleanup = previous_error_cleanup;
                ankus_memory_protection = scope.previous;
            }
            PG_END_TRY();
            if (status != 0)
            {
                ankus_report(&error, ERROR);
            }

            ankus_release_error(&error);
        }

        struct AnkusMemoryAllocation
        {
            uint64 id;
            void *pointer;
            Size size;
            Size alignment;
            int flags;
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
            AnkusMemoryProtection *retain_reset;
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

            if (ankus_memory_next_context == 0)
            {
                free(entry);
                ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg("Ankus memory context identity space is exhausted")));
            }

            entry->id = ankus_memory_next_context++;
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
        ankus_memory_check_infrastructure(MemoryContext context)
        {
            if (context == TopMemoryContext || context == ErrorContext || context == PostmasterContext ||
                context == CacheMemoryContext || context == MessageContext || context == TopTransactionContext ||
                context == CurTransactionContext || context == PortalContext)
            {
                ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                    errmsg("PostgreSQL infrastructure memory contexts cannot be reset or deleted by an extension")));
            }
        }

        static void
        ankus_memory_check_delete(AnkusMemoryContext *entry)
        {
            ankus_memory_check_infrastructure(entry->context);
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
        ankus_memory_validate_sizes(const AnkusMemoryContextSizes *sizes)
        {
            if (sizes->initial < 1024 || sizes->initial != MAXALIGN(sizes->initial) ||
                sizes->maximum < sizes->initial || !AllocHugeSizeIsValid(sizes->maximum) ||
                sizes->maximum != MAXALIGN(sizes->maximum) ||
                (sizes->minimum != 0 && (sizes->minimum < 1024 ||
                    sizes->minimum != MAXALIGN(sizes->minimum) || sizes->minimum > sizes->maximum)))
            {
                ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("invalid AllocSet context sizes")));
            }

        #if PG_VERSION_NUM >= 160000
            if (sizes->maximum > MEMORYCHUNK_MAX_BLOCKOFFSET)
            {
                ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("AllocSet maximum block size exceeds the native chunk offset limit")));
            }
        #endif
        }

        static int
        ankus_memory_validate_allocation(Size size, Size alignment, int options)
        {
            if ((options & ~7) != 0 || (alignment != 0 &&
                ((alignment & (alignment - 1)) != 0 || alignment >= ((Size) 128 * 1024 * 1024))))
            {
                ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("invalid allocation options or alignment")));
            }

            int flags = (options & 1) != 0 ? MCXT_ALLOC_ZERO : 0;
            if ((options & 2) != 0)
            {
                flags |= MCXT_ALLOC_NO_OOM;
            }

            if ((options & 4) != 0)
            {
                flags |= MCXT_ALLOC_HUGE;
            }

            Size limit = (flags & MCXT_ALLOC_HUGE) != 0 ? MaxAllocHugeSize : MaxAllocSize;
            if (size > limit)
            {
                elog(ERROR, "invalid memory alloc request size %zu", size);
            }

            if (alignment > MAXIMUM_ALIGNOF)
            {
        #if PG_VERSION_NUM >= 160000
                Size overhead = PallocAlignedExtraBytes(alignment);
        #ifdef MEMORY_CONTEXT_CHECKING
                overhead++;
        #endif
                if (overhead > limit || size > limit - overhead)
                {
                    elog(ERROR, "invalid aligned memory alloc request size %zu", size);
                }

        #if PG_VERSION_NUM < 160015 || (PG_VERSION_NUM >= 170000 && PG_VERSION_NUM < 170011) || (PG_VERSION_NUM >= 180000 && PG_VERSION_NUM < 180006)
                if ((flags & MCXT_ALLOC_NO_OOM) != 0)
                {
                    ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED),
                        errmsg("aligned no-OOM allocation requires PostgreSQL 16.15, 17.11, 18.6 or later")));
                }
        #endif
        #if PG_VERSION_NUM >= 190000 && PG_VERSION_NUM < 190001
                /* PostgreSQL prereleases share PG_VERSION_NUM, including the unfixed betas. */
                if ((flags & MCXT_ALLOC_NO_OOM) != 0 &&
                    (strstr(PG_VERSION, "devel") != NULL ||
                        (strncmp(PG_VERSION, "19beta", 6) == 0 && atoi(PG_VERSION + 6) < 3)))
                {
                    ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED),
                        errmsg("aligned no-OOM allocation requires PostgreSQL 19 beta 3 or later")));
                }
        #endif
        #else
                ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED), errmsg("aligned allocation requires PostgreSQL 16 or later")));
        #endif
            }

            return flags;
        }

        static void *
        ankus_memory_allocate(MemoryContext context, Size size, Size alignment, int options)
        {
            int flags = ankus_memory_validate_allocation(size, alignment, options);
        #if PG_VERSION_NUM >= 160000
            if (alignment > MAXIMUM_ALIGNOF)
            {
                return MemoryContextAllocAligned(context, size, alignment, flags);
            }
        #endif

            return MemoryContextAllocExtended(context, size, flags);
        }

        static AnkusMemoryAllocation *
        ankus_memory_register_allocation(AnkusMemoryContext *context, void *pointer,
            Size size, Size alignment, int options, bool release_on_failure)
        {
            AnkusMemoryAllocation *allocation = calloc(1, sizeof(*allocation));
            if (allocation == NULL || ankus_memory_next_allocation == 0)
            {
                free(allocation);
                if (release_on_failure)
                {
                    pfree(pointer);
                }

                ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg("unable to register an Ankus allocation")));
            }

            allocation->id = ankus_memory_next_allocation++;
            allocation->pointer = pointer;
            allocation->size = size;
            allocation->alignment = alignment;
            allocation->flags = options & 4;
            allocation->context_id = context->id;
            allocation->next = ankus_memory_allocations;
            ankus_memory_allocations = allocation;
            return allocation;
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
                    ankus_memory_check_protection(parent, true);
                    const AnkusMemoryContextSizes defaults = {ALLOCSET_DEFAULT_MINSIZE,
                        ALLOCSET_DEFAULT_INITSIZE, ALLOCSET_DEFAULT_MAXSIZE};
                    const AnkusMemoryContextSizes *sizes = request->pointer == 0 ? &defaults :
                        (const AnkusMemoryContextSizes *) request->pointer;
                    ankus_memory_validate_sizes(sizes);
                    MemoryContext context = AllocSetContextCreate(parent, "Ankus memory context",
                        sizes->minimum, sizes->initial, sizes->maximum);
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
                    ankus_memory_check_infrastructure(entry->context);
                    ankus_memory_check_protection(entry->context, false);
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

                    AnkusMemoryProtection scope = {0};
                    ankus_memory_protect(&scope, entry->context, true);
                    entry->retain_reset = &scope;
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
                        ankus_memory_protection = scope.previous;
                        entry->retain_reset = NULL;
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
                    ankus_memory_check_infrastructure(entry->context);
                    ankus_memory_check_protection(entry->context, false);
                    for (MemoryContext protected = api->current; protected != NULL; protected = MemoryContextGetParent(protected))
                    {
                        if (protected != api->current && protected == entry->context)
                        {
                            ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                                errmsg("children containing an active native callback memory context cannot be reset")));
                        }
                    }

                    AnkusMemoryProtection scope = {0};
                    ankus_memory_protect(&scope, entry->context, true);
                    for (AnkusMemoryContext *child = ankus_memory_contexts; child != NULL; child = child->next)
                    {
                        for (MemoryContext parent = MemoryContextGetParent(child->context); parent != NULL;
                             parent = MemoryContextGetParent(parent))
                        {
                            if (parent == entry->context)
                            {
                                child->retain_reset = &scope;
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
                        ankus_memory_protection = scope.previous;
                        for (AnkusMemoryContext *child = ankus_memory_contexts; child != NULL; child = child->next)
                        {
                            if (child->retain_reset == &scope)
                            {
                                child->retain_reset = NULL;
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
                        ankus_memory_check_protection(entry->context, false);
                        ankus_memory_check_delete(entry);
                        for (MemoryContext protected = api->current; protected != NULL; protected = MemoryContextGetParent(protected))
                        {
                            if (protected == entry->context)
                            {
                                ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                                    errmsg("an active native callback memory context or its ancestor cannot be deleted")));
                            }
                        }

                        AnkusMemoryProtection scope = {0};
                        ankus_memory_protect(&scope, entry->context, true);
                        PG_TRY();
                        {
                            MemoryContextDelete(entry->context);
                        }
                        PG_FINALLY();
                        {
                            ankus_memory_protection = scope.previous;
                        }
                        PG_END_TRY();
                    }
                    break;
                }
                case ANKUS_MEMORY_SWITCH:
                {
                    AnkusMemoryContext *target = ankus_memory_context_from_request(request);
                    for (AnkusMemoryProtection *scope = ankus_memory_protection; scope != NULL; scope = scope->previous)
                    {
                        if (scope->teardown && ankus_memory_contains(scope->owner, target->context))
                        {
                            ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                                errmsg("a context undergoing native cleanup cannot become current")));
                        }
                    }

                    MemoryContext previous = MemoryContextSwitchTo(target->context);
                    result->context = (intptr_t) ankus_memory_context_id(previous);
                    break;
                }
                case ANKUS_MEMORY_ALLOCATE:
                {
                    AnkusMemoryContext *entry = ankus_memory_context_from_request(request);
                    void *pointer = ankus_memory_allocate(entry->context, (Size) request->length,
                        (Size) request->alignment, request->flags);
                    if (pointer == NULL)
                    {
                        break;
                    }

                    AnkusMemoryAllocation *allocation = ankus_memory_register_allocation(entry, pointer,
                        (Size) request->length, (Size) request->alignment, request->flags, true);
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

                    if ((request->flags & ~3) != 0)
                    {
                        ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("invalid allocation resize options")));
                    }

                    Size size = (Size) request->length;
                    int options = allocation->flags | (request->flags & 2);
                    int flags = ankus_memory_validate_allocation(size, allocation->alignment, options);
                    void *pointer;
                    if (allocation->alignment > MAXIMUM_ALIGNOF || (flags & MCXT_ALLOC_NO_OOM) != 0)
                    {
                        /* Keep the old chunk intact until replacement succeeds, including
                         * older native aligned realloc implementations that lose flags. */
                        MemoryContext owner = GetMemoryChunkContext(allocation->pointer);
                        pointer = ankus_memory_allocate(owner, size, allocation->alignment, options);
                        if (pointer == NULL)
                        {
                            break;
                        }

                        memcpy(pointer, allocation->pointer, Min(size, allocation->size));
                        pfree(allocation->pointer);
                    }
                    else
                    {
                        pointer = (flags & MCXT_ALLOC_HUGE) != 0 ? repalloc_huge(allocation->pointer, size) :
                            repalloc(allocation->pointer, size);
                    }

                    if ((request->flags & 1) != 0 && size > allocation->size)
                    {
                        memset((char *) pointer + allocation->size, 0, size - allocation->size);
                    }

                    allocation->pointer = pointer;
                    allocation->size = size;
                    result->context = (intptr_t) allocation->id;
                    result->pointer = (intptr_t) allocation->id;
                    result->length = request->length;
                    break;
                }
                case ANKUS_MEMORY_DETACH:
                {
                    AnkusMemoryAllocation *allocation = ankus_memory_allocation_by_id((uint64) request->context);
                    if (allocation == NULL)
                    {
                        ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                            errmsg("the PostgreSQL allocation handle is stale")));
                    }

                    result->pointer = (intptr_t) allocation->pointer;
                    ankus_memory_free_allocation(allocation);
                    break;
                }
                case ANKUS_MEMORY_ADOPT:
                {
                    AnkusMemoryContext *entry = ankus_memory_context_from_request(request);
                    void *pointer = (void *) request->pointer;
                    if (pointer == NULL || (request->flags & ~4) != 0)
                    {
                        ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("invalid allocation adoption request")));
                    }

                    ankus_memory_validate_allocation((Size) request->length, (Size) request->alignment, request->flags);
                    for (AnkusMemoryAllocation *existing = ankus_memory_allocations; existing != NULL; existing = existing->next)
                    {
                        if (existing->pointer == pointer)
                        {
                            ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("the PostgreSQL allocation already has a checked owner")));
                        }
                    }

                    /* The caller guarantees live palloc provenance and the exact accessible
                     * length. Chunk headers cannot validate arbitrary pointers or lengths. */
                    if (GetMemoryChunkContext(pointer) != entry->context)
                    {
                        ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("the PostgreSQL allocation belongs to a different memory context")));
                    }

                    AnkusMemoryAllocation *allocation = ankus_memory_register_allocation(entry, pointer,
                        (Size) request->length, (Size) request->alignment, request->flags, false);
                    result->context = (intptr_t) entry->id;
                    result->pointer = (intptr_t) allocation->id;
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
                case ANKUS_MEMORY_REGISTER_CALLBACK:
                {
                    AnkusMemoryContext *entry = ankus_memory_context_from_request(request);
                    AnkusMemoryCallback *record = calloc(1, sizeof(*record));
                    if (record == NULL)
                    {
                        ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg("out of memory registering a memory callback")));
                    }

                    record->owner = entry->context;
                    /* This ancestor remains alive until this callback returns, even when
                     * AtSubCleanup_Memory has already changed CurTransactionContext to its parent. */
                    record->cleanup_context = ankus_memory_contains(CurTransactionContext, entry->context)
                        ? CurTransactionContext : NULL;
                    record->context_id = entry->id;
                    record->handle = request->other;
                    record->invoke = (AnkusMemoryCallbackFunction) request->pointer;
                    record->callback.func = ankus_memory_callback;
                    record->callback.arg = record;
                    record->next = ankus_memory_callbacks;
                    ankus_memory_callbacks = record;
                    MemoryContextRegisterResetCallback(entry->context, &record->callback);
                    break;
                }
                case ANKUS_MEMORY_CANCEL_CALLBACK:
                {
                    for (AnkusMemoryCallback *record = ankus_memory_callbacks; record != NULL; record = record->next)
                    {
                        if (record->context_id == (uint64) request->context && record->handle == request->other)
                        {
                            record->handle = 0;
                            break;
                        }
                    }

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
            memset(error, 0, sizeof(*error));
            if (ankus_memory_error_cleanup)
            {
                /* FlushErrorState would recursively reclaim this callback's owner and
                 * consume an outer error reporter's private error stack. Do not ereport. */
                error->sqlstate = ERRCODE_OBJECT_IN_USE;
                strlcpy(error->message, "Guarded memory operations are unavailable during ErrorContext cleanup", sizeof(error->message));
                return 1;
            }

            MemoryContext caller = CurrentMemoryContext;
            MemoryContext recovery = ankus_memory_contains(ErrorContext, caller) ? ErrorContext : caller;
            uint32 interrupt_holdoff = InterruptHoldoffCount;
            uint32 cancel_holdoff = QueryCancelHoldoffCount;
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
                    diagnostic = AllocSetContextCreate(TopMemoryContext, "Ankus memory diagnostics", ALLOCSET_SMALL_SIZES);
                    MemoryContextSwitchTo(diagnostic);
                    ErrorData *data = ankus_copy_error_data();
                    FlushErrorState();
                    ankus_capture_error(data, error);
                    ankus_free_error_data(data);
                    MemoryContextSwitchTo(recovery);
                    MemoryContextDelete(diagnostic);
                    status = 1;
                }
                PG_CATCH();
                {
                    MemoryContextSwitchTo(recovery);
                    FlushErrorState();
                    ereport(FATAL, (errmsg("Unable to recover PostgreSQL state after a guarded memory failure")));
                }
                PG_END_TRY();
            }
            PG_END_TRY();
            InterruptHoldoffCount = interrupt_holdoff;
            QueryCancelHoldoffCount = cancel_holdoff;
            return status;
        }

        """;
}
