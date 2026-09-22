namespace Ankus.Testing;

/// <summary>
/// Reports a backend test failure together with its PostgreSQL session log and original exception.
/// </summary>
public sealed class PostgresTestException : Exception
{
    /// <summary>
    /// Initializes a failure for a named PostgreSQL test session.
    /// </summary>
    /// <param name="testName">The name of the failed test.</param>
    /// <param name="serverLog">The log emitted by the test's backend session.</param>
    /// <param name="innerException">The original test or database exception.</param>
    public PostgresTestException(string testName, string serverLog, Exception innerException)
        : base($"PostgreSQL test '{testName}' failed: {innerException.Message}{Environment.NewLine}{serverLog}", innerException)
    {
        TestName = testName;
        ServerLog = serverLog;
    }

    /// <summary>
    /// Gets the name of the failed test.
    /// </summary>
    public string TestName { get; }

    /// <summary>
    /// Gets the server log associated with this test session.
    /// </summary>
    public string ServerLog { get; }
}
