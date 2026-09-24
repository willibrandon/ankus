using System.Runtime.InteropServices;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies raw datum identity, lifetime validation, and transport independently of PostgreSQL.
/// </summary>
[TestClass]
public sealed class PgDatumTests
{
    /// <summary>
    /// Raw words preserve every bit, and SQL NULL has an independent flag and type identity.
    /// </summary>
    /// <param name="isNull">Whether the datum represents SQL NULL.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RawBitsAndNullIdentitySurviveParameterTransport(bool isNull)
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        PgDatum datum = PgDatum.DangerousCreate(nuint.MaxValue, 98765, PgMemoryContext.Current, isNull);
        Assert.AreEqual(nuint.MaxValue, datum.DangerousGetBits());
        Assert.AreEqual(98765U, datum.TypeOid);
        Assert.AreEqual(isNull, datum.IsNull);
        SpiParameter parameter = SpiParameter.Create(datum);
        Assert.AreEqual(98765U, parameter.TypeOid);
        Assert.AreSame(datum, parameter.Value);
        Assert.AreEqual(parameter.TypeOid, SpiParameter.Create<PgDatum>(datum).TypeOid);
        NativeValue transport = SpiType.ToNative(parameter.Value);
        try
        {
            NativeDatumReference reference = MemoryMarshal.Read<NativeDatumReference>(transport.ReadBytes());
            Assert.AreEqual(nuint.MaxValue, reference._bits);
            Assert.AreEqual(101, reference._context);
            Assert.AreEqual((nuint)901, reference._generation);
            Assert.AreEqual(isNull ? (byte)1 : (byte)0, transport.IsNull);
        }
        finally
        {
            transport.Release();
        }
    }

    /// <summary>
    /// Context reset invalidates raw access, converters, and parameter marshalling while metadata remains readable.
    /// </summary>
    [TestMethod]
    public void ResetGenerationRejectsEveryNativeAccess()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        PgDatum datum = PgDatum.DangerousCreate(42, 23, PgMemoryContext.Current);
        fixture.Handler = request => request._operation == NativeMemoryOperation.CaptureGeneration
            ? new NativeMemoryResult { _value = 902 }
            : fixture.Respond(request);
        Assert.ThrowsExactly<ObjectDisposedException>(() => datum.DangerousGetBits());
        Assert.ThrowsExactly<ObjectDisposedException>(() => datum.ToNative());
        bool invoked = false;
        Assert.ThrowsExactly<ObjectDisposedException>(() => datum.Read(value =>
        {
            invoked = true;
            return value.TypeOid;
        }));
        Assert.IsFalse(invoked);
        Assert.AreEqual(23U, datum.TypeOid);
        Assert.IsFalse(datum.IsNull);
    }

    /// <summary>
    /// A deleted owner becomes a disposed error; unrelated PostgreSQL failures retain their diagnostics.
    /// </summary>
    [TestMethod]
    public void DeletedOwnerAndOperationalErrorsRemainDistinct()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        PgDatum datum = PgDatum.DangerousCreate(0, 23, PgMemoryContext.Current, isNull: true);
        fixture.Handler = static _ => throw new PgException("55000", "deleted owner");
        Assert.ThrowsExactly<ObjectDisposedException>(() => datum.DangerousGetBits());
        fixture.Handler = static _ => throw new PgException("53200", "allocation failed");
        PgException exception = Assert.ThrowsExactly<PgException>(() => datum.DangerousGetBits());
        Assert.AreEqual("53200", exception.SqlState);
        Assert.AreEqual("allocation failed", exception.Message);
    }

    /// <summary>
    /// Cross-provider and unbound access fail before invoking the native memory capability.
    /// </summary>
    [TestMethod]
    public void AccessRequiresOriginalBackendProvider()
    {
        using var fixture = new MemoryContextTestFixture();
        PgDatum datum;
        using (MemoryContextTestFixture.Enter())
        {
            datum = PgDatum.DangerousCreate(42, 23, PgMemoryContext.Current);
        }

        fixture.Requests.Clear();
        Assert.ThrowsExactly<InvalidOperationException>(() => datum.DangerousGetBits());
        using (MemoryContextTestFixture.Enter(provider: 18))
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => datum.DangerousGetBits());
        }

        Assert.IsEmpty(fixture.Requests);
    }

    /// <summary>
    /// A failed raw conversion cannot replace an existing managed cell or change its type.
    /// </summary>
    [TestMethod]
    public void FailedRawConversionPreservesManagedRow()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        PgDatum datum = PgDatum.DangerousCreate(42, 23, PgMemoryContext.Current);
        var row = new SpiRow([17], [new SpiColumn("value", 23)]);
        Assert.ThrowsExactly<InvalidOperationException>(() => row.Set(0, datum));
        Assert.AreEqual(17, row.Get<int>(0));
        Assert.AreEqual(23U, row.GetTypeOid(0));
        Assert.AreEqual(23U, SpiParameter.Create<PgDatum>(datum).TypeOid);
    }

    /// <summary>
    /// Missing types and objects are rejected before any backend operation.
    /// </summary>
    [TestMethod]
    public void InvalidCreationArgumentsDoNotInvokeBackend()
    {
        using var fixture = new MemoryContextTestFixture();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PgDatum.DangerousCreate(0, 0, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => PgDatum.DangerousCreate(0, 23, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => SpiParameter.Create(null!));
        Assert.IsEmpty(fixture.Requests);
    }
}
