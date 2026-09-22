using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ankus;

/// <summary>Converts intervals to and from PostgreSQL strings on the active backend, retaining IntervalStyle and independent components.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class PgIntervalConverter : JsonConverter<PgInterval>
{
    /// <inheritdoc />
    public override PgInterval Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => PgScalarJson.Read(ref reader, PgInterval.Parse);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, PgInterval value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToPostgresString());
    }
}
