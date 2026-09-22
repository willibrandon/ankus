using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies reusable native SPI plans, their transactional behavior, invalidation, and deterministic ownership.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class SpiPreparedTests(TestContext context)
{
    /// <summary>
    /// Verifies repeated plan execution, nullable/binary values, owned results, and explicit query modes.
    /// </summary>
    /// <param name="expression">The backend expression to evaluate.</param>
    /// <param name="expected">The exact result text, or SQL NULL.</param>
    [TestMethod]
    [DataRow("datatype.prepared_sum(10)", "55")]
    [DataRow("datatype.prepared_text('café 🐘')", "first:café 🐘")]
    [DataRow("datatype.prepared_text(NULL)", "first:<null>")]
    [DataRow("encode(datatype.prepared_bytes(decode('00ff7f00', 'hex')), 'hex')", "00ff7f00")]
    [DataRow("datatype.prepared_bytes(NULL)", null)]
    [DataRow("datatype.prepared_rows('SELECT generate_series(1, 5)', true, 2)", "2")]
    [DataRow("datatype.prepared_rows('SELECT generate_series(1, 5)', false, 0)", "5")]
    [DataRow("datatype.prepared_scalar('SELECT 42, ARRAY[1, 2]')", "42")]
    [DataRow("datatype.prepared_scalar('SELECT NULL::int')", null)]
    [DataRow("datatype.prepared_scalar('SELECT 42 WHERE false')", null)]
    [DataRow("datatype.prepared_scalar('SELECT FROM generate_series(1, 2)')", null)]
    public Task PreparedExecutionPreservesValuesAndModes(string expression, string? expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(PreparedExecutionPreservesValuesAndModes),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand($"SELECT ({expression})::text", connection, transaction);
                Assert.AreEqual((object?)expected ?? DBNull.Value, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies an error rolls back writes from the full plan and the same plan can execute again.
    /// </summary>
    [TestMethod]
    public Task PlanSurvivesExecutionErrorAndRollsBackItsWrites()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(PlanSurvivesExecutionErrorAndRollsBackItsWrites),
            async (connection, transaction, token) =>
            {
                await using (var setup = new NpgsqlCommand("CREATE TEMP TABLE prepared_values (value int)", connection, transaction))
                {
                    await setup.ExecuteNonQueryAsync(token);
                }

                await using (var command = new NpgsqlCommand("SELECT datatype.prepared_recover()", connection, transaction))
                {
                    Assert.AreEqual("22012:6", await command.ExecuteScalarAsync(token));
                }

                await using var rows = new NpgsqlCommand("SELECT array_agg(value) FROM prepared_values", connection, transaction);
                Assert.AreSequenceEqual<int>([2], Assert.IsInstanceOfType<int[]>(await rows.ExecuteScalarAsync(token)));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies scalar materialization does not truncate a retained plan's write effects.
    /// </summary>
    [TestMethod]
    public Task PreparedScalarCompletesAllWriteEffects()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(PreparedScalarCompletesAllWriteEffects),
            async (connection, transaction, token) =>
            {
                await using (var setup = new NpgsqlCommand("CREATE TEMP TABLE prepared_writes (value int)", connection, transaction))
                {
                    await setup.ExecuteNonQueryAsync(token);
                }

                await using (var command = new NpgsqlCommand("SELECT datatype.prepared_scalar($1)", connection, transaction))
                {
                    command.Parameters.AddWithValue("INSERT INTO prepared_writes SELECT generate_series(1, 5) RETURNING value");
                    Assert.AreEqual(1, await command.ExecuteScalarAsync(token));
                }

                await using var count = new NpgsqlCommand("SELECT count(*) FROM prepared_writes", connection, transaction);
                Assert.AreEqual(5L, await count.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies argument count/type mismatches are diagnosed before entering PostgreSQL and leave the plan reusable.
    /// </summary>
    /// <param name="mode">The invalid parameter-list case.</param>
    /// <param name="diagnostic">The distinguishing diagnostic text.</param>
    [TestMethod]
    [DataRow(0, "requires 1 parameters, but received 0")]
    [DataRow(1, "requires 1 parameters, but received 2")]
    [DataRow(2, "Parameter 1 must have PostgreSQL type OID 23")]
    [DataRow(3, "Parameter 1 must have PostgreSQL type OID 23")]
    public Task InvalidParametersLeavePlanUsable(int mode, string diagnostic)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(InvalidParametersLeavePlanUsable),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.prepared_invalid_arguments($1)", connection, transaction);
                command.Parameters.AddWithValue(mode);
                string result = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
                Assert.Contains(diagnostic, result);
                Assert.EndsWith("|42", result);
            }, context.CancellationToken);

    /// <summary>
    /// Verifies worker-thread execution and disposal fail before accessing the native plan.
    /// </summary>
    /// <param name="dispose">Whether the worker attempts disposal.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task PlanRejectsWorkerThreadAccess(bool dispose)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(PlanRejectsWorkerThreadAccess),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.prepared_worker($1)", connection, transaction);
                command.Parameters.AddWithValue(dispose);
                Assert.AreEqual("PostgreSQL APIs can only be used on the active PostgreSQL backend thread.|42",
                    await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies kept plans remain valid after their creation callback and transaction have ended.
    /// </summary>
    /// <param name="rollback">Whether to roll back the transaction that created the saved plan.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CachedPlanSurvivesTransactionBoundaries(bool rollback)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await using var create = new NpgsqlCommand("SELECT datatype.prepared_cache('SELECT $1 + 1')", connection, transaction);
            Assert.AreEqual(1, await create.ExecuteScalarAsync(token));
            if (rollback)
            {
                await transaction.RollbackAsync(token);
            }
            else
            {
                await transaction.CommitAsync(token);
            }
        }

        await using var execute = new NpgsqlCommand("SELECT datatype.prepared_cached_value(41)", connection);
        Assert.AreEqual(42, await execute.ExecuteScalarAsync(token));
        execute.CommandText = "SELECT datatype.prepared_cached_value(-1)";
        Assert.AreEqual(0, await execute.ExecuteScalarAsync(token));
        await using var dispose = new NpgsqlCommand("SELECT datatype.prepared_dispose_cached()", connection);
        await dispose.ExecuteNonQueryAsync(token);
    }

    /// <summary>
    /// Verifies a saved plan follows PostgreSQL invalidation when a relation is dropped and recreated with a new OID.
    /// </summary>
    [TestMethod]
    public async Task CachedPlanReplansAfterRelationReplacement()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using (var setup = new NpgsqlCommand("""
            CREATE TEMP TABLE prepared_replan (value int);
            INSERT INTO prepared_replan VALUES (10);
            SELECT datatype.prepared_cache('SELECT value + $1 FROM prepared_replan')
            """, connection))
        {
            Assert.AreEqual(1, await setup.ExecuteScalarAsync(token));
        }

        await using var execute = new NpgsqlCommand("SELECT datatype.prepared_cached_value(2)", connection);
        Assert.AreEqual(12, await execute.ExecuteScalarAsync(token));
        await using (var replace = new NpgsqlCommand("""
            DROP TABLE prepared_replan;
            CREATE TEMP TABLE prepared_replan (value int);
            INSERT INTO prepared_replan VALUES (70)
            """, connection))
        {
            await replace.ExecuteNonQueryAsync(token);
        }

        Assert.AreEqual(72, await execute.ExecuteScalarAsync(token));
        await using var dispose = new NpgsqlCommand("SELECT datatype.prepared_dispose_cached()", connection);
        await dispose.ExecuteNonQueryAsync(token);
    }

    /// <summary>
    /// Verifies disposal is idempotent, rejects later execution, and leaves the backend usable.
    /// </summary>
    [TestMethod]
    public async Task DisposedPlanCannotBeExecutedOrFreedTwice()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using (var setup = new NpgsqlCommand("""
            SELECT datatype.prepared_cache('SELECT $1');
            SELECT datatype.prepared_dispose_cached();
            SELECT datatype.prepared_dispose_cached()
            """, connection))
        {
            await setup.ExecuteNonQueryAsync(token);
        }

        await using var execute = new NpgsqlCommand("SELECT datatype.prepared_cached_value(42)", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => execute.ExecuteScalarAsync(token));
        Assert.AreEqual("38000", error.SqlState);
        Assert.Contains("disposed object", error.MessageText);
        execute.CommandText = "SELECT datatype.prepared_sum(3)";
        Assert.AreEqual(6, await execute.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Verifies recursive dispatch cannot free the native plan that the outer SPI execution is still using.
    /// </summary>
    [TestMethod]
    public Task ReentrantDisposalIsRejectedWhilePlanExecutes()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ReentrantDisposalIsRejectedWhilePlanExecutes),
            async (connection, transaction, token) =>
            {
                await using (var setup = new NpgsqlCommand(
                    "SELECT datatype.prepared_cache('SELECT datatype.prepared_reentrant_dispose($1)')", connection, transaction))
                {
                    Assert.AreEqual(1, await setup.ExecuteScalarAsync(token));
                }

                await using var execute = new NpgsqlCommand("SELECT datatype.prepared_cached_value(42)", connection, transaction);
                Assert.AreEqual(42, await execute.ExecuteScalarAsync(token));
                execute.CommandText = "SELECT datatype.prepared_cached_value(99)";
                Assert.AreEqual(99, await execute.ExecuteScalarAsync(token));
                execute.CommandText = "SELECT datatype.prepared_dispose_cached()";
                await execute.ExecuteNonQueryAsync(token);
            }, context.CancellationToken);

    /// <summary>
    /// Verifies nested SPI calls can safely execute the same plan while preserving its outer execution state.
    /// </summary>
    [TestMethod]
    public Task SamePlanCanExecuteRecursively()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SamePlanCanExecuteRecursively),
            async (connection, transaction, token) =>
            {
                await using (var setup = new NpgsqlCommand(
                    "SELECT datatype.prepared_cache('SELECT datatype.prepared_recursive($1)')", connection, transaction))
                {
                    Assert.AreEqual(1, await setup.ExecuteScalarAsync(token));
                }

                await using var execute = new NpgsqlCommand("SELECT datatype.prepared_cached_value(5)", connection, transaction);
                Assert.AreEqual(15, await execute.ExecuteScalarAsync(token));
                execute.CommandText = "SELECT datatype.prepared_dispose_cached()";
                await execute.ExecuteNonQueryAsync(token);
            }, context.CancellationToken);

    /// <summary>
    /// Verifies saved plans re-resolve unqualified names after the effective search path changes.
    /// </summary>
    [TestMethod]
    public Task CachedPlanFollowsSearchPathChanges()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(CachedPlanFollowsSearchPathChanges),
            async (connection, transaction, token) =>
            {
                await using (var setup = new NpgsqlCommand("""
                    CREATE SCHEMA plan_first;
                    CREATE SCHEMA plan_second;
                    CREATE TABLE plan_first.plan_item (value int);
                    CREATE TABLE plan_second.plan_item (value int);
                    INSERT INTO plan_first.plan_item VALUES (10);
                    INSERT INTO plan_second.plan_item VALUES (20);
                    SET LOCAL search_path = plan_first;
                    SELECT datatype.prepared_cache('SELECT value + $1 FROM plan_item')
                    """, connection, transaction))
                {
                    Assert.AreEqual(1, await setup.ExecuteScalarAsync(token));
                }

                await using var execute = new NpgsqlCommand("SELECT datatype.prepared_cached_value(2)", connection, transaction);
                Assert.AreEqual(12, await execute.ExecuteScalarAsync(token));
                await using (var change = new NpgsqlCommand("SET LOCAL search_path = plan_second", connection, transaction))
                {
                    await change.ExecuteNonQueryAsync(token);
                }

                Assert.AreEqual(22, await execute.ExecuteScalarAsync(token));
                execute.CommandText = "SELECT datatype.prepared_dispose_cached()";
                await execute.ExecuteNonQueryAsync(token);
            }, context.CancellationToken);

    /// <summary>
    /// Verifies a PostgreSQL timeout unwinds the plan's using scope without leaking its saved memory context.
    /// </summary>
    [TestMethod]
    public async Task CancellationDisposesScopedPlanAndPreservesBackend()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var count = new NpgsqlCommand("SELECT count(*) FROM pg_backend_memory_contexts WHERE name = 'SPI Plan'", connection);
        long before = Assert.IsInstanceOfType<long>(await count.ExecuteScalarAsync(token));
        await using (var configure = new NpgsqlCommand("SET statement_timeout = '100ms'", connection))
        {
            await configure.ExecuteNonQueryAsync(token);
        }

        await using var run = new NpgsqlCommand("SELECT datatype.prepared_rows('SELECT pg_sleep(5)', false, 0)", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => run.ExecuteScalarAsync(token));
        Assert.AreEqual(PostgresErrorCodes.QueryCanceled, error.SqlState);
        await using (var configure = new NpgsqlCommand("SET statement_timeout = 0", connection))
        {
            await configure.ExecuteNonQueryAsync(token);
        }

        Assert.AreEqual(before, await count.ExecuteScalarAsync(token));
        run.CommandText = "SELECT datatype.prepared_sum(3)";
        Assert.AreEqual(6, await run.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Verifies successful and failing using scopes return retained-plan memory contexts to their original count.
    /// </summary>
    /// <param name="fail">Whether execution raises a PostgreSQL error.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ScopedPlansReleaseNativeMemoryAfterSuccessAndError(bool fail)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var count = new NpgsqlCommand("SELECT count(*) FROM pg_backend_memory_contexts WHERE name = 'SPI Plan'", connection);
        long before = Assert.IsInstanceOfType<long>(await count.ExecuteScalarAsync(token));
        await using var run = new NpgsqlCommand("SELECT datatype.prepared_scoped($1)", connection);
        run.Parameters.AddWithValue(fail);
        if (fail)
        {
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => run.ExecuteScalarAsync(token));
            Assert.AreEqual(PostgresErrorCodes.DivisionByZero, error.SqlState);
        }
        else
        {
            Assert.AreEqual(1L, await run.ExecuteScalarAsync(token));
        }

        Assert.AreEqual(before, await count.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Verifies preparation and execution errors retain their PostgreSQL diagnostics and permit subsequent work.
    /// </summary>
    /// <param name="expression">The failing operation.</param>
    /// <param name="state">The expected SQLSTATE.</param>
    [TestMethod]
    [DataRow("datatype.prepared_scalar('SELECT FROM missing_prepared_relation')", "42P01")]
    [DataRow("datatype.prepared_rows('CREATE TEMP TABLE forbidden_plan (n int)', true, 0)", "0A000")]
    public async Task PreparationAndReadOnlyErrorsPreserveBackend(string expression, string state)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand($"SELECT {expression}", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual(state, error.SqlState);
        command.CommandText = "SELECT datatype.prepared_sum(4)";
        Assert.AreEqual(10, await command.ExecuteScalarAsync(token));
    }
}
