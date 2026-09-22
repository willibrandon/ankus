namespace Ankus.Generators;

/// <summary>
/// Tracks PostgreSQL portal lifetimes without exposing potentially dangling native pointers to managed code.
/// </summary>
internal static class NativeCursorBridge
{
    /// <summary>
    /// Gets cursor identity registration, memory-context invalidation, and guarded-operation helpers.
    /// </summary>
    internal const string Source = """
        #include "access/xact.h"

        typedef struct AnkusCursorEntry
        {
            Portal portal;
            int64 identity;
            bool close_pending;
            MemoryContextCallback cleanup;
            struct AnkusCursorEntry *next;
        } AnkusCursorEntry;

        static AnkusCursorEntry *ankus_cursors = NULL;
        static int64 ankus_next_cursor_id = 0;
        static bool ankus_cursor_callbacks_registered = false;
        static bool ankus_closing_aborted_cursors = false;

        static void
        ankus_close_aborted_cursors(void *argument)
        {
            AnkusCursorEntry *entry;
            (void) argument;
            if (ankus_closing_aborted_cursors)
                return;
            ankus_closing_aborted_cursors = true;
            /* Transaction callbacks and memory reset run after the abort portal scans. Entries for
             * portals PostgreSQL already dropped have been unlinked by their reset
             * callbacks; only surviving, owned cursors still need closing. */
            PG_TRY();
            {
                for (;;)
                {
                    for (entry = ankus_cursors; entry != NULL; entry = entry->next)
                        if (entry->close_pending && !entry->portal->portalPinned && entry->portal->status != PORTAL_ACTIVE)
                            break;
                    if (entry == NULL)
                        break;
                    entry->close_pending = false;
                    if (entry->portal->status == PORTAL_READY)
                        MarkPortalFailed(entry->portal);
                    SPI_cursor_close(entry->portal);
                    /* Closing may unlink several entries through nested iterator cleanup. */
                }
            }
            PG_FINALLY();
            {
                ankus_closing_aborted_cursors = false;
            }
            PG_END_TRY();
        }

        static void
        ankus_cursor_xact_callback(XactEvent event, void *argument)
        {
            if (event == XACT_EVENT_ABORT || event == XACT_EVENT_PARALLEL_ABORT ||
                event == XACT_EVENT_PRE_COMMIT || event == XACT_EVENT_PRE_PREPARE)
                ankus_close_aborted_cursors(argument);
        }

        static void
        ankus_cursor_subxact_callback(SubXactEvent event, SubTransactionId current,
            SubTransactionId parent, void *argument)
        {
            (void) current;
            (void) parent;
            if (event == SUBXACT_EVENT_ABORT_SUB)
                ankus_close_aborted_cursors(argument);
        }

        static void
        ankus_forget_cursor(void *argument)
        {
            AnkusCursorEntry *entry = (AnkusCursorEntry *) argument;
            AnkusCursorEntry **position = &ankus_cursors;
            while (*position != NULL)
            {
                if (*position == entry)
                {
                    *position = entry->next;
                    return;
                }

                position = &(*position)->next;
            }
        }

        static int
        ankus_return_cursor(Portal portal, AnkusResult *result)
        {
            AnkusCursorEntry *entry;
            char *utf8;
            if (portal == NULL)
            {
                return SPI_ERROR_CURSOR;
            }

            if (!ankus_cursor_callbacks_registered)
            {
                RegisterXactCallback(ankus_cursor_xact_callback, NULL);
                RegisterSubXactCallback(ankus_cursor_subxact_callback, NULL);
                ankus_cursor_callbacks_registered = true;
            }

            for (entry = ankus_cursors; entry != NULL; entry = entry->next)
            {
                if (entry->portal == portal)
                {
                    break;
                }
            }

            if (entry == NULL)
            {
                if (ankus_next_cursor_id == PG_INT64_MAX)
                {
                    ereport(ERROR, (errcode(ERRCODE_PROGRAM_LIMIT_EXCEEDED), errmsg("SPI cursor identity capacity exhausted")));
                }

                entry = MemoryContextAllocZero(portal->portalContext, sizeof(AnkusCursorEntry));
                entry->portal = portal;
                entry->identity = ++ankus_next_cursor_id;
                entry->cleanup.func = ankus_forget_cursor;
                entry->cleanup.arg = entry;
                MemoryContextRegisterResetCallback(portal->portalContext, &entry->cleanup);
                entry->next = ankus_cursors;
                ankus_cursors = entry;
            }

            result->cursor_id = entry->identity;
            utf8 = pg_server_to_any(portal->name, strlen(portal->name), PG_UTF8);
            ankus_copy_owned(&result->cursor_name, (unsigned char *) utf8, strlen(utf8));
            if (utf8 != portal->name)
            {
                pfree(utf8);
            }

            return SPI_OK_CURSOR;
        }

        static int
        ankus_cursor_operation(AnkusRequest *request, AnkusResult *result)
        {
            AnkusCursorEntry *entry;
            if (request->operation == ANKUS_SPI_FIND_CURSOR)
            {
                char *name = pg_any_to_server(request->command, request->command_length, PG_UTF8);
                Portal portal = SPI_cursor_find(name);
                if (portal == NULL)
                {
                    ereport(ERROR, (errcode(ERRCODE_INVALID_CURSOR_NAME), errmsg("Cursor \"%s\" does not exist", name)));
                }

                return ankus_return_cursor(portal, result);
            }

            for (entry = ankus_cursors; entry != NULL; entry = entry->next)
            {
                if (entry->identity == request->cursor_id)
                {
                    break;
                }
            }

            if (request->operation == ANKUS_SPI_CLOSE_CURSOR)
            {
                if (entry != NULL)
                {
                    if (request->cleanup_only && !IsTransactionState())
                    {
                        MemoryContextCallback *cleanup;
                        /* Memory cleanup can run inside At(Sub)Abort_Portals or its
                         * later cleanup scan. Removing another hash entry there is
                         * unsafe. CurTransactionContext outlives both scans. */
                        cleanup = MemoryContextAlloc(CurTransactionContext, sizeof(MemoryContextCallback));
                        cleanup->func = ankus_close_aborted_cursors;
                        cleanup->arg = NULL;
                        MemoryContextRegisterResetCallback(CurTransactionContext, cleanup);
                        entry->close_pending = true;
                    }
                    else
                    {
                        SPI_cursor_close(entry->portal);
                    }
                }

                return 0;
            }

            if (entry == NULL)
            {
                ereport(ERROR, (errcode(ERRCODE_INVALID_CURSOR_NAME), errmsg("The SPI cursor is no longer available")));
            }

            SPI_cursor_fetch(entry->portal, request->forward != 0, request->limit);
            return SPI_OK_FETCH;
        }

        """;
}
