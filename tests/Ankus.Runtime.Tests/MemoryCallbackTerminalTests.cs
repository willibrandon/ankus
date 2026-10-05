namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies the independent terminal capability supplied to memory cleanup callbacks.
/// </summary>
public sealed unsafe partial class PgMemoryCallbackTests
{
    /// <summary>
    /// Terminal diagnostics use the callback's memory provider while inherited SQL and ordinary logging remain masked.
    /// </summary>
    /// <param name="level">The terminal reporting severity.</param>
    [TestMethod]
    [DataRow(PgLogLevel.Fatal)]
    [DataRow(PgLogLevel.Panic)]
    public void MemoryCallbackTerminalTransportOwnsItsCapabilityAndRestoresNestedScopes(PgLogLevel level)
    {
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        int reports = 0;
        fixture.Handler = request =>
        {
            if (request._operation == NativeMemoryOperation.RecordTerminal)
            {
                reports++;
                Assert.AreEqual((int)level, request._flags);
                NativeCallError* transport = (NativeCallError*)request._pointer;
                Assert.AreEqual((int)level + 1, transport->_reportLevel);
                PgException diagnostic = transport->ToException();
                Assert.AreEqual("P7806", diagnostic.SqlState);
                Assert.AreEqual("terminal cleanup café", diagnostic.Message);
                Assert.AreEqual("terminal cleanup naïve", diagnostic.Detail);
                Assert.AreEqual("restart after terminal cleanup déjà", diagnostic.Hint);
            }

            return fixture.Respond(request);
        };
        nint previousLog = NativeLog.Enter(0);
        try
        {
            using PgMemoryCallback registration = PgMemoryContext.Current.RegisterResetCallback(() =>
            {
                Assert.ThrowsExactly<InvalidOperationException>(() => Spi.Execute("SELECT 42"));
                Assert.ThrowsExactly<InvalidOperationException>(() => PgLog.IsEnabled(PgLogLevel.Notice));
                nint nestedLog = NativeLog.Enter(0);
                try
                {
                    Assert.ThrowsExactly<InvalidOperationException>(() => PgLog.Write(level, "disabled nested logger"));
                    Assert.AreEqual(0, reports);
                }
                finally
                {
                    NativeLog.Exit(nestedLog);
                }

                var expected = new PgDiagnostic("terminal cleanup café")
                {
                    SqlState = "P7806",
                    Detail = "terminal cleanup naïve",
                    Hint = "restart after terminal cleanup déjà",
                };
                PgTerminalException terminal = Assert.ThrowsExactly<PgTerminalException>(() => PgLog.Write(level, expected));
                Assert.AreEqual(level, terminal.Level);
                Assert.AreSame(expected, terminal.Diagnostic);
                Assert.AreEqual(1, reports);
                Assert.ThrowsExactly<InvalidOperationException>(() => PgLog.IsEnabled(PgLogLevel.Notice));
            });
            Assert.IsNull(Dispatch(GetRegistration(fixture), scope.Address));
            Assert.IsFalse(registration.IsPending);
            Assert.AreEqual(1, reports);
            Assert.ThrowsExactly<InvalidOperationException>(() => PgLog.Write(level, "restored disabled logger"));
            Assert.AreEqual(1, reports);
        }
        finally
        {
            NativeLog.Exit(previousLog);
        }
    }
}
