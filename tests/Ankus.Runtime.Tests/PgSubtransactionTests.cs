using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies managed callback containment and recovery-scope state without modeling PostgreSQL rollback.
/// </summary>
[TestClass]
public sealed unsafe class PgSubtransactionTests
{
    [ThreadStatic]
    private static List<int>? s_statuses;

    [ThreadStatic]
    private static int s_failStage;

    /// <summary>
    /// Rejects null work and offline calls before executing user code.
    /// </summary>
    [TestMethod]
    public void RecoveryScopesRequireCallbackAndBackend()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => PgTransaction.RunInSubtransaction(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => PgTransaction.RunInSubtransaction<int>(null!));
        bool invoked = false;
        Assert.ThrowsExactly<InvalidOperationException>(() => PgTransaction.RunInSubtransaction(() => invoked = true));
        Assert.IsFalse(invoked);
    }

    /// <summary>
    /// Returns results and rethrows the original managed exception only after the native callback has returned failure.
    /// </summary>
    [TestMethod]
    public void RecoveryScopesPreserveResultsAndExceptionIdentity()
    {
        nint previous = Enter();
        try
        {
            object expected = new();
            Assert.AreSame(expected, PgTransaction.RunInSubtransaction(() => expected));
            var failure = new InvalidOperationException("original managed failure");
            bool unwound = false;
            InvalidOperationException actual = Assert.ThrowsExactly<InvalidOperationException>(() =>
                PgTransaction.RunInSubtransaction(() =>
                {
                    try
                    {
                        throw failure;
                    }
                    finally
                    {
                        unwound = true;
                    }
                }));
            Assert.AreSame(failure, actual);
            Assert.IsTrue(unwound);
            Assert.AreSequenceEqual([0, 1], s_statuses!);
            Assert.AreEqual(42, PgTransaction.RunInSubtransaction(static () => 42));
            Assert.AreSequenceEqual([0, 1, 0], s_statuses!);
        }
        finally
        {
            Exit(previous);
        }
    }

    /// <summary>
    /// A swallowed raw error blocks additional native work and rolls back only the inner recovery scope.
    /// </summary>
    [TestMethod]
    public void RawFailureCannotBeSwallowedOrPoisonRecoveredParent()
    {
        using var memory = new MemoryContextTestFixture
        {
            Handler = static _ => throw new PgException("22023", "native failure", "owned detail", "retry outside"),
        };
        using MemoryContextTestFixture.Scope memoryScope = MemoryContextTestFixture.Enter();
        nint previous = Enter();
        try
        {
            int result = PgTransaction.RunInSubtransaction(() =>
            {
                PgException? original = null;
                PgException failure = Assert.ThrowsExactly<PgException>(() => PgTransaction.RunInSubtransaction(() =>
                {
                    original = Assert.ThrowsExactly<PgException>(() => NativeRawCall.Invoke(1, [], 0, 0));
                    Assert.AreSame(original, Assert.ThrowsExactly<PgException>(() => NativeRawCall.Invoke(2, [], 0, 0)));
                    Assert.AreSame(original, Assert.ThrowsExactly<PgException>(() => Spi.Execute("SELECT 1")));
                    Assert.AreSame(original, Assert.ThrowsExactly<PgException>(() => PgTransaction.RunInSubtransaction(static () => 9)));
                }));
                Assert.AreSame(original, failure);
                Assert.AreEqual("owned detail", failure.Detail);
                Assert.ContainsSingle(memory.Requests);
                memory.Handler = null;
                NativeRawCall.Invoke(3, [], 0, 0);
                return PgTransaction.RunInSubtransaction(static () => 73);
            });
            Assert.AreEqual(73, result);
            Assert.AreSequenceEqual([1, 0, 0], s_statuses!);
            Assert.HasCount(2, memory.Requests);
            Assert.AreEqual(3, memory.ErrorReleases);
        }
        finally
        {
            Exit(previous);
        }
    }

    /// <summary>
    /// Native failures before callback entry or after success retain their own diagnostics and permit another scope.
    /// </summary>
    /// <param name="stage">One fails before entry; two fails after observing callback success.</param>
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void RecoveryScopesPreserveNativeBoundaryFailures(int stage)
    {
        nint previous = Enter();
        try
        {
            s_failStage = stage;
            bool invoked = false;
            PgException failure = Assert.ThrowsExactly<PgException>(() => PgTransaction.RunInSubtransaction(() => invoked = true));
            Assert.AreEqual("55000", failure.SqlState);
            Assert.AreEqual("native scope failure", failure.Message);
            Assert.AreEqual("native boundary detail", failure.Detail);
            Assert.AreEqual(stage == 2, invoked);
            Assert.HasCount(stage - 1, s_statuses!);
            s_failStage = 0;
            Assert.AreEqual(42, PgTransaction.RunInSubtransaction(static () => 42));
        }
        finally
        {
            Exit(previous);
        }
    }

    /// <summary>
    /// Installs a synchronous native callback fixture on the current test thread.
    /// </summary>
    /// <returns>The enclosing backend capability.</returns>
    private static nint Enter()
    {
        s_statuses = [];
        return NativeBackend.Enter((nint)(delegate* unmanaged[Cdecl]<NativeSpiRequest*, NativeSpiResult*, NativeCallError*, int>)&Execute);
    }

    /// <summary>
    /// Restores the enclosing backend capability and releases recorded fixture state.
    /// </summary>
    /// <param name="previous">The capability captured on entry.</param>
    private static void Exit(nint previous)
    {
        NativeBackend.Exit(previous);
        s_statuses = null;
        s_failStage = 0;
    }

    /// <summary>
    /// Observes the unmanaged callback status before returning a distinct native error to its managed caller.
    /// </summary>
    /// <param name="request">The borrowed recovery callback and state.</param>
    /// <param name="result">The unused result transport.</param>
    /// <param name="error">The independently generated native diagnostic.</param>
    /// <returns>Zero on success or one after observing callback failure.</returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Execute(NativeSpiRequest* request, NativeSpiResult* result, NativeCallError* error)
    {
        try
        {
            Assert.AreEqual(SpiOperation.Subtransaction, request->_operation);
            if (s_failStage == 1)
            {
                NativeError.Write(new PgException("55000", "native scope failure", "native boundary detail"), error);
                return 1;
            }

            var callback = (delegate* unmanaged[Cdecl]<nint, int>)request->_callback;
            int status = callback(request->_callbackState);
            s_statuses!.Add(status);
            if (s_failStage == 2)
            {
                NativeError.Write(new PgException("55000", "native scope failure", "native boundary detail"), error);
                return 1;
            }

            if (status != 0)
            {
                NativeError.Write(new PgException("38000", "native rollback completed"), error);
            }

            return status;
        }
        catch (Exception exception)
        {
            NativeError.Write(exception, error);
            return 1;
        }
    }
}
