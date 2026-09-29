using System.Text;

namespace Ankus.Runtime.Tests;

public sealed partial class PgBufferViewTests
{
    /// <summary>
    /// Malformed metadata cannot expose a C-string span and releases both the response and provisional owner.
    /// </summary>
    /// <param name="invalid">The missing datum, missing payload or negative-length response.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void MalformedCStringViewsReleaseOwners(int invalid)
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope memory = MemoryContextTestFixture.Enter();
        using var script = new BufferScript(fixture, [65]) { InvalidMetadata = invalid };
        PgDatum source = PgDatum.DangerousCreate(123, 2275, PgMemoryContext.Current);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            using var view = new PgCStringView(source);
        });
        Assert.AreEqual(1, script.Releases);
        Assert.AreSequenceEqual<nint>([202], script.Deleted);
        Assert.AreSequenceEqual<int>([13], script.Operations);
    }

    /// <summary>
    /// Native views retain non-UTF-8 bytes and one terminator while owned snapshots survive disposal.
    /// </summary>
    /// <param name="empty">Whether the native C string contains only its terminator.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void BorrowedCStringsRetainExactBytesAndTerminator(bool empty)
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope memory = MemoryContextTestFixture.Enter();
        byte[] payload = empty ? [] : [1, 128, 255];
        using var script = new BufferScript(fixture, payload);
        using var view = new PgCStringView(PgDatum.DangerousCreate(123, 2275, PgMemoryContext.Current));
        Assert.AreEqual(payload.Length, view.Count);
        Assert.AreEqual(2275U, view.TypeOid);
        Assert.IsFalse(view.Datum.IsNull);
        Assert.AreEqual((nuint)9011, view.Datum.DangerousGetBits());
        Assert.AreSequenceEqual<byte>(payload, [.. view.DangerousGetSpan()]);
        Assert.AreSequenceEqual<byte>([.. payload, 0], [.. view.DangerousGetNullTerminatedSpan()]);
        Assert.AreSequenceEqual(payload, view);
        Func<byte[]> copy = view.ToArray;
        Assert.AreSequenceEqual(payload, copy());
        PgCString owned = view.ToOwned();
        byte[] destination = [9, 9, 9, 9];
        view.CopyTo(destination);
        Assert.AreSequenceEqual<byte>(empty ? [9, 9, 9, 9] : [1, 128, 255, 9], destination);
        Assert.AreEqual("index", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = view[-1]).ParamName);
        Assert.AreEqual("index", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = view[view.Count]).ParamName);
        using IEnumerator<byte> cursor = view.GetEnumerator();
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = cursor.Current);
        Assert.AreEqual(!empty, cursor.MoveNext());
        if (!empty)
        {
            Assert.ThrowsExactly<DecoderFallbackException>(view.ToUtf8String);
            Assert.AreEqual((byte)1, cursor.Current);
            byte[] shortDestination = [7, 7];
            Assert.ThrowsExactly<ArgumentException>(() => view.CopyTo(shortDestination));
            Assert.AreSequenceEqual<byte>([7, 7], shortDestination);
        }
        else
        {
            Assert.AreEqual(string.Empty, view.ToUtf8String());
        }

        view.Dispose();
        Assert.AreSequenceEqual(payload, owned);
        Assert.AreSequenceEqual<byte>([.. payload, 0], [.. owned.AsNullTerminatedSpan()]);
        Assert.ThrowsExactly<ObjectDisposedException>(view.ToOwned);
        Assert.ThrowsExactly<ObjectDisposedException>(view.ToUtf8String);
        Assert.ThrowsExactly<ObjectDisposedException>(() => cursor.MoveNext());
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = cursor.Current);
        Assert.ThrowsExactly<ObjectDisposedException>(() => view.DangerousGetNullTerminatedSpan().ToArray());
        Assert.AreSequenceEqual<int>([13], script.Operations);
        Assert.AreSequenceEqual<nint>([202], script.Deleted);
        Assert.AreEqual(1, script.Releases);
    }

    /// <summary>
    /// Both SQL NULL and a null C-string address become absent without losing raw datum NULL identity.
    /// </summary>
    /// <param name="sqlNull">Whether SQL NULL is explicitly set.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CStringReadsRecognizeNullAddressWithoutRewritingRawDatum(bool sqlNull)
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope memory = MemoryContextTestFixture.Enter();
        using var script = new BufferScript(fixture, []);
        PgDatum absent = PgDatum.DangerousCreate(0, 2275, PgMemoryContext.Current, isNull: sqlNull);
        Assert.IsNull(absent.Read<PgCString>());
        Assert.IsNull(absent.Read<PgCStringView>());
        Assert.AreEqual(sqlNull, absent.IsNull);
        Assert.AreEqual((nuint)0, absent.DangerousGetBits());
        PgDatum unrelated = PgDatum.DangerousCreate(0, 17, PgMemoryContext.Current, isNull: sqlNull);
        Assert.ThrowsExactly<InvalidCastException>(unrelated.Read<PgCString>);
        Assert.ThrowsExactly<InvalidCastException>(unrelated.Read<PgCStringView>);
        Assert.IsEmpty(script.Operations);
        Assert.DoesNotContain(static request => request._operation == NativeMemoryOperation.Create, fixture.Requests);
        Assert.AreEqual(2275U, SpiParameter.Create<PgCString?>(null).TypeOid);
        Assert.AreEqual(2275U, SpiParameter.Create<PgCStringView?>(null).TypeOid);
    }

    /// <summary>
    /// Source-only reset invalidates nested aliases and escaped cursors even while their private contexts survive.
    /// </summary>
    /// <param name="nested">Whether the view borrows another view's checked datum.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void BorrowedCStringsRetainOriginalSourceLifetime(bool nested)
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope memory = MemoryContextTestFixture.Enter();
        using var script = new BufferScript(fixture, [255]);
        using var first = new PgCStringView(PgDatum.DangerousCreate(123, 2275, PgMemoryContext.Current));
        using PgCStringView? second = nested ? new PgCStringView(first.Datum) : null;
        PgCStringView view = second ?? first;
        PgCString copy = view.ToOwned();
        PgDatum escaped = view.Datum;
        using IEnumerator<byte> cursor = view.GetEnumerator();
        Assert.IsTrue(cursor.MoveNext());
        Assert.AreEqual((byte)255, cursor.Current);
        Func<NativeMemoryRequest, NativeMemoryResult> previous = fixture.Handler!;
        fixture.Handler = request => request._operation == NativeMemoryOperation.CaptureGeneration && request._context == 101
            ? new NativeMemoryResult { _value = 902 }
            : previous(request);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = view[0]);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = view.Datum);
        Assert.ThrowsExactly<ObjectDisposedException>(view.ToOwned);
        Assert.ThrowsExactly<ObjectDisposedException>(() => escaped.DangerousGetBits());
        Assert.ThrowsExactly<ObjectDisposedException>(() => cursor.MoveNext());
        Assert.ThrowsExactly<ObjectDisposedException>(() => view.DangerousGetNullTerminatedSpan().ToArray());
        Assert.AreSequenceEqual<byte>([255], copy);
        Assert.AreEqual(1, view.Count);
        Assert.AreEqual(2275U, view.TypeOid);
        Assert.IsEmpty(script.Deleted);
        view.Dispose();
        Assert.AreSequenceEqual<nint>(nested ? [203] : [202], script.Deleted);
    }

    /// <summary>
    /// Callback exit deletes forgotten C-string views and provisional result failures release them immediately.
    /// </summary>
    [TestMethod]
    public void CStringCallbackAndProvisionalOwnersExpire()
    {
        using var fixture = new MemoryContextTestFixture();
        using var script = new BufferScript(fixture, [65]);
        PgCStringView escaped;
        using (MemoryContextTestFixture.Enter())
        {
            var lifetime = new PgDatumLifetime(PgMemoryContext.Current, NativeMemoryContext.BorrowScope);
            escaped = new PgCStringView(new PgDatum(123, 2275, false, lifetime));
            using var provisional = new PgCStringView(new PgDatum(124, 2275, false, lifetime));
            using var conversions = new SpiConversionScope();
            Assert.AreSame(provisional, conversions.Add(provisional));
            var primary = new InvalidCastException("Later result failed.");
            conversions.ReleaseAfterFailure(primary);
            Assert.IsEmpty(primary.Data);
            Assert.AreSequenceEqual<nint>([203], script.Deleted);
            Assert.ThrowsExactly<ObjectDisposedException>(provisional.ToOwned);
            Assert.AreEqual((byte)65, escaped[0]);
        }

        Assert.AreSequenceEqual<nint>([203, 202], script.Deleted);
        using (MemoryContextTestFixture.Enter())
        {
            Assert.ThrowsExactly<ObjectDisposedException>(() => escaped.ToOwned());
        }
    }
}
