using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies cached function state through actual query, portal, and iterator lifetimes.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class FunctionStateTests(TestContext context)
{
    /// <summary>
    /// Reuses state across row resets, separates expressions, and releases every managed root at query end.
    /// </summary>
    /// <param name="isNull">Whether to retain a typed NULL first argument.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RowCallsRetainIndependentStateAndOwnedNativeValues(bool isNull)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        string first = isNull ? "NULL::text" : "repeat('owned',10000)";
        await using var command = new NpgsqlCommand($"""
            SELECT datatype.state_tick(CASE WHEN n=1 THEN {first} ELSE n::text END, false),
                datatype.state_tick('independent', false) FROM generate_series(1,3) n
            """, connection);
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
        {
            for (int row = 1; row <= 3; row++)
            {
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual($"{row}|{(isNull ? "NULL" : string.Concat(Enumerable.Repeat("owned", 10000)))}", reader.GetString(0));
                Assert.AreEqual($"{row}|independent", reader.GetString(1));
            }

            Assert.IsFalse(await reader.ReadAsync(token));
        }

        Assert.AreEqual("2|2|0|0", await ScalarAsync<string>(connection, "SELECT datatype.state_counts()", token));
        Assert.AreEqual(2, await ScalarAsync<int>(connection, "SELECT datatype.state_expired()", token));
        Assert.AreEqual("1|fresh", await ScalarAsync<string>(connection, "SELECT datatype.state_tick('fresh', false)", token));
        Assert.AreEqual("3|3|0|0", await ScalarAsync<string>(connection, "SELECT datatype.state_counts()", token));
    }

    /// <summary>
    /// Cached plans construct fresh expression state for every execution.
    /// </summary>
    [TestMethod]
    public async Task PreparedExecutionsDoNotReviveExpiredState()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand("SELECT array_agg(datatype.state_tick(n::text,false)) FROM generate_series(1,3) n", connection);
        await command.PrepareAsync(token);
        for (int execution = 1; execution <= 3; execution++)
        {
            string[] values = Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(token));
            Assert.AreSequenceEqual(["1|1", "2|1", "3|1"], values);
            Assert.AreEqual($"{execution}|{execution}|0|0", await ScalarAsync<string>(connection, "SELECT datatype.state_counts()", token));
        }
    }

    /// <summary>
    /// Preserves nullable values, exact declared types, failed initialization retries, and backend-thread guards.
    /// </summary>
    /// <param name="mode">The state contract probe.</param>
    /// <param name="expected">The exact observed result.</param>
    [TestMethod]
    [DataRow(0, "True|True")]
    [DataRow(1, "0|0")]
    [DataRow(2, "22012|42|43")]
    [DataRow(3, "recursive|42|43")]
    [DataRow(4, "type|42|43")]
    [DataRow(5, "rejected")]
    [DataRow(6, "1|nested")]
    public async Task InitializationContractsPreserveBackendRecovery(int mode, string expected)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        Assert.AreEqual(expected, await ScalarAsync<string>(connection, $"SELECT datatype.state_contracts({mode})", token));
        Assert.AreEqual(42, await ScalarAsync<int>(connection, "SELECT 42", token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Keeps one cache across iterator instances while PostgreSQL owns separate set-returning machinery.
    /// </summary>
    /// <param name="materialize">Whether the iterator uses materialization.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RepeatedIteratorsShareOnlyTheirCallSiteState(bool materialize)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        string function = materialize ? "state_materialized" : "state_rows";
        await using var command = new NpgsqlCommand($"SELECT value FROM generate_series(1,2) n CROSS JOIN LATERAL datatype.{function}(n,false) value", connection);
        var values = new List<int>();
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                values.Add(reader.GetInt32(0));
            }
        }

        Assert.AreSequenceEqual([1, 2, 3], values);
        Assert.AreEqual("1|1|2|0", await ScalarAsync<string>(connection, "SELECT datatype.state_counts()", token));
    }

    /// <summary>
    /// Disposes state once on early stop, empty iteration, managed error, and executor abort.
    /// </summary>
    /// <param name="materialize">Whether to require materialization.</param>
    /// <param name="mode">Limit, empty, managed error, or executor error.</param>
    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(true, 0)]
    [DataRow(false, 1)]
    [DataRow(true, 1)]
    [DataRow(false, 2)]
    [DataRow(true, 2)]
    [DataRow(false, 3)]
    [DataRow(true, 3)]
    public async Task IteratorTerminationReleasesStateAndRecovers(bool materialize, int mode)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        string function = materialize ? "state_materialized" : "state_rows";
        string sql = $"SELECT * FROM datatype.{function}({(mode == 1 ? 0 : 3)},{(mode == 2 ? "true" : "false")})";
        if (mode == 0)
        {
            sql += " LIMIT 1";
        }
        else if (mode == 3)
        {
            sql = $"SELECT 1/(value-value) FROM ({sql}) AS rows(value)";
        }

        await using var command = new NpgsqlCommand(sql, connection);
        if (mode >= 2)
        {
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
            Assert.AreEqual(mode == 2 ? "P7808" : PostgresErrorCodes.DivisionByZero, error.SqlState);
        }
        else
        {
            object? result = await command.ExecuteScalarAsync(token);
            if (mode == 0)
            {
                Assert.AreEqual(1, result);
            }
            else
            {
                Assert.IsNull(result);
            }
        }

        Assert.AreEqual("1|1|1|0", await ScalarAsync<string>(connection, "SELECT datatype.state_counts()", token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Retains state between portal fetches and disposes it when the portal closes or its transaction rolls back.
    /// </summary>
    /// <param name="rollback">Whether rollback closes the portal.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PortalOwnsStateAcrossFetches(bool rollback)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await using var command = new NpgsqlCommand("DECLARE state_cursor CURSOR FOR SELECT datatype.state_tick(n::text,false) FROM generate_series(1,5) n", connection, transaction);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "FETCH 1 FROM state_cursor";
        Assert.AreEqual("1|1", await command.ExecuteScalarAsync(token));
        Assert.AreEqual("2|1", await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT datatype.state_counts()";
        Assert.AreEqual("1|0|0|1", await command.ExecuteScalarAsync(token));
        if (!rollback)
        {
            command.CommandText = "CLOSE state_cursor";
            await command.ExecuteNonQueryAsync(token);
        }

        await transaction.RollbackAsync(token);
        Assert.AreEqual("1|1|0|0", await ScalarAsync<string>(connection, "SELECT datatype.state_counts()", token));
        Assert.AreEqual(2, await ScalarAsync<int>(connection, "SELECT datatype.state_expired()", token));
    }

    /// <summary>
    /// Cleanup errors retain owned diagnostics, consume all roots, and leave the same backend usable.
    /// </summary>
    [TestMethod]
    public async Task DisposalErrorsUnwindAndReleaseRootsBeforeRecovery()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand("SELECT datatype.state_tick('first',false),datatype.state_tick('failed',true)", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        Assert.AreEqual("P7810", error.SqlState);
        Assert.AreEqual("state disposal failed", error.MessageText);
        Assert.AreEqual("owned detail", error.Detail);
        Assert.AreEqual("owned hint", error.Hint);
        Assert.AreEqual("2|2|0|0", await ScalarAsync<string>(connection, "SELECT datatype.state_counts()", token));
        Assert.AreEqual("1|recovered", await ScalarAsync<string>(connection, "SELECT datatype.state_tick('recovered',false)", token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Reads one independently observed SQL scalar.
    /// </summary>
    /// <typeparam name="T">The expected managed result type.</typeparam>
    /// <param name="connection">The existing backend connection.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="token">The test cancellation token.</param>
    /// <returns>The exact typed result.</returns>
    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }
}
