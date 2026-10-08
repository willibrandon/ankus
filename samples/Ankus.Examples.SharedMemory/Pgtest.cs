using System.Text.Json.Serialization;

namespace Ankus.Examples.SharedMemory;

/// <summary>
/// Stores two integers in shared memory and exposes them to SQL as the custom type <c>pgtest</c>.
/// </summary>
/// <param name="Value1">The first value.</param>
/// <param name="Value2">The second value.</param>
/// <remarks>
/// Shared values must be unmanaged. The generated SQL text uses pgrx's JSON member names.
/// </remarks>
[PgType]
public readonly record struct Pgtest(
    [property: JsonPropertyName("value1")] int Value1,
    [property: JsonPropertyName("value2")] int Value2);
