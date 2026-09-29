using System.Text;
using Ankus.PgConfig;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies installed regression tools and exact database resets against PostgreSQL.
/// </summary>
/// <param name="context">The current test cancellation context.</param>
[TestClass]
public sealed class PostgresRegressionPrerequisiteTests(TestContext context)
{
    private readonly string _root = Directory.CreateTempSubdirectory("ankus regression prerequisites ").FullName;

    /// <summary>
    /// Removes only the temporary data owned by this test after its server has stopped.
    /// </summary>
    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);

    /// <summary>
    /// The regression executable follows authoritative PGXS relocation and is optional for ordinary installation discovery.
    /// </summary>
    [TestMethod]
    public async Task RegressionDriverFollowsPgxsAndReportsMissingTools()
    {
        CancellationToken token = context.CancellationToken;
        PostgresInstallation source = await IntegrationEnvironment.GetInstallationAsync(token);
        string original = await source.GetRegressionDriverPathAsync(token);
        ProcessResult pgxs = await ProcessRunner.RunAsync(source.PgConfigPath, ["--pgxs"], new Dictionary<string, string?>(), token);
        pgxs.EnsureSuccess("pg_config", ["--pgxs"]);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(pgxs.StandardOutput.Trim())!, "..", "test", "regress",
            OperatingSystem.IsWindows() ? "pg_regress.exe" : "pg_regress")), original);
        ProcessResult version = await ProcessRunner.RunAsync(original, ["--version"],
            new Dictionary<string, string?> { ["PATH"] = source.BinDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH") }, token);
        Assert.AreEqual(0, version.ExitCode, version.StandardError);
        ProcessResult configurationVersion = await ProcessRunner.RunAsync(source.PgConfigPath, ["--version"], new Dictionary<string, string?>(), token);
        configurationVersion.EnsureSuccess("pg_config", ["--version"]);
        Assert.AreEqual(configurationVersion.StandardOutput.Replace("PostgreSQL ", "pg_regress (PostgreSQL) ", StringComparison.Ordinal), version.StandardOutput);

        await using PostgresTestInstallation owner = await PostgresTestInstallation.StageAsync(source, Path.Combine(_root, "installation"), token);
        string relocated = await owner.Installation.GetRegressionDriverPathAsync(token);
        string staged = Directory.GetFiles(owner.RootDirectory, Path.GetFileName(original), SearchOption.AllDirectories).Single();
        Assert.AreNotEqual(original, relocated);
        Assert.IsTrue(File.Exists(staged));
        Assert.IsTrue(File.Exists(relocated));
        ProcessResult relocatedVersion = await ProcessRunner.RunAsync(relocated, ["--version"],
            new Dictionary<string, string?> { ["PATH"] = owner.Installation.BinDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH") }, token);
        Assert.AreEqual(0, relocatedVersion.ExitCode, relocatedVersion.StandardError);
        Assert.AreEqual(version.StandardOutput, relocatedVersion.StandardOutput);
        File.Delete(relocated);
        Assert.IsFalse(File.Exists(staged));
        PostgresInstallation stillUsable = await PostgresInstallation.CreateAsync(owner.Installation.PgConfigPath, token);
        Assert.AreEqual(source.Version, stillUsable.Version);
        FileNotFoundException error = await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => stillUsable.GetRegressionDriverPathAsync(token));
        Assert.AreEqual(relocated, error.FileName);
        Assert.Contains("regression tools", error.Message);
        Assert.IsTrue(File.Exists(original));
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => source.GetRegressionDriverPathAsync(canceled.Token));
    }

    /// <summary>
    /// Removal preserves exact names, enforces the byte limit, leaves other data intact and can recreate an empty database.
    /// </summary>
    [TestMethod]
    public async Task DatabaseRemovalPreservesNamesOtherDataAndResetIdentity()
    {
        CancellationToken token = context.CancellationToken;
        PostgresInstallation installation = await IntegrationEnvironment.GetInstallationAsync(token);
        var cluster = new PostgresDevelopmentCluster(installation, _root);
        using PortReservation reservation = PortReservation.Create();
        int port = reservation.Port;
        reservation.Dispose();
        try
        {
            Assert.IsTrue(await cluster.StartAsync(new PostgresDevelopmentOptions { Port = port }, token));
            await using NpgsqlConnection administration = await OpenAsync(port, "postgres", token);
            await using var retained = new NpgsqlCommand("CREATE TABLE retained(value integer); INSERT INTO retained VALUES (42); SELECT pg_backend_pid()", administration);
            object? backend = await retained.ExecuteScalarAsync(token);
            string[] names = ["ordinary", " café'\\\"; # ", "--help", "host=elsewhere dbname=other", "postgresql://elsewhere/db", " ",
                "line\n\\! echo forbidden", new string('a', 63), new string('é', 31) + "a"];
            foreach (string name in names)
            {
                Assert.IsFalse(await cluster.DropDatabaseAsync(name, cancellationToken: token));
                Assert.IsTrue(await cluster.CreateDatabaseAsync(name, token));
            }

            foreach (string oversized in new[] { new string('a', 64), new string('é', 31) + "ab", new string('é', 32) })
            {
                ArgumentException error = await Assert.ThrowsExactlyAsync<ArgumentException>(() => cluster.DropDatabaseAsync(oversized, cancellationToken: token));
                Assert.Contains("63-byte", error.Message);
            }

            await using var count = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE NOT datistemplate AND datname <> 'postgres'", administration);
            Assert.AreEqual((long)names.Length, await count.ExecuteScalarAsync(token));
            await using (NpgsqlConnection old = await OpenAsync(port, "ordinary", token))
            {
                await using var setup = new NpgsqlCommand("CREATE TABLE discarded(value integer); INSERT INTO discarded VALUES (99)", old);
                await setup.ExecuteNonQueryAsync(token);
            }

            for (int index = 0; index < names.Length; index++)
            {
                Assert.IsTrue(await cluster.DropDatabaseAsync(names[index], cancellationToken: token));
                Assert.IsFalse(await cluster.DropDatabaseAsync(names[index], cancellationToken: token));
                Assert.AreEqual((long)(names.Length - index - 1), await count.ExecuteScalarAsync(token));
            }

            Assert.IsTrue(await cluster.CreateDatabaseAsync("ordinary", token));
            await using NpgsqlConnection recreated = await OpenAsync(port, "ordinary", token);
            await using var empty = new NpgsqlCommand("SELECT to_regclass('public.discarded') IS NULL", recreated);
            Assert.IsTrue((bool)(await empty.ExecuteScalarAsync(token))!);
            await using var unchanged = new NpgsqlCommand("SELECT value FROM retained WHERE pg_backend_pid() = @backend", administration);
            unchanged.Parameters.AddWithValue("backend", backend!);
            Assert.AreEqual(42, await unchanged.ExecuteScalarAsync(token));
            Assert.IsTrue(await cluster.IsRunningAsync(token));
        }
        finally
        {
            await cluster.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Active sessions require explicit force, and errors, cancellation and operation locks preserve the database.
    /// </summary>
    [TestMethod]
    public async Task DatabaseRemovalHonorsForceLocksCancellationAndRecovery()
    {
        CancellationToken token = context.CancellationToken;
        PostgresInstallation installation = await IntegrationEnvironment.GetInstallationAsync(token);
        var cluster = new PostgresDevelopmentCluster(installation, _root);
        using PortReservation reservation = PortReservation.Create();
        int port = reservation.Port;
        reservation.Dispose();
        try
        {
            await cluster.StartAsync(new PostgresDevelopmentOptions { Port = port }, token);
            Assert.IsTrue(await cluster.CreateDatabaseAsync("busy", token));
            await using NpgsqlConnection busy = await OpenAsync(port, "busy", token);
            await using var witness = new NpgsqlCommand("SELECT 42", busy);
            InvalidOperationException active = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => cluster.DropDatabaseAsync("busy", cancellationToken: token));
            Assert.Contains("being accessed", active.Message);
            Assert.AreEqual(42, await witness.ExecuteScalarAsync(token));
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => cluster.DropDatabaseAsync("busy", true, canceled.Token));
            string operationPath = Path.Combine(_root, "clusters", installation.Label + ".lock");
            using (var operation = new FileStream(operationPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                await Assert.ThrowsExactlyAsync<IOException>(() => cluster.DropDatabaseAsync("busy", true, token));
            }

            Assert.AreEqual(42, await witness.ExecuteScalarAsync(token));
            Assert.IsTrue(await cluster.DropDatabaseAsync("busy", true, token));
            Assert.IsFalse(await cluster.DropDatabaseAsync("busy", true, token));
            await using NpgsqlConnection administration = await OpenAsync(port, "postgres", token);
            await using var state = new NpgsqlCommand("SELECT NOT EXISTS (SELECT FROM pg_database WHERE datname='busy') AND NOT EXISTS (SELECT FROM pg_stat_activity WHERE datname='busy')", administration);
            Assert.IsTrue((bool)(await state.ExecuteScalarAsync(token))!);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => cluster.DropDatabaseAsync("postgres", true, token));
            Assert.IsTrue((bool)(await state.ExecuteScalarAsync(token))!);
            Assert.IsTrue(await cluster.CreateDatabaseAsync("recovered", token));
            Assert.IsTrue(await cluster.DropDatabaseAsync("recovered", cancellationToken: token));
        }
        finally
        {
            await cluster.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Invalid input and a stopped server cannot create or modify a development directory.
    /// </summary>
    [TestMethod]
    public async Task DatabaseRemovalRejectsInvalidAndStoppedRequestsWithoutWrites()
    {
        CancellationToken token = context.CancellationToken;
        PostgresInstallation installation = await IntegrationEnvironment.GetInstallationAsync(token);
        var cluster = new PostgresDevelopmentCluster(installation, _root);
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => cluster.DropDatabaseAsync(null!, cancellationToken: token));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => cluster.DropDatabaseAsync("", cancellationToken: token));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => cluster.DropDatabaseAsync("bad\0name", cancellationToken: token));
        await Assert.ThrowsExactlyAsync<EncoderFallbackException>(() => cluster.DropDatabaseAsync("bad\ud800", cancellationToken: token));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => cluster.DropDatabaseAsync("missing", true, token));
        Assert.IsEmpty(Directory.GetFileSystemEntries(_root));
    }

    private static async Task<NpgsqlConnection> OpenAsync(int port, string database, CancellationToken token)
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1", Port = port, Username = "postgres", Database = database, Pooling = false,
        }.ConnectionString);
        try
        {
            await connection.OpenAsync(token);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
