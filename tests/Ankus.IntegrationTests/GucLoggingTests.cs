using System.Net.Sockets;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Exercises logging capabilities during transactional and restricted native configuration phases.
/// </summary>
/// <param name="context">The test cancellation and diagnostic context.</param>
[TestClass]
public sealed class GucLoggingTests(TestContext context)
{
    /// <summary>
    /// Assign-only callbacks report owned fields while reading old storage and restoring values without SQL access.
    /// </summary>
    [TestMethod]
    public async Task AssignmentLogsStructuredDiagnosticsDuringAbortAndFunctionRestoration()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        var notices = new List<PostgresNotice>();
        connection.Notice += (_, args) => notices.Add(args.Notice);
        await ExecuteAsync(connection, "SET log_min_messages = 'notice'; LOAD 'Ankus.GucAssignExtension'");
        Assert.AreEqual("assign=7;old=7;sql=unavailable;café 100%", Assert.ContainsSingle(notices).MessageText);
        await ExecuteAsync(connection, "SET ankus_guc_assign.count = '9'");
        await ExecuteAsync(connection, "BEGIN; SET LOCAL ankus_guc_assign.count = '11'");
        Assert.AreEqual("22012", (await FailureAsync(connection, "SELECT 1/0")).SqlState);
        await ExecuteAsync(connection, "ROLLBACK");
        await ExecuteAsync(connection,
            "CREATE FUNCTION pg_temp.guc_scoped() RETURNS integer LANGUAGE sql AS 'SELECT 42'; " +
            "ALTER FUNCTION pg_temp.guc_scoped() SET ankus_guc_assign.count = '14'");
        Assert.AreEqual(42, await ScalarAsync(connection, "SELECT pg_temp.guc_scoped()"));
        Assert.AreSequenceEqual<string>(
        [
            "assign=7;old=7;sql=unavailable;café 100%",
            "assign=9;old=7;sql=unavailable;café 100%",
            "assign=11;old=9;sql=unavailable;café 100%",
            "assign=9;old=11;sql=unavailable;café 100%",
            "assign=14;old=9;sql=unavailable;café 100%",
            "assign=9;old=14;sql=unavailable;café 100%",
        ], notices.Select(notice => notice.MessageText));
        foreach (PostgresNotice notice in notices)
        {
            Assert.AreEqual("NOTICE", notice.InvariantSeverity);
            Assert.AreEqual("01000", notice.SqlState);
            Assert.AreEqual("Owned détail", notice.Detail);
            Assert.AreEqual("Keep the accepted value.", notice.Hint);
            Assert.AreEqual("guc_schema", notice.SchemaName);
            Assert.AreEqual("guc_table", notice.TableName);
            Assert.AreEqual("guc_column", notice.ColumnName);
            Assert.AreEqual("integer", notice.DataTypeName);
            Assert.AreEqual("guc_constraint", notice.ConstraintName);
            Assert.Contains("Assignment hook", notice.Where ?? string.Empty);
            Assert.AreEqual("Settings.cs", notice.File);
            Assert.AreEqual("Assign", notice.Routine);
            Assert.AreEqual("73", notice.Line);
            Assert.AreEqual(3, notice.Position);
            Assert.AreEqual("SELECT 7", notice.InternalQuery);
            Assert.AreEqual(8, notice.InternalPosition);
        }

        Assert.AreEqual("9", await ScalarAsync(connection, "SHOW ankus_guc_assign.count"));
        Assert.Contains("DETAIL:  Server assignment detail", PostgresFixture.Cluster.ReadServerLog());
        Assert.AreEqual(42, await ScalarAsync(connection, "SELECT 42"));
    }

    /// <summary>
    /// Show-only logging obeys client thresholds without granting the hook transaction access.
    /// </summary>
    [TestMethod]
    public async Task ShowLoggingHonorsClientThresholdsWithoutEnablingSql()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        var notices = new List<PostgresNotice>();
        connection.Notice += (_, args) => notices.Add(args.Notice);
        await ExecuteAsync(connection, "LOAD 'Ankus.GucShowExtension'; SET client_min_messages = 'warning'");
        Assert.AreEqual("display=7;native=7;extra=none", await ScalarAsync(connection, "SELECT current_setting('ankus_guc_show.count')"));
        Assert.IsEmpty(notices);
        await ExecuteAsync(connection, "SET client_min_messages = 'notice'");
        Assert.AreEqual("display=7;native=7;extra=none", await ScalarAsync(connection, "SELECT current_setting('ankus_guc_show.count')"));
        Assert.AreEqual("show=7;sql=unavailable;café 100%", Assert.ContainsSingle(notices).MessageText);
        notices.Clear();
        await ExecuteAsync(connection, "SET client_min_messages = 'debug1'");
        Assert.AreEqual("display=7;native=7;extra=none", await ScalarAsync(connection, "SELECT current_setting('ankus_guc_show.count')"));
        PostgresNotice[] hookNotices = [.. notices.Where(notice =>
            notice.MessageText.StartsWith("show=", StringComparison.Ordinal) || notice.MessageText == "Display debug message.")];
        Assert.AreSequenceEqual<string>(["show=7;sql=unavailable;café 100%", "Display debug message."],
            hookNotices.Select(notice => notice.MessageText));
        Assert.AreEqual("DEBUG", hookNotices[1].InvariantSeverity);
    }

    /// <summary>
    /// Report hooks log outside transactions and keep the wire parameter status and typed storage coherent.
    /// </summary>
    [TestMethod]
    public async Task ParameterStatusShowLogsOutsideTransactions()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await ExecuteAsync(connection, "LOAD 'Ankus.TestExtension'; SET ankus_guc.control = 'logging'");
        var notices = new List<PostgresNotice>();
        connection.Notice += (_, args) => notices.Add(args.Notice);
        await ExecuteAsync(connection, "SET ankus_guc.reported = '2'");
        Assert.AreEqual("report=2;sql=unavailable", connection.PostgresParameters["ankus_guc.reported"]);
        Assert.AreEqual("report=2;sql=unavailable;café 100%", Assert.ContainsSingle(notices).MessageText);
        notices.Clear();
        await ExecuteAsync(connection, "BEGIN; SET LOCAL ankus_guc.reported = '3'");
        Assert.AreEqual("report=3;sql=unavailable", connection.PostgresParameters["ankus_guc.reported"]);
        await ExecuteAsync(connection, "ROLLBACK");
        Assert.AreEqual("report=2;sql=unavailable", connection.PostgresParameters["ankus_guc.reported"]);
        Assert.AreSequenceEqual<string>(["report=3;sql=unavailable;café 100%", "report=2;sql=unavailable;café 100%"],
            notices.Select(notice => notice.MessageText));
        Assert.AreEqual(42, await ScalarAsync(connection, "SELECT 42"));
    }

    /// <summary>
    /// A show hook's ERROR unwinds safely and can repeat without breaking the session or losing its diagnostic fields.
    /// </summary>
    [TestMethod]
    public async Task ShowErrorPreservesDiagnosticsAndSameSessionRecovery()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await ExecuteAsync(connection, "LOAD 'Ankus.GucShowExtension'; SET ankus_guc_show.count = '667'");
        var notices = new List<PostgresNotice>();
        connection.Notice += (_, args) => notices.Add(args.Notice);
        for (int iteration = 0; iteration < 20; iteration++)
        {
            notices.Clear();
            PostgresException error = await FailureAsync(connection, "SHOW ankus_guc_show.count");
            Assert.AreEqual("ERROR", error.InvariantSeverity);
            Assert.AreEqual("P0001", error.SqlState);
            Assert.AreEqual("Display requested termination.", error.MessageText);
            Assert.AreEqual("Managed frames unwind first.", error.Detail);
            Assert.AreEqual("Display.cs", error.File);
            Assert.AreEqual("Display", error.Routine);
            Assert.AreEqual("41", error.Line);
            Assert.AreEqual("Display finally 667.", Assert.ContainsSingle(notices).MessageText);
            Assert.AreEqual(42, await ScalarAsync(connection, "SELECT 42"));
        }

        await ExecuteAsync(connection, "SET ankus_guc_show.count = '9'");
        Assert.AreEqual("display=9;native=9;extra=none", await ScalarAsync(connection, "SHOW ankus_guc_show.count"));
    }

    /// <summary>
    /// An assignment error or explicit terminal report terminates only the affected backend after managed unwind.
    /// </summary>
    /// <param name="value">The accepted value selecting ERROR or FATAL reporting.</param>
    [TestMethod]
    [DataRow(667)]
    [DataRow(668)]
    public async Task AssignmentReportsTerminateTheAffectedBackend(int value)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await ExecuteAsync(connection, "LOAD 'Ankus.GucAssignExtension'");
        await AssertFatalAsync(connection, $"SET ankus_guc_assign.count = '{value}'",
            "Assignment requested termination.", $"Assignment finally {value}.", "Managed frames unwind first.");
    }

    /// <summary>
    /// Show hooks do not downgrade an explicit FATAL to a recoverable display error.
    /// </summary>
    [TestMethod]
    public async Task ShowFatalPreservesTerminalSeverity()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await ExecuteAsync(connection, "LOAD 'Ankus.GucShowExtension'; SET ankus_guc_show.count = '668'");
        await AssertFatalAsync(connection, "SHOW ankus_guc_show.count",
            "Display requested termination.", "Display finally 668.", "Managed frames unwind first.");
    }

    /// <summary>
    /// Check hooks preserve explicit terminal logging rather than converting it into an ordinary validation rejection.
    /// </summary>
    [TestMethod]
    public async Task CheckFatalPreservesTerminalSeverity()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await ExecuteAsync(connection, "LOAD 'Ankus.TestExtension'; SET ankus_guc.control = 'check-log-fatal'");
        await AssertFatalAsync(connection, "SET ankus_guc.hook_int = '20'",
            "Check requested termination.", "Check finally.", null);
    }

    /// <summary>
    /// Configuration reload check hooks can log before transaction startup while keeping SQL unavailable.
    /// </summary>
    [TestMethod]
    public async Task ReloadCheckLogsWithoutTransactionAccess()
    {
        PostgresTestClusterOptions defaults = await IntegrationEnvironment.CreateOptionsAsync(context.CancellationToken);
        var options = new PostgresTestClusterOptions
        {
            Installation = defaults.Installation,
            DataDirectoryBase = defaults.DataDirectoryBase,
            LogDirectory = defaults.LogDirectory,
            PostgreSqlConfiguration = [.. defaults.PostgreSqlConfiguration, "session_preload_libraries = 'Ankus.GucHooksExtension'"],
        };
        await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(options, context.CancellationToken);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(context.CancellationToken);
        await using NpgsqlConnection administrator = await cluster.OpenConnectionAsync(context.CancellationToken);
        var notices = new List<PostgresNotice>();
        connection.Notice += (_, args) => notices.Add(args.Notice);
        var probes = new List<NpgsqlCommand>();
        try
        {
            for (int index = 0; index < 100; index++)
            {
                var probe = new NpgsqlCommand($"SELECT {index}", connection);
                probes.Add(probe);
                await probe.PrepareAsync(context.CancellationToken);
            }

            await ExecuteAsync(administrator, "ALTER SYSTEM SET ankus_guc_hooks.enabled = 'off'");
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await ScalarAsync(administrator, "SELECT pg_reload_conf()")));
            // Close/Sync wakes command processing without starting a SQL transaction.
            foreach (NpgsqlCommand probe in probes)
            {
                await probe.UnprepareAsync(context.CancellationToken);
                if (notices.Count != 0)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), context.CancellationToken);
            }
        }
        finally
        {
            foreach (NpgsqlCommand probe in probes)
            {
                await probe.DisposeAsync();
            }
        }

        PostgresNotice notice = Assert.ContainsSingle(notices);
        Assert.AreEqual("Ankus hooks-only check entered.", notice.MessageText);
        Assert.AreEqual($"source=File;sql=unavailable;pid={connection.ProcessID}", notice.Detail);
        Assert.AreEqual("off", await ScalarAsync(connection, "SHOW ankus_guc_hooks.enabled"));
        Assert.AreEqual(42, await ScalarAsync(connection, "SELECT 42"));
    }

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

    private async Task<PostgresException> FailureAsync(NpgsqlConnection connection, string sql)
        => await Assert.ThrowsExactlyAsync<PostgresException>(() => ExecuteAsync(connection, sql));

    private async Task AssertFatalAsync(NpgsqlConnection connection, string sql, string message, string finallyMessage, string? detail)
    {
        string session = "ankus-guc-terminal-" + Guid.NewGuid().ToString("N");
        await ExecuteAsync(connection, $"SET application_name = '{session}'; SET log_error_verbosity = verbose; SET log_min_messages = notice");
        await using NpgsqlConnection observer = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int observerProcess = observer.ProcessID;
        var notices = new List<PostgresNotice>();
        connection.Notice += (_, args) => notices.Add(args.Notice);
        NpgsqlException failure = await Assert.ThrowsAsync<NpgsqlException>(() => ExecuteAsync(connection, sql));
        if (failure is PostgresException error)
        {
            Assert.AreEqual("FATAL", error.InvariantSeverity);
            Assert.AreEqual("P0001", error.SqlState);
            Assert.AreEqual(message, error.MessageText);
            Assert.AreEqual(detail, error.Detail);
            Assert.AreEqual(finallyMessage, Assert.ContainsSingle(notices).MessageText);
        }
        else
        {
            // PostgreSQL has already flushed FATAL, but Windows can reset the closing socket before it is read.
            Assert.IsTrue(OperatingSystem.IsWindows());
            IOException transport = Assert.IsInstanceOfType<IOException>(failure.InnerException);
            SocketException socket = Assert.IsInstanceOfType<SocketException>(transport.InnerException);
            Assert.AreEqual(SocketError.ConnectionReset, socket.SocketErrorCode);
            if (notices.Count != 0)
            {
                Assert.AreEqual(finallyMessage, Assert.ContainsSingle(notices).MessageText);
            }
        }

        string log = string.Join('\n', PostgresFixture.Cluster.ReadServerLog().Split('\n')
            .Where(line => line.Contains($"[{session}]:", StringComparison.Ordinal)));
        string terminal = "FATAL:  P0001: " + message;
        Assert.Contains(terminal, log);
        Assert.Contains(finallyMessage, log);
        Assert.IsLessThan(log.IndexOf(terminal, StringComparison.Ordinal), log.IndexOf(finallyMessage, StringComparison.Ordinal));
        if (detail is not null)
        {
            Assert.Contains("DETAIL:  " + detail, log);
        }

        Assert.AreEqual(System.Data.ConnectionState.Closed, connection.State);
        Assert.AreEqual(42, await ScalarAsync(observer, "SELECT 42"));
        Assert.AreEqual(observerProcess, await ScalarAsync(observer, "SELECT pg_backend_pid()"));
        await using NpgsqlConnection healthy = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual(42, await ScalarAsync(healthy, "SELECT 42"));
    }
}
