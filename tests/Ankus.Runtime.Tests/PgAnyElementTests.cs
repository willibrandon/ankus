using System.Runtime.InteropServices;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies polymorphic wrapper identity and checked result transport without a PostgreSQL process.
/// </summary>
[TestClass]
public sealed class PgAnyElementTests
{
    /// <summary>
    /// Typed raw reads retain exact identity, SQL NULL, name lookup, and the original generation.
    /// </summary>
    [TestMethod]
    public void TypedRawReadsPreserveIdentityNullsAndCheckedLifetime()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        PgDatum present = PgDatum.DangerousCreate(0, 98765, PgMemoryContext.Current);
        PgDatum absent = PgDatum.DangerousCreate(0, 98765, PgMemoryContext.Current, isNull: true);
        var row = new SpiRawRow([present, absent], [new SpiColumn("Value", 98765), new SpiColumn("Value", 98765)]);
        PgAnyElement wrapper = row.Get<PgAnyElement>("Value");
        Assert.AreSame(present, wrapper.Datum);
        Assert.AreEqual(98765U, wrapper.TypeOid);
        Assert.AreEqual((nuint)0, wrapper.Datum.DangerousGetBits());
        Assert.IsNull(row.Get<PgAnyElement?>(1));
        Assert.IsNull(absent.Read<PgAnyArray?>());
        Assert.ThrowsExactly<ArgumentException>(() => row.Get<PgAnyElement>("value"));
        Assert.ThrowsExactly<ArgumentNullException>(() => row.Get<PgAnyElement>(null!));
        Assert.ThrowsExactly<IndexOutOfRangeException>(() => row.Get<PgAnyElement>(2));
        fixture.Handler = static _ => new NativeMemoryResult { _value = 902 };
        Assert.ThrowsExactly<ObjectDisposedException>(() => wrapper.Read<PgAnyElement>());
        Assert.ThrowsExactly<ObjectDisposedException>(() => absent.Read<PgAnyElement?>());
        Assert.ThrowsExactly<ObjectDisposedException>(() => row.Get<PgAnyElement>(0));
    }

    /// <summary>
    /// Keeps the raw identity through parameter binding and verifies result envelopes retain every datum bit.
    /// </summary>
    [TestMethod]
    public void ExactIdentityAndLifetimeSurviveTransport()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        PgDatum datum = PgDatum.DangerousCreate(nuint.MaxValue, 98765, PgMemoryContext.Current);
        var element = new PgAnyElement(datum);
        Assert.AreSame(datum, element.Datum);
        Assert.AreEqual(98765U, element.TypeOid);
        Assert.AreSame(datum, SpiParameter.Create(element).Value);
        Assert.AreEqual(98765U, PgFunctionArgument.Create(element).TypeOid);
        NativeValue transport = NativeValue.FromPolymorphic(element.Datum);
        try
        {
            Assert.AreEqual(98765L, transport.Integral);
            Assert.AreEqual((byte)0, transport.IsNull);
            NativeDatumReference reference = MemoryMarshal.Read<NativeDatumReference>(transport.ReadBytes());
            Assert.AreEqual(nuint.MaxValue, reference._bits);
            Assert.AreEqual(101, reference._context);
            Assert.AreEqual((nuint)901, reference._generation);
        }
        finally
        {
            transport.Release();
        }

        fixture.Handler = static _ => new NativeMemoryResult { _value = 902 };
        Assert.ThrowsExactly<ObjectDisposedException>(() => NativeValue.FromPolymorphic(element.Datum));
        Assert.AreEqual(98765U, element.TypeOid);
    }

    /// <summary>
    /// Nullable managed wrappers represent absent SQL values without constructing invalid present wrappers.
    /// </summary>
    [TestMethod]
    public void NullInputsRequireNullableWrappers()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new PgAnyElement(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new PgAnyArray(null!));
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        PgDatum datum = PgDatum.DangerousCreate(0, 23, PgMemoryContext.Current, isNull: true);
        Assert.ThrowsExactly<ArgumentException>(() => new PgAnyElement(datum));
        Assert.ThrowsExactly<ArgumentException>(() => new PgAnyArray(datum));
    }
}
