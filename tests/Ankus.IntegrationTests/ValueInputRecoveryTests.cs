using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies valid input and required abort behavior where transaction callbacks forbid independent rollback.
/// </summary>
/// <param name="context">The test's cancellation and diagnostic context.</param>
[TestClass]
public sealed class ValueInputRecoveryTests(TestContext context)
{
    /// <summary>
    /// Successful TryParse remains usable, and a caught input ERROR still rejects the transaction.
    /// </summary>
    /// <param name="family">The native value family.</param>
    /// <param name="text">The independently expected valid value.</param>
    /// <param name="state">The expected native invalid-input SQLSTATE.</param>
    [TestMethod]
    [DataRow("numeric", "42", "22P02")]
    [DataRow("temporal", "2024-02-29", "22007")]
    [DataRow("network", "192.0.2.1", "22P02")]
    [DataRow("geometry", "(1,2)", "22P02")]
    [DataRow("range", "[1,3)", "22000")]
    public async Task CallbackInputSuccessAndFailurePreserveTransaction(string family, string text, string state)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand("CREATE TEMP TABLE callback_input_writes(value integer)", connection);
        await command.ExecuteNonQueryAsync(token);
        for (int index = 0; index < 2; index++)
        {
            bool invalid = index != 0;
            PostgresException? failure = null;
            await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
            {
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO callback_input_writes VALUES (42)";
                await command.ExecuteNonQueryAsync(token);
                command.CommandText = "SELECT datatype.value_input_callback_register($1,$2)";
                command.Parameters.AddWithValue(family);
                command.Parameters.AddWithValue(invalid);
                await command.ExecuteNonQueryAsync(token);
                if (invalid)
                {
                    failure = await Assert.ThrowsExactlyAsync<PostgresException>(() => transaction.CommitAsync(token));
                    Assert.AreEqual(state, failure.SqlState);
                }
                else
                {
                    await transaction.CommitAsync(token);
                }
            }

            command.Transaction = null;
            command.Parameters.Clear();
            command.CommandText = "SELECT datatype.value_input_callback_snapshot()";
            string[] observed = Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(token));
            Assert.AreSequenceEqual(invalid ? [state, failure!.MessageText, "caught"] : ["True", text, "returned"], observed);
            command.CommandText = "SELECT sum(value) FROM callback_input_writes";
            Assert.AreEqual(42L, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT pg_backend_pid()";
            Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
        }
    }
}

public sealed partial class GucParallelTests
{
    /// <summary>
    /// Actual workers parse valid values on every supported major and recover invalid input only when rollback is supported.
    /// </summary>
    /// <param name="family">The native value family.</param>
    /// <param name="text">The independently expected valid text.</param>
    /// <param name="state">The invalid-input SQLSTATE where parallel rollback is unavailable.</param>
    [TestMethod]
    [DataRow("numeric", "42", "22P02")]
    [DataRow("temporal", "2024-02-29", "22007")]
    [DataRow("network", "192.0.2.1", "22P02")]
    [DataRow("geometry", "(1,2)", "22P02")]
    [DataRow("range", "[1,3)", "22000")]
    public Task ParallelInputPreservesValuesAndRecovery(string family, string text, string state)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ParallelInputPreservesValuesAndRecovery), async (connection, transaction, token) =>
        {
            await PrepareInputAsync(connection, transaction, token);
            await AssertWorkerPlanAsync(connection, transaction, "datatype.guc_parallel_snapshot(value % 2)", token);
            for (int index = 0; index < 2; index++)
            {
                bool invalid = index != 0;
                string expression = $"datatype.value_input_parallel(value % 2, '{family}', {(invalid ? "true" : "false")})";
                await transaction.SaveAsync("input_recovery", token);
                if (invalid && connection.PostgreSqlVersion.Major < 17)
                {
                    PostgresException failure = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
                        ReadGroupsAsync(connection, transaction, expression, token));
                    Assert.AreEqual(state, failure.SqlState);
                    await transaction.RollbackAsync("input_recovery", token);
                }
                else
                {
                    IReadOnlyList<(string?[] Snapshot, long Rows)> groups = await ReadGroupsAsync(connection, transaction, expression, token);
                    AssertWorkerRows(connection.ProcessID, groups, processIndex: 0);
                    foreach ((string?[] snapshot, _) in groups)
                    {
                        Assert.HasCount(3, snapshot);
                        Assert.AreEqual(invalid ? "False" : "True", snapshot[1]);
                        Assert.AreEqual(invalid ? string.Empty : text, snapshot[2]);
                    }
                }

                Assert.AreEqual(42, await ScalarAsync<int>(connection, transaction, "SELECT 42", token));
            }
        }, context.CancellationToken);
}
