using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies SPI cursor batching, ownership, portal invalidation, and guarded operations in PostgreSQL.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class SpiCursorTests(TestContext context)
{
    /// <summary>
    /// Verifies ordered batches, empty metadata, prepared-plan independence, and owned text/binary results.
    /// </summary>
    /// <param name="expression">The backend operation.</param>
    /// <param name="expected">The expected text, or SQL NULL.</param>
    [TestMethod]
    [DataRow("datatype.cursor_batches(7, 3, false)", "1,2,3,4,5,6,7|value:23")]
    [DataRow("datatype.cursor_batches(7, 3, true)", "1,2,3,4,5,6,7|value:23")]
    [DataRow("datatype.cursor_batches(0, 2, false)", "|value:23")]
    [DataRow("datatype.cursor_batches(0, 2, true)", "|value:23")]
    [DataRow("datatype.cursor_zero_fetch(true)", "0:1:1:1:2")]
    [DataRow("datatype.cursor_backward()", "1,2,3,4|3,2|3,4")]
    [DataRow("datatype.cursor_text('café 🐘')", "café 🐘")]
    [DataRow("datatype.cursor_text(NULL)", null)]
    [DataRow("encode(datatype.cursor_bytes(decode('00ff7f00', 'hex')), 'hex')", "00ff7f00")]
    [DataRow("datatype.cursor_bytes(NULL)", null)]
    [DataRow("datatype.cursor_recover()", "22012:42")]
    public Task CursorBatchesPreserveValuesAndOwnership(string expression, string? expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(CursorBatchesPreserveValuesAndOwnership),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand($"SELECT ({expression})::text", connection, transaction);
                Assert.AreEqual((object?)expected ?? DBNull.Value, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies detached cursors preserve position across independent extension callbacks and close on final disposal.
    /// </summary>
    [TestMethod]
    public Task DetachedCursorCanBeFoundAndContinuedByName()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DetachedCursorCanBeFoundAndContinuedByName),
            async (connection, transaction, token) =>
            {
                await using var open = new NpgsqlCommand(
                    "SELECT datatype.cursor_cache('SELECT generate_series(1, 5)')", connection, transaction);
                string name = Assert.IsInstanceOfType<string>(await open.ExecuteScalarAsync(token));
                await using var cached = new NpgsqlCommand("SELECT datatype.cursor_cached_rows(2)", connection, transaction);
                Assert.AreEqual("1,2", await cached.ExecuteScalarAsync(token));
                cached.CommandText = "SELECT datatype.cursor_detach_cached()";
                Assert.AreEqual(name, await cached.ExecuteScalarAsync(token));

                cached.CommandText = "SAVEPOINT detached_owner";
                await cached.ExecuteNonQueryAsync(token);
                cached.CommandText = "SELECT datatype.cursor_cached_rows(1)";
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => cached.ExecuteScalarAsync(token));
                Assert.AreEqual("38000", error.SqlState);
                Assert.Contains("disposed object", error.MessageText);
                cached.CommandText = "ROLLBACK TO SAVEPOINT detached_owner";
                await cached.ExecuteNonQueryAsync(token);

                await using var fetch = new NpgsqlCommand("SELECT datatype.cursor_fetch_named($1, 2, true)", connection, transaction);
                fetch.Parameters.AddWithValue(name);
                Assert.AreEqual("3,4", await fetch.ExecuteScalarAsync(token));
                fetch.CommandText = "SELECT datatype.cursor_fetch_named($1, 2, false)";
                Assert.AreEqual("5", await fetch.ExecuteScalarAsync(token));

                await using var exists = new NpgsqlCommand(
                    "SELECT EXISTS (SELECT FROM pg_cursors WHERE name = $1)", connection, transaction);
                exists.Parameters.AddWithValue(name);
                Assert.IsFalse(Assert.IsInstanceOfType<bool>(await exists.ExecuteScalarAsync(token)));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies stale objects cannot operate on a different portal subsequently created with the same name.
    /// </summary>
    [TestMethod]
    public async Task ReusedPortalNameDoesNotReviveStaleCursor()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await using (var setup = new NpgsqlCommand("""
            DECLARE "café cursor" CURSOR FOR SELECT 1;
            SELECT datatype.cursor_cache_named('café cursor');
            CLOSE "café cursor";
            DECLARE "café cursor" CURSOR FOR SELECT 99;
            SAVEPOINT stale_cursor
            """, connection, transaction))
        {
            await setup.ExecuteNonQueryAsync(token);
        }

        await using var stale = new NpgsqlCommand("SELECT datatype.cursor_cached_rows(1)", connection, transaction);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => stale.ExecuteScalarAsync(token));
        Assert.AreEqual(PostgresErrorCodes.InvalidCursorName, error.SqlState);
        await using (var recover = new NpgsqlCommand(
            "ROLLBACK TO SAVEPOINT stale_cursor; SELECT datatype.cursor_dispose_cached()", connection, transaction))
        {
            await recover.ExecuteNonQueryAsync(token);
        }

        await using var current = new NpgsqlCommand(
            "SELECT datatype.cursor_fetch_named('café cursor', 1, false)", connection, transaction);
        Assert.AreEqual("99", await current.ExecuteScalarAsync(token));
        await transaction.RollbackAsync(token);
    }

    /// <summary>
    /// Verifies portal destruction on commit or rollback invalidates managed identities before another callback uses them.
    /// </summary>
    /// <param name="rollback">Whether to roll back the creation transaction.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TransactionEndInvalidatesCursor(bool rollback)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await using var open = new NpgsqlCommand("SELECT datatype.cursor_cache('SELECT 42')", connection, transaction);
            Assert.IsInstanceOfType<string>(await open.ExecuteScalarAsync(token));
            if (rollback)
            {
                await transaction.RollbackAsync(token);
            }
            else
            {
                await transaction.CommitAsync(token);
            }
        }

        await using var fetch = new NpgsqlCommand("SELECT datatype.cursor_cached_rows(1)", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => fetch.ExecuteScalarAsync(token));
        Assert.AreEqual(PostgresErrorCodes.InvalidCursorName, error.SqlState);
        await using var dispose = new NpgsqlCommand("SELECT datatype.cursor_dispose_cached()", connection);
        await dispose.ExecuteNonQueryAsync(token);
        await dispose.ExecuteNonQueryAsync(token);
        fetch.CommandText = "SELECT datatype.spi_int(42)";
        Assert.AreEqual(42, await fetch.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Verifies explicit disposal removes the native portal and later fetches fail before entering native code.
    /// </summary>
    [TestMethod]
    public Task DisposedCursorClosesPortalAndRejectsFetch()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DisposedCursorClosesPortalAndRejectsFetch),
            async (connection, transaction, token) =>
            {
                await using var open = new NpgsqlCommand("SELECT datatype.cursor_cache('SELECT 42')", connection, transaction);
                string name = Assert.IsInstanceOfType<string>(await open.ExecuteScalarAsync(token));
                await using var exists = new NpgsqlCommand(
                    "SELECT EXISTS (SELECT FROM pg_cursors WHERE name = $1)", connection, transaction);
                exists.Parameters.AddWithValue(name);
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await exists.ExecuteScalarAsync(token)));
                await using (var dispose = new NpgsqlCommand(
                    "SELECT datatype.cursor_dispose_cached(); SELECT datatype.cursor_dispose_cached()", connection, transaction))
                {
                    await dispose.ExecuteNonQueryAsync(token);
                }

                Assert.IsFalse(Assert.IsInstanceOfType<bool>(await exists.ExecuteScalarAsync(token)));
                await using var fetch = new NpgsqlCommand("SELECT datatype.cursor_cached_rows(1)", connection, transaction);
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => fetch.ExecuteScalarAsync(token));
                Assert.AreEqual("38000", error.SqlState);
                Assert.Contains("disposed object", error.MessageText);
            }, context.CancellationToken);

    /// <summary>
    /// Verifies worker-thread fetch/dispose/detach attempts leave the backend's cursor usable and at its original position.
    /// </summary>
    /// <param name="mode">The attempted worker operation.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public Task WorkerThreadCannotUseCursor(int mode)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(WorkerThreadCannotUseCursor),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.cursor_worker($1)", connection, transaction);
                command.Parameters.AddWithValue(mode);
                Assert.AreEqual("PostgreSQL APIs can only be used on the active PostgreSQL backend thread.|42",
                    await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies recursive callbacks cannot dispose a cursor that is currently fetching their rows.
    /// </summary>
    [TestMethod]
    public Task ReentrantDisposalCannotInvalidateActiveFetch()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ReentrantDisposalCannotInvalidateActiveFetch),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("""
                    SELECT datatype.cursor_cache('SELECT datatype.cursor_reentrant_close(n) FROM generate_series(1, 3) s(n)')
                    """, connection, transaction);
                Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT datatype.cursor_cached_rows(3)";
                Assert.AreEqual("1,2,3", await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT datatype.cursor_dispose_cached()";
                await command.ExecuteNonQueryAsync(token);
            }, context.CancellationToken);

    /// <summary>
    /// Verifies a partial fetch from INSERT RETURNING preserves the command's complete write effects.
    /// </summary>
    [TestMethod]
    public Task PartialFetchDoesNotTruncateReturningWrites()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(PartialFetchDoesNotTruncateReturningWrites),
            async (connection, transaction, token) =>
            {
                await using (var setup = new NpgsqlCommand("CREATE TEMP TABLE cursor_writes (value int)", connection, transaction))
                {
                    await setup.ExecuteNonQueryAsync(token);
                }

                await using var fetch = new NpgsqlCommand("SELECT datatype.cursor_first_batch($1, false, 2)", connection, transaction);
                fetch.Parameters.AddWithValue("INSERT INTO cursor_writes SELECT generate_series(1, 5) RETURNING value");
                Assert.AreEqual("1,2", await fetch.ExecuteScalarAsync(token));
                await using var rows = new NpgsqlCommand(
                    "SELECT array_agg(value ORDER BY value) FROM cursor_writes", connection, transaction);
                Assert.AreSequenceEqual<int>([1, 2, 3, 4, 5], Assert.IsInstanceOfType<int[]>(await rows.ExecuteScalarAsync(token)));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies cursor lookup and invalid command errors leave PostgreSQL usable.
    /// </summary>
    /// <param name="expression">The failing operation.</param>
    /// <param name="state">The expected SQLSTATE.</param>
    [TestMethod]
    [DataRow("datatype.cursor_fetch_named('missing cursor', 1, false)", "34000")]
    [DataRow("datatype.cursor_first_batch('SELECT 1; SELECT 2', false, 1)", "42P11")]
    [DataRow("datatype.cursor_first_batch('SELECT 1', false, -1)", "38000")]
    [DataRow("datatype.cursor_zero_fetch(false)", "55000")]
    public async Task CursorErrorsPreserveBackend(string expression, string state)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand($"SELECT {expression}", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual(state, error.SqlState);
        command.CommandText = "SELECT datatype.cursor_first_batch('SELECT 42', false, 1)";
        Assert.AreEqual("42", await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Verifies rolling back a creation savepoint invalidates the cursor while preserving the outer transaction.
    /// </summary>
    [TestMethod]
    public Task SavepointRollbackInvalidatesCursor()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SavepointRollbackInvalidatesCursor),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SAVEPOINT cursor_creation", connection, transaction);
                await command.ExecuteNonQueryAsync(token);
                command.CommandText = "SELECT datatype.cursor_cache('SELECT 42')";
                string name = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
                command.CommandText = "ROLLBACK TO SAVEPOINT cursor_creation; SAVEPOINT cursor_fetch";
                await command.ExecuteNonQueryAsync(token);
                command.CommandText = "SELECT datatype.cursor_cached_rows(1)";
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
                Assert.AreEqual(PostgresErrorCodes.InvalidCursorName, error.SqlState);
                command.CommandText = "ROLLBACK TO SAVEPOINT cursor_fetch; SELECT datatype.cursor_dispose_cached()";
                await command.ExecuteNonQueryAsync(token);

                await using var exists = new NpgsqlCommand(
                    "SELECT EXISTS (SELECT FROM pg_cursors WHERE name = $1)", connection, transaction);
                exists.Parameters.AddWithValue(name);
                Assert.IsFalse(Assert.IsInstanceOfType<bool>(await exists.ExecuteScalarAsync(token)));
                command.CommandText = "SELECT datatype.spi_int(42)";
                Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies read-only cursor execution rejects data modification and preserves the table and backend.
    /// </summary>
    [TestMethod]
    public async Task ReadOnlyCursorRejectsWrites()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand("CREATE TEMP TABLE cursor_readonly (value int)", connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT datatype.cursor_first_batch('INSERT INTO cursor_readonly VALUES (1) RETURNING value', true, 1)";
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual(PostgresErrorCodes.FeatureNotSupported, error.SqlState);
        command.CommandText = "SELECT count(*) FROM cursor_readonly";
        Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Verifies cancellation during a cursor fetch runs managed disposal and releases the native portal.
    /// </summary>
    [TestMethod]
    public async Task CancellationClosesPortalAndPreservesBackend()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var count = new NpgsqlCommand("SELECT count(*) FROM pg_cursors", connection);
        long before = Assert.IsInstanceOfType<long>(await count.ExecuteScalarAsync(token));
        await using var command = new NpgsqlCommand("SET statement_timeout = '100ms'", connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT datatype.cursor_first_batch('SELECT 1 FROM pg_sleep(5)', false, 1)";
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual(PostgresErrorCodes.QueryCanceled, error.SqlState);
        command.CommandText = "SET statement_timeout = 0";
        await command.ExecuteNonQueryAsync(token);
        Assert.AreEqual(before, await count.ExecuteScalarAsync(token));
        command.CommandText = "SELECT datatype.cursor_first_batch('SELECT 42', false, 1)";
        Assert.AreEqual("42", await command.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Verifies an externally declared holdable portal retains its registered identity after commit.
    /// </summary>
    [TestMethod]
    public async Task ExistingHoldableCursorSurvivesCommit()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await using var setup = new NpgsqlCommand("""
                DECLARE held_cursor CURSOR WITH HOLD FOR SELECT generate_series(1, 3);
                SELECT datatype.cursor_cache_named('held_cursor')
                """, connection, transaction);
            Assert.AreEqual("held_cursor", await setup.ExecuteScalarAsync(token));
            await transaction.CommitAsync(token);
        }

        await using var fetch = new NpgsqlCommand("SELECT datatype.cursor_cached_rows(2)", connection);
        Assert.AreEqual("1,2", await fetch.ExecuteScalarAsync(token));
        Assert.AreEqual("3", await fetch.ExecuteScalarAsync(token));
        fetch.CommandText = "SELECT datatype.cursor_dispose_cached()";
        await fetch.ExecuteNonQueryAsync(token);
        fetch.CommandText = "SELECT count(*) FROM pg_cursors WHERE name = 'held_cursor'";
        Assert.AreEqual(0L, await fetch.ExecuteScalarAsync(token));
    }
}
