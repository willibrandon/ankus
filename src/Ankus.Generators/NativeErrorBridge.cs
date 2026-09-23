namespace Ankus.Generators;

/// <summary>
/// Transports complete PostgreSQL diagnostics with explicit buffer ownership and native-only error reporting.
/// </summary>
internal static class NativeErrorBridge
{
    /// <summary>
    /// Gets diagnostic declarations and allocator-matched cleanup shared with configuration hooks.
    /// </summary>
    internal const string Declarations = """
        #include "utils/memutils.h"
        #include "miscadmin.h"
        #include "tcop/dest.h"

        enum AnkusDiagnosticField
        {
            ANKUS_ERROR_MESSAGE, ANKUS_ERROR_DETAIL, ANKUS_ERROR_HINT, ANKUS_ERROR_CONTEXT,
            ANKUS_ERROR_SCHEMA, ANKUS_ERROR_TABLE, ANKUS_ERROR_COLUMN, ANKUS_ERROR_DATATYPE,
            ANKUS_ERROR_CONSTRAINT, ANKUS_ERROR_QUERY, ANKUS_ERROR_FILE, ANKUS_ERROR_ROUTINE,
            ANKUS_ERROR_DETAIL_LOG, ANKUS_ERROR_BACKTRACE, ANKUS_ERROR_FIELD_COUNT
        };

        enum AnkusErrorFlags
        {
            ANKUS_ERROR_RETHROW = 1, ANKUS_ERROR_SERVER = 2, ANKUS_ERROR_CLIENT = 4,
            ANKUS_ERROR_HIDE_STATEMENT = 8, ANKUS_ERROR_HIDE_CONTEXT = 16,
            ANKUS_ERROR_INCOMPLETE = 32, ANKUS_ERROR_SHOW_FUNCTION = 64
        };

        typedef struct AnkusError
        {
            int sqlstate;
            char message[2048];
            int position;
            int internal_position;
            int line;
            int flags;
            AnkusValue fields[ANKUS_ERROR_FIELD_COUNT];
            int report_level;
        } AnkusError;

        static void
        ankus_release_error(AnkusError *error)
        {
            for (int index = 0; index < ANKUS_ERROR_FIELD_COUNT; index++)
            {
                AnkusValue *value = &error->fields[index];
                if (value->release != NULL)
                {
                    value->release(value->data);
                }

                memset(value, 0, sizeof(AnkusValue));
            }
        }

        static void
        ankus_free_error_buffer(void *data)
        {
            free(data);
        }

        static ErrorData *
        ankus_copy_error_data(void)
        {
            ErrorData *data = CopyErrorData();
        #if PG_VERSION_NUM < 170000
            /* Older PostgreSQL releases leave these pointers in ErrorContext or their original owner. */
            if (data->filename != NULL) data->filename = pstrdup(data->filename);
            if (data->funcname != NULL) data->funcname = pstrdup(data->funcname);
            if (data->domain != NULL) data->domain = pstrdup(data->domain);
            if (data->context_domain != NULL) data->context_domain = pstrdup(data->context_domain);
            if (data->message_id != NULL) data->message_id = pstrdup(data->message_id);
        #endif
            return data;
        }

        static void
        ankus_free_error_data(ErrorData *data)
        {
            /* CopyErrorData owns these strings, but FreeErrorData treats them as constant. */
            const char *fields[] = {
                data->filename, data->funcname, data->domain, data->context_domain, data->message_id
            };
            for (int index = 0; index < 5; index++)
            {
                if (fields[index] != NULL)
                    pfree((void *) fields[index]);
            }

            data->filename = NULL;
            data->funcname = NULL;
            data->domain = NULL;
            data->context_domain = NULL;
            data->message_id = NULL;
            FreeErrorData(data);
        }

        """;

    /// <summary>
    /// Gets severity mapping and transaction-independent PostgreSQL message filtering.
    /// </summary>
    internal const string Logging = """
        static int
        ankus_log_level(int level)
        {
            switch (level)
            {
                case 0: return DEBUG5;
                case 1: return DEBUG4;
                case 2: return DEBUG3;
                case 3: return DEBUG2;
                case 4: return DEBUG1;
                case 5: return LOG;
                case 6: return LOG_SERVER_ONLY;
                case 7: return INFO;
                case 8: return NOTICE;
                case 9: return WARNING;
                case 11: return FATAL;
                case 12: return PANIC;
                default: return ERROR;
            }
        }

        static bool
        ankus_log_enabled(int level)
        {
        #if PG_VERSION_NUM >= 140000
            return message_level_is_interesting(level);
        #else
            /* PostgreSQL 13 predates message_level_is_interesting. Mirror errstart's routing rules. */
            bool server = (level == LOG || level == LOG_SERVER_ONLY)
                ? (log_min_messages == LOG || log_min_messages <= ERROR)
                : (log_min_messages == LOG ? level >= FATAL : level >= log_min_messages);
            bool client = whereToSendOutput == DestRemote && level != LOG_SERVER_ONLY &&
                (ClientAuthInProgress ? level >= ERROR : level >= client_min_messages || level == INFO);
            return level >= ERROR || server || client;
        #endif
        }

        """;

    /// <summary>
    /// Gets diagnostic capture, allocator-matched cleanup, and backend error reconstruction helpers.
    /// </summary>
    internal const string Source = Declarations + "\n" + Logging + "\n" + """
        static void
        ankus_capture_error(ErrorData *data, AnkusError *error)
        {
            const char *fields[ANKUS_ERROR_FIELD_COUNT] = {
                data->message, data->detail, data->hint, data->context,
                data->schema_name, data->table_name, data->column_name, data->datatype_name,
                data->constraint_name, data->internalquery, data->filename, data->funcname,
                data->detail_log, data->backtrace
            };
            error->sqlstate = data->sqlerrcode;
            error->position = data->cursorpos;
            error->internal_position = data->internalpos;
            error->line = data->lineno;
            error->flags = ANKUS_ERROR_RETHROW |
                (data->output_to_server ? ANKUS_ERROR_SERVER : 0) |
                (data->output_to_client ? ANKUS_ERROR_CLIENT : 0) |
                (data->hide_stmt ? ANKUS_ERROR_HIDE_STATEMENT : 0) |
                (data->hide_ctx ? ANKUS_ERROR_HIDE_CONTEXT : 0);
        #if PG_VERSION_NUM < 140000
            error->flags |= data->show_funcname ? ANKUS_ERROR_SHOW_FUNCTION : 0;
        #endif
            for (int index = 0; index < ANKUS_ERROR_FIELD_COUNT; index++)
            {
                const char *text = fields[index];
                if (text != NULL)
                {
                    char *utf8 = pg_server_to_any(text, strlen(text), PG_UTF8);
                    int length = strlen(utf8);
                    AnkusValue *value = &error->fields[index];
                    if (index == ANKUS_ERROR_MESSAGE)
                    {
                        int clipped = pg_encoding_mbcliplen(PG_UTF8, utf8, length, sizeof(error->message) - 1);
                        memcpy(error->message, utf8, clipped);
                        error->message[clipped] = '\0';
                    }

                    value->data = malloc((Size) length + 1);
                    if (value->data != NULL)
                    {
                        memcpy(value->data, utf8, (Size) length + 1);
                        value->length = length;
                        value->release = ankus_free_error_buffer;
                    }
                    else
                    {
                        error->flags |= ANKUS_ERROR_INCOMPLETE;
                    }

                    if (utf8 != text)
                    {
                        pfree(utf8);
                    }
                }
            }
        }

        static char *
        ankus_error_field(AnkusError *error, enum AnkusDiagnosticField field)
        {
            AnkusValue *value = &error->fields[field];
            char *converted;
            char *copy;
            if (value->data == NULL)
            {
                return NULL;
            }

            converted = pg_any_to_server((char *) value->data, value->length, PG_UTF8);
            /* Own every string before releasing the native or managed transport allocator. */
            copy = pstrdup(converted);
            if (converted != (char *) value->data)
            {
                pfree(converted);
            }

            return copy;
        }

        static void
        ankus_report(AnkusError *error, int level)
        {
            ErrorData data = {0};
            bool rethrow = level == ERROR && (error->flags & ANKUS_ERROR_RETHROW) != 0;
            PG_TRY();
            {
                data.elevel = level;
                data.sqlerrcode = error->sqlstate;
                data.cursorpos = error->position;
                data.internalpos = error->internal_position;
                data.lineno = error->line;
                data.output_to_server = (error->flags & ANKUS_ERROR_SERVER) != 0;
                data.output_to_client = (error->flags & ANKUS_ERROR_CLIENT) != 0;
                data.hide_stmt = (error->flags & ANKUS_ERROR_HIDE_STATEMENT) != 0;
                data.hide_ctx = (error->flags & ANKUS_ERROR_HIDE_CONTEXT) != 0;
        #if PG_VERSION_NUM < 140000
                data.show_funcname = (error->flags & ANKUS_ERROR_SHOW_FUNCTION) != 0;
        #endif
                data.message = ankus_error_field(error, ANKUS_ERROR_MESSAGE);
                if (data.message == NULL)
                {
                    data.message = pstrdup(pg_any_to_server(error->message, strlen(error->message), PG_UTF8));
                }

                data.detail = ankus_error_field(error, ANKUS_ERROR_DETAIL);
                data.hint = ankus_error_field(error, ANKUS_ERROR_HINT);
                data.context = ankus_error_field(error, ANKUS_ERROR_CONTEXT);
                data.schema_name = ankus_error_field(error, ANKUS_ERROR_SCHEMA);
                data.table_name = ankus_error_field(error, ANKUS_ERROR_TABLE);
                data.column_name = ankus_error_field(error, ANKUS_ERROR_COLUMN);
                data.datatype_name = ankus_error_field(error, ANKUS_ERROR_DATATYPE);
                data.constraint_name = ankus_error_field(error, ANKUS_ERROR_CONSTRAINT);
                data.internalquery = ankus_error_field(error, ANKUS_ERROR_QUERY);
                data.filename = ankus_error_field(error, ANKUS_ERROR_FILE);
                data.funcname = ankus_error_field(error, ANKUS_ERROR_ROUTINE);
                data.detail_log = ankus_error_field(error, ANKUS_ERROR_DETAIL_LOG);
                data.backtrace = ankus_error_field(error, ANKUS_ERROR_BACKTRACE);
                /* ErrorData treats source locations as constant: make them survive caller-context cleanup. */
                data.filename = data.filename == NULL ? __FILE__ :
                    (level >= ERROR ? MemoryContextStrdup(ErrorContext, data.filename) : data.filename);
                data.funcname = data.funcname == NULL ? "ankus_report" :
                    (level >= ERROR ? MemoryContextStrdup(ErrorContext, data.funcname) : data.funcname);
                data.assoc_context = CurrentMemoryContext;
            }
            PG_FINALLY();
            {
                ankus_release_error(error);
            }
            PG_END_TRY();
            if (rethrow)
            {
                /* Context callbacks already ran for this error; replaying them would duplicate SQL frames. */
                ReThrowError(&data);
            }

            ThrowErrorData(&data);
        }

        """;

    /// <summary>
    /// Gets error raising used after managed SQL and initialization callbacks unwind.
    /// </summary>
    internal const string RaiseError = """
        static void
        ankus_raise_error(AnkusError *error)
        {
            ankus_report(error, error->report_level == 0 ? ERROR : ankus_log_level(error->report_level - 1));
        }

        """;
}
