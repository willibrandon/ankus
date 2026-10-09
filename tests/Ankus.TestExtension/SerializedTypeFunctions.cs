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
    /// Carries pgrx's <c>RandomData</c> members: an unsigned 64-bit integer, text and a list of dates.
    /// </summary>
    /// <param name="I">The unsigned value.</param>
    /// <param name="S">The text.</param>
    /// <param name="A">The dates.</param>
    [PgType(Name = "random_data", BinaryProtocol = true)]
    public sealed record RandomData(ulong I, string S, List<DateOnly> A);

    /// <summary>
    /// Carries each framework value type that System.Text.Json writes as a string.
    /// </summary>
    /// <param name="Id">The identifier.</param>
    /// <param name="Day">The date.</param>
    /// <param name="Time">The time of day.</param>
    /// <param name="Stamp">The date and time with its kind.</param>
    /// <param name="Instant">The date and time with its offset.</param>
    /// <param name="Duration">The duration.</param>
    /// <param name="Optional">An optional offset date and time.</param>
    [PgType(Name = "moment", BinaryProtocol = true)]
    public sealed record Moment(Guid Id, DateOnly Day, TimeOnly Time, DateTime Stamp, DateTimeOffset Instant, TimeSpan Duration,
        DateTimeOffset? Optional);

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
        public int Number
        {
            get;
            init;
        }

        /// <summary>
        /// Gets or sets an optional string that must be present in input.
        /// </summary>
        public required string? Text
        {
            get;
            set;
        }

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
        public Node? Next
        {
            get;
            set;
        }
    }

    /// <summary>
    /// Exchanges a nested owned value through each direct and SPI ownership path.
    /// </summary>
    [PgFunction]
    public static Envelope? SerializedEnvelope(Envelope? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges random data through each direct and SPI ownership path.
    /// </summary>
    [PgFunction]
    public static RandomData? SerializedRandomData(RandomData? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges framework value members through each direct and SPI ownership path.
    /// </summary>
    [PgFunction]
    public static Moment? SerializedMoment(Moment? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Creates seeded random data as pgrx's <c>RandomData::random</c> does: up to 1,000 characters and 1,000 dates.
    /// </summary>
    /// <param name="seed">The generator seed.</param>
    /// <returns>The deterministic value for the seed.</returns>
    [PgFunction]
    public static RandomData RandomDataCreate(int seed) => CreateRandomData(new Random(seed));

    /// <summary>
    /// Compares a value with the one its seed creates, member by member.
    /// </summary>
    /// <param name="value">The stored or exchanged value.</param>
    /// <param name="seed">The generator seed.</param>
    /// <returns>Whether every member matches exactly.</returns>
    [PgFunction]
    public static bool RandomDataMatches(RandomData value, int seed) => Matches(value, CreateRandomData(new Random(seed)));

    /// <summary>
    /// Creates ten seeded random values with a SQL NULL element, as pgrx's <c>test_rt_array_random_data</c> does.
    /// </summary>
    /// <param name="seed">The generator seed.</param>
    /// <returns>The deterministic array for the seed.</returns>
    [PgFunction]
    public static PgArray<RandomData?> RandomDataArray(int seed)
    {
        var random = new Random(seed);
        return new([.. Enumerable.Range(0, 10).Select(index => index == 3 ? null : CreateRandomData(random))]);
    }

    /// <summary>
    /// Compares an array with the one its seed creates, element by element.
    /// </summary>
    /// <param name="values">The stored or exchanged array.</param>
    /// <param name="seed">The generator seed.</param>
    /// <returns>Whether every element, including the NULL element, matches exactly.</returns>
    [PgFunction]
    public static bool RandomDataArrayMatches(PgArray<RandomData?> values, int seed)
    {
        PgArray<RandomData?> expected = RandomDataArray(seed);
        return values.Count == expected.Count && Enumerable.Range(0, values.Count)
            .All(index => values[index] is null ? expected[index] is null : expected[index] is { } item && Matches(values[index]!, item));
    }

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

    private static RandomData CreateRandomData(Random random)
    {
        const string alphanumeric = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        ulong number = unchecked((ulong)random.NextInt64(long.MinValue, long.MaxValue));
        string text = new([.. Enumerable.Range(0, random.Next(0, 1001)).Select(_ => alphanumeric[random.Next(alphanumeric.Length)])]);
        List<DateOnly> dates = [.. Enumerable.Range(0, random.Next(0, 1001))
            .Select(_ => new DateOnly(random.Next(1, 3001), random.Next(1, 13), random.Next(1, 29)))];
        return new RandomData(number, text, dates);
    }

    private static bool Matches(RandomData actual, RandomData expected)
        => actual.I == expected.I && string.Equals(actual.S, expected.S, StringComparison.Ordinal) && actual.A.SequenceEqual(expected.A);
}
