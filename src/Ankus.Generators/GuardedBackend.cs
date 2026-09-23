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

        typedef int (*AnkusExecute)(AnkusRequest *, AnkusResult *, AnkusError *);

        static int
        ankus_spi_execute(AnkusRequest *request, AnkusResult *result, AnkusError *error)
        {
            MemoryContext caller_context = CurrentMemoryContext;
            ResourceOwner caller_owner = CurrentResourceOwner;
            int caller_nest_level = GetCurrentTransactionNestLevel();
            volatile int status = 0;
            volatile bool retained_plan = false;
            bool standalone = request->session_id == 0;
            bool quote = ankus_is_quote_request(request->operation);
            bool reporting = request->operation == ANKUS_SPI_REPORT;
            bool temporal = request->operation == ANKUS_SPI_TEMPORAL;
            bool numeric = request->operation == ANKUS_SPI_NUMERIC;
            bool network = request->operation == ANKUS_SPI_NETWORK;
            bool geometry = request->operation == ANKUS_SPI_GEOMETRY;
            bool range = request->operation == ANKUS_SPI_RANGE;
            bool enumeration = request->operation == ANKUS_SPI_ENUM;
            bool tuple = request->operation == ANKUS_SPI_TUPLE;
            bool direct = quote || reporting || temporal || numeric || network || geometry || range || enumeration || tuple;
            result->release = ankus_release_result;

            if (request->operation == ANKUS_SPI_GUC_READ)
            {
                result->release = NULL;
                if (ankus_read_guc != NULL)
                    return ankus_read_guc(request->command, request->scalar_operation, &result->text, error);
                error->sqlstate = ERRCODE_UNDEFINED_OBJECT;
                strlcpy(error->message, "This extension has no declared configuration parameters", sizeof(error->message));
                return 1;
            }

            if (request->operation == ANKUS_SPI_IS_LOG_ENABLED)
            {
                result->processed = ankus_log_enabled(ankus_log_level(request->log_level));
                return 0;
            }

            if (request->operation == ANKUS_SPI_CLOSE_SESSION && ankus_session != NULL &&
                ankus_session->identity == request->session_id)
            {
                caller_context = ankus_session->caller_context;
                caller_owner = ankus_session->caller_owner;
                caller_nest_level = ankus_session->caller_nest_level;
            }

            /* Error recovery itself is guarded: it must not jump over the managed caller. */
            PG_TRY();
            {
                PG_TRY();
                {
                    int code;
                    MemoryContext operation_context = NULL;
                    if (request->cleanup_only)
                    {
                        /* Abort cleanup releases owned resources without SQL or a new subtransaction.
                         * The outer recovery guard also protects diagnostic capture on this path. */
                        if (request->session_id != 0 ||
                            (request->operation != ANKUS_SPI_FREE_PLAN && request->operation != ANKUS_SPI_CLOSE_CURSOR))
                            ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                                errmsg("Only owned resource release is allowed during Ankus abort cleanup")));
                        code = ankus_run_spi_request(request, result);
                        if (code < 0)
                            ereport(ERROR, (errmsg("Ankus resource cleanup failed: %s", SPI_result_code_string(code))));
                    }
                    else if (request->operation == ANKUS_SPI_OPEN_SESSION)
                    {
                        /* Keep this subtransaction until close so partial SPI_connect failures can be rolled back safely. */
                        BeginInternalSubTransaction(NULL);
                        code = SPI_connect();
                        if (code != SPI_OK_CONNECT)
                        {
                            ereport(ERROR, (errmsg("SPI_connect failed: %s", SPI_result_code_string(code))));
                        }

                        ankus_register_trigger_data();
                        ankus_register_session(request, caller_context, caller_owner, caller_nest_level);
                    }
                    else
                    {
                        BeginInternalSubTransaction(NULL);
                        if (standalone && !direct)
                        {
                            code = SPI_connect();
                            if (code != SPI_OK_CONNECT)
                            {
                                ereport(ERROR, (errmsg("SPI_connect failed: %s", SPI_result_code_string(code))));
                            }

                            ankus_register_trigger_data();
                        }
                        else if (!standalone)
                        {
                            ankus_require_session(request->session_id);
                        }

                        if ((direct || !standalone) && request->operation != ANKUS_SPI_CLOSE_SESSION)
                        {
                            operation_context = AllocSetContextCreate(CurrentMemoryContext, "Ankus SPI operation", ALLOCSET_SMALL_SIZES);
                            MemoryContextSwitchTo(operation_context);
                        }

                        if (request->operation != ANKUS_SPI_CLOSE_SESSION)
                        {
                            if (reporting)
                            {
                                ankus_report(request->diagnostic, ankus_log_level(request->log_level));
                                code = 0;
                            }
                            else if (temporal)
                            {
                                ankus_temporal_operation(request, result);
                                code = 0;
                            }
                            else if (numeric)
                            {
                                ankus_numeric_operation(request, result);
                                code = 0;
                            }
                            else if (network)
                            {
                                ankus_network_operation(request, result);
                                code = 0;
                            }
                            else if (geometry)
                            {
                                ankus_geometry_operation(request, result);
                                code = 0;
                            }
                            else if (enumeration)
                            {
                                ankus_enum_operation(request, result);
                                code = 0;
                            }
                            else if (tuple)
                            {
                                ankus_tuple_operation(request, result);
                                code = 0;
                            }
                            else if (range)
                            {
                                ankus_range_operation(request, result);
                                code = 0;
                            }
                            else
                            {
                                code = quote ? ankus_quote(request, result) : ankus_run_spi_request(request, result);
                            }

                            if (code < 0)
                            {
                                ereport(ERROR, (errmsg("SPI operation failed: %s", SPI_result_code_string(code))));
                            }

                            retained_plan = request->operation == ANKUS_SPI_KEEP_PLAN;
                            if (request->operation == ANKUS_SPI_EXECUTE || request->operation == ANKUS_SPI_EXECUTE_PLAN ||
                                request->operation == ANKUS_SPI_FETCH_CURSOR || request->operation == ANKUS_SPI_EXPLAIN)
                            {
                                if (SPI_processed > PG_INT64_MAX)
                                {
                                    ereport(ERROR, (errcode(ERRCODE_NUMERIC_VALUE_OUT_OF_RANGE), errmsg("SPI row count exceeds Int64")));
                                }

                                result->processed = (int64) SPI_processed;
                                if (request->result_mode)
                                {
                                    ankus_collect_result(result, request->result_mode == 2);
                                }

                                SPI_freetuptable(SPI_tuptable);
                            }
                        }

                        if (operation_context != NULL)
                        {
                            MemoryContextSwitchTo(CurTransactionContext);
                            MemoryContextDelete(operation_context);
                        }

                        if ((standalone && !direct) || request->operation == ANKUS_SPI_CLOSE_SESSION)
                        {
                            code = SPI_finish();
                            if (code != SPI_OK_FINISH)
                            {
                                ereport(ERROR, (errmsg("SPI_finish failed: %s", SPI_result_code_string(code))));
                            }

                            if (request->operation == ANKUS_SPI_CLOSE_SESSION)
                            {
                                request->session_id = 0;
                            }
                        }

                        ReleaseCurrentSubTransaction();
                        if (request->operation == ANKUS_SPI_CLOSE_SESSION)
                        {
                            ReleaseCurrentSubTransaction();
                        }
                    }

                    MemoryContextSwitchTo(caller_context);
                    if (request->operation != ANKUS_SPI_OPEN_SESSION)
                    {
                        CurrentResourceOwner = caller_owner;
                    }
                }
                PG_CATCH();
                {
                    ErrorData *data;
                    MemoryContext diagnostic_context;
                    MemoryContextSwitchTo(caller_context);
                    diagnostic_context = AllocSetContextCreate(caller_context, "Ankus error diagnostics", ALLOCSET_SMALL_SIZES);
                    MemoryContextSwitchTo(diagnostic_context);
                    data = CopyErrorData();
                    FlushErrorState();
                    while (GetCurrentTransactionNestLevel() > caller_nest_level)
                    {
                        RollbackAndReleaseCurrentSubTransaction();
                    }

                    MemoryContextSwitchTo(diagnostic_context);
                    CurrentResourceOwner = caller_owner;
                    if ((request->operation == ANKUS_SPI_PREPARE || retained_plan) && request->plan != NULL)
                    {
                        if (request->session_id != 0)
                        {
                            ankus_detach_session_plan(request->plan);
                        }

                        SPI_freeplan(request->plan);
                        request->plan = NULL;
                    }

                    ankus_capture_error(data, error);
                    MemoryContextSwitchTo(caller_context);
                    MemoryContextDelete(diagnostic_context);
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
