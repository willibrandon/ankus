using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises pgrx's array serialization cases through borrowed cells and statically generated scalar metadata.
/// </summary>
[PgSchema("array_json")]
public static class ArrayJsonFunctions
{
    /// <summary>
    /// Preserves pgrx's anyarray iteration case by writing the present native Datum words as bigint cells.
    /// </summary>
    /// <param name="values">The actual polymorphic array whose NULL cells are rejected.</param>
    /// <returns>The PostgreSQL-generated JSON array of native words.</returns>
    /// <remarks>
    /// For by-reference elements these words are addresses, as in pgrx's raw-word example, rather than converted values.
    /// </remarks>
    [PgFunction(Name = "anyarray_iter_arg")]
    public static PgJson SerializeNativeWords(PgAnyArray values)
    {
        long[] words = new long[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            PgAnyElement cell = values[index] ?? throw new InvalidOperationException("element is null");
            words[index] = unchecked((long)cell.Datum.DangerousGetBits());
        }

        return PgFunctions.Call<PgJson>("pg_catalog.array_to_json", PgFunctionArgument.Create(words));
    }

    /// <summary>
    /// Writes nullable integer cells as numbers and JSON null in row-major order.
    /// </summary>
    /// <param name="values">The borrowed native integer array.</param>
    /// <returns>The independently owned values document.</returns>
    [PgFunction(Name = "serde_serialize_array_i32")]
    public static PgJson SerializeNullableIntegers(PgArrayView<int?> values)
        => WriteValues(values, ArrayScalarJsonContext.Default.NullableInteger);

    /// <summary>
    /// Rejects SQL NULL anywhere before enumeration, including cells after another consumer's shorter input.
    /// </summary>
    /// <param name="values">The borrowed array whose cells must all be present.</param>
    /// <returns>The independently owned values document.</returns>
    [PgFunction(Name = "serde_serialize_array_i32_deny_null")]
    public static PgJson SerializeRequiredIntegers(PgArrayView<int> values)
        => WriteValues(values, ArrayScalarJsonContext.Default.Int32);

    /// <summary>
    /// Writes nullable native dates through their full-range ISO converter.
    /// </summary>
    /// <param name="values">The borrowed native date array.</param>
    /// <returns>The independently owned values document.</returns>
    [PgFunction(Name = "serde_serialize_array_date")]
    public static PgJson SerializeDates(PgArrayView<PgDate?> values)
        => WriteValues(values, ArrayScalarJsonContext.Default.NullableDate);

    /// <summary>
    /// Writes nullable native timestamps through their full-range ISO converter.
    /// </summary>
    /// <param name="values">The borrowed native timestamp array.</param>
    /// <returns>The independently owned values document.</returns>
    [PgFunction(Name = "serde_serialize_array_timestamp")]
    public static PgJson SerializeTimestamps(PgArrayView<PgTimestamp?> values)
        => WriteValues(values, ArrayScalarJsonContext.Default.NullableTimestamp);

    /// <summary>
    /// Embeds JSON objects, arrays and scalars without stringifying their original value kinds.
    /// </summary>
    /// <param name="values">The borrowed native array with independently nullable JSON cells.</param>
    /// <returns>The independently owned values document.</returns>
    [PgFunction(Name = "serde_serialize_array_json")]
    public static PgJson SerializeJson(PgArrayView<PgJson?> values)
        => WriteValues(values, ArrayScalarJsonContext.Default.NullableJson);

    /// <summary>
    /// Enumerates each checked cell once and uses only its explicit AOT serialization contract.
    /// </summary>
    /// <typeparam name="T">The statically registered scalar representation.</typeparam>
    /// <param name="values">The checked native cells.</param>
    /// <param name="metadata">The source-generated scalar metadata.</param>
    /// <returns>The independent managed JSON text written back to PostgreSQL.</returns>
    private static PgJson WriteValues<T>(IEnumerable<T> values, JsonTypeInfo<T> metadata)
    {
        var output = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(output);
        writer.WriteStartObject();
        writer.WriteStartArray("values");
        foreach (T value in values)
        {
            JsonSerializer.Serialize(writer, value, metadata);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return new PgJson(Encoding.UTF8.GetString(output.WrittenSpan));
    }
}
