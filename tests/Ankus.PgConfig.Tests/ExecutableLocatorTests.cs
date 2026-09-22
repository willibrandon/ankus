namespace Ankus.PgConfig.Tests;

/// <summary>
/// Verifies executable discovery without replacing the process environment or
/// using a mocking framework.
/// </summary>
[TestClass]
public sealed class ExecutableLocatorTests
{
    /// <summary>
    /// Verifies an explicit path to the current test host resolves to its absolute
    /// location on every supported operating system.
    /// </summary>
    [TestMethod]
    public void FindExistingAbsolutePathReturnsCanonicalPath()
    {
        string? processPath = Environment.ProcessPath;
        Assert.IsNotNull(processPath);

        string? result = ExecutableLocator.Find(processPath);

        Assert.AreEqual(Path.GetFullPath(processPath), result);
    }

    /// <summary>
    /// Verifies a unique missing executable name returns no result instead of
    /// constructing an invalid platform-specific path.
    /// </summary>
    [TestMethod]
    public void FindMissingExecutableReturnsNull()
    {
        string? result = ExecutableLocator.Find($"ankus-missing-{Guid.NewGuid():N}");

        Assert.IsNull(result);
    }
}
