namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies cached-state initialization, exact type identity, and cleanup independently of PostgreSQL.
/// </summary>
[TestClass]
public sealed class PgFunctionStateTests
{
    /// <summary>
    /// Successful initialization preserves object identity and rejects a different declared type without invoking its factory.
    /// </summary>
    [TestMethod]
    public void SuccessfulFactoryRunsOnceAndRetainsExactType()
    {
        var entry = new PgFunctionStateEntry();
        object expected = new();
        int calls = 0;
        Assert.AreSame(expected, entry.GetOrCreate(() =>
        {
            calls++;
            return expected;
        }));
        Assert.AreSame(expected, entry.GetOrCreate<object>(() => throw new InvalidOperationException("Factory ran twice.")));
        Assert.ThrowsExactly<InvalidCastException>(() => entry.GetOrCreate(() =>
        {
            calls++;
            return "wrong type";
        }));
        Assert.AreEqual(1, calls);
        entry.Release();
    }

    /// <summary>
    /// Null reference and nullable-value results count as successfully cached values.
    /// </summary>
    [TestMethod]
    public void NullAndZeroValuesAreCached()
    {
        var reference = new PgFunctionStateEntry();
        Assert.IsNull(reference.GetOrCreate<string?>(static () => null));
        Assert.IsNull(reference.GetOrCreate<string?>(static () => throw new InvalidOperationException("Null was not cached.")));
        var nullable = new PgFunctionStateEntry();
        Assert.IsNull(nullable.GetOrCreate<int?>(static () => null));
        Assert.IsNull(nullable.GetOrCreate<int?>(static () => throw new InvalidOperationException("Nullable was not cached.")));
        var zero = new PgFunctionStateEntry();
        Assert.AreEqual(0, zero.GetOrCreate(static () => 0));
        Assert.AreEqual(0, zero.GetOrCreate(static () => 42));
        reference.Release();
        nullable.Release();
        zero.Release();
    }

    /// <summary>
    /// Failed and recursively entered factories leave no published state and permit a subsequent initializer.
    /// </summary>
    [TestMethod]
    public void FailedAndRecursiveFactoriesPermitRetry()
    {
        var entry = new PgFunctionStateEntry();
        var expected = new PgException("P7806", "factory failed");
        Assert.AreSame(expected, Assert.ThrowsExactly<PgException>(() => entry.GetOrCreate<int>(() => throw expected)));
        Assert.ThrowsExactly<InvalidOperationException>(() => entry.GetOrCreate(() => entry.GetOrCreate(static () => 17)));
        Assert.AreEqual("recovered", entry.GetOrCreate(static () => "recovered"));
        entry.Release();
    }

    /// <summary>
    /// Release rejects reentry before disposal and consumes the value once even when its disposer fails.
    /// </summary>
    /// <param name="fail">Whether user disposal throws.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReleaseIsFinalAndDisposesExactlyOnce(bool fail)
    {
        var entry = new PgFunctionStateEntry();
        int disposed = 0;
        var expected = new InvalidOperationException("dispose failed");
        entry.GetOrCreate(() => new DisposalAction(() =>
        {
            disposed++;
            Assert.ThrowsExactly<ObjectDisposedException>(() => entry.GetOrCreate(static () => 42));
            if (fail)
            {
                throw expected;
            }
        }));
        if (fail)
        {
            Assert.AreSame(expected, Assert.ThrowsExactly<InvalidOperationException>(entry.Release));
        }
        else
        {
            entry.Release();
        }

        entry.Release();
        Assert.AreEqual(1, disposed);
        Assert.ThrowsExactly<ObjectDisposedException>(() => entry.GetOrCreate(static () => 17));
    }

    /// <summary>
    /// A value returned after native ownership ends is immediately disposed instead of being published.
    /// </summary>
    [TestMethod]
    public void OwnerEndingDuringFactoryDisposesUnpublishedValue()
    {
        var entry = new PgFunctionStateEntry();
        int disposed = 0;
        Assert.ThrowsExactly<ObjectDisposedException>(() => entry.GetOrCreate(() =>
        {
            entry.Release();
            return new DisposalAction(() => disposed++);
        }));
        entry.Release();
        Assert.AreEqual(1, disposed);
    }

    /// <summary>
    /// Null factories fail before requesting a backend or state owner.
    /// </summary>
    [TestMethod]
    public void NullFactoryFailsBeforeBackendAccess()
    {
        var call = new PgFunctionContext(1, 23, 0, []);
        ArgumentNullException error = Assert.ThrowsExactly<ArgumentNullException>(() => call.GetOrCreateState<int>(null!));
        Assert.AreEqual("factory", error.ParamName);
    }

    /// <summary>
    /// Owner resets and deletions fail before initialization; unrelated native errors keep their diagnostic.
    /// </summary>
    /// <param name="mode">Reset, deleted owner, or operational failure.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void NativeOwnerFailuresNeverRunFactory(int mode)
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        var state = new PgFunctionStateScope(17, 543, 101, 901);
        fixture.Handler = _ => mode switch
        {
            0 => new NativeMemoryResult { _value = 902 },
            1 => throw new PgException("55000", "deleted owner"),
            _ => throw new PgException("53200", "allocation failed"),
        };
        int calls = 0;
        if (mode < 2)
        {
            Assert.ThrowsExactly<ObjectDisposedException>(() => state.GetOrCreate(() => ++calls));
            Assert.ThrowsExactly<ObjectDisposedException>(() => state.GetMemoryContext());
        }
        else
        {
            PgException error = Assert.ThrowsExactly<PgException>(() => state.GetOrCreate(() => ++calls));
            Assert.AreEqual("53200", error.SqlState);
            Assert.AreEqual("allocation failed", error.Message);
        }

        Assert.AreEqual(0, calls);
        Assert.IsTrue(fixture.Requests.All(static request => request._operation == NativeMemoryOperation.CaptureGeneration));
    }

    /// <summary>
    /// A failed reset-callback registration removes its empty entry and retries registration before any factory call.
    /// </summary>
    [TestMethod]
    public void RegistrationFailureDoesNotPublishOrStrandState()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        var state = new PgFunctionStateScope(17, 987, 101, 901);
        fixture.Handler = request => request._operation == NativeMemoryOperation.RegisterCallback
            ? throw new PgException("53200", "registration failed") : fixture.Respond(request);
        int calls = 0;
        nint previous = NativeBackend.Enter(1);
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                PgException error = Assert.ThrowsExactly<PgException>(() => state.GetOrCreate(() => ++calls));
                Assert.AreEqual("53200", error.SqlState);
                Assert.AreEqual("registration failed", error.Message);
            }
        }
        finally
        {
            NativeBackend.Exit(previous);
        }

        Assert.AreEqual(0, calls);
        Assert.HasCount(2, fixture.Requests.Where(static request => request._operation == NativeMemoryOperation.RegisterCallback));
    }

    /// <summary>
    /// A different extension provider cannot reach the original owner's native interface or initialize state.
    /// </summary>
    [TestMethod]
    public void ProviderMismatchFailsBeforeNativeAccess()
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter(18);
        var state = new PgFunctionStateScope(17, 543, 101, 901);
        int calls = 0;
        Assert.ThrowsExactly<InvalidOperationException>(() => state.GetOrCreate(() => ++calls));
        Assert.ThrowsExactly<InvalidOperationException>(() => state.GetMemoryContext());
        Assert.AreEqual(0, calls);
        Assert.IsEmpty(fixture.Requests);
    }

    /// <summary>
    /// Gives individual cases a deterministic disposable value.
    /// </summary>
    /// <param name="dispose">The action executed during disposal.</param>
    private sealed class DisposalAction(Action dispose) : IDisposable
    {
        /// <summary>
        /// Executes the case's cleanup probe.
        /// </summary>
        public void Dispose() => dispose();
    }
}
