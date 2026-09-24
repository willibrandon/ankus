#include "postgres.h"
#include "fmgr.h"
#include "utils/builtins.h"
#include "utils/memutils.h"

PG_MODULE_MAGIC;

PG_FUNCTION_INFO_V1(ankus_test_function_address);
PGDLLEXPORT Datum
ankus_test_function_address(PG_FUNCTION_ARGS)
{
    FmgrInfo function;
    fmgr_info(PG_GETARG_OID(0), &function);
    PG_RETURN_INT64((int64) (intptr_t) function.fn_addr);
}

PG_FUNCTION_INFO_V1(ankus_test_nullable_sum);
PGDLLEXPORT Datum
ankus_test_nullable_sum(PG_FUNCTION_ARGS)
{
    if (fcinfo->flinfo != NULL || fcinfo->context != NULL || fcinfo->resultinfo != NULL)
        ereport(ERROR, (errmsg("Direct native call unexpectedly supplied catalog execution fields")));
    int32 total = 0;
    for (int index = 0; index < PG_NARGS(); index++)
    {
        if (PG_ARGISNULL(index))
            PG_RETURN_NULL();
        total += PG_GETARG_INT32(index);
    }

    PG_RETURN_INT32(total);
}

static MemoryContext fixture_parent = NULL;
static MemoryContextCallback fixture_cleanup;

/* The callback lives outside the transaction whose deletion clears this pointer. */
static void
allocator_fixture_cleanup(void *argument)
{
    (void) argument;
    fixture_parent = NULL;
}

PG_FUNCTION_INFO_V1(ankus_test_allocator_create);
PGDLLEXPORT Datum
ankus_test_allocator_create(PG_FUNCTION_ARGS)
{
    int kind = PG_GETARG_INT32(0);
    Oid callback = PG_GETARG_OID(1);
    char *name = text_to_cstring(PG_GETARG_TEXT_PP(2));
    MemoryContext caller = CurrentMemoryContext;
    MemoryContext child;
    FmgrInfo function;
    Datum result = (Datum) 0;
    LOCAL_FCINFO(call, 0);

    /* Resolve catalog entries and load the managed library before selecting Slab. */
    fmgr_info(callback, &function);
    InitFunctionCallInfoData(*call, &function, 0, InvalidOid, NULL, NULL);
    if (fixture_parent != NULL)
    {
        MemoryContextDelete(fixture_parent);
    }

    fixture_parent = AllocSetContextCreate(TopTransactionContext,
        "Ankus allocator fixture", ALLOCSET_SMALL_SIZES);
    fixture_cleanup.func = allocator_fixture_cleanup;
    fixture_cleanup.arg = NULL;
    MemoryContextRegisterResetCallback(fixture_parent, &fixture_cleanup);
    name = MemoryContextStrdup(fixture_parent, name);
    switch (kind)
    {
        case 0:
            child = SlabContextCreate(fixture_parent, name, 8192, 64);
            break;
        case 1:
#if PG_VERSION_NUM >= 150000
            child = GenerationContextCreate(fixture_parent, name, ALLOCSET_DEFAULT_SIZES);
#else
            child = GenerationContextCreate(fixture_parent, name, 8192);
#endif
            break;
        case 2:
#if PG_VERSION_NUM >= 170000
            child = BumpContextCreate(fixture_parent, name, ALLOCSET_DEFAULT_SIZES);
            break;
#else
            ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED), errmsg("Bump requires PostgreSQL 17 or later")));
#endif
        default:
            ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("unknown test allocator kind")));
    }

    PG_TRY();
    {
        MemoryContextSwitchTo(child);
        result = FunctionCallInvoke(call);
    }
    PG_FINALLY();
    {
        MemoryContextSwitchTo(caller);
    }
    PG_END_TRY();

    if (call->isnull)
    {
        ereport(ERROR, (errcode(ERRCODE_NULL_VALUE_NOT_ALLOWED), errmsg("allocator capture returned NULL")));
    }

    return result;
}

PG_FUNCTION_INFO_V1(ankus_test_allocator_delete);
PGDLLEXPORT Datum
ankus_test_allocator_delete(PG_FUNCTION_ARGS)
{
    (void) fcinfo;
    if (fixture_parent != NULL)
    {
        MemoryContextDelete(fixture_parent);
    }

    PG_RETURN_VOID();
}

PG_FUNCTION_INFO_V1(ankus_test_allocator_flags);
PGDLLEXPORT Datum
ankus_test_allocator_flags(PG_FUNCTION_ARGS)
{
    int flags = 0;
    (void) fcinfo;
#ifdef USE_ASSERT_CHECKING
    flags |= 1;
#endif
#ifdef MEMORY_CONTEXT_CHECKING
    flags |= 2;
#endif
    PG_RETURN_INT32(flags);
}
