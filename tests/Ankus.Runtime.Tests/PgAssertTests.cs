namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies the failure messages and exception checks of backend-test assertions without a backend; work that would
/// run in a subtransaction runs directly.
/// </summary>
[TestClass]
public sealed class PgAssertTests
{
    private static readonly int[] s_pair = [1, 2];

    /// <summary>
    /// Passing checks return quietly, and failing ones name the check, the values and the caller's explanation.
    /// </summary>
    [TestMethod]
    public void FailuresNameTheCheckAndValues()
    {
        PgAssert.IsTrue(1 + 1 == 2);
        PgAssert.IsFalse(1 + 1 == 3);
        PgAssert.AreEqual(42, 42);
        PgAssert.AreNotEqual("a", "b");
        PgAssert.AreSequenceEqual(s_pair, new List<int> { 1, 2 });
        PgAssert.IsNull(null);
        PgAssert.IsNotNull("value");
        int answer = 41;
        Assert.AreEqual("PgAssert.IsTrue failed. Expected true: answer == 42. the answer",
            Assert.ThrowsExactly<PgAssertException>(() => PgAssert.IsTrue(answer == 42, "the answer")).Message);
        Assert.AreEqual("PgAssert.AreEqual failed. Expected: \"a\\nb\". Actual: \"a\".",
            Assert.ThrowsExactly<PgAssertException>(() => PgAssert.AreEqual("a\nb", "a")).Message);
        Assert.AreEqual("PgAssert.AreSequenceEqual failed. Expected: [1, 2]. Actual: [1, null].",
            Assert.ThrowsExactly<PgAssertException>(() => PgAssert.AreSequenceEqual(new int?[] { 1, 2 }, [1, null])).Message);
        Assert.AreEqual("PgAssert.IsNull failed. Expected null. Actual: 3.",
            Assert.ThrowsExactly<PgAssertException>(() => PgAssert.IsNull(3)).Message);
        Assert.AreEqual("PgAssert.Fail failed. stop", Assert.ThrowsExactly<PgAssertException>(() => PgAssert.Fail("stop")).Message);
    }

    /// <summary>
    /// Exception checks return the exact exception, reject other types and completed work, and match SQLSTATEs.
    /// </summary>
    [TestMethod]
    public void ExceptionChecksMatchExactlyAndReturnTheError()
    {
        InvalidOperationException thrown = PgAssert.ThrowsExactly<InvalidOperationException>(
            static () => throw new InvalidOperationException("boom"), null, static action => action());
        Assert.AreEqual("boom", thrown.Message);
        Assert.Contains("Expected InvalidOperationException. Actual: ArgumentException: bad",
            Assert.ThrowsExactly<PgAssertException>(() => PgAssert.ThrowsExactly<InvalidOperationException>(
                static () => throw new ArgumentException("bad"), null, static action => action())).Message);
        Assert.Contains("no exception was thrown", Assert.ThrowsExactly<PgAssertException>(() =>
            PgAssert.ThrowsExactly<InvalidOperationException>(static () => { }, null, static action => action())).Message);
        PgException error = PgAssert.ThrowsSqlState(PgSqlStates.DivisionByZero,
            static () => throw new PgException(PgSqlStates.DivisionByZero, "division by zero"), null, static action => action());
        Assert.AreEqual("division by zero", error.Message);
        Assert.Contains("Expected SQLSTATE 22012. Actual: 22003: out of range", Assert.ThrowsExactly<PgAssertException>(() =>
            PgAssert.ThrowsSqlState(PgSqlStates.DivisionByZero, static () => throw new PgException(PgSqlStates.NumericValueOutOfRange,
                "out of range"), null, static action => action())).Message);
        Assert.Contains("but no error was raised", Assert.ThrowsExactly<PgAssertException>(() =>
            PgAssert.ThrowsSqlState(PgSqlStates.DivisionByZero, static () => { }, null, static action => action())).Message);
        Assert.AreEqual(PgSqlStates.QueryCanceled, Assert.ThrowsExactly<PgException>(() =>
            PgAssert.ThrowsSqlState(PgSqlStates.DivisionByZero, static () => throw new PgException(PgSqlStates.QueryCanceled,
                "canceling statement due to user request"), null, static action => action())).SqlState);
    }
}
