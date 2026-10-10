using System.Buffers.Binary;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Checks flat array limits before allocation, as pgrx's <c>error_cases</c> does for <c>FlatArray::new_zeroed_in</c>.
/// </summary>
[TestClass]
public sealed class PgFlatArrayTests
{
    /// <summary>
    /// Too many elements, a zero-length dimension, too many bytes and too many dimensions fail without allocating.
    /// </summary>
    [TestMethod]
    public void LimitsAreCheckedBeforeAllocation()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        PgMemoryContext context = PgMemoryContext.Current;
        Assert.Contains("134217727 elements", Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => context.CreateFlatArray<int>([int.MaxValue])).Message);
        Assert.Contains("134217727 elements", Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => context.CreateFlatArray<int>([65536, 65536])).Message);
        Assert.Contains("must be positive", Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => context.CreateFlatArray<int>([0])).Message);
        Assert.Contains("allocation limit", Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => context.CreateFlatArray<long>([(0x3FFF_FFFF / 8) - 1])).Message);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => context.CreateFlatArray<int>([1, 1, 1, 1, 1, 1, 1]));
        Assert.ThrowsExactly<ArgumentException>(() => context.CreateFlatArray<int>([2, 2], [1]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => context.CreateFlatArray<int>([2], [int.MaxValue]));
        Assert.ThrowsExactly<NotSupportedException>(() => context.CreateFlatArray<Guid>([1]));
        Assert.IsEmpty(fixture.Requests.Where(static request => request._operation == NativeMemoryOperation.Allocate));
    }

    /// <summary>
    /// The array header uses PostgreSQL's 4-byte varlena encoding, as pgrx's <c>encode_vlen_4b_known_values</c> and
    /// <c>encode_vlen_4b_roundtrip</c> check: on little-endian targets the total size is shifted left two bits, and
    /// the rank, data offset, element type, length and lower bound follow.
    /// </summary>
    [TestMethod]
    public void HeadersUseTheFourByteVarlenaEncoding()
    {
        Assert.IsTrue(BitConverter.IsLittleEndian, "The supported targets are little-endian.");
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        byte[] storage = new byte[8192];
        fixture.Handler = request => fixture.RespondWithStorage(request, storage);
        PgMemoryContext context = PgMemoryContext.Current;
        _ = context.CreateFlatArray<int>([3]);
        Assert.AreEqual(36u << 2, BinaryPrimitives.ReadUInt32LittleEndian(storage));
        Assert.AreEqual(36u, BinaryPrimitives.ReadUInt32LittleEndian(storage) >> 2);
        Assert.AreEqual(1, BinaryPrimitives.ReadInt32LittleEndian(storage.AsSpan(4)));
        Assert.AreEqual(0, BinaryPrimitives.ReadInt32LittleEndian(storage.AsSpan(8)));
        Assert.AreEqual(23u, BinaryPrimitives.ReadUInt32LittleEndian(storage.AsSpan(12)));
        Assert.AreEqual(3, BinaryPrimitives.ReadInt32LittleEndian(storage.AsSpan(16)));
        Assert.AreEqual(1, BinaryPrimitives.ReadInt32LittleEndian(storage.AsSpan(20)));
        _ = context.CreateFlatArray<long>([1000], [-5]);
        Assert.AreEqual(8024u << 2, BinaryPrimitives.ReadUInt32LittleEndian(storage));
        Assert.AreEqual(20u, BinaryPrimitives.ReadUInt32LittleEndian(storage.AsSpan(12)));
        Assert.AreEqual(-5, BinaryPrimitives.ReadInt32LittleEndian(storage.AsSpan(20)));
    }
}
