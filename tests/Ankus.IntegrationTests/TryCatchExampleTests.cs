using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes the ported pgrx pgtrybuilder sample through its published Native AOT extension.
/// </summary>
/// <param name="context">The current cancellation and diagnostic context.</param>
[TestClass]
public sealed class TryCatchExampleTests(TestContext context)
{
    /// <summary>
    /// A filtered catch rethrows the original SQLSTATE and message; accepted values return unchanged.
    /// </summary>
    /// <param name="rejected">A number below 42.</param>
    [TestMethod]
    [DataRow("41")]
    [DataRow("-2147483648")]
    public Task TryCatchSampleRethrowsSelectedErrors(string rejected) => RunInstalledAsync(async (connection, token) =>
    {
        Assert.AreEqual(42, await ScalarAsync<int>(connection, "SELECT is_valid_number(42)", token));
        Assert.AreEqual(int.MaxValue, await ScalarAsync<int>(connection, "SELECT is_valid_number(2147483647)", token));
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
            ScalarAsync<int>(connection, $"SELECT is_valid_number({rejected})", token));
        Assert.AreEqual("22003", error.SqlState);
        Assert.AreEqual("number too small", error.MessageText);
        Assert.AreEqual(42, await ScalarAsync<int>(connection, "SELECT is_valid_number(42)", token));
    });

    /// <summary>
    /// A PostgreSQL error from relation_open rolls back before the fallback; finally runs once on both paths.
    /// </summary>
    [TestMethod]
    public Task TryCatchSampleRecoversFromBackendErrors() => RunInstalledAsync(async (connection, token) =>
    {
        var notices = new List<PostgresNotice>();
        connection.Notice += (_, arguments) => notices.Add(arguments.Notice);
        Assert.AreEqual("pg_class", await ScalarAsync<string>(connection, "SELECT get_relation_name('pg_class'::regclass)", token));
        Assert.AreEqual("<4294967295 is not a relation>", await ScalarAsync<string>(connection, "SELECT get_relation_name(4294967295)", token));
        Assert.AreEqual("<0 is not a relation>", await ScalarAsync<string>(connection, "SELECT get_relation_name(0)", token));

        // The recovered transaction continues with further work in the same statement and backend.
        Assert.AreEqual("<4294967295 is not a relation>|pg_proc|42", await ScalarAsync<string>(connection,
            "SELECT get_relation_name(4294967295) || '|' || get_relation_name('pg_proc'::regclass) || '|' || 6 * 7", token));
        Assert.AreEqual(5, await ScalarAsync<int>(connection, """
            SELECT count(*)::integer FROM (
                SELECT get_relation_name(CASE WHEN value % 2 = 0 THEN 'pg_type'::regclass::oid ELSE 4294967295 END)
                FROM generate_series(1, 5) AS value) AS names
            """, token));

        // Only the finally warnings are reported: opened relations close, so commit reports no reference leak.
        Assert.AreSequenceEqual(Enumerable.Repeat("WARNING:01000:FINALLY!", 10),
            notices.Select(static notice => $"{notice.InvariantSeverity}:{notice.SqlState}:{notice.MessageText}"));
    });

    /// <summary>
    /// A trapped managed exception becomes warnings, while an untrapped one fails after the finally warning.
    /// </summary>
    [TestMethod]
    public Task TryCatchSampleTrapsManagedExceptions() => RunInstalledAsync(async (connection, token) =>
    {
        var notices = new List<string>();
        connection.Notice += (_, arguments) => notices.Add($"{arguments.Notice.InvariantSeverity}:{arguments.Notice.MessageText}");
        await ExecuteAsync(connection, "SELECT maybe_panic(false, false, 'unused'), maybe_panic(false, true, 'unused')", token);
        Assert.AreSequenceEqual<string>(["WARNING:FINALLY!", "WARNING:FINALLY!"], notices);
        notices.Clear();

        await ExecuteAsync(connection, "SELECT maybe_panic(true, true, 'café 🐘')", token);
        Assert.AreSequenceEqual<string>(
        [
            "WARNING:System.InvalidOperationException: panic says: café 🐘",
            "WARNING:panic says: café 🐘",
            "WARNING:FINALLY!",
        ], notices);
        notices.Clear();

        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
            ExecuteAsync(connection, "SELECT maybe_panic(true, false, 'untrapped')", token));
        Assert.AreEqual("38000", error.SqlState);
        Assert.AreEqual("panic says: untrapped", error.MessageText);
        Assert.AreSequenceEqual<string>(["WARNING:FINALLY!"], notices);
        notices.Clear();

        Assert.IsTrue(await ScalarAsync<bool>(connection, "SELECT maybe_panic(NULL, true, 'strict') IS NULL", token));
        Assert.IsEmpty(notices);
    });

    /// <summary>
    /// Runs autocommit statements in a fresh database with the sample installed, then removes the database.
    /// </summary>
    /// <remarks>
    /// Committing each statement lets PostgreSQL report leaked relation references, which a rolled-back test transaction would hide.
    /// </remarks>
    private async Task RunInstalledAsync(Func<NpgsqlConnection, CancellationToken, Task> test)
    {
        CancellationToken token = context.CancellationToken;
        string database = "try_catch_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection administrator = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await ExecuteAsync(administrator, $"CREATE DATABASE {database} TEMPLATE template0", token);
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(administrator.ConnectionString) { Database = database, Pooling = false };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(token);
            int backend = connection.ProcessID;
            await ExecuteAsync(connection, "CREATE EXTENSION ankus_try_catch", token);
            await test(connection, token);
            Assert.AreEqual(backend, await ScalarAsync<int>(connection, "SELECT pg_backend_pid()", token));
        }
        finally
        {
            await ExecuteAsync(administrator, $"DROP DATABASE {database} WITH (FORCE)", CancellationToken.None);
        }
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(token);
    }
}
