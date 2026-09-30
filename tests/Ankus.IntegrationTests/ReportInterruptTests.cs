using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies interrupt deferral during actual native report emission and balanced holdoffs after failures.
/// </summary>
/// <param name="context">The per-test cancellation context.</param>
[TestClass]
public sealed class ReportInterruptTests(TestContext context)
{
    /// <summary>
    /// Initializer and transaction callback logging use their restricted native capabilities with balanced holdoffs.
    /// </summary>
    /// <param name="transactionCallback">Whether to invoke a pre-commit callback instead of loading the initializer-only module.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RestrictedCallbacksHoldInterruptsDuringReporting(bool transactionCallback)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int backend = connection.ProcessID;
        string marker = transactionCallback ? "Ankus pre-commit callback" : "Ankus initialization sample loaded.";
        var notices = new List<PostgresNotice>();
        connection.Notice += (_, args) => notices.Add(args.Notice);
        await ExecuteAsync(connection, "SET log_min_messages = 'notice'");
        await using var arm = new NpgsqlCommand("SELECT tests.log_arm($1, 0)", connection);
        arm.Parameters.AddWithValue(marker);
        await arm.ExecuteNonQueryAsync(context.CancellationToken);
        await ExecuteAsync(connection, transactionCallback
            ? "SELECT datatype.transaction_callback_register_outer(true, false, false, false)"
            : "LOAD 'Ankus.Examples.Initialization'");
        Assert.ContainsSingle(notices.Where(notice => notice.MessageText == marker));
        await AssertRecoveredAsync(connection, backend, 1);
    }

    /// <summary>
    /// Reporting returns to managed code while cancellation remains pending for an explicit poll.
    /// </summary>
    /// <param name="level">The report's nonterminal severity.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(8)]
    [DataRow(9)]
    public async Task NonterminalReportDefersCancellationUntilPolling(int level)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int backend = connection.ProcessID;
        var notices = new List<PostgresNotice>();
        connection.Notice += (_, args) => notices.Add(args.Notice);
        await ExecuteAsync(connection, "SET client_min_messages = 'debug5'; SET log_min_messages = 'debug5'; " +
            "SELECT tests.log_arm('pending report cancellation', 1)");
        await using var command = new NpgsqlCommand("SELECT datatype.log_deferred_cancellation($1)", connection);
        command.Parameters.AddWithValue(level);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(context.CancellationToken));
        Assert.AreEqual(PostgresErrorCodes.QueryCanceled, error.SqlState);
        Assert.AreEqual("canceling statement due to user request", error.MessageText);
        Assert.AreSequenceEqual<string>(["pending report cancellation", "continued after report"],
            notices.Select(notice => notice.MessageText).Where(message =>
                message is "pending report cancellation" or "continued after report"));
        await AssertRecoveredAsync(connection, backend, 1);
    }

    /// <summary>
    /// The GUC-only module finishes both reports before PostgreSQL processes the pending cancellation.
    /// </summary>
    [TestMethod]
    public async Task ConfigurationReportsDeferCancellation()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int backend = connection.ProcessID;
        var notices = new List<PostgresNotice>();
        connection.Notice += (_, args) => notices.Add(args.Notice);
        await ExecuteAsync(connection, "LOAD 'Ankus.GucShowExtension'; SET client_min_messages = 'debug1'; " +
            "SET log_min_messages = 'debug1'; SELECT tests.log_arm('show=7;sql=unavailable;café 100%', 1)");
        await using var command = new NpgsqlCommand("SELECT current_setting('ankus_guc_show.count'), pg_sleep(0)", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(context.CancellationToken));
        Assert.AreEqual(PostgresErrorCodes.QueryCanceled, error.SqlState);
        Assert.AreEqual("canceling statement due to user request", error.MessageText);
        Assert.AreSequenceEqual<string>(["show=7;sql=unavailable;café 100%", "Display debug message."],
            notices.Select(notice => notice.MessageText).Where(message =>
                message.StartsWith("show=", StringComparison.Ordinal) || message == "Display debug message."));
        await AssertRecoveredAsync(connection, backend, 1);
    }

    /// <summary>
    /// A native reporter failure restores inherited holdoffs and preserves its diagnostic.
    /// </summary>
    /// <param name="configuration">Whether the report comes from the GUC-only module.</param>
    /// <param name="fail">Whether the report hook raises ERROR.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task ReportsPreserveInheritedHoldoffs(bool configuration, bool fail)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int backend = connection.ProcessID;
        await ExecuteAsync(connection, "LOAD 'Ankus.GucShowExtension'; SET log_min_messages = 'notice'; SELECT tests.raw_call_control(1)");
        try
        {
            await using var arm = new NpgsqlCommand("SELECT tests.log_arm($1, $2)", connection);
            arm.Parameters.AddWithValue(configuration ? "show=7;sql=unavailable;café 100%" : "native hook report");
            arm.Parameters.AddWithValue(fail ? 2 : 0);
            await arm.ExecuteNonQueryAsync(context.CancellationToken);
            await using var report = new NpgsqlCommand(configuration
                ? "SELECT current_setting('ankus_guc_show.count')"
                : fail ? "SELECT datatype.log_catch_native_report_error()"
                : "SELECT datatype.log_message(8, 'native hook report')", connection);
            if (fail && configuration)
            {
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => report.ExecuteScalarAsync(context.CancellationToken));
                Assert.AreEqual(PostgresErrorCodes.InvalidParameterValue, error.SqlState);
                Assert.AreEqual("native report hook failure", error.MessageText);
            }
            else
            {
                object? result = await report.ExecuteScalarAsync(context.CancellationToken);
                object expected = configuration ? "display=7;native=7;extra=none"
                    : fail ? "22023|native report hook failure|4294967297" : 42;
                Assert.AreEqual(expected, result);
            }

            await using var state = new NpgsqlCommand("SELECT tests.log_holdoff(), tests.raw_call_holdoffs()", connection);
            await using NpgsqlDataReader reader = await state.ExecuteReaderAsync(context.CancellationToken);
            Assert.IsTrue(await reader.ReadAsync(context.CancellationToken));
            Assert.AreEqual(2L, reader.GetInt64(0));
            Assert.AreEqual(0x100000001L, reader.GetInt64(1));
        }
        finally
        {
            await ExecuteAsync(connection, "SELECT tests.raw_call_control(-1)");
        }

        await AssertRecoveredAsync(connection, backend, 2);
    }

    private async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(context.CancellationToken);
    }

    private async Task AssertRecoveredAsync(NpgsqlConnection connection, int backend, long reportHoldoff)
    {
        await using var command = new NpgsqlCommand("SELECT tests.log_holdoff(), tests.raw_call_holdoffs(), 42", connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(context.CancellationToken);
        Assert.IsTrue(await reader.ReadAsync(context.CancellationToken));
        Assert.AreEqual(reportHoldoff, reader.GetInt64(0));
        Assert.AreEqual(0L, reader.GetInt64(1));
        Assert.AreEqual(42, reader.GetInt32(2));
        Assert.AreEqual(backend, connection.ProcessID);
    }
}
