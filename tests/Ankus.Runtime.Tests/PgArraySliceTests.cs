using System.Runtime.InteropServices;

namespace Ankus.Runtime.Tests;

public sealed partial class PgArrayViewTests
{
    /// <summary>
    /// Every supported scalar maps to its exact SQL identity and retains literal native values.
    /// </summary>
    [TestMethod]
    public void NativeArraySlicesPreserveScalarValuesAndIdentity()
    {
        CheckSlice<sbyte>([-128, 0, 127], 18, 1);
        CheckSlice<short>([-32768, 0, 32767], 21, 2);
        CheckSlice<int>([int.MinValue, 0, int.MaxValue], 23, 4);
        CheckSlice<long>([long.MinValue, 0, long.MaxValue], 20, 8);
        CheckSlice<float>([-1.5F, 0F, 7.25F], 700, 4);
        CheckSlice<double>([-1.5, 0, 7.25], 701, 8);
    }

    /// <summary>
    /// Floating point slices retain NaN payloads, signed zero and infinity without conversion.
    /// </summary>
    [TestMethod]
    public void NativeArraySlicesPreserveFloatingPointBits()
    {
        CheckSlice<float>([BitConverter.Int32BitsToSingle(0x7FC12345), -0F, float.NegativeInfinity], 700, 4);
        CheckSlice<double>([BitConverter.Int64BitsToDouble(0x7FF8123456789ABC), -0D, double.PositiveInfinity], 701, 8);
    }

    /// <summary>
    /// UUID byte spans retain network order and survive view disposal only through an explicit copy.
    /// </summary>
    [TestMethod]
    public unsafe void NativeUuidSlicesRetainNetworkByteOrder()
    {
        byte[] expected = Convert.FromHexString(
            "00112233445566778899AABBCCDDEEFF" + "00000000000000000000000000000000" + "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF");
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope memory = MemoryContextTestFixture.Enter();
        using var script = new ArrayScript(fixture) { SliceType = 2950 };
        script.SetSlice(expected);
        using var view = new PgArrayView(PgDatum.DangerousCreate(123, 2951, PgMemoryContext.Current));
        ReadOnlySpan<byte> bytes = view.DangerousGetUuidBytes();
        Assert.AreSequenceEqual(expected, bytes.ToArray());
        Assert.AreEqual(new Guid("00112233-4455-6677-8899-aabbccddeeff"), new Guid(bytes[..16], bigEndian: true));
        fixed (byte* address = bytes)
        {
            Assert.AreEqual(script.SliceStorage, (nint)address);
        }

        Assert.AreSequenceEqual<(uint, long)>([(2950, 16)], script.Slices);
        byte[] copy = bytes.ToArray();
        view.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => view.DangerousGetUuidBytes());
        Assert.AreSequenceEqual(expected, copy);
        Assert.AreEqual(2, script.Releases);
    }

    /// <summary>
    /// Unmanaged types without a declared native layout reject before requesting a native slice.
    /// </summary>
    [TestMethod]
    public void NativeArraySlicesRejectUnsupportedManagedTypes()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope memory = MemoryContextTestFixture.Enter();
        using var script = new ArrayScript(fixture);
        using var view = new PgArrayView(PgDatum.DangerousCreate(123, 1007, PgMemoryContext.Current));
        Assert.ThrowsExactly<NotSupportedException>(() => view.DangerousGetSpan<bool>());
        Assert.ThrowsExactly<NotSupportedException>(() => view.DangerousGetSpan<byte>());
        Assert.ThrowsExactly<NotSupportedException>(() => view.DangerousGetSpan<char>());
        Assert.ThrowsExactly<NotSupportedException>(() => view.DangerousGetSpan<uint>());
        Assert.ThrowsExactly<NotSupportedException>(() => view.DangerousGetSpan<decimal>());
        Assert.ThrowsExactly<NotSupportedException>(() => view.DangerousGetSpan<Guid>());
        Assert.ThrowsExactly<NotSupportedException>(() => view.DangerousGetSpan<(int, int)>());
        Assert.IsEmpty(script.Slices);
        Assert.HasCount(1, script.Requests);
    }

    /// <summary>
    /// Invalid native addresses, alignment, counts and types reject before any payload access.
    /// </summary>
    /// <param name="mode">The malformed metadata partition.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void NativeArraySlicesRejectMalformedMetadataAndReleaseResponses(int mode)
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope memory = MemoryContextTestFixture.Enter();
        using var script = new ArrayScript(fixture)
        {
            SliceType = 23,
            InvalidSlice = mode,
        };
        script.SetSlice(new byte[12]);
        using var view = new PgArrayView(PgDatum.DangerousCreate(123, 1007, PgMemoryContext.Current));
        Assert.AreEqual("Invalid borrowed array slice metadata.",
            Assert.ThrowsExactly<InvalidOperationException>(() => view.DangerousGetSpan<int>()).Message);
        Assert.AreEqual(2, script.Releases);
        Assert.AreSequenceEqual<(uint, long)>([(23, 4)], script.Slices);
        Assert.IsEmpty(script.Deleted);
    }

    /// <summary>
    /// Checks typed values, exact bits, original storage, requested identity and a surviving copy.
    /// </summary>
    private static unsafe void CheckSlice<T>(T[] expected, uint oid, int width) where T : unmanaged
    {
        byte[] original = MemoryMarshal.AsBytes(expected.AsSpan()).ToArray();
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope memory = MemoryContextTestFixture.Enter();
        using var script = new ArrayScript(fixture) { SliceType = oid };
        script.SetSlice(original);
        using var view = new PgArrayView(PgDatum.DangerousCreate(123, 1007, PgMemoryContext.Current));
        ReadOnlySpan<T> values = view.DangerousGetSpan<T>();
        Assert.AreSequenceEqual(original, MemoryMarshal.AsBytes(values).ToArray());
        Assert.AreSequenceEqual(expected, values.ToArray());
        fixed (T* address = values)
        {
            Assert.AreEqual(script.SliceStorage, (nint)address);
        }

        Assert.AreSequenceEqual<(uint, long)>([(oid, width)], script.Slices);
        T[] copy = values.ToArray();
        view.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => view.DangerousGetSpan<T>());
        Assert.AreSequenceEqual(original, MemoryMarshal.AsBytes(copy.AsSpan()).ToArray());
        Assert.AreEqual(2, script.Releases);
    }
}
