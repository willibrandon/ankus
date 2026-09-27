
/* Observe only the private worker owner, including allocations inside PostgreSQL. */
#include "postmaster/bgworker.h"
#include "storage/fd.h"
#include "storage/ipc.h"
#include "storage/latch.h"
#include "libpq/pqsignal.h"

static bool fault_worker_monitor;
static int fault_worker_stage;
static int fault_worker_created;
static int fault_worker_deleted;
static int fault_worker_allocations;
static MemoryContext fault_worker_owner;
static const MemoryContextMethods *fault_worker_original;
static MemoryContextMethods fault_worker_methods;

static void
fault_worker_error(const char *message)
{
    fault_worker_stage = 0;
    ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg_internal("%s", message),
        errdetail("worker registration allocation failed"), errhint("inspect registration before retrying")));
}

static void *
fault_worker_allocate(MemoryContext context, Size size
#if PG_VERSION_NUM >= 170000
    , int flags
#endif
)
{
    fault_worker_allocations++;
    if (fault_worker_stage == 2 && fault_worker_allocations == 1)
    {
        fault_worker_error("controlled worker entry allocation failure");
    }

    if (fault_worker_stage == 3 && fault_worker_allocations == 2)
    {
        fault_worker_error("controlled PostgreSQL worker handle allocation failure");
    }

    return fault_worker_original->alloc(context, size
#if PG_VERSION_NUM >= 170000
        , flags
#endif
    );
}

static void
fault_worker_delete(MemoryContext context)
{
    fault_worker_deleted++;
    fault_worker_owner = NULL;
    const MemoryContextMethods *original = fault_worker_original;
    context->methods = original;
    original->delete_context(context);
}

static MemoryContext
fault_worker_create(MemoryContext parent, const char *name, Size minimum, Size initial, Size maximum)
{
    bool selected = fault_worker_monitor && strcmp(name, "Ankus background-worker handle") == 0;
    if (selected && fault_worker_stage == 1)
    {
        fault_worker_error("controlled worker owner allocation failure");
    }

    MemoryContext owner = AllocSetContextCreateInternal(parent, name, minimum, initial, maximum);
    if (selected)
    {
        if (fault_worker_owner != NULL)
        {
            elog(ERROR, "worker fault fixture has overlapping owners");
        }

        fault_worker_owner = owner;
        fault_worker_created++;
        fault_worker_original = owner->methods;
        fault_worker_methods = *owner->methods;
        fault_worker_methods.alloc = fault_worker_allocate;
        fault_worker_methods.delete_context = fault_worker_delete;
        owner->methods = &fault_worker_methods;
    }

    return owner;
}

#define AllocSetContextCreateInternal fault_worker_create
