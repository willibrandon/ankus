using System.Runtime.InteropServices;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies deterministic scalar hashes and their agreement with .NET key equality.
/// </summary>
[TestClass]
public sealed class PgFixedKeyComparerTests
{
    /// <summary>
    /// Fixed FNV vectors establish canonical byte order independently of map round trips.
    /// </summary>
    [TestMethod]
    public void FixedComparersProduceStableHashVectors()
    {
        Assert.AreEqual(1268118805, PgFixedKeyComparer.Create<int>().GetHashCode(0));
        Assert.AreEqual(-76958204, PgFixedKeyComparer.Create<int>().GetHashCode(1));
        Assert.AreEqual(-485093455, PgFixedKeyComparer.Create<uint>().GetHashCode(uint.MaxValue));
        Assert.AreEqual(-76958204, PgFixedKeyComparer.Create<Marker>().GetHashCode(Marker.First));
        Assert.AreEqual(458782360, PgFixedKeyComparer.Create<float>().GetHashCode(1));
        Assert.AreEqual(1768495365, PgFixedKeyComparer.Create<Guid>().GetHashCode(Guid.Empty));
        Assert.AreEqual(890083012, PgFixedKeyComparer.Create<UInt128>().GetHashCode(1));
        Assert.AreEqual(890083012, PgFixedKeyComparer.Create<Int128>().GetHashCode(1));
        Assert.AreEqual(890083012, PgFixedKeyComparer.Create<decimal>().GetHashCode(1.00m));
        CheckScalar(false, true);
        CheckScalar(byte.MinValue, byte.MaxValue);
        CheckScalar(sbyte.MinValue, sbyte.MaxValue);
        CheckScalar(short.MinValue, short.MaxValue);
        CheckScalar(ushort.MinValue, ushort.MaxValue);
        CheckScalar(char.MinValue, char.MaxValue);
        CheckScalar(int.MinValue, int.MaxValue);
        CheckScalar(uint.MinValue, uint.MaxValue);
        CheckScalar(long.MinValue, long.MaxValue);
        CheckScalar(ulong.MinValue, ulong.MaxValue);
        CheckScalar(nint.MinValue, nint.MaxValue);
        CheckScalar(nuint.MinValue, nuint.MaxValue);
        CheckScalar(Int128.MinValue, Int128.MaxValue);
        CheckScalar(UInt128.MinValue, UInt128.MaxValue);
        CheckScalar(Guid.Empty, new Guid("12345678-1234-5678-90ab-123456789abc"));
        CheckScalar(decimal.MinValue, decimal.MaxValue);
        CheckScalar(float.NegativeInfinity, float.PositiveInfinity);
        CheckScalar(double.NegativeInfinity, double.PositiveInfinity);
        CheckScalar(Half.NegativeInfinity, Half.PositiveInfinity);
    }

    /// <summary>
    /// Equal floating and decimal keys use identical hashes despite different physical representations.
    /// </summary>
    [TestMethod]
    public void FixedComparersPreserveEquivalentKeys()
    {
        CheckEquivalent(0f, BitConverter.Int32BitsToSingle(int.MinValue));
        CheckEquivalent(BitConverter.Int32BitsToSingle(0x7FC00073), BitConverter.Int32BitsToSingle(unchecked((int)0xFFC00079)));
        CheckEquivalent(0d, BitConverter.Int64BitsToDouble(long.MinValue));
        CheckEquivalent(BitConverter.Int64BitsToDouble(0x7FF8000000000073), BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8000000000079)));
        CheckEquivalent((Half)0, BitConverter.UInt16BitsToHalf(0x8000));
        CheckEquivalent(BitConverter.UInt16BitsToHalf(0x7E73), BitConverter.UInt16BitsToHalf(0xFE79));
        CheckEquivalent(0m, new decimal(0, 0, 0, true, 28));
        CheckEquivalent(1m, 1.0000000000000000000000000000m);
        CheckEquivalent(-123.45m, -123.4500m);
        CheckEquivalent(1234567890123456789012345m, 1234567890123456789012345.000m);
    }

    /// <summary>
    /// Arbitrary struct padding and generated record hashes are never accepted implicitly.
    /// </summary>
    [TestMethod]
    public void FixedComparersRequireExplicitCustomKeyHashing()
    {
        Assert.ThrowsExactly<NotSupportedException>(() => PgFixedKeyComparer.Create<CustomKey>());
        Assert.ThrowsExactly<NotSupportedException>(() => PgFixedKeyComparer.Create<DateTime>());
        var entries = new PgFixedMapEntry<CustomKey, long>[2];
        int[] indices = new int[2];
        int count = 0;
        Assert.ThrowsExactly<NotSupportedException>(() => _ = new PgFixedMap<CustomKey, long>(entries, indices, ref count));
        Assert.AreEqual(0, count);
        Assert.AreSequenceEqual([0, 0], indices);
        var map = new PgFixedMap<CustomKey, long>(entries, indices, ref count, new CustomKeyComparer());
        map.Add(new CustomKey(11, 13), 73);
        var attached = new PgFixedMap<CustomKey, long>(entries, indices, ref count, new CustomKeyComparer());
        Assert.AreEqual(73L, attached[new CustomKey(11, 13)]);
        Assert.IsFalse(attached.ContainsKey(new CustomKey(11, 17)));
        Assert.IsNull(attached.Set(new CustomKey(11, 17), 79));
        Assert.AreEqual(79L, map[new CustomKey(11, 17)]);
    }

    /// <summary>
    /// Distinct scalar keys remain distinguishable and independent comparer instances agree.
    /// </summary>
    private static void CheckScalar<T>(T first, T second) where T : unmanaged
    {
        IEqualityComparer<T> comparer = PgFixedKeyComparer.Create<T>();
        IEqualityComparer<T> attached = PgFixedKeyComparer.Create<T>();
        Assert.IsFalse(comparer.Equals(first, second));
        Assert.AreEqual(comparer.GetHashCode(first), attached.GetHashCode(first));
        Assert.AreEqual(comparer.GetHashCode(second), attached.GetHashCode(second));
        var entries = new PgFixedMapEntry<T, int>[2];
        int[] indices = new int[2];
        int count = 0;
        var map = new PgFixedMap<T, int>(entries, indices, ref count, comparer);
        map.Add(first, 73);
        map.Add(second, 79);
        Assert.AreEqual(73, map[first]);
        Assert.AreEqual(79, map[second]);
    }

    /// <summary>
    /// Equivalent representations replace one entry and preserve the original key bits.
    /// </summary>
    private static void CheckEquivalent<T>(T first, T second) where T : unmanaged
    {
        IEqualityComparer<T> comparer = PgFixedKeyComparer.Create<T>();
        Assert.IsTrue(comparer.Equals(first, second));
        Assert.AreEqual(comparer.GetHashCode(first), comparer.GetHashCode(second));
        var entries = new PgFixedMapEntry<T, int>[1];
        int[] indices = new int[1];
        int count = 0;
        var map = new PgFixedMap<T, int>(entries, indices, ref count);
        map.Add(first, 73);
        Assert.AreEqual(73, map.Set(second, 79));
        Assert.AreEqual(1, map.Count);
        Assert.AreEqual(79, map[first]);
        Assert.AreEqual(79, map[second]);
        T stored = map.Entries[0].Key;
        Assert.AreSequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in first)).ToArray(),
            MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in stored)).ToArray());
    }

    /// <summary>
    /// Supplies a supported enum's exact underlying integer value.
    /// </summary>
    private enum Marker
    {
        First = 1,
    }

    /// <summary>
    /// Requires a deliberate field-based comparer rather than generated record hashing.
    /// </summary>
    private readonly record struct CustomKey(int Part, int Revision);

    /// <summary>
    /// Defines deterministic custom field hashing and exact key equality.
    /// </summary>
    private sealed class CustomKeyComparer : EqualityComparer<CustomKey>
    {
        /// <inheritdoc/>
        public override bool Equals(CustomKey x, CustomKey y) => x.Part == y.Part && x.Revision == y.Revision;

        /// <inheritdoc/>
        public override int GetHashCode(CustomKey obj) => unchecked(obj.Part * 397 ^ obj.Revision);
    }
}
