using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies exact callback frames and values independently of generated native entry points.
/// </summary>
[TestClass]
public sealed unsafe class NativeRawCallbackTests
{
    /// <summary>
    /// Rejects missing and mismatched envelopes before a handler effect, then accepts the corrected frame.
    /// </summary>
    /// <param name="scenario">The independently malformed frame partition.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    [DataRow(10)]
    public void NativeCallbackFramesRejectInvalidStorageAndRecover(int scenario)
    {
        int value = 731;
        NativeCallArgument argument = new((nint)(&value), sizeof(int));
        nint descriptor = (nint)(&argument);
        nint destination = (nint)(&value);
        nuint count = 1;
        int expectedCount = 1;
        nuint resultSize = sizeof(int);
        int expectedSize = sizeof(int);
        switch (scenario)
        {
            case 0: count = 0; break;
            case 1: count = 2; break;
            case 2: count = nuint.MaxValue; break;
            case 3: descriptor = 0; break;
            case 4: expectedCount = -1; break;
            case 5: destination = 0; break;
            case 6: resultSize--; break;
            case 7: resultSize++; break;
            case 8: expectedSize = -2; break;
            case 9: expectedSize = -1; resultSize = 0; break;
            default: expectedSize = -1; destination = 0; break;
        }

        int effects = 0;
        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(ValidateAndRun);
        Assert.Contains(scenario < 5 ? "argument frame" : "result storage", error.Message);
        Assert.AreEqual(0, effects);
        Assert.AreEqual(731, value);
        NativeRawCallback.ValidateFrame(&argument, 1, 1, (nint)(&value), sizeof(int), sizeof(int));
        NativeRawCallback.ValidateFrame(null, 0, 0, 0, 0, -1);
        NativeRawCallback.ValidateFrame(null, 0, 0, (nint)(&value), 0, 0);
        Assert.AreEqual(731, value);

        void ValidateAndRun()
        {
            NativeRawCallback.ValidateFrame((NativeCallArgument*)descriptor, count, expectedCount, destination, resultSize, expectedSize);
            effects++;
        }
    }

    /// <summary>
    /// Unaligned copies retain full-width signed values and aggregates while empty native values never read or write placeholder bytes.
    /// </summary>
    [TestMethod]
    public void NativeCallbackValuesPreserveExactUnalignedStorage()
    {
        byte* storage = stackalloc byte[32];
        new Span<byte>(storage, 32).Fill(0xA7);
        const long Expected = long.MinValue + 0x71234567;
        NativeRawCallback.Write((nint)(storage + 1), sizeof(long), Expected);
        Assert.AreEqual(Expected, Unsafe.ReadUnaligned<long>(storage + 1));
        Assert.AreEqual(Expected, NativeRawCallback.Read<long>(new((nint)(storage + 1), sizeof(long))));
        Assert.AreEqual((byte)0xA7, storage[0]);
        Assert.AreEqual((byte)0xA7, storage[9]);
        var aggregate = new Pair(0x0123456789ABCDEF, long.MinValue + 31);
        NativeRawCallback.WriteNative((nint)(storage + 3), 16, aggregate);
        Assert.AreEqual(aggregate, NativeRawCallback.ReadNative<Pair>(new((nint)(storage + 3), 16)));
        Assert.AreEqual((byte)0xA7, storage[19]);
        byte[] before = new ReadOnlySpan<byte>(storage, 32).ToArray();
        Assert.AreEqual(default, NativeRawCallback.ReadNative<Empty>(new(1, 0)));
        NativeRawCallback.WriteNative((nint)(storage + 1), 0, default(Empty));
        Assert.AreSequenceEqual(before, new ReadOnlySpan<byte>(storage, 32).ToArray());
        Assert.AreEqual(0, NativeRawCallback.NativeSize<Empty>());
        Assert.AreEqual(16, NativeRawCallback.NativeSize<Pair>());
        Assert.AreEqual(5 * sizeof(nint), sizeof(NativeCallbackContext));
    }

    /// <summary>
    /// Incorrect individual storage sizes cannot be copied or overwrite the destination, including null empty storage.
    /// </summary>
    /// <param name="size">A rejected size immediately below, above, or far outside the primitive representation.</param>
    [TestMethod]
    [DataRow(0UL)]
    [DataRow(7UL)]
    [DataRow(9UL)]
    [DataRow(ulong.MaxValue)]
    public void NativeCallbackCopiesRejectInvalidSizes(ulong size)
    {
        long original = 912345678901234;
        nint address = (nint)(&original);
        Assert.ThrowsExactly<InvalidOperationException>(() => NativeRawCallback.Read<long>(new(address, (nuint)size)));
        Assert.ThrowsExactly<InvalidOperationException>(() => NativeRawCallback.Write(address, (nuint)size, 71L));
        Assert.AreEqual(912345678901234, original);
        Assert.ThrowsExactly<InvalidOperationException>(() => NativeRawCallback.Read<long>(new(0, sizeof(long))));
        Assert.ThrowsExactly<InvalidOperationException>(() => NativeRawCallback.Write(0, sizeof(long), 71L));
        Assert.ThrowsExactly<InvalidOperationException>(() => NativeRawCallback.ReadNative<Empty>(new(0, 0)));
        Assert.ThrowsExactly<InvalidOperationException>(() => NativeRawCallback.WriteNative(0, 0, default(Empty)));
        NativeRawCallback.Write(address, sizeof(long), 73L);
        Assert.AreEqual(73, original);
    }

    /// <summary>
    /// Callback registration validates the exact measured identity within each active native capability and retains owned errors.
    /// </summary>
    [TestMethod]
    public void NativeCallbackBindingRequiresMatchingCapability()
    {
        using var fixture = new MemoryContextTestFixture();
        Assert.ThrowsExactly<InvalidOperationException>(NativeRawCallback.ValidateBinding<CallbackValue>);
        using (MemoryContextTestFixture.Enter(29))
        {
            fixture.Handler = request =>
            {
                Assert.AreEqual(NativeMemoryOperation.NativeBinding, request._operation);
                Assert.AreEqual(18, request._value);
                Assert.AreEqual((nuint)64, request._length);
                Assert.AreEqual(new string('A', 64), Encoding.UTF8.GetString(new ReadOnlySpan<byte>((void*)request._data, 64)));
                Assert.AreEqual(29, NativeMemoryContext.Provider);
                return default;
            };
            NativeRawCallback.ValidateBinding<CallbackValue>();
            Assert.HasCount(1, fixture.Requests);
            fixture.Handler = static _ => throw new PgException("0A000", "callback binding café", "different native graph");
            PgException error = Assert.ThrowsExactly<PgException>(NativeRawCallback.ValidateBinding<CallbackValue>);
            Assert.AreEqual("0A000", error.SqlState);
            Assert.AreEqual("callback binding café", error.Message);
            Assert.AreEqual("different native graph", error.Detail);
            Assert.AreEqual(2, fixture.ErrorReleases);
            fixture.Handler = null;
            NativeRawCallback.ValidateBinding<CallbackValue>();
            Assert.HasCount(3, fixture.Requests);
            Assert.AreEqual(2, fixture.ErrorReleases);
        }

        Assert.ThrowsExactly<InvalidOperationException>(NativeRawCallback.ValidateBinding<CallbackValue>);
    }

    /// <summary>
    /// Invalid generated representations reject without consulting a native provider or touching caller storage.
    /// </summary>
    [TestMethod]
    public void NativeCallbackMetadataRejectsInvalidRepresentations()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        Assert.ThrowsExactly<PlatformNotSupportedException>(NativeRawCallback.ValidateBinding<Empty>);
        Assert.ThrowsExactly<PlatformNotSupportedException>(NativeRawCallback.ValidateBinding<Pair>);
        Assert.ThrowsExactly<PlatformNotSupportedException>(NativeRawCallback.ValidateBinding<ForeignValue>);
        Assert.ThrowsExactly<PlatformNotSupportedException>(NativeRawCallback.ValidateBinding<InvalidIdentity>);
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => NativeRawCallback.NativeSize<InvalidSize>());
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => NativeRawCallback.ReadNative<InvalidSize>(new(1, 3)));
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => NativeRawCallback.WriteNative(1, 3, default(InvalidSize)));
        Assert.IsEmpty(fixture.Requests);
        Assert.AreEqual("signature", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new NativeFunctionPointerAttribute(-1)).ParamName);
        Assert.AreEqual(0, new NativeFunctionPointerAttribute(0).Signature);
        Assert.AreEqual(int.MaxValue, new NativeFunctionPointerAttribute(int.MaxValue).Signature);
        Assert.AreEqual("method", Assert.ThrowsExactly<ArgumentException>(() => new PgNativeCallbackAttribute(" ")).ParamName);
        Assert.AreEqual("Handler", new PgNativeCallbackAttribute("Handler").Method);
    }

    private readonly record struct CallbackValue(nint Address) : IPgNativeType
    {
        static int IPgNativeType.PostgresMajor => 18;
        static string IPgNativeType.AbiIdentity => new('A', 64);
        static string IPgNativeType.RuntimeIdentifier => RuntimeInformation.RuntimeIdentifier;
        static int IPgNativeType.NativeSize => sizeof(nint);
        static int IPgNativeType.NativeAlignment => sizeof(nint);
    }

    private readonly record struct Pair(long First, long Second) : IPgNativeType
    {
        static int IPgNativeType.PostgresMajor => 18;
        static string IPgNativeType.AbiIdentity => new('A', 64);
        static string IPgNativeType.RuntimeIdentifier => RuntimeInformation.RuntimeIdentifier;
        static int IPgNativeType.NativeSize => 16;
        static int IPgNativeType.NativeAlignment => 8;
    }

    private readonly record struct Empty : IPgNativeType
    {
        static int IPgNativeType.PostgresMajor => 18;
        static string IPgNativeType.AbiIdentity => new('A', 64);
        static string IPgNativeType.RuntimeIdentifier => RuntimeInformation.RuntimeIdentifier;
        static int IPgNativeType.NativeSize => 0;
        static int IPgNativeType.NativeAlignment => 1;
    }

    private readonly record struct InvalidSize(int Value) : IPgNativeType
    {
        static int IPgNativeType.PostgresMajor => 18;
        static string IPgNativeType.AbiIdentity => new('A', 64);
        static string IPgNativeType.RuntimeIdentifier => RuntimeInformation.RuntimeIdentifier;
        static int IPgNativeType.NativeSize => 3;
        static int IPgNativeType.NativeAlignment => 4;
    }

    private readonly record struct ForeignValue(nint Address) : IPgNativeType
    {
        static int IPgNativeType.PostgresMajor => 18;
        static string IPgNativeType.AbiIdentity => new('A', 64);
        static string IPgNativeType.RuntimeIdentifier => "foreign-abi";
        static int IPgNativeType.NativeSize => sizeof(nint);
        static int IPgNativeType.NativeAlignment => sizeof(nint);
    }

    private readonly record struct InvalidIdentity(nint Address) : IPgNativeType
    {
        static int IPgNativeType.PostgresMajor => 18;
        static string IPgNativeType.AbiIdentity => new('é', 64);
        static string IPgNativeType.RuntimeIdentifier => RuntimeInformation.RuntimeIdentifier;
        static int IPgNativeType.NativeSize => sizeof(nint);
        static int IPgNativeType.NativeAlignment => sizeof(nint);
    }
}
