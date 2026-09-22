namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies SQLSTATE validation and managed error contracts before diagnostics cross the native boundary.
/// </summary>
[TestClass]
public sealed class PgExceptionTests
{
    /// <summary>
    /// Verifies explicit diagnostics and the original managed cause are preserved.
    /// </summary>
    [TestMethod]
    public void StructuredErrorRetainsDiagnosticsAndCause()
    {
        var cause = new InvalidOperationException("original");
        var error = new PgException("23505", "duplicate", "key exists", "choose another", cause);

        Assert.AreEqual("23505", error.SqlState);
        Assert.AreEqual("duplicate", error.Message);
        Assert.AreEqual("key exists", error.Detail);
        Assert.AreEqual("choose another", error.Hint);
        Assert.AreSame(cause, error.InnerException);
    }

    /// <summary>
    /// Verifies malformed or successful SQLSTATE codes cannot be sent to PostgreSQL's error machinery.
    /// </summary>
    /// <param name="state">The invalid SQLSTATE.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("2202")]
    [DataRow("220230")]
    [DataRow("2202a")]
    [DataRow("22 23")]
    [DataRow("00000")]
    public void InvalidSqlStatesAreRejected(string state)
    {
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => new PgException(state, "invalid"));
        Assert.AreEqual("sqlState", error.ParamName);
    }

    /// <summary>
    /// Verifies an ordinary managed process cannot invoke a PostgreSQL entry point without a backend dispatch scope.
    /// </summary>
    [TestMethod]
    public void SpiOutsideBackendFailsBeforeNativeCall()
    {
        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() => Spi.Execute("SELECT 1"));
        Assert.AreEqual("PostgreSQL APIs can only be used on the active PostgreSQL backend thread.", error.Message);
    }
}
