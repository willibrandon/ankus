namespace Ankus.Generators;

/// <summary>
/// Supplies PostgreSQL call guards that confine longjmp-based error handling to native frames.
/// </summary>
internal static class GuardedBackend
{
    /// <summary>
    /// Gets structured error transport, reporting, and an atomic SPI execution guard.
    /// </summary>
    internal const string Source = """
        #include "access/xact.h"
        #include "executor/spi.h"
        #include "utils/memutils.h"
        #include "utils/resowner.h"

        typedef struct AnkusError
        {
            int sqlstate;
            char message[2048];
            char detail[2048];
            char hint[1024];
        } AnkusError;

        typedef int (*AnkusExecute)(const char *, int, const AnkusParameter *, int, uint8, int, uint8, AnkusResult *, AnkusError *);

        static void
        ankus_copy_diagnostic(const char *value, char *buffer, int capacity)
        {
            if (value != NULL)
            {
                char *utf8 = pg_server_to_any(value, strlen(value), PG_UTF8);
                int length = pg_encoding_mbcliplen(PG_UTF8, utf8, strlen(utf8), capacity - 1);
                memcpy(buffer, utf8, length);
                buffer[length] = '\0';
                if (utf8 != value)
                {
                    pfree(utf8);
                }
            }
        }

        static void
        ankus_raise_error(const AnkusError *error)
        {
            char *message = pg_any_to_server(error->message, strlen(error->message), PG_UTF8);
            char *detail = pg_any_to_server(error->detail, strlen(error->detail), PG_UTF8);
            char *hint = pg_any_to_server(error->hint, strlen(error->hint), PG_UTF8);
            ereport(ERROR, (errcode(error->sqlstate), errmsg_internal("%s", message),
                detail[0] == '\0' ? 0 : errdetail_internal("%s", detail),
                hint[0] == '\0' ? 0 : errhint("%s", hint)));
        }

        static int
        ankus_spi_execute(const char *command, int length, const AnkusParameter *parameters, int parameter_count,
            uint8 read_only, int limit, uint8 collect_result, AnkusResult *result, AnkusError *error)
        {
            MemoryContext caller_context = CurrentMemoryContext;
            ResourceOwner caller_owner = CurrentResourceOwner;
            int caller_nest_level = GetCurrentTransactionNestLevel();
            volatile int status = 0;
            result->release = ankus_release_result;

            /* Error recovery itself is guarded: it must not jump over the managed caller. */
            PG_TRY();
            {
                PG_TRY();
                {
                    int code;
                    char *sql;
                    BeginInternalSubTransaction(NULL);
                    code = SPI_connect();
                    if (code != SPI_OK_CONNECT)
                    {
                        ereport(ERROR, (errmsg("SPI_connect failed: %s", SPI_result_code_string(code))));
                    }
                    sql = pg_any_to_server(command, length, PG_UTF8);
                    if (parameter_count == 0)
                    {
                        code = SPI_execute(sql, read_only != 0, limit);
                    }
                    else
                    {
                        Oid *types = palloc(sizeof(Oid) * (Size) parameter_count);
                        Datum *values = palloc(sizeof(Datum) * (Size) parameter_count);
                        char *nulls = palloc((Size) parameter_count);
                        for (int index = 0; index < parameter_count; index++)
                        {
                            types[index] = parameters[index].type_oid;
                            values[index] = ankus_parameter_datum(&parameters[index]);
                            nulls[index] = parameters[index].value.is_null ? 'n' : ' ';
                        }
                        code = SPI_execute_with_args(sql, parameter_count, types, values, nulls, read_only != 0, limit);
                    }
                    if (code < 0)
                    {
                        ereport(ERROR, (errmsg("SPI_execute failed: %s", SPI_result_code_string(code))));
                    }
                    if (SPI_processed > PG_INT64_MAX)
                    {
                        ereport(ERROR, (errcode(ERRCODE_NUMERIC_VALUE_OUT_OF_RANGE), errmsg("SPI row count exceeds Int64")));
                    }
                    result->processed = (int64) SPI_processed;
                    if (collect_result)
                    {
                        ankus_collect_result(result, collect_result == 2);
                    }
                    code = SPI_finish();
                    if (code != SPI_OK_FINISH)
                    {
                        ereport(ERROR, (errmsg("SPI_finish failed: %s", SPI_result_code_string(code))));
                    }
                    ReleaseCurrentSubTransaction();
                    MemoryContextSwitchTo(caller_context);
                    CurrentResourceOwner = caller_owner;
                }
                PG_CATCH();
                {
                    ErrorData *data;
                    MemoryContextSwitchTo(caller_context);
                    data = CopyErrorData();
                    FlushErrorState();
                    while (GetCurrentTransactionNestLevel() > caller_nest_level)
                    {
                        RollbackAndReleaseCurrentSubTransaction();
                    }
                    MemoryContextSwitchTo(caller_context);
                    CurrentResourceOwner = caller_owner;
                    error->sqlstate = data->sqlerrcode;
                    ankus_copy_diagnostic(data->message, error->message, sizeof(error->message));
                    ankus_copy_diagnostic(data->detail, error->detail, sizeof(error->detail));
                    ankus_copy_diagnostic(data->hint, error->hint, sizeof(error->hint));
                    FreeErrorData(data);
                    ankus_release_result(result);
                    status = 1;
                }
                PG_END_TRY();
            }
            PG_CATCH();
            {
                /* An unrecoverable recovery error cannot safely return to managed extension code. */
                MemoryContextSwitchTo(caller_context);
                FlushErrorState();
                ereport(FATAL, (errmsg("Unable to recover PostgreSQL state after a guarded SPI failure")));
            }
            PG_END_TRY();
            return status;
        }

        """;
}
