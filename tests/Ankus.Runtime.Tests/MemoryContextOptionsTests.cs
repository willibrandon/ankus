using System.Runtime.InteropServices;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies AllocSet sizing transport and transient context selection, restoration, and cleanup failures.
/// </summary>
[TestClass]
public sealed unsafe class MemoryContextOptionsTests
{
    /// <summary>
    /// All presets preserve PostgreSQL's documented minimum, initial, and maximum block sizes.
    /// </summary>
    [TestMethod]
    public void PresetsMatchPostgreSqlAllocSetSizes()
    {
        AssertSizes(PgMemoryContextOptions.Default, 0, 8192, 8388608);
        AssertSizes(PgMemoryContextOptions.Small, 0, 1024, 8192);
        AssertSizes(PgMemoryContextOptions.StartSmall, 0, 1024, 8388608);
        AssertSizes(new PgMemoryContextOptions(), 0, 8192, 8388608);
    }

    /// <summary>
    /// Portable bound failures are detected before PostgreSQL can encounter an allocator assertion.
    /// </summary>
    /// <param name="minimum">The requested retained first block size.</param>
    /// <param name="initial">The requested initial block size.</param>
    /// <param name="maximum">The requested maximum block size.</param>
    /// <param name="parameter">The invalid property reported to the caller.</param>
    [TestMethod]
    [DataRow(0, 0, 8192, "InitialBlockSize")]
    [DataRow(0, 1023, 8192, "InitialBlockSize")]
    [DataRow(0, 1024, 1023, "MaximumBlockSize")]
    [DataRow(1, 1024, 8192, "MinimumContextSize")]
    [DataRow(1023, 1024, 8192, "MinimumContextSize")]
    [DataRow(8193, 1024, 8192, "MinimumContextSize")]
    public void InvalidContextSizesFailBeforeNativeAccess(int minimum, int initial, int maximum, string parameter)
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        var options = new PgMemoryContextOptions((nuint)minimum, (nuint)initial, (nuint)maximum);
        ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PgMemoryContext.Create("invalid sizes", options: options));
        Assert.AreEqual(parameter, error.ParamName);
        Assert.IsEmpty(fixture.Requests);
    }

    /// <summary>
    /// The portable huge-size bound rejects values that cannot safely support allocator doubling.
    /// </summary>
    [TestMethod]
    public void NativeSizeMaximumCannotOverflowAllocSetGrowth()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        var options = new PgMemoryContextOptions(maximumBlockSize: (nuint.MaxValue / 2) + 1);
        ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PgMemoryContext.Create("oversized blocks", options: options));
        Assert.AreEqual("MaximumBlockSize", error.ParamName);
        Assert.IsEmpty(fixture.Requests);
    }

    /// <summary>
    /// Explicit block sizes use a native-width sequential payload whose lifetime covers the native call.
    /// </summary>
    /// <param name="minimum">A valid retained first-block size, including one larger than the initial size.</param>
    /// <param name="initial">A valid initial size, including a non-power-of-two multiple of native alignment.</param>
    /// <param name="maximum">A valid maximum size.</param>
    [TestMethod]
    [DataRow(0, 1024, 1024)]
    [DataRow(1024, 1536, 8192)]
    [DataRow(8192, 1024, 8192)]
    public void ExplicitSizesAndParentUseNativeWidthPayload(int minimum, int initial, int maximum)
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        PgMemoryContext parent = PgMemoryContext.Current;
        var options = new PgMemoryContextOptions((nuint)minimum, (nuint)initial, (nuint)maximum);
        NativeMemoryContextSizes observed = default;
        string? observedName = null;
        nint observedParent = 0;
        fixture.Handler = request =>
        {
            if (request._operation == NativeMemoryOperation.Create)
            {
                Assert.AreNotEqual(nint.Zero, request._pointer);
                observed = *(NativeMemoryContextSizes*)request._pointer;
                observedName = Marshal.PtrToStringUTF8(request._data, checked((int)request._length));
                observedParent = request._context;
            }

            return fixture.Respond(request);
        };
        using PgMemoryContext context = PgMemoryContext.Create("sized café", parent, options);
        Assert.AreEqual((nuint)minimum, observed._minimumContextSize);
        Assert.AreEqual((nuint)initial, observed._initialBlockSize);
        Assert.AreEqual((nuint)maximum, observed._maximumBlockSize);
        Assert.AreEqual("sized café", observedName);
        Assert.AreEqual((nint)101, observedParent);
        Assert.AreEqual((nint)202, context.Id);
        Assert.AreEqual((nint)101, fixture.Current);
        Assert.AreEqual(3 * sizeof(nuint), Marshal.SizeOf<NativeMemoryContextSizes>());
        Assert.AreEqual(nint.Zero, Marshal.OffsetOf<NativeMemoryContextSizes>(nameof(NativeMemoryContextSizes._minimumContextSize)));
        Assert.AreEqual((nint)sizeof(nuint), Marshal.OffsetOf<NativeMemoryContextSizes>(nameof(NativeMemoryContextSizes._initialBlockSize)));
        Assert.AreEqual((nint)(2 * sizeof(nuint)), Marshal.OffsetOf<NativeMemoryContextSizes>(nameof(NativeMemoryContextSizes._maximumBlockSize)));
    }

    /// <summary>
    /// Omitted options use the native default preset rather than a borrowed payload with zero sizes.
    /// </summary>
    [TestMethod]
    public void OmittedSizesLeaveNativeDefaultSelectionExplicit()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        using PgMemoryContext context = PgMemoryContext.Create("default sizes");
        NativeMemoryRequest request = Find(fixture, NativeMemoryOperation.Create);
        Assert.AreEqual(nint.Zero, request._pointer);
        Assert.AreEqual(nint.Zero, request._context);
    }

    /// <summary>
    /// Target-specific size validation errors stay native errors with no falsely created managed owner.
    /// </summary>
    [TestMethod]
    public void NativeSizeValidationFailurePreservesDiagnosticsWithoutDeletingUnknownContext()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        fixture.Handler = request => request._operation == NativeMemoryOperation.Create
            ? throw new PgException("22023", "native block alignment")
            : fixture.Respond(request);
        PgException error = Assert.ThrowsExactly<PgException>(() => PgMemoryContext.Create("misaligned sizes", options: new PgMemoryContextOptions(0, 1025, 8192)));
        Assert.AreEqual("22023", error.SqlState);
        Assert.AreEqual("native block alignment", error.Message);
        Assert.AreEqual(1, fixture.ErrorReleases);
        NativeMemoryRequest request = Assert.ContainsSingle(fixture.Requests);
        Assert.AreEqual(NativeMemoryOperation.Create, request._operation);
        Assert.AreEqual((nint)101, fixture.Current);
    }

    /// <summary>
    /// Missing transient callbacks fail before creating any native context.
    /// </summary>
    [TestMethod]
    public void NullTransientCallbacksNeverCreateContext()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        ArgumentNullException func = Assert.ThrowsExactly<ArgumentNullException>(() => PgMemoryContext.RunTransient<int>("missing callback", null!));
        ArgumentNullException action = Assert.ThrowsExactly<ArgumentNullException>(() => PgMemoryContext.RunTransient("missing callback", (Action<PgMemoryContext>)null!));
        Assert.AreEqual("func", func.ParamName);
        Assert.AreEqual("action", action.ParamName);
        Assert.IsEmpty(fixture.Requests);
    }

    /// <summary>
    /// A transient result is copied before restoration and deletion, and escaped owned handles become disposed.
    /// </summary>
    [TestMethod]
    public void TransientScopeSelectsChildReturnsResultAndDeletesAfterRestoration()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        PgMemoryContext parent = PgMemoryContext.Current;
        PgMemoryContext? escaped = null;
        int result = PgMemoryContext.RunTransient("temporary", context =>
        {
            escaped = context;
            Assert.AreEqual((nint)202, context.Id);
            Assert.AreEqual(context.Id, PgMemoryContext.Current.Id);
            return 42;
        }, parent, PgMemoryContextOptions.Small);
        Assert.AreEqual(42, result);
        Assert.IsNotNull(escaped);
        Assert.IsFalse(escaped.IsAlive);
        Assert.AreEqual((nint)101, fixture.Current);
        NativeMemoryRequest created = Find(fixture, NativeMemoryOperation.Create);
        Assert.AreEqual((nint)101, created._context);
        Assert.AreNotEqual(nint.Zero, created._pointer);
        NativeMemoryRequest[] switches = [.. fixture.Requests.Where(static request => request._operation == NativeMemoryOperation.Switch)];
        Assert.AreSequenceEqual([(nint)202, (nint)101], switches.Select(static request => request._context));
        NativeMemoryRequest deleted = Find(fixture, NativeMemoryOperation.Delete);
        Assert.AreEqual((nint)202, deleted._context);
        Assert.AreEqual(NativeMemoryOperation.Delete, fixture.Requests[^1]._operation);
    }

    /// <summary>
    /// Nested transient calls allocate distinct native identities and restore each immediate caller.
    /// </summary>
    [TestMethod]
    public void NestedTransientScopesHaveDistinctContextsAndRestoreTheirImmediateCaller()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        nint next = 201;
        fixture.Handler = request => request._operation == NativeMemoryOperation.Create
            ? new NativeMemoryResult { _context = ++next }
            : fixture.Respond(request);
        List<nint> observed = [];
        PgMemoryContext.RunTransient("outer", outer =>
        {
            observed.Add(PgMemoryContext.Current.Id);
            PgMemoryContext.RunTransient("inner", inner =>
            {
                observed.Add(PgMemoryContext.Current.Id);
                Assert.AreNotEqual(outer.Id, inner.Id);
            }, outer);
            observed.Add(PgMemoryContext.Current.Id);
        });
        observed.Add(PgMemoryContext.Current.Id);
        Assert.AreSequenceEqual([(nint)202, (nint)203, (nint)202, (nint)101], observed);
        NativeMemoryRequest[] deleted = [.. fixture.Requests.Where(static request => request._operation == NativeMemoryOperation.Delete)];
        Assert.AreSequenceEqual([(nint)203, (nint)202], deleted.Select(static request => request._context));
    }

    /// <summary>
    /// A managed callback failure is preserved exactly after the caller is restored and the child is deleted.
    /// </summary>
    [TestMethod]
    public void TransientManagedFailurePreservesExceptionAfterCleanup()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        var expected = new InvalidOperationException("primary managed failure");
        InvalidOperationException actual = Assert.ThrowsExactly<InvalidOperationException>(() => PgMemoryContext.RunTransient<int>("failure", _ => throw expected));
        Assert.AreSame(expected, actual);
        Assert.AreEqual((nint)101, fixture.Current);
        Assert.AreEqual((nint)202, Find(fixture, NativeMemoryOperation.Delete)._context);
    }

    /// <summary>
    /// A guarded native failure inside the action remains the primary error after scope cleanup.
    /// </summary>
    [TestMethod]
    public void TransientNativeFailurePreservesOwnedDiagnosticAfterCleanup()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        fixture.Handler = request => request._operation == NativeMemoryOperation.Allocate
            ? throw new PgException("XX000", "native allocation failure", "native detail", "native hint")
            : fixture.Respond(request);
        PgException error = Assert.ThrowsExactly<PgException>(() => PgMemoryContext.RunTransient("native failure", context =>
        {
            using PgAllocation allocation = context.Allocate(8);
        }));
        Assert.AreEqual("XX000", error.SqlState);
        Assert.AreEqual("native allocation failure", error.Message);
        Assert.AreEqual("native detail", error.Detail);
        Assert.AreEqual("native hint", error.Hint);
        Assert.AreEqual(3, fixture.ErrorReleases);
        Assert.AreEqual((nint)101, fixture.Current);
        Assert.AreEqual((nint)202, Find(fixture, NativeMemoryOperation.Delete)._context);
    }

    /// <summary>
    /// Restoration and deletion errors both survive alongside the original action failure.
    /// </summary>
    [TestMethod]
    public void TransientActionRestoreAndDeleteFailuresRemainIndependentlyObservable()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        fixture.Handler = request => request._operation switch
        {
            NativeMemoryOperation.Switch when request._context == 101 => throw new PgException("XX001", "restore failure"),
            NativeMemoryOperation.Delete => throw new PgException("55006", "delete failure"),
            _ => fixture.Respond(request),
        };
        var primary = new InvalidOperationException("action failure");
        AggregateException error = Assert.ThrowsExactly<AggregateException>(() => PgMemoryContext.RunTransient<int>("three failures", _ => throw primary));
        Assert.HasCount(2, error.InnerExceptions);
        AggregateException run = Assert.IsInstanceOfType<AggregateException>(error.InnerExceptions[0]);
        Assert.HasCount(2, run.InnerExceptions);
        Assert.AreSame(primary, run.InnerExceptions[0]);
        PgException restore = Assert.IsInstanceOfType<PgException>(run.InnerExceptions[1]);
        PgException delete = Assert.IsInstanceOfType<PgException>(error.InnerExceptions[1]);
        Assert.AreEqual("XX001", restore.SqlState);
        Assert.AreEqual("restore failure", restore.Message);
        Assert.AreEqual("55006", delete.SqlState);
        Assert.AreEqual("delete failure", delete.Message);
        Assert.AreEqual(2, fixture.ErrorReleases);
        Assert.AreEqual(NativeMemoryOperation.Delete, fixture.Requests[^1]._operation);
    }

    /// <summary>
    /// Deletion failures are not hidden by a successful callback result, and its escaped owner remains retryable.
    /// </summary>
    [TestMethod]
    public void TransientDeletionFailurePropagatesAndRetainsRetryableOwner()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        PgMemoryContext? escaped = null;
        fixture.Handler = request => request._operation == NativeMemoryOperation.Delete
            ? throw new PgException("55006", "callback prevented deletion")
            : fixture.Respond(request);
        PgException error = Assert.ThrowsExactly<PgException>(() => PgMemoryContext.RunTransient("delete failure", context =>
        {
            escaped = context;
            return 42;
        }));
        Assert.AreEqual("55006", error.SqlState);
        Assert.AreEqual("callback prevented deletion", error.Message);
        Assert.AreEqual((nint)101, fixture.Current);
        Assert.IsNotNull(escaped);
        Assert.IsTrue(escaped.IsAlive);
        fixture.Handler = null;
        escaped.Dispose();
        Assert.IsFalse(escaped.IsAlive);
    }

    /// <summary>
    /// Context-creation failure prevents action execution, switching, and deletion of an unknown owner.
    /// </summary>
    [TestMethod]
    public void TransientCreationFailureNeverExecutesActionOrDeletesUnknownContext()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        int calls = 0;
        fixture.Handler = static _ => throw new PgException("53200", "create failed");
        PgException error = Assert.ThrowsExactly<PgException>(() => PgMemoryContext.RunTransient("create failure", _ => calls++));
        Assert.AreEqual("53200", error.SqlState);
        Assert.AreEqual(0, calls);
        NativeMemoryRequest request = Assert.ContainsSingle(fixture.Requests);
        Assert.AreEqual(NativeMemoryOperation.Create, request._operation);
    }

    /// <summary>
    /// Failure to select an already created child still deletes it without executing the user action.
    /// </summary>
    [TestMethod]
    public void TransientInitialSwitchFailureDeletesUnusedChild()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        int calls = 0;
        fixture.Handler = request => request._operation == NativeMemoryOperation.Switch
            ? throw new PgException("55006", "selection failed")
            : fixture.Respond(request);
        PgException error = Assert.ThrowsExactly<PgException>(() => PgMemoryContext.RunTransient("switch failure", _ => calls++));
        Assert.AreEqual("55006", error.SqlState);
        Assert.AreEqual("selection failed", error.Message);
        Assert.AreEqual(0, calls);
        Assert.AreEqual((nint)101, fixture.Current);
        Assert.AreEqual((nint)202, Find(fixture, NativeMemoryOperation.Switch)._context);
        Assert.AreEqual((nint)202, Find(fixture, NativeMemoryOperation.Delete)._context);
        Assert.AreEqual(NativeMemoryOperation.Delete, fixture.Requests[^1]._operation);
    }

    /// <summary>
    /// Compares all three independently meaningful AllocSet settings.
    /// </summary>
    private static void AssertSizes(PgMemoryContextOptions options, nuint minimum, nuint initial, nuint maximum)
    {
        Assert.AreEqual(minimum, options.MinimumContextSize);
        Assert.AreEqual(initial, options.InitialBlockSize);
        Assert.AreEqual(maximum, options.MaximumBlockSize);
    }

    /// <summary>
    /// Selects one expected native operation from the recorded transport requests.
    /// </summary>
    private static NativeMemoryRequest Find(MemoryContextTestFixture fixture, NativeMemoryOperation operation)
        => Assert.ContainsSingle(request => request._operation == operation, fixture.Requests);
}
