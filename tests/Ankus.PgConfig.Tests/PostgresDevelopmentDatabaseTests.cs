using System.Text;

namespace Ankus.PgConfig.Tests;

/// <summary>
/// Verifies exact database connection values before invoking PostgreSQL.
/// </summary>
[TestClass]
public sealed class PostgresDevelopmentDatabaseTests
{
    /// <summary>
    /// Connection metacharacters and Unicode retain their exact meaning in an ASCII URI.
    /// </summary>
    [TestMethod]
    public void ConnectionValuesPreserveDatabaseNames()
    {
        Assert.AreEqual("postgresql://postgres@127.0.0.1:15432/%20caf%C3%A9%27%5C%20host%3Delsewhere?hostaddr=127.0.0.1&connect_timeout=10",
            PostgresDevelopmentCluster.CreateConnectionString(15432, " café'\\ host=elsewhere"));
        Assert.Contains(":1/%20?", PostgresDevelopmentCluster.CreateConnectionString(1, " "));
        Assert.Contains(":65535/postgresql%3A%2F%2Felsewhere%2Fdb?", PostgresDevelopmentCluster.CreateConnectionString(65535, "postgresql://elsewhere/db"));
    }

    /// <summary>
    /// Invalid names and ports are rejected without a native process.
    /// </summary>
    [TestMethod]
    public void InvalidConnectionValuesFailExplicitly()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => PostgresDevelopmentCluster.CreateConnectionString(15432, null!));
        Assert.ThrowsExactly<ArgumentException>(() => PostgresDevelopmentCluster.CreateConnectionString(15432, ""));
        Assert.ThrowsExactly<ArgumentException>(() => PostgresDevelopmentCluster.CreateConnectionString(15432, "bad\0name"));
        Assert.ThrowsExactly<EncoderFallbackException>(() => PostgresDevelopmentCluster.CreateConnectionString(15432, "bad\ud800"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PostgresDevelopmentCluster.CreateConnectionString(0, "db"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PostgresDevelopmentCluster.CreateConnectionString(-1, "db"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PostgresDevelopmentCluster.CreateConnectionString(65536, "db"));
    }
}
