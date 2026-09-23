
/* Only this translation unit interposes allocation boundaries. PostgreSQL and
 * production extensions retain their actual allocator implementations. */
#include <stdlib.h>
#include "utils/memutils.h"
#include "utils/palloc.h"
#include "nodes/memnodes.h"
#if PG_VERSION_NUM >= 160000
#include "utils/memutils_internal.h"
#include "utils/memutils_memorychunk.h"
#endif

static void *fault_records[64];
static int fault_live_records;
static int fault_successful_reservations;
static int fault_released_reservations;
static bool fault_fail_calloc;
static bool fault_return_null;
static bool fault_monitor_storage;
static int fault_storage_calls;
static MemoryContext fault_owner;

static void *
fault_calloc(size_t count, size_t size)
{
    if (fault_fail_calloc)
    {
        fault_fail_calloc = false;
        return NULL;
    }

    void *record = calloc(count, size);
    if (record == NULL)
    {
        return NULL;
    }

    for (int index = 0; index < 64; index++)
    {
        if (fault_records[index] == NULL)
        {
            fault_records[index] = record;
            fault_live_records++;
            fault_successful_reservations++;
            return record;
        }
    }

    free(record);
    elog(ERROR, "registry fault fixture exceeded its bounded record capacity");
    return NULL;
}

static void
fault_free(void *record)
{
    if (record != NULL)
    {
        for (int index = 0; index < 64; index++)
        {
            if (fault_records[index] == record)
            {
                fault_records[index] = NULL;
                fault_live_records--;
                fault_released_reservations++;
                break;
            }
        }
    }

    free(record);
}

static void *
fault_allocate(MemoryContext owner, Size size, int flags)
{
    if (fault_monitor_storage && owner == fault_owner)
    {
        fault_storage_calls++;
        if (fault_return_null)
        {
            fault_return_null = false;
            if ((flags & MCXT_ALLOC_NO_OOM) == 0)
            {
                elog(ERROR, "registry fault fixture requires native NO_OOM for NULL");
            }

            return NULL;
        }
    }

    return MemoryContextAllocExtended(owner, size, flags);
}

#define calloc fault_calloc
#define free fault_free
#define MemoryContextAllocExtended fault_allocate
