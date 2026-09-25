using System.Text.Json.Serialization;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises statically tagged custom-type variants in published Native AOT callbacks.
/// </summary>
[PgSchema("tagged_values")]
public static class PolymorphicTypeFunctions
{
    /// <summary>
    /// Declares the closed set of persisted variants.
    /// </summary>
    [PgType(Name = "message", BinaryProtocol = true)]
    [JsonDerivedType(typeof(NumberMessage), 7)]
    [JsonDerivedType(typeof(TextMessage), "text")]
    [JsonDerivedType(typeof(LinkMessage), "link")]
    public abstract record Message;

    /// <summary>
    /// Carries an exact integer variant.
    /// </summary>
    /// <param name="Number">The stored integer.</param>
    [PgType(Name = "number_message", BinaryProtocol = true)]
    public sealed record NumberMessage(int Number) : Message;

    /// <summary>
    /// Carries an independently owned text variant.
    /// </summary>
    /// <param name="Text">The stored text.</param>
    public sealed record TextMessage(string Text) : Message;

    /// <summary>
    /// Supports recursive nullable variant graphs.
    /// </summary>
    public sealed record LinkMessage : Message
    {
        /// <summary>
        /// Gets or sets the optional next variant.
        /// </summary>
        public Message? Next { get; set; }
    }

    /// <summary>
    /// Exercises rejection of an unregistered runtime subtype.
    /// </summary>
    /// <param name="Secret">The state that must not be discarded.</param>
    public sealed record UnexpectedMessage(string Secret) : Message;

    /// <summary>
    /// Carries nullable variants in each supported collection shape.
    /// </summary>
    /// <param name="Items">The optional array.</param>
    /// <param name="List">The list including nullable elements.</param>
    /// <param name="Map">The ordinal map including nullable values.</param>
    [PgType(Name = "batch")]
    public sealed record Batch(Message?[]? Items, List<Message?> List, Dictionary<string, Message?> Map);

    /// <summary>
    /// Exchanges variants through each direct and SPI ownership path.
    /// </summary>
    [PgFunction]
    public static Message? TaggedMessage(Message? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges the independently mapped variant using its declared concrete SQL identity.
    /// </summary>
    [PgFunction]
    public static NumberMessage? TaggedNumberMessage(NumberMessage? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Preserves shaped arrays of independently nullable variants.
    /// </summary>
    [PgFunction]
    public static PgArray<Message?>? TaggedMessages(PgArray<Message?>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges collection graphs containing tagged values.
    /// </summary>
    [PgFunction]
    public static Batch TaggedBatch(Batch value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Reads and replaces tuple cells while preserving distinct base and concrete scalar and array identities.
    /// </summary>
    [PgFunction]
    public static PgHeapTuple TaggedTuple(PgHeapTuple value, int mode)
    {
        PgHeapTuple copy = value.Clone();
        copy.Set(0, copy.Get<Message?>(0));
        copy.Set(1, copy.Get<NumberMessage?>(1));
        copy.Set(2, copy.Get<PgArray<Message?>?>(2));
        copy.Set(3, copy.Get<PgArray<NumberMessage?>?>(3));
        return ArrayFunctions.Exchange(copy, mode);
    }

    /// <summary>
    /// Preserves a declared base SQL vector when its managed array has a covariant concrete element type.
    /// </summary>
    [PgFunction]
    public static Message?[] TaggedCovariantVector(int mode)
    {
        NumberMessage?[] concrete = [new(7), null];
        Message?[] values = concrete;
        return ArrayFunctions.Exchange(values, mode);
    }

    /// <summary>
    /// Assigns a covariant vector to a base-array tuple attribute before its type is erased into a cell.
    /// </summary>
    [PgFunction]
    public static PgHeapTuple TaggedCovariantTuple(PgHeapTuple value, int mode)
    {
        PgHeapTuple copy = value.Clone();
        NumberMessage?[] concrete = [new(7), null];
        Message?[] values = concrete;
        copy.Set(0, values);
        return ArrayFunctions.Exchange(copy, mode);
    }

    /// <summary>
    /// Observes the concrete managed variant materialized by generated storage.
    /// </summary>
    [PgFunction]
    public static string TaggedKind(Message value) => value switch
    {
        NumberMessage number => "number:" + number.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        TextMessage text => "text:" + text.Text,
        LinkMessage => "link",
        _ => throw new InvalidOperationException("Unknown variant reached the callback."),
    };

    /// <summary>
    /// Produces variants and SQL NULL through set-returning materialization.
    /// </summary>
    [PgFunction]
    public static IEnumerable<Message?> TaggedRows(Message value) => [value, null, new NumberMessage(9)];

    /// <summary>
    /// Produces unregistered state to exercise the managed writer failure boundary.
    /// </summary>
    [PgFunction]
    public static Message TaggedUnknown() => new UnexpectedMessage("must not disappear");

    /// <summary>
    /// Produces a cycle to exercise bounded write failure and backend recovery.
    /// </summary>
    [PgFunction]
    public static Message TaggedCycle()
    {
        var value = new LinkMessage();
        value.Next = value;
        return value;
    }
}
