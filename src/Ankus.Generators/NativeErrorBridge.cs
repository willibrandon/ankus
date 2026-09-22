namespace Ankus.Generators;

/// <summary>
/// Transports complete PostgreSQL diagnostics with explicit buffer ownership and native-only error reporting.
/// </summary>
internal static class NativeErrorBridge
{
    /// <summary>
    /// Gets diagnostic capture, allocator-matched cleanup, and error reconstruction helpers.
    /// </summary>
    internal const string Source = """
        #include "utils/memutils.h"

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
        ankus_raise_error(AnkusError *error)
        {
            ErrorData data = {0};
            bool rethrow = (error->flags & ANKUS_ERROR_RETHROW) != 0;
            PG_TRY();
            {
                data.elevel = ERROR;
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
                data.filename = data.filename == NULL ? __FILE__ : MemoryContextStrdup(ErrorContext, data.filename);
                data.funcname = data.funcname == NULL ? "ankus_raise_error" : MemoryContextStrdup(ErrorContext, data.funcname);
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
}
