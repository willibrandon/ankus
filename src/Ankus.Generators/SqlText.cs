using System.Text;

namespace Ankus.Generators;

/// <summary>
/// Formats identifiers and literal values for generated PostgreSQL declarations.
/// </summary>
internal static class SqlText
{
    /// <summary>
    /// Converts a managed identifier to the generator's snake-case convention.
    /// </summary>
    /// <param name="value">The unescaped managed identifier.</param>
    /// <returns>The default SQL name.</returns>
    internal static string SnakeCase(string value)
    {
        var result = new StringBuilder();
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (char.IsUpper(current) && index > 0 &&
                (char.IsLower(value[index - 1]) || (index + 1 < value.Length && char.IsLower(value[index + 1]))))
            {
                result.Append('_');
            }

            result.Append(char.ToLowerInvariant(current));
        }

        return result.ToString();
    }

    /// <summary>
    /// Checks identifier length in UTF-8 without accepting embedded zeroes or invalid Unicode.
    /// </summary>
    /// <param name="value">The proposed identifier.</param>
    /// <returns>Whether PostgreSQL can retain the complete identifier without truncation.</returns>
    internal static bool IsIdentifier(string? value)
    {
        return !string.IsNullOrEmpty(value) && IsText(value!) && Encoding.UTF8.GetByteCount(value!) <= 63;
    }

    /// <summary>
    /// Rejects embedded zeroes and invalid Unicode before emitting UTF-8 SQL artifacts.
    /// </summary>
    /// <param name="value">The proposed literal or SQL expression.</param>
    /// <returns>Whether the complete text can be represented in PostgreSQL UTF-8.</returns>
    internal static bool IsText(string value)
    {
        if (value.Contains('\0'))
        {
            return false;
        }

        try
        {
            _ = new UTF8Encoding(false, true).GetByteCount(value);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Quotes an already validated SQL identifier, including embedded double quotes.
    /// </summary>
    /// <param name="value">The exact identifier.</param>
    /// <returns>The quoted identifier.</returns>
    internal static string Identifier(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// Escapes a string literal independently of standard_conforming_strings.
    /// </summary>
    /// <param name="value">The literal's content.</param>
    /// <returns>An escape-string SQL literal.</returns>
    internal static string Literal(string value) => "E'" + value.Replace("\\", "\\\\").Replace("'", "''") + "'";
}
