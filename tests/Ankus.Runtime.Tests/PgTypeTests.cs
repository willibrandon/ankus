using System.Buffers;
using System.Globalization;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies closed custom mappings, detached arrays and exact native payload ownership.
/// </summary>
[TestClass]
public sealed class PgTypeTests
{
    /// <summary>
    /// Installs one isolated value-type mapping for backend-independent checks.
    /// </summary>
    static PgTypeTests() => PgTypeRegistry.RegisterValue<Number>("test_number", null, static () => new NumberCodec());

    /// <summary>
    /// Copies bytes rather than retaining the caller's span and validates exact unsigned OID identity.
    /// </summary>
    [TestMethod]
    public void CustomPayloadCopiesStorageAndRequiresExactIdentity()
    {
        byte[] input = [0, 255, 42];
        NativeValue value = NativeValue.FromCustomPayload(input, uint.MaxValue);
        try
        {
            input[0] = 9;
            byte[] expected = [0, 255, 42];
            Assert.AreSequenceEqual(expected, value.ReadCustomPayload(uint.MaxValue).ToArray());
            Assert.ThrowsExactly<InvalidOperationException>(() => value.ReadCustomPayload(uint.MaxValue - 1).ToArray());
            Assert.ThrowsExactly<InvalidOperationException>(() => value.ReadCustomPayload(0).ToArray());
        }
        finally
        {
            value.Release();
        }
    }

    /// <summary>
    /// Distinguishes a present empty payload, SQL NULL and unrelated byte transports.
    /// </summary>
    [TestMethod]
    public void CustomPayloadEmptyAndNullRemainDistinct()
    {
        NativeValue empty = NativeValue.FromCustomPayload([], 42);
        NativeValue bytes = NativeValue.FromBytes([]);
        try
        {
            Assert.AreEqual(0, empty.IsNull);
            Assert.IsEmpty(empty.ReadCustomPayload(42).ToArray());
            Assert.ThrowsExactly<InvalidOperationException>(() => bytes.ReadCustomPayload(42).ToArray());
            NativeValue absent = NativeValue.FromCustom<Number?>(null);
            Assert.AreEqual(1, absent.IsNull);
            Assert.ThrowsExactly<InvalidOperationException>(() => absent.ReadCustomPayload(42).ToArray());
        }
        finally
        {
            empty.Release();
            bytes.Release();
        }
    }

    /// <summary>
    /// Preserves shape and NULL cells while rejecting vector shape loss and required NULL elements.
    /// </summary>
    [TestMethod]
    public void CustomArraysPreserveShapeAndRejectLossyConversions()
    {
        var array = new PgArray<Number?>([new(1), null, new(3), new(4)], ([2, 2], [-1, 3]));
        PgArray<Number?> copy = (PgArray<Number?>)SpiArray.Convert(array, typeof(PgArray<Number?>));
        Assert.AreSequenceEqual([2, 2], copy.Lengths.ToArray());
        Assert.AreSequenceEqual([-1, 3], copy.LowerBounds.ToArray());
        Assert.AreEqual(new Number(1), copy[0]);
        Assert.IsNull(copy[1]);
        Assert.ThrowsExactly<InvalidOperationException>(() => SpiArray.Convert(array, typeof(Number?[])));
        Assert.ThrowsExactly<InvalidOperationException>(() => SpiArray.Convert(array, typeof(PgArray<Number>)));
        var required = new PgArray<Number>([new(9)]);
        Assert.AreSequenceEqual([new Number(9)], (Number?[])SpiArray.Convert(required, typeof(Number?[])));
        Assert.ThrowsExactly<InvalidCastException>(() => SpiArray.Convert(new PgArray<long>([9]), typeof(Number[])));
    }

    /// <summary>
    /// Duplicate registration cannot replace a mapping already used by generated callbacks.
    /// </summary>
    [TestMethod]
    public void CustomRegistrationRetainsOriginalContractOnFailure()
    {
        CustomTypeMapping original = PgTypeRegistry.Require(typeof(Number));
        Assert.AreSame(original, PgTypeRegistry.Require(typeof(Number?)));
        Assert.AreSame(original, PgTypeRegistry.FindArray(typeof(Number?[])));
        Assert.ThrowsExactly<InvalidOperationException>(() => PgTypeRegistry.RegisterValue<Number>("replacement", null, static () => new NumberCodec()));
        Assert.AreSame(original, PgTypeRegistry.Require(typeof(Number)));
        Assert.AreEqual(new Number(42), original.Parse("42"));
        Assert.AreEqual("42", original.Format(new Number(42)));
        Assert.ThrowsExactly<ArgumentException>(() => PgTypeRegistry.RegisterValue<Number>("", null, static () => new NumberCodec()));
        Assert.ThrowsExactly<ArgumentNullException>(() => PgTypeRegistry.RegisterValue<Number>("number", null, null!));
    }

    /// <summary>
    /// A present reference value cannot silently become SQL NULL through text conversion.
    /// </summary>
    [TestMethod]
    public void NullCodecResultsAreRejected()
    {
        var mapping = new CustomTypeMapping<string, string?>("bad", null, static () => new NullCodec());
        Assert.ThrowsExactly<InvalidOperationException>(() => mapping.Parse("input"));
        Assert.ThrowsExactly<InvalidOperationException>(() => mapping.Format("value"));
    }

    /// <summary>
    /// Defers user construction until an operation can catch it and creates a single shared codec.
    /// </summary>
    [TestMethod]
    public void CustomCodecConstructionIsDeferredAndCached()
    {
        int constructions = 0;
        var mapping = new CustomTypeMapping<Number, Number?>("lazy", null, () =>
        {
            constructions++;
            return new NumberCodec();
        });
        Assert.AreEqual(0, constructions);
        Assert.AreEqual(new Number(2), mapping.Parse("2"));
        Assert.AreEqual("3", mapping.Format(new Number(3)));
        Assert.AreEqual(1, constructions);
        var missing = new CustomTypeMapping<Number, Number?>("missing", null, static () => null!);
        Assert.ThrowsExactly<InvalidOperationException>(() => missing.Parse("1"));
    }

    /// <summary>
    /// Carries a direct-test value independently of backend OIDs.
    /// </summary>
    /// <param name="Value">The exact value.</param>
    private readonly record struct Number(long Value);

    /// <summary>
    /// Provides text conversions used by registry tests without requiring native callbacks.
    /// </summary>
    private sealed class NumberCodec : PgTypeCodec<Number>
    {
        /// <inheritdoc />
        public override Number Parse(string text) => new(long.Parse(text, CultureInfo.InvariantCulture));

        /// <inheritdoc />
        public override string Format(Number value) => value.Value.ToString(CultureInfo.InvariantCulture);

        /// <inheritdoc />
        public override Number Read(ReadOnlySpan<byte> payload) => new(System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(payload));

        /// <inheritdoc />
        public override void Write(Number value, IBufferWriter<byte> destination)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(destination.GetSpan(8), value.Value);
            destination.Advance(8);
        }
    }

    /// <summary>
    /// Violates the present-value contract to exercise defensive checks.
    /// </summary>
    private sealed class NullCodec : PgTypeCodec<string>
    {
        /// <inheritdoc />
        public override string Parse(string text) => null!;

        /// <inheritdoc />
        public override string Format(string value) => null!;

        /// <inheritdoc />
        public override string Read(ReadOnlySpan<byte> payload) => null!;

        /// <inheritdoc />
        public override void Write(string value, IBufferWriter<byte> destination) => destination.Advance(0);
    }
}
