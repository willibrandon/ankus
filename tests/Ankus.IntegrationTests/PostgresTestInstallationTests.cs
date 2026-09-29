using Ankus.PgConfig;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies relocated installations preserve the selected server and leave source files untouched.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class PostgresTestInstallationTests(TestContext context)
{
    /// <summary>
    /// A Unix directory alias such as Homebrew's opt path relocates through the reported binary directory.
    /// </summary>
    [TestMethod]
    [OSCondition(OperatingSystems.Linux | OperatingSystems.OSX)]
    public async Task DirectoryAliasStagesRunnableServerAndPreservesSource()
    {
        CancellationToken token = context.CancellationToken;
        PostgresInstallation source = await IntegrationEnvironment.GetInstallationAsync(token);
        byte[] original = await File.ReadAllBytesAsync(source.PgConfigPath, token);
        string root = Directory.CreateTempSubdirectory("ankus-relocation-").FullName;
        try
        {
            string alias = Path.Combine(root, "linked-bin");
            Directory.CreateSymbolicLink(alias, source.BinDirectory);
            PostgresInstallation linked = await PostgresInstallation.CreateAsync(Path.Combine(alias, "pg_config"), token);
            Assert.AreEqual(source.BinDirectory, linked.BinDirectory);
            Assert.AreNotEqual(Path.GetDirectoryName(linked.PgConfigPath), linked.BinDirectory);
            string stage = Path.Combine(root, "stage");
            await using (PostgresTestInstallation owner = await PostgresTestInstallation.StageAsync(linked, stage, token))
            {
                Assert.AreEqual(source.Version, owner.Installation.Version);
                Assert.StartsWith(stage + Path.DirectorySeparatorChar, owner.Installation.PgConfigPath);
                Assert.IsTrue(File.Exists(owner.Installation.PgConfigPath));
                await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(new PostgresTestClusterOptions
                {
                    Installation = owner.Installation,
                    DataDirectoryBase = Path.Combine(root, "data"),
                    LogDirectory = Path.Combine(root, "logs"),
                }, token);
                await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
                await using var command = new NpgsqlCommand("SELECT 19 + 23", connection);
                Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
                command.CommandText = "SHOW server_version_num";
                string version = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
                Assert.AreEqual(source.Version.Major, int.Parse(version, System.Globalization.CultureInfo.InvariantCulture) / 10000);
            }

            Assert.IsFalse(Directory.Exists(stage));
            Assert.AreSequenceEqual(original, await File.ReadAllBytesAsync(source.PgConfigPath, token));
            PostgresInstallation retained = await PostgresInstallation.CreateAsync(source.PgConfigPath, token);
            Assert.AreEqual(source.Version, retained.Version);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
