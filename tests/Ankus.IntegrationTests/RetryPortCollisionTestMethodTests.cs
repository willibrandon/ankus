using System.Reflection;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies that released-port tests rerun only after PostgreSQL loses its port, and within the attempt limit.
/// </summary>
[TestClass]
public sealed class RetryPortCollisionTestMethodTests
{
    private const string Collision = """
        LOG:  could not bind IPv4 address "127.0.0.1": Only one usage of each socket address (protocol/network address/port) is normally permitted.
        HINT:  Is another postmaster already running on port 40952? If not, wait a few seconds and retry.
        FATAL:  could not create any TCP/IP sockets
        """;

    /// <summary>
    /// A collision reruns the test, and the final result records the superseded attempt's failure.
    /// </summary>
    [TestMethod]
    public async Task CollisionRerunsAndRecordsSupersededAttempt()
    {
        var method = new ScriptedTestMethod(Failed(Collision), Passed());

        TestResult[] results = await new RetryPortCollisionTestMethodAttribute().ExecuteAsync(method);

        Assert.AreEqual(2, method.Invocations);
        TestResult result = Assert.ContainsSingle(results);
        Assert.AreEqual(UnitTestOutcome.Passed, result.Outcome);
        Assert.StartsWith("Attempt 1 lost its PostgreSQL port to another process and was rerun:", result.LogOutput);
        Assert.Contains("Is another postmaster already running on port 40952?", result.LogOutput);
        Assert.EndsWith("final output", result.LogOutput);
    }

    /// <summary>
    /// Repeated collisions stop at the attempt limit with the last failure.
    /// </summary>
    [TestMethod]
    public async Task RepeatedCollisionsStopAtAttemptLimit()
    {
        var method = new ScriptedTestMethod(Failed(Collision), Failed(Collision), Failed(Collision), Passed());

        TestResult[] results = await new RetryPortCollisionTestMethodAttribute().ExecuteAsync(method);

        Assert.AreEqual(RetryPortCollisionTestMethodAttribute.MaximumAttempts, method.Invocations);
        TestResult result = Assert.ContainsSingle(results);
        Assert.AreEqual(UnitTestOutcome.Failed, result.Outcome);
        Assert.StartsWith("Attempt 1 lost its PostgreSQL port", result.LogOutput);
        Assert.AreEqual(1, result.LogOutput!.Split("Attempt 2 lost its PostgreSQL port").Length - 1);
        Assert.DoesNotContain("Attempt 3 lost", result.LogOutput);
    }

    /// <summary>
    /// A passing test runs once and keeps its output unchanged.
    /// </summary>
    [TestMethod]
    public async Task PassingTestRunsOnce()
    {
        var method = new ScriptedTestMethod(Passed(), Failed(Collision));

        TestResult[] results = await new RetryPortCollisionTestMethodAttribute().ExecuteAsync(method);

        Assert.AreEqual(1, method.Invocations);
        Assert.AreEqual("final output", Assert.ContainsSingle(results).LogOutput);
    }

    /// <summary>
    /// Failures that do not report both parts of PostgreSQL's bind collision run once and keep their output unchanged.
    /// </summary>
    /// <param name="message">The failure message.</param>
    [TestMethod]
    [DataRow("Assert.AreEqual failed. Expected:<42>. Actual:<0>.")]
    [DataRow("HINT:  Is another postmaster already running on port 40952? If not, wait a few seconds and retry.")]
    [DataRow("LOG:  could not bind IPv6 address \"::1\": Address already in use")]
    public async Task OtherFailuresRunOnce(string message)
    {
        var method = new ScriptedTestMethod(Failed(message), Passed());

        TestResult[] results = await new RetryPortCollisionTestMethodAttribute().ExecuteAsync(method);

        Assert.AreEqual(1, method.Invocations);
        TestResult result = Assert.ContainsSingle(results);
        Assert.AreEqual(UnitTestOutcome.Failed, result.Outcome);
        Assert.AreEqual("final output", result.LogOutput);
    }

    /// <summary>
    /// An attempt is a collision only when every failed result reports one; passing and skipped results are ignored.
    /// </summary>
    [TestMethod]
    public void CollisionRequiresEveryFailureToReportIt()
    {
        Assert.IsTrue(RetryPortCollisionTestMethodAttribute.IsPortCollision([Failed(Collision), Passed()]));
        Assert.IsTrue(RetryPortCollisionTestMethodAttribute.IsPortCollision(
            [Failed(new InvalidOperationException("PostgreSQL startup failed.", new InvalidOperationException(Collision)))]));
        Assert.IsTrue(RetryPortCollisionTestMethodAttribute.IsPortCollision([Result(UnitTestOutcome.Timeout, Collision)]));
        Assert.IsFalse(RetryPortCollisionTestMethodAttribute.IsPortCollision([Failed(Collision), Failed("division by zero")]));
        Assert.IsFalse(RetryPortCollisionTestMethodAttribute.IsPortCollision([Passed(), Result(UnitTestOutcome.Inconclusive, Collision)]));
        Assert.IsFalse(RetryPortCollisionTestMethodAttribute.IsPortCollision([]));
    }

    private static TestResult Passed() => Result(UnitTestOutcome.Passed, null);

    private static TestResult Failed(string message) => Result(UnitTestOutcome.Failed, message);

    private static TestResult Failed(Exception error) => new() { Outcome = UnitTestOutcome.Failed, TestFailureException = error, LogOutput = "final output" };

    private static TestResult Result(UnitTestOutcome outcome, string? message) => new()
    {
        Outcome = outcome,
        TestFailureException = message is null ? null : new AssertFailedException(message),
        LogOutput = "final output",
    };

    /// <summary>
    /// Returns one scripted result per invocation and counts invocations.
    /// </summary>
    /// <param name="results">The result of each successive invocation.</param>
    private sealed class ScriptedTestMethod(params TestResult[] results) : ITestMethod
    {
        /// <summary>
        /// Gets the number of invocations.
        /// </summary>
        public int Invocations { get; private set; }

        /// <inheritdoc />
        public string TestMethodName => nameof(ScriptedTestMethod);

        /// <inheritdoc />
        public string TestClassName => nameof(RetryPortCollisionTestMethodTests);

        /// <inheritdoc />
        public Type ReturnType => typeof(Task);

        /// <inheritdoc />
        public object?[]? Arguments => null;

        /// <inheritdoc />
        public ParameterInfo[] ParameterTypes => [];

        /// <inheritdoc />
        public MethodInfo MethodInfo => typeof(ScriptedTestMethod).GetMethod(nameof(InvokeAsync))!;

        /// <inheritdoc />
        public Task<TestResult> InvokeAsync(object?[]? arguments)
        {
            TestResult result = results[Invocations];
            Invocations++;
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public Attribute[]? GetAllAttributes() => [];

        /// <inheritdoc />
        public TAttributeType[] GetAttributes<TAttributeType>()
            where TAttributeType : Attribute
            => [];
    }
}
