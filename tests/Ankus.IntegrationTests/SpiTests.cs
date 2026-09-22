using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Exercises managed-to-native SPI calls and PostgreSQL error recovery inside the calling backend.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class SpiTests(TestContext context)
{
    /// <summary>
    /// Verifies SPI statements mutate the invoking transaction and return the final statement's row count.
    /// </summary>
    [TestMethod]
    public Task ExecuteMutatesCallingTransaction()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ExecuteMutatesCallingTransaction),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.execute_sql($1)", connection, transaction);
                command.Parameters.AddWithValue("CREATE TEMP TABLE spi_values (value integer); INSERT INTO spi_values VALUES (10), (20)");
                Assert.AreEqual(2L, Assert.IsInstanceOfType<long>(await command.ExecuteScalarAsync(token)));

                command.Parameters[0].Value = "UPDATE spi_values SET value = value + 1 WHERE value = 10";
                Assert.AreEqual(1L, Assert.IsInstanceOfType<long>(await command.ExecuteScalarAsync(token)));

                await using var read = new NpgsqlCommand("SELECT array_agg(value ORDER BY value) FROM spi_values", connection, transaction);
                Assert.AreSequenceEqual<int>([11, 20], Assert.IsInstanceOfType<int[]>(await read.ExecuteScalarAsync(token)));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies a caught SQL error rolls back the entire SPI call while leaving the enclosing transaction usable.
    /// </summary>
    [TestMethod]
    public Task CaughtErrorRollsBackSpiCallAndAllowsRecovery()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(CaughtErrorRollsBackSpiCallAndAllowsRecovery),
            async (connection, transaction, token) =>
            {
                await using (var setup = new NpgsqlCommand("CREATE TEMP TABLE spi_rollback (value integer)", connection, transaction))
                {
                    await setup.ExecuteNonQueryAsync(token);
                }

                await using (var fail = new NpgsqlCommand("SELECT datatype.catch_sql_error($1)", connection, transaction))
                {
                    fail.Parameters.AddWithValue("INSERT INTO spi_rollback VALUES (99); SELECT 1 / 0");
                    Assert.AreEqual("22012:1", await fail.ExecuteScalarAsync(token));
                }

                await using var count = new NpgsqlCommand("SELECT count(*) FROM spi_rollback", connection, transaction);
                Assert.AreEqual(0L, await count.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies native errors propagate through a managed exception and execute managed finally before returning to PostgreSQL.
    /// </summary>
    [TestMethod]
    public async Task NativeErrorPreservesDiagnosticsAndRunsManagedFinally()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int backend = connection.ProcessID;
        await using var counter = new NpgsqlCommand("SELECT datatype.spi_finally_count()", connection);
        int before = Assert.IsInstanceOfType<int>(await counter.ExecuteScalarAsync(context.CancellationToken));
        await using var command = new NpgsqlCommand("SELECT datatype.execute_sql($1)", connection);
        command.Parameters.AddWithValue("""
            DO $$ BEGIN
                RAISE EXCEPTION USING ERRCODE = '23505', MESSAGE = 'native café', DETAIL = 'duplicate key', HINT = 'choose another';
            END $$
            """);

        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(
            () => command.ExecuteScalarAsync(context.CancellationToken));
        Assert.AreEqual("23505", error.SqlState);
        Assert.AreEqual("native café", error.MessageText);
        Assert.AreEqual("duplicate key", error.Detail);
        Assert.AreEqual("choose another", error.Hint);
        Assert.AreEqual(before + 1, await counter.ExecuteScalarAsync(context.CancellationToken));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Verifies nested native-to-managed dispatch restores the enclosing callback's SPI binding after success and failure.
    /// </summary>
    /// <param name="sql">SQL that recursively invokes an Ankus extension.</param>
    [TestMethod]
    [DataRow("SELECT datatype.execute_sql('SELECT public.add(40, 2)')")]
    [DataRow("SELECT datatype.catch_sql_error('SELECT public.add(2147483647, 1)')")]
    public Task RecursiveDispatchRestoresSpiContext(string sql)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RecursiveDispatchRestoresSpiContext),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.execute_sql_twice($1)", connection, transaction);
                command.Parameters.AddWithValue(sql);
                Assert.AreEqual(1L, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies a reentrant managed exception crosses only native error guards before returning to the enclosing managed catch.
    /// </summary>
    [TestMethod]
    public Task RecursiveManagedErrorCanBeCaughtByOuterFunction()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RecursiveManagedErrorCanBeCaughtByOuterFunction),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.catch_sql_error($1)", connection, transaction);
                command.Parameters.AddWithValue("SELECT datatype.execute_sql('SELECT public.add(2147483647, 1)')");
                Assert.AreEqual("38000:1", await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies PostgreSQL cancellation unwinds managed cleanup and preserves the backend for subsequent calls.
    /// </summary>
    [TestMethod]
    public async Task StatementTimeoutUnwindsManagedFinally()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using var counter = new NpgsqlCommand("SELECT datatype.spi_finally_count()", connection);
        int before = Assert.IsInstanceOfType<int>(await counter.ExecuteScalarAsync(context.CancellationToken));
        await using (var configure = new NpgsqlCommand("SET statement_timeout = '100ms'", connection))
        {
            await configure.ExecuteNonQueryAsync(context.CancellationToken);
        }

        await using var command = new NpgsqlCommand("SELECT datatype.execute_sql('SELECT pg_sleep(5)')", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(
            () => command.ExecuteScalarAsync(context.CancellationToken));
        Assert.AreEqual(PostgresErrorCodes.QueryCanceled, error.SqlState);

        await using (var configure = new NpgsqlCommand("SET statement_timeout = 0", connection))
        {
            await configure.ExecuteNonQueryAsync(context.CancellationToken);
        }

        Assert.AreEqual(before + 1, await counter.ExecuteScalarAsync(context.CancellationToken));
    }

    /// <summary>
    /// Verifies extension-authored errors preserve SQLSTATE and Unicode diagnostics.
    /// </summary>
    [TestMethod]
    public async Task ManagedErrorPreservesExplicitDiagnostics()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT datatype.report_error('22023', 'bad 🐘', 'detail café', 'retry')", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(
            () => command.ExecuteScalarAsync(context.CancellationToken));
        Assert.AreEqual("22023", error.SqlState);
        Assert.AreEqual("bad 🐘", error.MessageText);
        Assert.AreEqual("detail café", error.Detail);
        Assert.AreEqual("retry", error.Hint);
    }

    /// <summary>
    /// Verifies a worker thread cannot enter PostgreSQL's thread-unsafe SPI state.
    /// </summary>
    [TestMethod]
    public Task BackgroundThreadCannotCallPostgres()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(BackgroundThreadCannotCallPostgres),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.background_sql()", connection, transaction);
                Assert.AreEqual("PostgreSQL APIs can only be used on the active PostgreSQL backend thread.",
                    await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);
}
