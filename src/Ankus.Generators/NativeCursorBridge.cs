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
        typedef struct AnkusCursorEntry
        {
            Portal portal;
            int64 identity;
            MemoryContextCallback cleanup;
            struct AnkusCursorEntry *next;
        } AnkusCursorEntry;

        static AnkusCursorEntry *ankus_cursors = NULL;
        static int64 ankus_next_cursor_id = 0;

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
                    SPI_cursor_close(entry->portal);
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
