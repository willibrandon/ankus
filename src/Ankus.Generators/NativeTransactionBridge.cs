namespace Ankus.Generators;

/// <summary>
/// Emits PostgreSQL transaction and subtransaction dispatch into managed per-transaction registrations.
/// </summary>
internal static class NativeTransactionBridge
{
    /// <summary>
    /// Gets native callback registration, stable event mapping, capability binding, and managed error reporting.
    /// </summary>
    internal const string Source = """
        #include "access/xact.h"
        #include "utils/snapmgr.h"

        typedef int (*AnkusTransactionManaged)(int, int, uint32, uint32, AnkusError *,
            AnkusExecute, intptr_t, AnkusMemoryApi *);
        typedef int (*AnkusTransactionLog)(int, int, AnkusError *, AnkusError *, int *);

        static int ankus_spi_execute(AnkusRequest *, AnkusResult *, AnkusError *);
        static AnkusTransactionManaged ankus_transaction_managed;
        static bool ankus_transaction_registered;
        static bool ankus_subtransaction_registered;
        static int ankus_internal_subtransaction_depth;

        typedef struct AnkusTransactionFrame
        {
            struct AnkusTransactionFrame *previous;
            AnkusError failure;
            bool direct_spi;
            bool failed;
        } AnkusTransactionFrame;

        static AnkusTransactionFrame *ankus_transaction_frame;

        static void
        ankus_transaction_id_operation(AnkusResult *result)
        {
            result->processed = (int64) U64FromFullTransactionId(ReadNextFullTransactionId());
        }

        static int
        ankus_transaction_log(int operation, int level, AnkusError *report, AnkusError *error, int *enabled)
        {
            MemoryContext caller = CurrentMemoryContext;
            MemoryContext recovery = ankus_error_recovery_context(caller);
            uint32 interrupt_holdoff = InterruptHoldoffCount;
            uint32 cancel_holdoff = QueryCancelHoldoffCount;
            volatile int status = 0;
            PG_TRY();
            {
                PG_TRY();
                {
                    if ((operation != 0 && operation != 1) || level < 0 || level > 12 || enabled == NULL)
                    {
                        ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE),
                            errmsg("Invalid transaction callback logging request")));
                    }

                    if (operation == 1 && (level >= 10 || report == NULL))
                    {
                        ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE),
                            errmsg("Terminal transaction callback messages must unwind managed code before reporting")));
                    }

                    *enabled = ankus_log_enabled(ankus_log_level(level)) ? 1 : 0;
                    if (operation == 1 && *enabled != 0)
                    {
                        ankus_report(report, ankus_log_level(level));
                    }
                }
                PG_CATCH();
                {
                    ErrorData *data;
                    MemoryContext diagnostic;
                    MemoryContextSwitchTo(recovery);
                    diagnostic = AllocSetContextCreate(TopMemoryContext, "Ankus transaction callback diagnostics", ALLOCSET_SMALL_SIZES);
                    MemoryContextSwitchTo(diagnostic);
                    data = ankus_copy_error_data();
                    FlushErrorState();
                    ankus_capture_error(data, error);
                    ankus_free_error_data(data);
                    MemoryContextSwitchTo(recovery);
                    MemoryContextDelete(diagnostic);
                    status = 1;
                }
                PG_END_TRY();
            }
            PG_CATCH();
            {
                MemoryContextSwitchTo(recovery);
                FlushErrorState();
                ereport(FATAL, (errmsg("Unable to recover an Ankus transaction callback logging failure")));
            }
            PG_END_TRY();
            InterruptHoldoffCount = interrupt_holdoff;
            QueryCancelHoldoffCount = cancel_holdoff;
            return status;
        }

        static void
        ankus_transaction_report(AnkusError *error, int level)
        {
            PG_TRY();
            {
                ankus_report(error, level);
            }
            PG_FINALLY();
            {
                ankus_release_error(error);
            }
            PG_END_TRY();
        }

        static void
        ankus_transaction_dispatch(int kind, int event, uint32 subtransaction_id,
            uint32 parent_subtransaction_id, bool reversible, bool sql)
        {
            AnkusError error = {0};
            AnkusMemoryApi memory = {0};
            AnkusMemoryProtection protection = {0};
            AnkusTransactionFrame frame = {0};
            bool snapshot_owned = false;
            int status;
            if (ankus_transaction_managed == NULL)
            {
                return;
            }

            if (sql && !ActiveSnapshotSet())
            {
                PushActiveSnapshot(GetTransactionSnapshot());
                snapshot_owned = true;
            }

            frame.previous = ankus_transaction_frame;
            frame.direct_spi = sql;
            ankus_transaction_frame = &frame;
            ankus_memory_initialize(&memory);
            ankus_memory_protect(&protection, CurrentMemoryContext, false);
            status = ankus_transaction_managed(kind, event, subtransaction_id, parent_subtransaction_id,
                &error, sql ? ankus_spi_execute : NULL, (intptr_t) ankus_transaction_log, &memory);
            ankus_transaction_frame = frame.previous;
            ankus_memory_protection = protection.previous;
            if (snapshot_owned)
            {
                PopActiveSnapshot();
            }

            if (frame.failed)
            {
                ankus_release_error(&error);
                error = frame.failure;
                memset(&frame.failure, 0, sizeof(frame.failure));
                status = 1;
            }

            if (status != 0)
            {
                ankus_transaction_report(&error, reversible ? ERROR : FATAL);
            }

            ankus_release_error(&error);
        }

        static void
        ankus_transaction_callback(XactEvent event, void *argument)
        {
            (void) argument;
            switch (event)
            {
                case XACT_EVENT_ABORT:
                    ankus_transaction_dispatch(0, 0, 0, 0, false, false);
                    break;
                case XACT_EVENT_COMMIT:
                    ankus_transaction_dispatch(0, 1, 0, 0, false, false);
                    break;
                case XACT_EVENT_PRE_COMMIT:
                    ankus_transaction_dispatch(0, 2, 0, 0, true, true);
                    break;
                case XACT_EVENT_PARALLEL_ABORT:
                    ankus_transaction_dispatch(0, 3, 0, 0, false, false);
                    break;
                case XACT_EVENT_PARALLEL_COMMIT:
                    ankus_transaction_dispatch(0, 4, 0, 0, false, false);
                    break;
                case XACT_EVENT_PARALLEL_PRE_COMMIT:
                    ankus_transaction_dispatch(0, 5, 0, 0, true, false);
                    break;
                case XACT_EVENT_PREPARE:
                    ankus_transaction_dispatch(0, 6, 0, 0, false, false);
                    break;
                case XACT_EVENT_PRE_PREPARE:
                    ankus_transaction_dispatch(0, 7, 0, 0, true, true);
                    break;
            }
        }

        static void
        ankus_subtransaction_callback(SubXactEvent event, SubTransactionId subtransaction_id,
            SubTransactionId parent_subtransaction_id, void *argument)
        {
            (void) argument;
            if (ankus_internal_subtransaction_depth != 0)
            {
                return;
            }

            switch (event)
            {
                case SUBXACT_EVENT_ABORT_SUB:
                    ankus_transaction_dispatch(1, 0, subtransaction_id, parent_subtransaction_id, false, false);
                    break;
                case SUBXACT_EVENT_COMMIT_SUB:
                    ankus_transaction_dispatch(1, 1, subtransaction_id, parent_subtransaction_id, false, false);
                    break;
                case SUBXACT_EVENT_PRE_COMMIT_SUB:
                    ankus_transaction_dispatch(1, 2, subtransaction_id, parent_subtransaction_id, true, true);
                    break;
                case SUBXACT_EVENT_START_SUB:
                    ankus_transaction_dispatch(1, 3, subtransaction_id, parent_subtransaction_id, true, true);
                    break;
            }
        }

        static void
        ankus_transaction_ensure(AnkusTransactionManaged callback, int dispatchers)
        {
            if (callback == NULL || dispatchers < 1 || dispatchers > 2)
            {
                ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE),
                    errmsg("Invalid managed transaction callback registration")));
            }

            if (!IsTransactionState())
            {
                ereport(ERROR, (errcode(ERRCODE_NO_ACTIVE_SQL_TRANSACTION),
                    errmsg("Transaction callbacks require an active PostgreSQL transaction")));
            }

            if (ankus_transaction_managed != NULL && ankus_transaction_managed != callback)
            {
                ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                    errmsg("The managed transaction callback dispatcher has already been installed")));
            }

            ankus_transaction_managed = callback;
            if (!ankus_transaction_registered)
            {
                RegisterXactCallback(ankus_transaction_callback, NULL);
                ankus_transaction_registered = true;
            }

            if (dispatchers == 2 && !ankus_subtransaction_registered)
            {
                RegisterSubXactCallback(ankus_subtransaction_callback, NULL);
                ankus_subtransaction_registered = true;
            }
        }

        """;
}
