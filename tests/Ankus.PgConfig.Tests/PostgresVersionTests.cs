namespace Ankus.PgConfig.Tests;

/// <summary>
/// Verifies PostgreSQL version parsing across stable, beta, and release-candidate
/// formats emitted by supported <c>pg_config</c> versions.
/// </summary>
[TestClass]
public sealed class PostgresVersionTests
{
    /// <summary>
    /// Verifies stable PostgreSQL output, including vendor suffixes, produces the
    /// expected major version, minor version, label, and display value.
    /// </summary>
    /// <param name="input">The complete <c>pg_config --version</c> output.</param>
    /// <param name="major">The expected major version.</param>
    /// <param name="minor">The expected minor version.</param>
    [TestMethod]
    [DataRow("PostgreSQL 13.23", 13, 23)]
    [DataRow("PostgreSQL 18.6 (Debian 18.6-1.pgdg13+1)", 18, 6)]
    public void ParseStableVersionReturnsExpectedValues(string input, int major, int minor)
    {
        PostgresVersion version = PostgresVersion.Parse(input);

        Assert.AreEqual(major, version.Major);
        Assert.AreEqual(minor, version.Minor);
        Assert.AreEqual(PostgresReleaseStage.Stable, version.Stage);
        Assert.AreEqual(0, version.StageNumber);
        Assert.AreEqual($"pg{major}", version.Label);
        Assert.AreEqual($"{major}.{minor}", version.ToString());
    }

    /// <summary>
    /// Verifies PostgreSQL beta output retains both the major version and beta
    /// sequence number used by the pgrx-supported prerelease matrix.
    /// </summary>
    [TestMethod]
    public void ParseBetaVersionReturnsExpectedValues()
    {
        PostgresVersion version = PostgresVersion.Parse("PostgreSQL 19beta2");

        Assert.AreEqual(19, version.Major);
        Assert.AreEqual(0, version.Minor);
        Assert.AreEqual(PostgresReleaseStage.Beta, version.Stage);
        Assert.AreEqual(2, version.StageNumber);
        Assert.AreEqual("19beta2", version.ToString());
    }

    /// <summary>
    /// Verifies PostgreSQL release-candidate output retains both the major version
    /// and release-candidate sequence number.
    /// </summary>
    [TestMethod]
    public void ParseReleaseCandidateVersionReturnsExpectedValues()
    {
        PostgresVersion version = PostgresVersion.Parse("PostgreSQL 19rc1");

        Assert.AreEqual(19, version.Major);
        Assert.AreEqual(0, version.Minor);
        Assert.AreEqual(PostgresReleaseStage.ReleaseCandidate, version.Stage);
        Assert.AreEqual(1, version.StageNumber);
        Assert.AreEqual("19rc1", version.ToString());
    }

    /// <summary>
    /// Verifies malformed and non-PostgreSQL version text is rejected rather than
    /// silently selecting the wrong server ABI.
    /// </summary>
    /// <param name="input">Invalid version output.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("18.6")]
    [DataRow("PostgreSQL unknown")]
    [DataRow("PostgreSQL 19beta")]
    public void ParseInvalidVersionThrowsFormatException(string input)
    {
        Assert.ThrowsExactly<FormatException>(() => PostgresVersion.Parse(input));
    }
}
