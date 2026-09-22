using System.Text.Json;

namespace Ankus;

/// <summary>
/// Shares JSON token and input-error handling without reflection or loss of numeric precision.
/// </summary>
internal static class PgScalarJson
{
    internal static T Read<T>(ref Utf8JsonReader reader, Func<string, T> parse, bool allowNumber = false)
    {
        string text;
        if (reader.TokenType == JsonTokenType.String)
        {
            text = reader.GetString()!;
        }
        else if (allowNumber && reader.TokenType == JsonTokenType.Number)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            text = document.RootElement.GetRawText();
        }
        else
        {
            throw new JsonException(allowNumber ? "Expected a numeric string or JSON number." : "Expected a PostgreSQL temporal string.");
        }

        if (text.Contains('\0', StringComparison.Ordinal))
        {
            throw new JsonException("PostgreSQL input cannot contain a zero character.");
        }

        try
        {
            return parse(text);
        }
        catch (PgException error) when (error.SqlState is "22P02" or "22003" or "22007" or "22008" or "22009" or "22015" or "22023")
        {
            throw new JsonException("Invalid PostgreSQL value.", error);
        }
        catch (System.Text.EncoderFallbackException error)
        {
            throw new JsonException("Invalid Unicode in PostgreSQL input.", error);
        }
    }
}
