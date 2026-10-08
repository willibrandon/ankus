using System.Text.Json.Serialization;

namespace Ankus.Examples.Operators;

/// <summary>
/// Stores an integer using generated CBOR storage and pgrx's JSON text, such as <c>{"value":1}</c>.
/// </summary>
/// <param name="Value">The compared integer, serialized under pgrx's value field name.</param>
[PgType(Name = "mytype")]
public sealed record MyType([property: JsonPropertyName("value")] int Value);
