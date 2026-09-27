#include "postgres.h"
#include "fmgr.h"
#include <signal.h>

PG_MODULE_MAGIC;
PG_FUNCTION_INFO_V1(ankus_test_worker_child_signal);

/* Exercise the actual selected-platform signal delivery, including pgkill on Windows. */
PGDLLEXPORT Datum
ankus_test_worker_child_signal(PG_FUNCTION_ARGS)
{
    int32 process = PG_GETARG_INT32(0);
    if (process <= 0)
    {
        ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("a worker process is required")));
    }

    if (kill(process, SIGCHLD) != 0)
    {
        ereport(ERROR, (errcode_for_file_access(), errmsg("could not signal test worker: %m")));
    }

    PG_RETURN_VOID();
}
