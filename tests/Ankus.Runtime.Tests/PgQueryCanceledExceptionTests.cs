namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies .NET cancellation identity and lossless PostgreSQL diagnostic transport.
/// </summary>
[TestClass]
public sealed class PgQueryCanceledExceptionTests
{
    /// <summary>
    /// Public constructors retain their message, cause and cancellation SQLSTATE.
    /// </summary>
    [TestMethod]
    public void ConstructorsRetainCancellationContract()
    {
        var cause = new InvalidOperationException("cause");
        var error = new PgQueryCanceledException("stopped", cause);
        Assert.IsInstanceOfType<OperationCanceledException>(error);
        Assert.AreEqual("stopped", error.Message);
        Assert.AreEqual(PgSqlStates.QueryCanceled, error.Diagnostic.SqlState);
        Assert.AreSame(cause, error.InnerException);
        Assert.AreSame(cause, error.Diagnostic.InnerException);
        Assert.AreEqual("stopped", new PgQueryCanceledException("stopped").Message);
        Assert.AreEqual("PostgreSQL query was canceled.", new PgQueryCanceledException().Message);
    }

    /// <summary>
    /// Owned diagnostics survive both directions and release of the original native transport.
    /// </summary>
    [TestMethod]
    public unsafe void NativeCancellationRetainsDiagnosticsAfterRelease()
    {
        NativeCallError transport = default;
        PgQueryCanceledException canceled;
        try
        {
            NativeError.Write(new PgException(PgSqlStates.QueryCanceled, "cancel café", "detail", "hint")
            {
                Context = "nested query",
                Position = 7,
                Routine = "ProcessInterrupts",
                NativeFlags = NativeErrorFlags.Rethrow | NativeErrorFlags.OutputToClient,
            }, &transport);
            canceled = Assert.IsInstanceOfType<PgQueryCanceledException>(transport.ToManagedException());
        }
        finally
        {
            transport.Release();
        }

        try
        {
            NativeError.Write(canceled, &transport);
            PgException roundTrip = transport.ToException();
            Assert.AreEqual(PgSqlStates.QueryCanceled, roundTrip.SqlState);
            Assert.AreEqual("cancel café", roundTrip.Message);
            Assert.AreEqual("detail", roundTrip.Detail);
            Assert.AreEqual("hint", roundTrip.Hint);
            Assert.AreEqual("nested query", roundTrip.Context);
            Assert.AreEqual(7, roundTrip.Position);
            Assert.AreEqual("ProcessInterrupts", roundTrip.Routine);
            Assert.AreEqual(NativeErrorFlags.Rethrow | NativeErrorFlags.OutputToClient, roundTrip.NativeFlags);
        }
        finally
        {
            transport.Release();
        }
    }
}
