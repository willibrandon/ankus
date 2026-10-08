using System.Globalization;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies recoverable and atomic explicit subtransaction scopes through PostgreSQL's real transaction machinery.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class SubtransactionModeTests(TestContext context)
{
    /// <summary>
    /// A recoverable scope survives a caught statement error; an atomic scope rethrows it on the next statement and rolls
    /// back entirely before the caller sees it, while earlier transaction work remains.
    /// </summary>
    /// <param name="atomic">Whether the scope is atomic.</param>
    /// <param name="failAt">The row before which a duplicate is attempted, or zero.</param>
    /// <param name="expected">The function's reported outcomes.</param>
    /// <param name="rows">The committed rows, including the row inserted before the scope.</param>
    [TestMethod]
    [DataRow(false, 0, "none|ok", 6)]
    [DataRow(true, 0, "none|ok", 6)]
    [DataRow(false, 3, "23505:ok|ok", 6)]
    [DataRow(true, 3, "23505:23505|23505", 1)]
    public async Task ScopesRecoverStatementsOrRollBackTogether(bool atomic, int failAt, string expected, int rows)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await ExecuteAsync(connection, "CREATE TEMPORARY TABLE scope_values (value integer PRIMARY KEY); INSERT INTO scope_values VALUES (0)");
        Assert.AreEqual(expected, await ScalarAsync(connection,
            $"SELECT datatype.subtransaction_scope_insert(5, {(atomic ? "true" : "false")}, {failAt.ToString(CultureInfo.InvariantCulture)})"));
        Assert.AreEqual((long)rows, await ScalarAsync(connection, "SELECT count(*) FROM scope_values"));
        Assert.AreEqual(42, await ScalarAsync(connection, "SELECT 42"));
    }

    /// <summary>
    /// A recoverable scope nested in an atomic one recovers its own statement, and the atomic scope then continues.
    /// </summary>
    [TestMethod]
    public async Task NestedRecoverableScopesRecoverInsideAtomicScopes()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await ExecuteAsync(connection, "CREATE TEMPORARY TABLE scope_values (value integer PRIMARY KEY)");
        Assert.AreEqual("23505|ok", await ScalarAsync(connection, "SELECT datatype.subtransaction_scope_nested()"));
        Assert.AreEqual("1,2", await ScalarAsync(connection, "SELECT string_agg(value::text, ',' ORDER BY value) FROM scope_values"));
    }

    /// <summary>
    /// Writes in an atomic scope consume one subtransaction ID, against one per statement plus the scope when recoverable.
    /// </summary>
    /// <remarks>A private cluster keeps other sessions from assigning transaction IDs during the measurement.</remarks>
    [TestMethod]
    public async Task AtomicScopesConsumeOneSubtransactionId()
    {
        CancellationToken token = context.CancellationToken;
        PostgresTestClusterOptions options = await IntegrationEnvironment.CreateOptionsAsync(token);
        await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(options, token);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        await ExecuteAsync(connection, "CREATE SCHEMA datatype; CREATE EXTENSION ankus_test WITH SCHEMA datatype; " +
            "CREATE TEMPORARY TABLE scope_values (value integer PRIMARY KEY)");
        Assert.AreEqual(1L, await AssignedAsync(connection, true));
        Assert.AreEqual(201L, await AssignedAsync(connection, false));
    }

    /// <summary>
    /// Counts transaction IDs assigned inside one transaction besides its own: the next transaction's ID minus this
    /// transaction's ID, minus one.
    /// </summary>
    private async Task<long> AssignedAsync(NpgsqlConnection connection, bool atomic)
    {
        await ExecuteAsync(connection, "TRUNCATE scope_values");
        await ExecuteAsync(connection, "BEGIN");
        long first = await CurrentAsync(connection);
        Assert.AreEqual("none|ok", await ScalarAsync(connection, $"SELECT datatype.subtransaction_scope_insert(200, {(atomic ? "true" : "false")}, 0)"));
        await ExecuteAsync(connection, "COMMIT; BEGIN");
        long next = await CurrentAsync(connection);
        await ExecuteAsync(connection, "ROLLBACK");
        return next - first - 1;
    }

    private async Task<long> CurrentAsync(NpgsqlConnection connection)
        => long.Parse(Assert.IsInstanceOfType<string>(await ScalarAsync(connection, "SELECT pg_catalog.pg_current_xact_id()::text")),
            CultureInfo.InvariantCulture);

    private async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(context.CancellationToken);
    }

    private async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(context.CancellationToken);
    }
}
