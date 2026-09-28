using System.Globalization;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class GucParallelTests
{
    /// <summary>
    /// Actual workers retain plans after nested sessions close and exhaust read-only cursors with exact values.
    /// </summary>
    [TestMethod]
    public Task ParallelSqlSessionsRetainPlansAndCursorValues()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ParallelSqlSessionsRetainPlansAndCursorValues),
            async (connection, transaction, token) =>
            {
                await PrepareInputAsync(connection, transaction, token);
                const string Expression = "datatype.parallel_spi_session(value % 2)";
                await AssertWorkerPlanAsync(connection, transaction, Expression, token);
                IReadOnlyList<(string?[] Snapshot, long Rows)> groups = await ReadGroupsAsync(connection, transaction, Expression, token);
                AssertWorkerRows(connection.ProcessID, groups, processIndex: 0);
                foreach ((string?[] snapshot, _) in groups)
                {
                    Assert.HasCount(6, snapshot);
                    int value = int.Parse(snapshot[1]!, CultureInfo.InvariantCulture);
                    Assert.IsInRange(0, 1, value);
                    Assert.AreEqual((value + 10).ToString(CultureInfo.InvariantCulture), snapshot[2]);
                    Assert.AreEqual((value + 40).ToString(CultureInfo.InvariantCulture), snapshot[3]);
                    Assert.AreEqual((value + 41).ToString(CultureInfo.InvariantCulture), snapshot[4]);
                    Assert.AreEqual("0", snapshot[5]);
                }
            }, context.CancellationToken);

    /// <summary>
    /// A caught worker failure preserves the first error where savepoints are unavailable and always unwinds managed cleanup.
    /// </summary>
    /// <param name="mode">Division, raw provider lookup, or nested full diagnostic transport.</param>
    /// <param name="replace">Whether managed code attempts to replace the original diagnostic.</param>
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(0, true)]
    [DataRow(1, false)]
    [DataRow(1, true)]
    [DataRow(2, false)]
    [DataRow(2, true)]
    public Task ParallelCaughtNativeFailureUnwindsAndLeaderRecovers(int mode, bool replace)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ParallelCaughtNativeFailureUnwindsAndLeaderRecovers),
            async (connection, transaction, token) =>
            {
                await PrepareInputAsync(connection, transaction, token);
                await AssertWorkerPlanAsync(connection, transaction, "datatype.guc_parallel_snapshot(value % 2)", token);
                int backend = connection.ProcessID;
                bool unrecoverable = connection.PostgreSqlVersion.Major < 17;
                string identity = Guid.NewGuid().ToString("N");
                string directory = PostgresFixture.Cluster.DataDirectory;
                string originalState = mode switch
                {
                    0 => "22012",
                    1 => "42704",
                    _ => "P7802",
                };
                string originalMessage = mode switch
                {
                    0 => "division by zero",
                    1 => "ExtensibleNodeMethods \"ankus_missing_parallel_provider\" was not registered",
                    _ => new string('x', 4096) + " owned 🐘",
                };
                string? detail = mode == 2 ? "Worker détail" : null;
                string? hint = mode == 2 ? "Retain the original diagnostic." : null;
                await transaction.SaveAsync("parallel_failure", token);
                await using var command = new NpgsqlCommand("""
                    SELECT sum(datatype.parallel_spi_failure(value, $1, $2, $3, $4))
                    FROM guc_parallel_input
                    """, connection, transaction);
                command.Parameters.AddWithValue(directory);
                command.Parameters.AddWithValue(identity);
                command.Parameters.AddWithValue(mode);
                command.Parameters.AddWithValue(replace);
                try
                {
                    if (unrecoverable || replace)
                    {
                        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
                        Assert.AreEqual(unrecoverable ? originalState : "P7801", error.SqlState);
                        Assert.AreEqual(unrecoverable ? originalMessage : "Managed replacement after parallel failure.", error.MessageText);
                        Assert.AreEqual(unrecoverable ? detail : null, error.Detail);
                        Assert.AreEqual(unrecoverable ? hint : null, error.Hint);
                        Assert.Contains("parallel worker", error.Where ?? string.Empty);
                    }
                    else
                    {
                        Assert.AreEqual(450015000L, Assert.IsInstanceOfType<long>(await command.ExecuteScalarAsync(token)));
                    }

                    await transaction.RollbackAsync("parallel_failure", token);
                    string[] receipts = Directory.GetFiles(directory, $"ankus-parallel-recovery-{identity}-*.done");
                    Assert.IsNotEmpty(receipts, "Workers must publish receipts from their managed finally blocks.");
                    foreach (string receipt in receipts)
                    {
                        int worker = int.Parse(Path.GetFileNameWithoutExtension(receipt).Split('-')[^1], CultureInfo.InvariantCulture);
                        Assert.AreNotEqual(backend, worker);
                        string[] observations = await File.ReadAllLinesAsync(receipt, token);
                        Assert.HasCount(14, observations);
                        Assert.AreEqual(originalState, observations[0]);
                        Assert.AreEqual(originalMessage, observations[1]);
                        Assert.AreEqual(detail ?? "<null>", observations[2]);
                        Assert.AreEqual(hint ?? "<null>", observations[3]);
                        if (unrecoverable)
                        {
                            Assert.AreSequenceEqual(observations.Take(4), observations.Skip(4).Take(4));
                            Assert.AreSequenceEqual(observations.Take(4), observations.Skip(8).Take(4));
                        }
                        else
                        {
                            Assert.AreSequenceEqual<string>(["42", "<success>", "<success>", "<success>"], observations.Skip(4).Take(4));
                            Assert.AreSequenceEqual<string>(["1", "<success>", "<success>", "<success>"], observations.Skip(8).Take(4));
                        }

                        Assert.AreEqual("disposed:1", observations[12]);
                        Assert.AreEqual("finally", observations[13]);
                    }

                    Assert.AreEqual(backend, connection.ProcessID);
                    Assert.AreEqual(42, await ScalarAsync<int>(connection, transaction, "SELECT 42", token));
                    IReadOnlyList<(string?[] Snapshot, long Rows)> recovered = await ReadGroupsAsync(connection, transaction,
                        "datatype.guc_parallel_snapshot(value % 2)", token);
                    AssertWorkerRows(backend, recovered, processIndex: 1);
                }
                finally
                {
                    foreach (string receipt in Directory.GetFiles(directory, $"ankus-parallel-recovery-{identity}-*.done"))
                    {
                        File.Delete(receipt);
                    }
                }
            }, context.CancellationToken);

    /// <summary>
    /// Native rollback of an enclosing subtransaction clears an inner parallel failure before the next managed SQL call.
    /// </summary>
    [TestMethod]
    public Task EnclosingRecoveryScopeRestoresParallelState()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(EnclosingRecoveryScopeRestoresParallelState),
            async (connection, transaction, token) =>
            {
                string outcome = connection.PostgreSqlVersion.Major < 17 ? "rolled-back" : "committed";
                Assert.AreEqual($"{outcome}|False|42", await ScalarAsync<string>(connection, transaction,
                    "SELECT datatype.parallel_scope_recovery()", token));
                Assert.AreEqual(42, await ScalarAsync<int>(connection, transaction, "SELECT 42", token));
            }, context.CancellationToken);
}
