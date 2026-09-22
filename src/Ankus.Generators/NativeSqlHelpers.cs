namespace Ankus.Generators;

/// <summary>
/// Invokes PostgreSQL's identifier and literal quoting functions with guarded encoding and owned result transport.
/// </summary>
internal static class NativeSqlHelpers
{
    /// <summary>
    /// Gets direct native helpers which do not require a SPI connection or SQL execution.
    /// </summary>
    internal const string Source = """
        static bool
        ankus_is_quote_request(uint8 operation)
        {
            return operation >= ANKUS_SPI_QUOTE_IDENTIFIER && operation <= ANKUS_SPI_QUOTE_LITERAL;
        }

        static const char *
        ankus_quote_argument(const AnkusParameter *parameter)
        {
            if (parameter->value.is_null)
            {
                return NULL;
            }
            return pg_any_to_server((char *) parameter->value.data, parameter->value.length, PG_UTF8);
        }

        static int
        ankus_quote(AnkusRequest *request, AnkusResult *result)
        {
            const char *first = ankus_quote_argument(&request->parameters[0]);
            const char *quoted;
            char *utf8;
            if (request->operation == ANKUS_SPI_QUOTE_IDENTIFIER)
            {
                quoted = quote_identifier(first);
            }
            else if (request->operation == ANKUS_SPI_QUOTE_QUALIFIED_IDENTIFIER)
            {
                const char *second = ankus_quote_argument(&request->parameters[1]);
                quoted = quote_qualified_identifier(first, second);
            }
            else
            {
                quoted = quote_literal_cstr(first);
            }
            utf8 = pg_server_to_any(quoted, strlen(quoted), PG_UTF8);
            ankus_copy_owned(&result->text, (unsigned char *) utf8, strlen(utf8));
            /* PostgreSQL allocations belong to the disposable operation context. */
            return 0;
        }

        """;
}
