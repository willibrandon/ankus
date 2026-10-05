using System.Text.Json.Serialization;

namespace Ankus.TestExtension;

/// <summary>
/// Provides bounded source-generated JSON metadata for each tested native scalar representation.
/// </summary>
[JsonSerializable(typeof(int?), TypeInfoPropertyName = "NullableInteger")]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(PgDate?), TypeInfoPropertyName = "NullableDate")]
[JsonSerializable(typeof(PgTimestamp?), TypeInfoPropertyName = "NullableTimestamp")]
[JsonSerializable(typeof(PgJson?), TypeInfoPropertyName = "NullableJson")]
internal sealed partial class ArrayScalarJsonContext : JsonSerializerContext;
