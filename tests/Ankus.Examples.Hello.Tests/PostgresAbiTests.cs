using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ankus.Examples.Hello.Tests;

/// <summary>
/// Verifies that the managed ABI overlays exactly match the PostgreSQL 18 C
/// structures consumed by the dynamic module loader and function manager.
/// </summary>
[TestClass]
public sealed class PostgresAbiTests
{
    /// <summary>
    /// Verifies all module-magic structures have the exact native sizes expected
    /// by a 64-bit PostgreSQL 18 server.
    /// </summary>
    [TestMethod]
    public void ModuleMagicStructuresHavePostgreSql18NativeSizes()
    {
        Assert.AreEqual(32, Unsafe.SizeOf<PgAbiExtra>());
        Assert.AreEqual(52, Unsafe.SizeOf<PgAbiValues>());
        Assert.AreEqual(72, Unsafe.SizeOf<PgMagic>());
        Assert.AreEqual(4, Unsafe.SizeOf<PgFinfoRecord>());
    }

    /// <summary>
    /// Verifies the function-call structures preserve PostgreSQL's field offsets
    /// and native alignment before any datum is read from unmanaged memory.
    /// </summary>
    [TestMethod]
    public void FunctionCallStructuresHavePostgreSql18NativeSizes()
    {
        Assert.AreEqual(32, Unsafe.SizeOf<FunctionCallInfo>());
        Assert.AreEqual(16, Unsafe.SizeOf<NullableDatum>());
    }

    /// <summary>
    /// Verifies the generated magic block contains every value PostgreSQL compares
    /// byte-for-byte before accepting the extension library.
    /// </summary>
    [TestMethod]
    public void ModuleMagicContainsPostgreSql18AbiValues()
    {
        PgMagic magic = PgMagic.Create();
        ReadOnlySpan<byte> bytes = AsBytes(ref magic);

        Assert.AreEqual(72, ReadInt32(bytes, 0));
        Assert.AreEqual(1800, ReadInt32(bytes, 4));
        Assert.AreEqual(100, ReadInt32(bytes, 8));
        Assert.AreEqual(32, ReadInt32(bytes, 12));
        Assert.AreEqual(64, ReadInt32(bytes, 16));
        Assert.AreEqual(1, ReadInt32(bytes, 20));
        Assert.AreSequenceEqual("PostgreSQL\0"u8, bytes.Slice(24, 11));
        Assert.AreSequenceEqual(new byte[21], bytes.Slice(35, 21));
        Assert.AreEqual(0L, BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(56, 8)));
        Assert.AreEqual(0L, BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(64, 8)));
    }

    /// <summary>
    /// Verifies each SQL-callable export advertises PostgreSQL's version-1 function
    /// manager convention through its finfo record.
    /// </summary>
    [TestMethod]
    public void FunctionInfoAdvertisesVersionOneConvention()
    {
        PgFinfoRecord functionInfo = PgFinfoRecord.Create();
        ReadOnlySpan<byte> bytes = AsBytes(ref functionInfo);

        Assert.AreEqual(1, ReadInt32(bytes, 0));
    }

    private static ReadOnlySpan<byte> AsBytes<T>(ref T value)
        where T : struct
        => MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1));

    private static int ReadInt32(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, sizeof(int)));
}
