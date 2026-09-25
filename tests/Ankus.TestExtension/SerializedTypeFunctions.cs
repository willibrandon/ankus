using System.Text.Json.Serialization;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises generated JSON text and CBOR storage in published Native AOT callbacks.
/// </summary>
[PgSchema("serialized_values")]
public static class SerializedTypeFunctions
{
    /// <summary>
    /// Carries a nullable nested value without declaring another PostgreSQL type.
    /// </summary>
    /// <param name="Number">The required numeric member.</param>
    /// <param name="Text">An optional nested string.</param>
    public sealed record Item(int Number, string? Text);

    /// <summary>
    /// Preserves a nested immutable graph and independently nullable collection elements.
    /// </summary>
    /// <param name="Name">The exact Unicode string.</param>
    /// <param name="Count">The exact signed count.</param>
    /// <param name="Items">A nullable vector with nullable nested records.</param>
    /// <param name="Tags">A nullable list with nullable strings.</param>
    /// <param name="Lookup">A nullable map with nullable nested records.</param>
    [PgType(Name = "envelope", BinaryProtocol = true)]
    public sealed record Envelope(
        string Name,
        long Count,
        Item?[]? Items,
        List<string?>? Tags,
        Dictionary<string, Item?>? Lookup);

    /// <summary>
    /// Provides a small independently specified CBOR wire fixture.
    /// </summary>
    /// <param name="Value">The signed numeric member.</param>
    [PgType(Name = "counter", BinaryProtocol = true)]
    public readonly record struct Counter(int Value);

    /// <summary>
    /// Exercises enum-name serialization independently of PostgreSQL enum mappings.
    /// </summary>
    [PgType(Name = "mode", BinaryProtocol = true)]
    public enum Mode
    {
        /// <summary>
        /// Represents the zero named value.
        /// </summary>
        Stopped,

        /// <summary>
        /// Represents a named noncontiguous value.
        /// </summary>
        Ready = 7,
    }

    /// <summary>
    /// Exercises generated init-only and settable members with ordinary JSON attributes.
    /// </summary>
    [PgType(Name = "mutable")]
    public sealed class Mutable
    {
        /// <summary>
        /// Gets the renamed required integer.
        /// </summary>
        [JsonPropertyName("n")]
        public int Number { get; init; }

        /// <summary>
        /// Gets or sets an optional string that must be present in input.
        /// </summary>
        public required string? Text { get; set; }

        /// <summary>
        /// Gets an unsupported property excluded from the serialized contract.
        /// </summary>
        [JsonIgnore]
        public Uri Ignored { get; } = new("https://example.com/");
    }

    /// <summary>
    /// Exercises a mutable recursive graph through bounded generated storage.
    /// </summary>
    [PgType(Name = "node")]
    public sealed class Node
    {
        /// <summary>
        /// Gets or sets the optional next node.
        /// </summary>
        public Node? Next { get; set; }
    }

    /// <summary>
    /// Exchanges a nested owned value through each direct and SPI ownership path.
    /// </summary>
    [PgFunction]
    public static Envelope? SerializedEnvelope(Envelope? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges a generated value type through each direct and SPI ownership path.
    /// </summary>
    [PgFunction]
    public static Counter? SerializedCounter(Counter? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges generated type arrays while preserving their shape and NULL cells.
    /// </summary>
    [PgFunction]
    public static PgArray<Counter?>? SerializedCounters(PgArray<Counter?>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Returns generated custom values through a materialized set, including SQL NULL.
    /// </summary>
    [PgFunction]
    public static IEnumerable<Counter?> SerializedRows(Counter value) => [value, null, new Counter(9)];

    /// <summary>
    /// Returns a directly observed property to prove generated mutable construction.
    /// </summary>
    [PgFunction]
    public static int SerializedMutableNumber(Mutable value) => value.Number;

    /// <summary>
    /// Returns an enum through the owned default serialized mapping.
    /// </summary>
    [PgFunction]
    public static Mode SerializedMode(Mode value) => value;

    /// <summary>
    /// Exercises the managed writer error boundary for an unnamed enum value.
    /// </summary>
    [PgFunction]
    public static Mode SerializedInvalidMode() => (Mode)3;

    /// <summary>
    /// Exercises bounded write failure for a cyclic managed object graph.
    /// </summary>
    [PgFunction]
    public static Node SerializedCycle()
    {
        var value = new Node();
        value.Next = value;
        return value;
    }
}
