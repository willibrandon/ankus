using System.Text.Json;

namespace Ankus.Examples.WalDecoder;

/// <summary>
/// Holds the serialized columns of one row image, before or after a change.
/// </summary>
/// <param name="columns">
/// The non-NULL columns in attribute order. Names are quoted SQL identifiers; values are <see cref="int"/> or
/// <see cref="string"/>.
/// </param>
/// <remarks>
/// NULL columns are omitted, as are all columns of an old row that PostgreSQL did not log.
/// </remarks>
public sealed class DecodedRow(IReadOnlyList<(string Name, object Value)> columns)
{
    /// <summary>
    /// Gets a row image without columns, used when PostgreSQL logged no tuple.
    /// </summary>
    public static DecodedRow Empty { get; } = new([]);

    /// <summary>
    /// Gets the serialized columns in attribute order.
    /// </summary>
    public IReadOnlyList<(string Name, object Value)> Columns { get; } = columns ?? throw new ArgumentNullException(nameof(columns));

    /// <summary>
    /// Writes the row as a JSON object whose members follow attribute order.
    /// </summary>
    /// <param name="writer">The destination writer.</param>
    /// <exception cref="InvalidOperationException">A value is neither an integer nor text.</exception>
    public void WriteTo(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartObject();
        foreach ((string name, object value) in Columns)
        {
            switch (value)
            {
                case int number:
                    writer.WriteNumber(name, number);
                    break;
                case string text:
                    writer.WriteString(name, text);
                    break;
                default:
                    throw new InvalidOperationException($"Column {name} holds an unsupported {value.GetType().Name} value.");
            }
        }

        writer.WriteEndObject();
    }
}
