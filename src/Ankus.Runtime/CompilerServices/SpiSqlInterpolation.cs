namespace Ankus.CompilerServices;

/// <summary>
/// Keeps interpolation-owned PostgreSQL parameters separate from literal positional tokens and quoted SQL text.
/// This recognizes token boundaries; PostgreSQL remains responsible for parsing and planning the statement.
/// </summary>
internal static class SpiSqlInterpolation
{
    /// <summary>
    /// Validates generated parameters under both supported ordinary-string escape settings without a backend call.
    /// </summary>
    /// <param name="command">The built SQL, including generated parenthesized parameters.</param>
    /// <param name="positions">The exact dollar-token positions emitted by the interpolation builder.</param>
    /// <exception cref="ArgumentException">A literal positional token aliases the binding vector, or a hole occurs inside quoted or commented text.</exception>
    internal static void Validate(string command, IReadOnlyList<int> positions)
    {
        Validate(command, positions, escapeOrdinaryStrings: false);
        if (command.Contains('\\'))
        {
            Validate(command, positions, escapeOrdinaryStrings: true);
        }
    }

    /// <summary>
    /// Visits PostgreSQL token contexts without treating dollars in strings, identifiers or comments as parameters.
    /// </summary>
    /// <param name="command">The complete SQL text.</param>
    /// <param name="positions">The generated parameter token positions.</param>
    /// <param name="escapeOrdinaryStrings">Whether ordinary single quotes recognize backslash escapes.</param>
    private static void Validate(string command, IReadOnlyList<int> positions, bool escapeOrdinaryStrings)
    {
        int next = 0;
        for (int index = 0; index < command.Length;)
        {
            if (next < positions.Count && positions[next] < index)
            {
                throw new ArgumentException("SQL interpolations must occur outside strings, quoted identifiers and comments.");
            }

            char value = command[index];
            char following = index + 1 < command.Length ? command[index + 1] : '\0';
            if (value == '-' && following == '-')
            {
                index += 2;
                while (index < command.Length && command[index] is not ('\r' or '\n'))
                {
                    index++;
                }
            }
            else if (value == '/' && following == '*')
            {
                index = SkipComment(command, index + 2);
            }
            else if (value is '\'' or '"')
            {
                index = SkipQuoted(command, index, value == '\'' && escapeOrdinaryStrings);
            }
            else if (value is 'e' or 'E' && following == '\'')
            {
                index = SkipQuoted(command, index + 1, escapes: true);
            }
            else if (value is 'u' or 'U' && following == '&' && index + 2 < command.Length && command[index + 2] is '\'' or '"')
            {
                index = SkipQuoted(command, index + 2, escapes: false);
            }
            else if (value is 'b' or 'B' or 'x' or 'X' && following == '\'')
            {
                index = SkipQuoted(command, index + 1, escapes: false);
            }
            else if (IsIdentifierStart(value))
            {
                do
                {
                    index++;
                }
                while (index < command.Length && (IsIdentifierStart(command[index]) || IsDigit(command[index]) || command[index] == '$'));
            }
            else if (value == '$' && IsDigit(following))
            {
                if (next == positions.Count || positions[next] != index)
                {
                    throw new ArgumentException("Spi.Sql owns its positional parameters. Use interpolations instead of literal $n tokens.");
                }

                next++;
                index += 2;
                while (index < command.Length && IsDigit(command[index]))
                {
                    index++;
                }
            }
            else if (value == '$')
            {
                int length = DollarDelimiterLength(command, index);
                if (length != 0)
                {
                    string delimiter = command.Substring(index, length);
                    int end = command.IndexOf(delimiter, index + length, StringComparison.Ordinal);
                    index = end < 0 ? command.Length : end + length;
                }
                else
                {
                    index++;
                }
            }
            else
            {
                index++;
            }
        }

        if (next != positions.Count)
        {
            throw new ArgumentException("SQL interpolations must occur outside strings, quoted identifiers and comments.");
        }
    }

    /// <summary>
    /// Skips single or double quotes, including doubled delimiters and explicit escape-string characters.
    /// </summary>
    /// <param name="command">The SQL text.</param>
    /// <param name="start">The opening quote.</param>
    /// <param name="escapes">Whether a backslash escapes the following character.</param>
    /// <returns>The first character after the quoted token, or the end of incomplete SQL.</returns>
    private static int SkipQuoted(string command, int start, bool escapes)
    {
        char quote = command[start];
        for (int index = start + 1; index < command.Length; index++)
        {
            if (escapes && command[index] == '\\')
            {
                index++;
            }
            else if (command[index] == quote)
            {
                if (index + 1 < command.Length && command[index + 1] == quote)
                {
                    index++;
                }
                else
                {
                    int continuation = quote == '\'' ? ContinuedQuote(command, index + 1) : -1;
                    if (continuation >= 0)
                    {
                        index = continuation;
                        continue;
                    }

                    return index + 1;
                }
            }
        }

        return command.Length;
    }

    /// <summary>
    /// Preserves a string token's escape mode when PostgreSQL concatenates quoted parts across a newline.
    /// </summary>
    /// <param name="command">The SQL text.</param>
    /// <param name="index">The character after a quoted part.</param>
    /// <returns>The continued opening quote, or minus one for a complete string token.</returns>
    private static int ContinuedQuote(string command, int index)
    {
        bool newline = false;
        while (index < command.Length)
        {
            if (command[index] is ' ' or '\t' or '\n' or '\r' or '\f' or '\v')
            {
                newline |= command[index] is '\n' or '\r';
                index++;
            }
            else if (command[index] == '-' && index + 1 < command.Length && command[index + 1] == '-')
            {
                index += 2;
                while (index < command.Length && command[index] is not ('\n' or '\r'))
                {
                    index++;
                }
            }
            else
            {
                break;
            }
        }

        return newline && index < command.Length && command[index] == '\'' ? index : -1;
    }

    /// <summary>
    /// Skips PostgreSQL's nested block comments without inspecting their apparent positional tokens.
    /// </summary>
    /// <param name="command">The SQL text.</param>
    /// <param name="index">The first character inside the initial comment.</param>
    /// <returns>The first character after the comment, or the end of incomplete SQL.</returns>
    private static int SkipComment(string command, int index)
    {
        int depth = 1;
        while (index + 1 < command.Length)
        {
            if (command[index] == '/' && command[index + 1] == '*')
            {
                depth++;
                index += 2;
            }
            else if (command[index] == '*' && command[index + 1] == '/')
            {
                index += 2;
                if (--depth == 0)
                {
                    return index;
                }
            }
            else
            {
                index++;
            }
        }

        return command.Length;
    }

    /// <summary>
    /// Measures a PostgreSQL dollar-quote delimiter whose tag has identifier syntax without a dollar character.
    /// </summary>
    /// <param name="command">The SQL text.</param>
    /// <param name="start">The initial dollar character.</param>
    /// <returns>The delimiter length, or zero for a non-delimiter dollar.</returns>
    private static int DollarDelimiterLength(string command, int start)
    {
        int index = start + 1;
        if (index < command.Length && command[index] == '$')
        {
            return 2;
        }

        if (index == command.Length || !IsIdentifierStart(command[index]))
        {
            return 0;
        }

        do
        {
            index++;
        }
        while (index < command.Length && (IsIdentifierStart(command[index]) || IsDigit(command[index])));
        return index < command.Length && command[index] == '$' ? index - start + 1 : 0;
    }

    /// <summary>
    /// Recognizes PostgreSQL's ASCII positional ordinal digits independently of culture.
    /// </summary>
    private static bool IsDigit(char value) => value is >= '0' and <= '9';

    /// <summary>
    /// Recognizes identifier starts, including server-encoding multibyte characters.
    /// </summary>
    private static bool IsIdentifierStart(char value) => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_' or >= '\u0080';
}
