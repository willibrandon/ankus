using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Exercises cluster ownership, transaction isolation, expected errors, and failure diagnostics against a real server.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class PostgresTestClusterTests(TestContext context)
{
    /// <summary>
    /// Verifies backend test functions leave no committed rows after successful execution.
    /// </summary>
    [TestMethod]
    public async Task BackendFunctionWritesAreRolledBack()
    {
        await PostgresFixture.Cluster.RunTestAsync("tests", "insert_probe", cancellationToken: context.CancellationToken);

        Assert.AreEqual(0L, await ReadProbeCountAsync());
    }

    /// <summary>
    /// Verifies a test-host assertion failure also rolls back database changes and preserves the original exception.
    /// </summary>
    [TestMethod]
    public async Task FailedCallbackRollsBackWrites()
    {
        var expected = new InvalidOperationException("intentional test failure");
        PostgresTestException error = await Assert.ThrowsExactlyAsync<PostgresTestException>(() =>
            PostgresFixture.Cluster.RunInTransactionAsync("failed-callback", async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("INSERT INTO tests.rollback_probe VALUES (7)", connection, transaction);
                await command.ExecuteNonQueryAsync(token);
                throw expected;
            }, context.CancellationToken));

        Assert.AreSame(expected, error.InnerException);
        Assert.AreEqual("failed-callback", error.TestName);
        Assert.AreEqual(0L, await ReadProbeCountAsync());
    }

    /// <summary>
    /// Verifies expected server errors are accepted and a fresh backend remains usable afterward.
    /// </summary>
    [TestMethod]
    public async Task ExpectedErrorMatchesAndClusterRemainsUsable()
    {
        await PostgresFixture.Cluster.RunTestAsync("tests", "fail_probe", "division by zero", context.CancellationToken);

        Assert.AreEqual(0L, await ReadProbeCountAsync());
    }

    /// <summary>
    /// Verifies wrong expected messages report the actual SQLSTATE and the failing session's PostgreSQL log.
    /// </summary>
    [TestMethod]
    public async Task WrongExpectedErrorIncludesServerDiagnostics()
    {
        PostgresTestException error = await Assert.ThrowsExactlyAsync<PostgresTestException>(() =>
            PostgresFixture.Cluster.RunTestAsync("tests", "fail_probe", "not the actual error", context.CancellationToken));

        PostgresException databaseError = Assert.IsInstanceOfType<PostgresException>(error.InnerException);
        Assert.AreEqual(PostgresErrorCodes.DivisionByZero, databaseError.SqlState);
        Assert.AreEqual("division by zero", databaseError.MessageText);
        Assert.Contains("division by zero", error.ServerLog);
        Assert.Contains("fail_probe", error.ServerLog);
    }

    /// <summary>
    /// Verifies that an expected error cannot silently pass when the backend function succeeds.
    /// </summary>
    [TestMethod]
    public async Task MissingExpectedErrorFailsAndRollsBack()
    {
        PostgresTestException error = await Assert.ThrowsExactlyAsync<PostgresTestException>(() =>
            PostgresFixture.Cluster.RunTestAsync("tests", "insert_probe", "expected failure", context.CancellationToken));

        Assert.IsInstanceOfType<InvalidOperationException>(error.InnerException);
        Assert.Contains("but the test succeeded", error.Message);
        Assert.AreEqual(0L, await ReadProbeCountAsync());
    }

    /// <summary>
    /// Verifies cancellation after a database write preserves cancellation and rolls back without the canceled token.
    /// </summary>
    [TestMethod]
    public async Task CanceledCallbackRollsBackWrites()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        await Assert.ThrowsAsync<OperationCanceledException>(() => PostgresFixture.Cluster.RunInTransactionAsync(
            "canceled-callback", async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("INSERT INTO tests.rollback_probe VALUES (9)", connection, transaction);
                await command.ExecuteNonQueryAsync(token);
                await cancellation.CancelAsync();
                token.ThrowIfCancellationRequested();
            }, cancellation.Token));

        Assert.AreEqual(0L, await ReadProbeCountAsync());
    }

    /// <summary>
    /// Verifies multiple clusters in one process have independent ports and data, and disposal stops only the owned server.
    /// </summary>
    [TestMethod]
    public async Task DisposeStopsOwnedClusterAndPreservesLogs()
    {
        PostgresTestClusterOptions options = await IntegrationEnvironment.CreateOptionsAsync(context.CancellationToken);
        await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(options, context.CancellationToken);
        Assert.AreNotEqual(PostgresFixture.Cluster.Port, cluster.Port);
        Assert.AreNotEqual(PostgresFixture.Cluster.DataDirectory, cluster.DataDirectory);
        Assert.IsTrue(Directory.Exists(cluster.DataDirectory));

        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(context.CancellationToken);
        string connectionString = connection.ConnectionString;
        await connection.CloseAsync();
        await cluster.DisposeAsync();
        await cluster.DisposeAsync();

        Assert.IsFalse(Directory.Exists(cluster.DataDirectory));
        Assert.IsFalse(Directory.Exists(cluster.SocketDirectory));
        Assert.Contains("database system is shut down", cluster.ReadServerLog());
        await using var stoppedConnection = new NpgsqlConnection(connectionString);
        await Assert.ThrowsAsync<NpgsqlException>(() => stoppedConnection.OpenAsync(context.CancellationToken));
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => cluster.OpenConnectionAsync(context.CancellationToken));
        Assert.AreEqual(0L, await ReadProbeCountAsync());
    }

    /// <summary>
    /// Verifies invalid PostgreSQL settings fail startup, remove the invocation's data, and retain the startup diagnostic.
    /// </summary>
    [TestMethod]
    public async Task FailedStartupCleansDataAndRetainsDiagnostic()
    {
        PostgresTestClusterOptions defaults = await IntegrationEnvironment.CreateOptionsAsync(context.CancellationToken);
        string root = Path.Combine(defaults.DataDirectoryBase, $"failure-{Guid.NewGuid():N}");
        var options = new PostgresTestClusterOptions
        {
            Installation = defaults.Installation,
            SharedDirectory = defaults.SharedDirectory,
            DataDirectoryBase = root,
            LogDirectory = defaults.LogDirectory,
            PostgreSqlConfiguration = ["ankus_invalid_configuration = 'invalid'"],
        };
        try
        {
            InvalidOperationException error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                PostgresTestCluster.StartAsync(options, context.CancellationToken));

            Assert.Contains("ankus_invalid_configuration", error.Message);
            Assert.IsEmpty(Directory.EnumerateFileSystemEntries(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private async Task<long> ReadProbeCountAsync()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM tests.rollback_probe", connection);
        return Assert.IsInstanceOfType<long>(await command.ExecuteScalarAsync(context.CancellationToken));
    }
}
