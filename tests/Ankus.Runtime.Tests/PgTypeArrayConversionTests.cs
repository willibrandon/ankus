namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies custom-type vector conversion preserves writable CLR element types and PostgreSQL identity.
/// </summary>
[TestClass]
public sealed class PgTypeArrayConversionTests
{
    /// <summary>
    /// Registers isolated contracts whose vector conversions must not invoke a codec or backend.
    /// </summary>
    static PgTypeArrayConversionTests()
    {
        PgTypeRegistry.RegisterReference<Message>("test_array_message", null,
            static () => throw new InvalidOperationException("Vector conversion must not construct a codec."));
        PgTypeRegistry.RegisterReference<NumberMessage>("test_array_number_message", null,
            static () => throw new InvalidOperationException("Vector conversion must not construct a codec."));
        PgTypeRegistry.RegisterValue<MessageKind>("test_array_message_kind", null,
            static () => throw new InvalidOperationException("Vector conversion must not construct a codec."));
    }

    /// <summary>
    /// Copies a covariant vector into its requested root element type so a sibling variant can be assigned safely.
    /// </summary>
    [TestMethod]
    public void CovariantCustomVectorsReturnIndependentWritableRootArrays()
    {
        NumberMessage[] source = [new(7), new(42)];
        var row = new SpiRow([source], [new SpiColumn("value", 42)]);
        CustomTypeMapping mapping = PgTypeRegistry.Require(typeof(Message));
        Assert.IsTrue(mapping.AcceptsArray(source));
        PgArray<Message> wrapped = Assert.IsInstanceOfType<PgArray<Message>>(mapping.Wrap(source));
        Assert.AreSequenceEqual<Message>([new NumberMessage(7), new NumberMessage(42)], wrapped.ToVector());

        Message[] converted = row.Get<Message[]>("value");

        Assert.AreEqual(typeof(Message[]), converted.GetType());
        Assert.AreNotSame(source, converted);
        Assert.AreSequenceEqual<Message>([new NumberMessage(7), new NumberMessage(42)], converted);
        var sibling = new TextMessage("replacement");
        converted[0] = sibling;
        Assert.AreSame(sibling, converted[0]);
        Assert.AreEqual(new NumberMessage(42), converted[1]);
        Assert.AreSame(source, row["value"]);
        Assert.AreEqual(typeof(NumberMessage[]), row["value"]!.GetType());
        Assert.AreSequenceEqual<NumberMessage>([new(7), new(42)], source);
    }

    /// <summary>
    /// Refuses CLR-compatible integer arrays when the requested elements have an independently mapped custom enum identity.
    /// </summary>
    [TestMethod]
    public void CustomEnumVectorsRejectUnderlyingIntegerArrays()
    {
        int[] source = [1, 2];
        var row = new SpiRow([source], [new SpiColumn("value", 42)]);
        CustomTypeMapping mapping = PgTypeRegistry.Require(typeof(MessageKind));
        int?[] nullableSource = [1, null, 2];

        Assert.IsFalse(mapping.AcceptsArray(source));
        Assert.ThrowsExactly<InvalidCastException>(() => mapping.Wrap(source));
        Assert.IsFalse(mapping.AcceptsArray(nullableSource));
        Assert.ThrowsExactly<InvalidCastException>(() => mapping.Wrap(nullableSource));

        Assert.ThrowsExactly<InvalidCastException>(() => row.Get<MessageKind[]>("value"));

        Assert.AreSame(source, row["value"]);
        Assert.AreSame(source, row.Get<int[]>("value"));
        Assert.AreSequenceEqual<int>([1, 2], source);
        Assert.AreSequenceEqual<int?>([1, null, 2], nullableSource);
        MessageKind[] valid = [MessageKind.Number, MessageKind.Text];
        Assert.IsTrue(mapping.AcceptsArray(valid));
        PgArray<MessageKind> wrapped = Assert.IsInstanceOfType<PgArray<MessageKind>>(mapping.Wrap(valid));
        Assert.AreSequenceEqual<MessageKind>([MessageKind.Number, MessageKind.Text], wrapped.ToVector());
        MessageKind?[] nullableValid = [MessageKind.Number, null, MessageKind.Text];
        Assert.IsTrue(mapping.AcceptsArray(nullableValid));
        PgArray<MessageKind?> nullableWrapped = Assert.IsInstanceOfType<PgArray<MessageKind?>>(mapping.Wrap(nullableValid));
        Assert.AreSequenceEqual<MessageKind?>([MessageKind.Number, null, MessageKind.Text], nullableWrapped.ToVector());
        var validRow = new SpiRow([valid], [new SpiColumn("value", 42)]);
        Assert.AreSequenceEqual<MessageKind>([MessageKind.Number, MessageKind.Text], validRow.Get<MessageKind[]>("value"));
    }

    /// <summary>
    /// Defines a registered root contract for sibling reference variants.
    /// </summary>
    private abstract record Message;

    /// <summary>
    /// Carries a distinct numeric variant in the original narrower vector.
    /// </summary>
    private sealed record NumberMessage(int Value) : Message;

    /// <summary>
    /// Carries a sibling variant that a correctly widened vector must accept.
    /// </summary>
    private sealed record TextMessage(string Value) : Message;

    /// <summary>
    /// Provides a custom base-type enum whose SQL identity differs from its CLR integer representation.
    /// </summary>
    private enum MessageKind
    {
        /// <summary>
        /// Identifies numeric messages.
        /// </summary>
        Number = 1,

        /// <summary>
        /// Identifies textual messages.
        /// </summary>
        Text = 2,
    }
}
