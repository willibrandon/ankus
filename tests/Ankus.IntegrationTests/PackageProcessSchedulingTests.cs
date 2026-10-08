namespace Ankus.IntegrationTests;

/// <summary>
/// Keeps compiler capacity available without queuing commands that only inspect or use existing publications.
/// </summary>
[TestClass]
public sealed class PackageProcessSchedulingTests
{
    /// <summary>
    /// Existing-publication and dry-run modes bypass compiler capacity while real builds remain bounded.
    /// </summary>
    /// <param name="executable">The child process filename.</param>
    /// <param name="arguments">The tool's literal argument list.</param>
    /// <param name="reserved">Whether this invocation can compile an extension.</param>
    [TestMethod]
    [DataRow("ankus", new[] { "regress", "--dry-run" }, false)]
    [DataRow("ankus.exe", new[] { "regress", "--dry-run=true" }, false)]
    [DataRow("ankus", new[] { "regress", "--dry-run:false" }, true)]
    [DataRow("ankus", new[] { "regress", "--dry-run", "false" }, true)]
    [DataRow("ankus", new[] { "regress", "--no-build" }, false)]
    [DataRow("ankus", new[] { "regress" }, true)]
    [DataRow("ankus", new[] { "run", "--no-build" }, false)]
    [DataRow("ankus", new[] { "run", "--", "--no-build" }, true)]
    [DataRow("ankus", new[] { "run", "--no-build", "false" }, true)]
    [DataRow("ankus", new[] { "bench", "--no-build:true" }, false)]
    [DataRow("ankus", new[] { "bench", "--no-build=false" }, true)]
    [DataRow("ankus", new[] { "publish" }, true)]
    [DataRow("ankus", new[] { "upgrade" }, false)]
    [DataRow("dotnet", new[] { "test", "--no-build" }, true)]
    [DataRow("dotnet.exe", new[] { "publish", "--no-restore" }, true)]
    [DataRow("psql.exe", new[] { "--no-build" }, false)]
    public void ReservesCapacityForCompilation(string executable, string[] arguments, bool reserved)
        => Assert.AreEqual(reserved, PackageProcessRunner.RequiresSlot(executable, arguments));
}
