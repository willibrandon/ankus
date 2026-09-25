using System.Net;
using System.Net.Sockets;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Forces real TCP reservation handoff races and checks PostgreSQL startup, cleanup and failure boundaries.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class ClusterPortHandoffTests(TestContext context)
{
    /// <summary>
    /// A stolen port produces a fresh isolated cluster while preserving the competing listener and failed startup log.
    /// </summary>
    [TestMethod]
    public async Task PortCollisionRetriesAndPreservesCompetingListener()
    {
        PostgresTestClusterOptions options = await IntegrationEnvironment.CreateOptionsAsync(context.CancellationToken);
        var attempts = new List<PostgresTestCluster>();
        TcpListener? competitor = null;
        try
        {
            await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(options, attempt =>
            {
                attempts.Add(attempt);
                if (attempts.Count == 1) { competitor = Occupy(attempt.Port); }
            }, context.CancellationToken);

            Assert.HasCount(2, attempts);
            Assert.AreSame(cluster, attempts[1]);
            Assert.AreNotEqual(attempts[0].Port, cluster.Port);
            Assert.IsFalse(Directory.Exists(attempts[0].DataDirectory));
            Assert.IsFalse(Directory.Exists(attempts[0].SocketDirectory));
            Assert.Contains("could not create any TCP/IP sockets", attempts[0].ReadServerLog());
            Assert.IsNotNull(competitor);
            await AssertListenerAliveAsync(competitor);
            await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(context.CancellationToken);
            await using var command = new NpgsqlCommand("SELECT current_setting('port')::integer", connection);
            Assert.AreEqual(cluster.Port, await command.ExecuteScalarAsync(context.CancellationToken));
            command.CommandText = "SELECT current_setting('data_directory')";
            string dataDirectory = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(context.CancellationToken));
            Assert.AreEqual(cluster.DataDirectory, Path.GetFullPath(dataDirectory));
            command.CommandText = "SELECT 42";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(context.CancellationToken));
        }
        finally
        {
            competitor?.Stop();
        }
    }

    /// <summary>
    /// Persistent collisions stop after three attempts, clean every owned cluster and retain the final native error.
    /// </summary>
    [TestMethod]
    public async Task RepeatedPortCollisionsAreBoundedAndCleanEveryAttempt()
    {
        PostgresTestClusterOptions options = await IntegrationEnvironment.CreateOptionsAsync(context.CancellationToken);
        var attempts = new List<PostgresTestCluster>();
        var competitors = new List<TcpListener>();
        try
        {
            InvalidOperationException error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                PostgresTestCluster.StartAsync(options, attempt =>
                {
                    attempts.Add(attempt);
                    competitors.Add(Occupy(attempt.Port));
                }, context.CancellationToken));

            Assert.HasCount(3, attempts);
            Assert.HasCount(3, attempts.Select(static attempt => attempt.Port).Distinct());
            Assert.Contains("could not create any TCP/IP sockets", error.Message);
            Assert.Contains(attempts[^1].LogFilePath, error.Message);
            Assert.IsInstanceOfType<InvalidOperationException>(error.InnerException);
            foreach (PostgresTestCluster attempt in attempts)
            {
                Assert.IsFalse(Directory.Exists(attempt.DataDirectory));
                Assert.IsFalse(Directory.Exists(attempt.SocketDirectory));
                Assert.Contains($"Is another postmaster already running on port {attempt.Port}?", attempt.ReadServerLog());
            }

            foreach (TcpListener competitor in competitors) { await AssertListenerAliveAsync(competitor); }
        }
        finally
        {
            foreach (TcpListener competitor in competitors) { competitor.Stop(); }
        }
    }

    /// <summary>
    /// An unrelated failure after a collision is not retried using stale diagnostics from the prior attempt.
    /// </summary>
    [TestMethod]
    public async Task ConfigurationFailureAfterCollisionStopsImmediately()
    {
        PostgresTestClusterOptions options = await IntegrationEnvironment.CreateOptionsAsync(context.CancellationToken);
        var attempts = new List<PostgresTestCluster>();
        TcpListener? competitor = null;
        try
        {
            InvalidOperationException error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                PostgresTestCluster.StartAsync(options, attempt =>
                {
                    attempts.Add(attempt);
                    if (attempts.Count == 1)
                    {
                        competitor = Occupy(attempt.Port);
                    }
                    else
                    {
                        File.AppendAllText(Path.Combine(attempt.DataDirectory, "postgresql.auto.conf"), "ankus_invalid_configuration = 'invalid'\n");
                    }
                }, context.CancellationToken));

            Assert.HasCount(2, attempts);
            Assert.Contains("ankus_invalid_configuration", error.Message);
            Assert.DoesNotContain("could not create any TCP/IP sockets", error.Message);
            foreach (PostgresTestCluster attempt in attempts)
            {
                Assert.IsFalse(Directory.Exists(attempt.DataDirectory));
                Assert.IsFalse(Directory.Exists(attempt.SocketDirectory));
            }

            Assert.IsNotNull(competitor);
            await AssertListenerAliveAsync(competitor);
        }
        finally
        {
            competitor?.Stop();
        }
    }

    /// <summary>
    /// Cancellation during the handoff cleans initialized storage without starting a postmaster or retrying.
    /// </summary>
    [TestMethod]
    public async Task CancellationAtHandoffDoesNotStartOrRetryPostgres()
    {
        PostgresTestClusterOptions options = await IntegrationEnvironment.CreateOptionsAsync(context.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var attempts = new List<PostgresTestCluster>();
        await Assert.ThrowsAsync<OperationCanceledException>(() => PostgresTestCluster.StartAsync(options, attempt =>
        {
            attempts.Add(attempt);
            cancellation.Cancel();
        }, cancellation.Token));

        PostgresTestCluster failed = Assert.ContainsSingle(attempts);
        Assert.IsFalse(Directory.Exists(failed.DataDirectory));
        Assert.IsFalse(Directory.Exists(failed.SocketDirectory));
        Assert.IsFalse(File.Exists(failed.LogFilePath));
    }

    private static TcpListener Occupy(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
        listener.Start();
        return listener;
    }

    private async Task AssertListenerAliveAsync(TcpListener listener)
    {
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, context.CancellationToken);
        using TcpClient accepted = await listener.AcceptTcpClientAsync(context.CancellationToken);
        Assert.IsTrue(client.Connected);
        Assert.IsTrue(accepted.Connected);
    }
}
