namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies backend test identities without invoking backend-only user methods in the host process.
/// </summary>
[TestClass]
public sealed class PgTestCaseTests
{
    /// <summary>
    /// Preserves quoted identifiers, original test names, exact expected errors and explicit ignore metadata.
    /// </summary>
    [TestMethod]
    public void PreservesBackendTestIdentity()
    {
        var test = new PgTestCase("Checks.Expected()", "Case \"Schema\"", "test café", "expected\nerror", "Requires a feature");
        Assert.AreEqual("Checks.Expected()", test.Name);
        Assert.AreEqual(test.Name, test.ToString());
        Assert.AreEqual("Case \"Schema\"", test.Schema);
        Assert.AreEqual("test café", test.FunctionName);
        Assert.AreEqual("expected\nerror", test.ExpectedError);
        Assert.AreEqual("Requires a feature", test.IgnoreReason);
    }

    /// <summary>
    /// Null defaults distinguish ordinary success from an explicitly empty expected message.
    /// </summary>
    [TestMethod]
    public void PreservesExpectedErrorAndIgnoreReason()
    {
        var ordinary = new PgTestCase("Checks.Success()", null, "test");
        Assert.IsNull(ordinary.Schema);
        Assert.IsNull(ordinary.ExpectedError);
        Assert.IsNull(ordinary.IgnoreReason);
        var empty = new PgTestCase("Checks.Empty()", " ", " ", "");
        Assert.AreEqual("", empty.ExpectedError);
        Assert.AreEqual(" ", empty.Schema);
        Assert.AreEqual(" ", empty.FunctionName);
    }

    /// <summary>
    /// Validates PostgreSQL's identifier limit in bytes rather than UTF-16 characters.
    /// </summary>
    /// <param name="bytes">The exact encoded identifier length.</param>
    [TestMethod]
    [DataRow(62)]
    [DataRow(63)]
    [DataRow(64)]
    public void BackendTestIdentifiersRespectUtf8Boundary(int bytes)
    {
        string identifier = new string('é', 31) + new string('a', bytes - 62);
        if (bytes <= 63)
        {
            var test = new PgTestCase("Checks.Length()", identifier, identifier);
            Assert.AreEqual(identifier, test.Schema);
            Assert.AreEqual(identifier, test.FunctionName);
        }
        else
        {
            ArgumentException schema = Assert.ThrowsExactly<ArgumentException>(() => new PgTestCase("test", identifier, "function"));
            ArgumentException function = Assert.ThrowsExactly<ArgumentException>(() => new PgTestCase("test", null, identifier));
            Assert.AreEqual("schema", schema.ParamName);
            Assert.AreEqual("functionName", function.ParamName);
        }
    }

    /// <summary>
    /// Rejects missing names, malformed Unicode, NUL and empty ignore reasons without changing their exception identity.
    /// </summary>
    /// <param name="field">The field receiving invalid text.</param>
    /// <param name="value">The invalid text.</param>
    [TestMethod]
    [DataRow("name", "")]
    [DataRow("name", " ")]
    [DataRow("schema", "")]
    [DataRow("functionName", "")]
    [DataRow("ignoreReason", "")]
    [DataRow("ignoreReason", " ")]
    [DataRow("expectedError", "bad\0value")]
    [DataRow("name", "bad\0value")]
    [DataRow("schema", "bad\0value")]
    [DataRow("functionName", "bad\0value")]
    [DataRow("ignoreReason", "bad\0value")]
    public void RejectsInvalidBackendTestIdentity(string field, string value)
    {
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => new PgTestCase(
            field == "name" ? value : "Checks.Test()", field == "schema" ? value : null,
            field == "functionName" ? value : "test", field == "expectedError" ? value : null,
            field == "ignoreReason" ? value : null));
        Assert.AreEqual(field, error.ParamName);
    }

    /// <summary>
    /// Required identities reject null with the original parameter name.
    /// </summary>
    [TestMethod]
    public void RejectsNullBackendTestIdentity()
    {
        ArgumentNullException name = Assert.ThrowsExactly<ArgumentNullException>(() => new PgTestCase(null!, null, "test"));
        ArgumentNullException function = Assert.ThrowsExactly<ArgumentNullException>(() => new PgTestCase("test", null, null!));
        Assert.AreEqual("name", name.ParamName);
        Assert.AreEqual("functionName", function.ParamName);
    }

    /// <summary>
    /// Invalid surrogate text is rejected before UTF-8 replacement could change an identity or expected error.
    /// </summary>
    [TestMethod]
    public void RejectsMalformedBackendTestUnicode()
    {
        string invalid = new('\ud800', 1);
        ArgumentException name = Assert.ThrowsExactly<ArgumentException>(() => new PgTestCase(invalid, null, "function"));
        ArgumentException schema = Assert.ThrowsExactly<ArgumentException>(() => new PgTestCase("test", invalid, "function"));
        ArgumentException function = Assert.ThrowsExactly<ArgumentException>(() => new PgTestCase("test", null, invalid));
        ArgumentException expected = Assert.ThrowsExactly<ArgumentException>(() => new PgTestCase("test", null, "function", invalid));
        ArgumentException ignored = Assert.ThrowsExactly<ArgumentException>(() => new PgTestCase("test", null, "function", ignoreReason: invalid));
        Assert.AreEqual("name", name.ParamName);
        Assert.AreEqual("schema", schema.ParamName);
        Assert.AreEqual("functionName", function.ParamName);
        Assert.AreEqual("expectedError", expected.ParamName);
        Assert.AreEqual("ignoreReason", ignored.ParamName);
    }
}
