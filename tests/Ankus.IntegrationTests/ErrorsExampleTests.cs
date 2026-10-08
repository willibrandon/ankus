using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes the ported pgrx errors sample through its published Native AOT extension.
/// </summary>
/// <param name="context">The current cancellation and diagnostic context.</param>
[TestClass]
public sealed class ErrorsExampleTests(TestContext context)
{
    /// <summary>
    /// Preserves ordinary values, message routing, managed failures, native diagnostics, and same-backend recovery.
    /// </summary>
    [TestMethod]
    public Task ErrorsSamplePreservesDiagnosticsAndRecovery()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ErrorsSamplePreservesDiagnosticsAndRecovery),
            async (connection, transaction, token) =>
            {
                await ExecuteAsync(connection, transaction,
                    "CREATE EXTENSION ankus_errors; SET LOCAL client_min_messages = info", token);
                Assert.AreEqual(6L, await ScalarAsync<long>(connection, transaction,
                    "SELECT errors.array_with_null_and_panic(ARRAY[1,2,3])", token));

                var notices = new List<PostgresNotice>();
                connection.Notice += (_, args) => notices.Add(args.Notice);
                await using (var reports = new NpgsqlCommand(
                    "SELECT errors.raise_pg_info($1), errors.raise_pg_warning($2)", connection, transaction))
                {
                    reports.Parameters.AddWithValue("information café %s");
                    reports.Parameters.AddWithValue("warning 🐘 %n");
                    await reports.ExecuteNonQueryAsync(token);
                }

                Assert.AreSequenceEqual<string>(["INFO:information café %s:00000", "WARNING:warning 🐘 %n:01000"],
                    notices.Select(static notice => $"{notice.InvariantSeverity}:{notice.MessageText}:{notice.SqlState}"));

                await AssertFailureAsync(connection, transaction,
                    "SELECT errors.array_with_null_and_panic(ARRAY[1,NULL,3])", "38000",
                    "NULL elements in input array are not supported.", token);
                await AssertFailureAsync(connection, transaction,
                    "SELECT errors.cause_unwrap_panic()", "38000", "The nullable value is absent.", token);
                await AssertFailureAsync(connection, transaction,
                    "SELECT errors.throw_managed_exception('managed café')", "38000", "managed café", token);
                await AssertFailureAsync(connection, transaction,
                    "SELECT errors.cause_pg_error()", "42602", "invalid name syntax", token);

                Assert.AreSequenceEqual<string>(
                [
                    "array_with_null_and_panic:1007:20", "cause_pg_error::2278", "cause_unwrap_panic::2278",
                    "raise_pg_info:25:2278", "raise_pg_warning:25:2278", "throw_managed_exception:25:2278",
                    "throw_pg_error:25:2278", "throw_pg_fatal:25:2278", "throw_pg_panic:25:2278",
                ], await ScalarAsync<string[]>(connection, transaction, """
                    SELECT array_agg(p.proname||':'||p.proargtypes::text||':'||p.prorettype::text ORDER BY p.proname COLLATE "C")
                    FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='errors'
                    """, token));
                Assert.IsTrue(await ScalarAsync<bool>(connection, transaction, """
                    SELECT NOT extrelocatable AND EXISTS(
                        SELECT FROM pg_depend d JOIN pg_extension owned ON owned.oid=d.refobjid
                        WHERE d.classid='pg_namespace'::regclass AND d.objid='errors'::regnamespace
                            AND owned.extname='ankus_errors' AND d.deptype='e')
                    FROM pg_extension WHERE extname='ankus_errors'
                    """, token));
                Assert.AreEqual(connection.ProcessID,
                    await ScalarAsync<int>(connection, transaction, "SELECT pg_backend_pid()", token));
            }, context.CancellationToken);

    /// <summary>
    /// ERROR remains an owned diagnostic, FATAL ends one backend, and PANIC completes crash recovery.
    /// </summary>
    /// <param name="function">The terminal sample function.</param>
    /// <param name="severity">The expected server severity.</param>
    /// <param name="crashRecovery">Whether PostgreSQL must recover the whole cluster.</param>
    [TestMethod]
    [DataRow("throw_pg_fatal", "FATAL", false)]
    [DataRow("throw_pg_panic", "PANIC", true)]
    public async Task ErrorsSamplePreservesTerminalBehavior(string function, string severity, bool crashRecovery)
    {
        CancellationToken token = context.CancellationToken;
        using IDisposable recoverySlot = await CrashRecovery.ReserveAsync(token);
        PostgresTestClusterOptions options = await IntegrationEnvironment.CreateOptionsAsync(token);
        await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(options, token);
        await using (NpgsqlConnection setup = await cluster.OpenConnectionAsync(token))
        {
            await using var install = new NpgsqlCommand("CREATE EXTENSION ankus_errors", setup);
            await install.ExecuteNonQueryAsync(token);
        }

        string errorMarker = "sample-error-" + Guid.NewGuid().ToString("N");
        await using (NpgsqlConnection errorConnection = await cluster.OpenConnectionAsync(token))
        {
            await using var errorCommand = new NpgsqlCommand("SELECT errors.throw_pg_error($1)", errorConnection);
            errorCommand.Parameters.AddWithValue(errorMarker);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
                errorCommand.ExecuteScalarAsync(token));
            Assert.AreEqual("XX000", error.SqlState);
            Assert.AreEqual(errorMarker, error.MessageText);
        }

        string marker = "sample-" + severity.ToLowerInvariant() + '-' + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        await using (var identify = new NpgsqlCommand(
            $"SET application_name = '{marker}'; SET log_error_verbosity = verbose", connection))
        {
            await identify.ExecuteNonQueryAsync(token);
        }

        await using (var command = new NpgsqlCommand($"SELECT errors.{function}($1)", connection))
        {
            command.Parameters.AddWithValue(marker);
            _ = await Assert.ThrowsAsync<NpgsqlException>(() => command.ExecuteScalarAsync(token));
        }

        Assert.AreEqual(System.Data.ConnectionState.Closed, connection.State);
        if (crashRecovery)
        {
            using CancellationTokenSource recovery = CrashRecovery.CreateDeadline(token);
            _ = await CrashRecovery.WaitAsync(cluster, recovery.Token);

            await AssertRecoveredAsync(cluster, recovery.Token);
        }
        else
        {
            await AssertRecoveredAsync(cluster, token);
        }

        Assert.Contains(severity + ":  XX000: " + marker, cluster.ReadServerLog());
    }

    private static async Task AssertFailureAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string sql, string sqlState, string message, CancellationToken token)
    {
        string savepoint = "errors_sample_failure";
        await transaction.SaveAsync(savepoint, token);
        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual(sqlState, error.SqlState);
            Assert.AreEqual(message, error.MessageText);
        }
        finally
        {
            await transaction.RollbackAsync(savepoint, CancellationToken.None);
        }
    }

    private static async Task AssertRecoveredAsync(PostgresTestCluster cluster, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await using NpgsqlConnection recovered = await cluster.OpenConnectionAsync(token);
                await using var query = new NpgsqlCommand(
                    "SELECT errors.array_with_null_and_panic(ARRAY[20,22])", recovered);
                Assert.AreEqual(42L, await query.ExecuteScalarAsync(token));
                return;
            }
            catch (NpgsqlException failure) when (failure is not PostgresException or PostgresException
            {
                SqlState: PostgresErrorCodes.CannotConnectNow or
                    PostgresErrorCodes.AdminShutdown or PostgresErrorCodes.CrashShutdown
            })
            {
                await Task.Delay(50, token);
            }
        }
    }

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
