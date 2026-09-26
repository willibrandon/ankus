namespace Ankus.Runtime.Tests;

public sealed unsafe partial class NativeReferenceTests
{
    /// <summary>
    /// Typed casts share the exact allocation offset, subsequent value mutations and resized allocation identity.
    /// </summary>
    [TestMethod]
    public void AllocationReinterpretPreservesOffsetAndResize()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        byte[] storage = [11, 12, 13, 14, .. BitConverter.GetBytes(0x1020304050607080L), 21, 22, 23, 24];
        fixture.Handler = request => fixture.RespondWithStorage(request, storage);
        using PgAllocation allocation = PgMemoryContext.Current.Allocate(16);
        PgNativeReference<long> original = allocation.Borrow<long>(4);
        PgNativeReference<int> prefix = original.Reinterpret<int>();
        PgNativeReference<long> roundtrip = prefix.Reinterpret<long>();
        Assert.AreEqual(0x1020304050607080L, roundtrip.Value);
        Assert.AreEqual((nint)original.DangerousGetPointer(), (nint)prefix.DangerousGetPointer());
        Assert.AreEqual((nuint)12, prefix.AvailableLength);
        roundtrip.Value = -17;
        Assert.AreEqual(-17, original.Value);
        Assert.AreSequenceEqual<byte>([11, 12, 13, 14], storage[..4]);
        Assert.AreSequenceEqual<byte>([21, 22, 23, 24], storage[12..]);
        allocation.Reallocate(12);
        Assert.AreEqual((nuint)8, prefix.AvailableLength);
        Assert.AreEqual(-17, roundtrip.Value);
        Assert.AreEqual(502, fixture.Requests[^1]._context);
        allocation.Reallocate(11);
        Assert.AreEqual((nuint)7, prefix.AvailableLength);
        Assert.ThrowsExactly<InvalidCastException>(() => prefix.Reinterpret<long>());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => roundtrip.Value);
        allocation.Reallocate(12);
        Assert.AreEqual(-17, prefix.Reinterpret<long>().Value);
        Assert.IsEmpty(fixture.Requests.Where(static request => request._operation is NativeMemoryOperation.Adopt or NativeMemoryOperation.CaptureGeneration));
    }

    /// <summary>
    /// Shrinking away any part of a source view rejects reinterpretation even when a smaller target would fit.
    /// </summary>
    [TestMethod]
    public void ReinterpretRejectsAnInvalidSourceRange()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        using PgAllocation allocation = PgMemoryContext.Current.Allocate(8);
        PgNativeReference<long> reference = allocation.Borrow<long>();
        allocation.Reallocate(4);
        fixture.Requests.Clear();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => reference.Reinterpret<int>());
        Assert.IsEmpty(fixture.Requests);
        allocation.Dispose();
        fixture.Requests.Clear();
        Assert.ThrowsExactly<ObjectDisposedException>(() => reference.Reinterpret<int>());
        Assert.IsEmpty(fixture.Requests);
    }

    /// <summary>
    /// A raw upcast and downcast preserve the original entire extent, address, generation and shared bytes.
    /// </summary>
    [TestMethod]
    public void RawReinterpretRetainsExtentAndCapturedGeneration()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        long value = 0x1020304050607080L;
        fixture.Handler = request => RespondRaw(fixture, request);
        PgNativeReference<long>? reference = PgMemoryContext.Current.DangerousBorrow<long>(&value);
        Assert.IsNotNull(reference);
        fixture.Requests.Clear();
        PgNativeReference<int> prefix = reference.Reinterpret<int>();
        PgNativeReference<long> roundtrip = prefix.Reinterpret<long>();
        Assert.AreEqual((nuint)sizeof(long), prefix.AvailableLength);
        Assert.AreEqual((nint)(&value), (nint)roundtrip.DangerousGetPointer());
        Assert.AreEqual(value, roundtrip.Value);
        roundtrip.Value = -93;
        Assert.AreEqual(-93, value);
        Assert.AreEqual(-93, reference.Value);
        Assert.IsTrue(fixture.Requests.All(static request => request._context == 101 && request._other == 901));
        Assert.IsEmpty(fixture.Requests.Where(static request => request._operation == NativeMemoryOperation.CaptureGeneration));
    }

    /// <summary>
    /// A raw view cannot infer adjacent accessible storage from its address or from a smaller intermediate view.
    /// </summary>
    [TestMethod]
    public void RawReinterpretCannotWidenThePromisedExtent()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        long value = 0x1020304050607080L;
        fixture.Handler = request => RespondRaw(fixture, request);
        PgNativeReference<int>? reference = PgMemoryContext.Current.DangerousBorrow<int>(&value);
        Assert.IsNotNull(reference);
        PgNativeReference<byte> prefix = reference.Reinterpret<byte>();
        Assert.AreEqual((nuint)sizeof(int), prefix.AvailableLength);
        Assert.ThrowsExactly<InvalidCastException>(() => reference.Reinterpret<long>());
        Assert.ThrowsExactly<InvalidCastException>(() => prefix.Reinterpret<long>());
        Assert.AreEqual(0x1020304050607080L, value);
    }

    /// <summary>
    /// Explicit raw extent permits a complete larger view while preserving the original single generation capture.
    /// </summary>
    [TestMethod]
    public void ExplicitRawExtentSupportsACompleteLargerView()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        long value = 71;
        fixture.Handler = request => RespondRaw(fixture, request);
        PgNativeReference<int>? reference = PgMemoryContext.Current.DangerousBorrow<int>(&value, sizeof(long));
        Assert.IsNotNull(reference);
        PgNativeReference<long> complete = reference.Reinterpret<long>();
        Assert.AreEqual(71, complete.Value);
        complete.Value = 99;
        Assert.AreEqual(99, value);
        Assert.AreEqual((nint)(&value), (nint)complete.DangerousGetPointer());
        Assert.ContainsSingle(static request => request._operation == NativeMemoryOperation.CaptureGeneration, fixture.Requests);
    }

    /// <summary>
    /// An incomplete explicit extent fails before generation capture; a null address still needs no live backend.
    /// </summary>
    [TestMethod]
    public void ExplicitRawExtentValidatesBeforeCapturingGeneration()
    {
        using var fixture = new MemoryContextTestFixture();
        PgMemoryContext context;
        using (MemoryContextTestFixture.Enter())
        {
            context = PgMemoryContext.Current;
            fixture.Requests.Clear();
            long value = 7;
            nint address = (nint)(&value);
            ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => context.DangerousBorrow<long>((void*)address, 7));
            Assert.AreEqual("byteLength", error.ParamName);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => context.DangerousBorrow<long>((void*)address, 0));
            Assert.IsEmpty(fixture.Requests);
        }

        context.Dispose();
        Assert.IsNull(context.DangerousBorrow<long>(null, 0));
        Assert.IsNull(context.DangerousBorrow<long>(null, nuint.MaxValue));
        Assert.IsEmpty(fixture.Requests);
    }

    /// <summary>
    /// Reinterpreting a raw view never recaptures a changed generation, bypasses a foreign provider, or survives reset.
    /// </summary>
    [TestMethod]
    public void ReinterpretRejectsExpiredAndForeignAnchors()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        long value = 7;
        fixture.Handler = request => RespondRaw(fixture, request);
        PgNativeReference<long>? reference = PgMemoryContext.Current.DangerousBorrow<long>(&value);
        Assert.IsNotNull(reference);
        PgNativeReference<int> prefix = reference.Reinterpret<int>();
        fixture.Requests.Clear();
        using (MemoryContextTestFixture.Enter(29))
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => prefix.Reinterpret<long>());
        }

        Assert.IsEmpty(fixture.Requests);
        fixture.Handler = request => request._operation == NativeMemoryOperation.ReadReference
            ? throw new PgException("55000", "old generation")
            : fixture.Respond(request);
        Assert.ThrowsExactly<ObjectDisposedException>(() => reference.Reinterpret<int>());
        Assert.ThrowsExactly<ObjectDisposedException>(() => prefix.Reinterpret<long>());
        Assert.IsTrue(fixture.Requests.All(static request => request._operation == NativeMemoryOperation.ReadReference && request._other == 901));
        Assert.AreEqual(2, fixture.ErrorReleases);
        Assert.AreEqual(7, value);
    }
}
