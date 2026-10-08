using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes the ported pgrx pgthread sample through its published Native AOT extension.
/// </summary>
/// <param name="context">The current cancellation and diagnostic context.</param>
[TestClass]
public sealed class ThreadsExampleTests(TestContext context)
{
    /// <summary>
    /// Statements whose text differs from <c>SELECT 1;</c>.
    /// </summary>
    private static readonly string[] s_accepted = ["SELECT 1", "SELECT 2;", " SELECT 1;", "select 1;"];

    /// <summary>
    /// Mirrors pgrx's greeting test, then shows that worker threads compute but cannot call PostgreSQL.
    /// </summary>
    [TestMethod]
    public Task ThreadsSampleKeepsPostgresOnTheBackendThread()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ThreadsSampleKeepsPostgresOnTheBackendThread),
            async (connection, transaction, token) =>
            {
                await ExecuteAsync(connection, transaction, "CREATE EXTENSION ankus_threads", token);
                await ErrorTrap.InstallAsync(connection, transaction, token);
                Assert.AreEqual("Hello, pgthread", await ScalarAsync<string>(connection, transaction, "SELECT hello_pgthread()", token));

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    Assert.AreEqual("XX000: thread SPI work failed: PostgreSQL APIs can only be used on the active PostgreSQL backend thread.",
                        await ErrorTrap.RunAsync(connection, transaction, "SELECT start_thread()", token));
                    Assert.AreEqual(42, await ScalarAsync<int>(connection, transaction, "SELECT 6 * 7", token));
                }

                Assert.AreEqual(5000050000L, await ScalarAsync<long>(connection, transaction,
                    "SELECT thread_sum(array_agg(value)) FROM generate_series(1::bigint, 100000) AS value", token));
                Assert.AreEqual(0L, await ScalarAsync<long>(connection, transaction, "SELECT thread_sum('{}')", token));
                Assert.AreEqual(long.MaxValue, await ScalarAsync<long>(connection, transaction,
                    "SELECT thread_sum(ARRAY[9223372036854775807, 1, -1])", token));
                Assert.AreEqual(long.MinValue, await ScalarAsync<long>(connection, transaction,
                    "SELECT thread_sum(ARRAY[-9223372036854775807, 1, -1, -1])", token));
                await transaction.SaveAsync("threads_sample_overflow", token);
                await using (var overflow = new NpgsqlCommand("SELECT thread_sum(ARRAY[9223372036854775807, 1])", connection, transaction))
                {
                    PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => overflow.ExecuteScalarAsync(token));
                    Assert.AreEqual("38000", error.SqlState);
                    Assert.AreEqual("Arithmetic operation resulted in an overflow.", error.MessageText);
                }

                await transaction.RollbackAsync("threads_sample_overflow", token);
                Assert.AreEqual(connection.ProcessID, await ScalarAsync<int>(connection, transaction, "SELECT pg_backend_pid()", token));
            }, context.CancellationToken);

    /// <summary>
    /// The parse-analysis hook rejects exactly <c>SELECT 1;</c>, including through SPI, and chains every other statement.
    /// </summary>
    [TestMethod]
    public Task ThreadsSampleHookRejectsExactQueryText()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ThreadsSampleHookRejectsExactQueryText),
            async (connection, transaction, token) =>
            {
                await ExecuteAsync(connection, transaction, "CREATE EXTENSION ankus_threads", token);
                await ErrorTrap.InstallAsync(connection, transaction, token);
                Assert.AreEqual("XX000: oh no", await ErrorTrap.RunAsync(connection, transaction, "SELECT 1;", token));
                foreach (string statement in s_accepted)
                {
                    Assert.AreEqual("completed", await ErrorTrap.RunAsync(connection, transaction, statement, token));
                }

                // Loading the library again keeps the single installed hook; a self-chained hook could not return.
                await ExecuteAsync(connection, transaction, "LOAD 'Ankus.Examples.Threads'", token);
                Assert.AreEqual("XX000: oh no", await ErrorTrap.RunAsync(connection, transaction, "SELECT 1;", token));
                Assert.AreEqual(42, await ScalarAsync<int>(connection, transaction, "SELECT 6 * 7", token));
                Assert.AreEqual(connection.ProcessID, await ScalarAsync<int>(connection, transaction, "SELECT pg_backend_pid()", token));
            }, context.CancellationToken);

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(token);
    }
}
