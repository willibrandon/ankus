using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies scoped native SPI connections, session-bound plans, retention, and cleanup in PostgreSQL.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class SpiSessionTests(TestContext context)
{
    /// <summary>
    /// Verifies session results, nesting, recovery, cursors, and recursive callback isolation.
    /// </summary>
    /// <param name="expression">The backend operation.</param>
    /// <param name="expected">The expected text or SQL NULL.</param>
    [TestMethod]
    [DataRow("datatype.session_owned_result('café 🐘')", "café 🐘")]
    [DataRow("datatype.session_owned_result(NULL)", null)]
    [DataRow("datatype.session_nested()", "1|2:rejected|1")]
    [DataRow("datatype.session_recover()", "22012:42")]
    [DataRow("datatype.session_nested_recovery()", "22012:42")]
    [DataRow("datatype.session_replan()", "42")]
    [DataRow("datatype.session_writes()", "2|3|1,2,3,4,5")]
    [DataRow("datatype.session_managed_failure()", "42")]
    [DataRow("datatype.session_cursor(false)", "1,2,3")]
    [DataRow("datatype.session_cursor(true)", "1,2,3")]
    [DataRow("datatype.session_recursive()", "SPI sessions require their owning callback and innermost active session scope.:7|42")]
    [DataRow("datatype.session_tuple_cleanup()", "2:1:42")]
    public Task SessionOperationsPreserveScopeAndResults(string expression, string? expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SessionOperationsPreserveScopeAndResults),
            async (connection, transaction, token) =>
            {
                await using var count = new NpgsqlCommand(
                    "SELECT count(*) FROM pg_backend_memory_contexts WHERE name IN ('SPI Proc', 'SPI Plan')", connection, transaction);
                long before = Assert.IsInstanceOfType<long>(await count.ExecuteScalarAsync(token));
                await using var command = new NpgsqlCommand($"SELECT ({expression})::text", connection, transaction);
                Assert.AreEqual((object?)expected ?? DBNull.Value, await command.ExecuteScalarAsync(token));
                Assert.AreEqual(before, await count.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies all worker-thread attempts fail before changing the connection stack or borrowed plan.
    /// </summary>
    /// <param name="mode">The attempted worker operation.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public Task WorkerThreadCannotAccessSession(int mode)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(WorkerThreadCannotAccessSession),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.session_worker($1)", connection, transaction);
                command.Parameters.AddWithValue(mode);
                Assert.AreEqual("PostgreSQL APIs can only be used on the active PostgreSQL backend thread.|42",
                    await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies an escaped session or borrowed plan cannot access its freed native context.
    /// </summary>
    /// <param name="setup">The callback that stores an expired owner.</param>
    /// <param name="operation">The rejected operation.</param>
    [TestMethod]
    [DataRow("datatype.session_escape()", "datatype.session_escaped_value()")]
    [DataRow("datatype.session_cache_plan(false)", "datatype.session_cached_value(40)")]
    [DataRow("datatype.session_cache_plan(false)", "datatype.session_keep_expired()")]
    public async Task ExpiredSessionOwnershipIsRejected(string setup, string operation)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand($"SELECT {setup}", connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = $"SELECT {operation}";
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        Assert.AreEqual("38000", error.SqlState);
        Assert.Contains("disposed object", error.MessageText);
        command.CommandText = "SELECT datatype.session_dispose_plan(); SELECT datatype.session_dispose_plan()";
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT datatype.spi_int(42)";
        Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Verifies Keep transfers a borrowed plan into retained storage across callback and transaction boundaries.
    /// </summary>
    /// <param name="rollback">Whether to roll back the transaction that created the retained plan.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task KeptSessionPlanSurvivesTransactionEnd(bool rollback)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var count = new NpgsqlCommand("SELECT count(*) FROM pg_backend_memory_contexts WHERE name = 'SPI Plan'", connection);
        long before = Assert.IsInstanceOfType<long>(await count.ExecuteScalarAsync(token));
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await using var cache = new NpgsqlCommand("SELECT datatype.session_cache_plan(true)", connection, transaction);
            await cache.ExecuteNonQueryAsync(token);
            if (rollback)
            {
                await transaction.RollbackAsync(token);
            }
            else
            {
                await transaction.CommitAsync(token);
            }
        }

        Assert.AreEqual(before + 1, await count.ExecuteScalarAsync(token));
        await using var command = new NpgsqlCommand("SELECT datatype.session_cached_value(40)", connection);
        Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT datatype.session_cached_value(10)";
        Assert.AreEqual(12, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT datatype.session_dispose_plan()";
        await command.ExecuteNonQueryAsync(token);
        Assert.AreEqual(before, await count.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Verifies managed/native failures unwind the session's connection and borrowed plans before returning an error.
    /// </summary>
    /// <param name="mode">The failing operation.</param>
    /// <param name="state">The expected SQLSTATE.</param>
    [TestMethod]
    [DataRow(0, "22012")]
    [DataRow(2, "0A000")]
    [DataRow(3, "38000")]
    [DataRow(4, "38000")]
    [DataRow(5, "42703")]
    public async Task FailedCallbackClosesSessionAndPlans(int mode, string state)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var count = new NpgsqlCommand(
            "SELECT count(*) FROM pg_backend_memory_contexts WHERE name IN ('SPI Proc', 'SPI Plan')", connection);
        long before = Assert.IsInstanceOfType<long>(await count.ExecuteScalarAsync(token));
        await using var command = new NpgsqlCommand("SELECT datatype.session_fail($1)", connection);
        command.Parameters.AddWithValue(mode);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        Assert.AreEqual(state, error.SqlState);
        Assert.AreEqual(before, await count.ExecuteScalarAsync(token));
        command.Parameters.Clear();
        command.CommandText = "SELECT datatype.session_nested()";
        Assert.AreEqual("1|2:rejected|1", await command.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Verifies statement cancellation runs the callback's session cleanup and restores the backend connection stack.
    /// </summary>
    [TestMethod]
    public async Task CancellationClosesSessionAndPlans()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand("SET statement_timeout = '100ms'", connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT datatype.session_fail(1)";
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        Assert.AreEqual(PostgresErrorCodes.QueryCanceled, error.SqlState);
        command.CommandText = "SET statement_timeout = 0";
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT count(*) FROM pg_backend_memory_contexts WHERE name IN ('SPI Proc', 'SPI Plan')";
        Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT datatype.session_nested()";
        Assert.AreEqual("1|2:rejected|1", await command.ExecuteScalarAsync(token));
    }
}
