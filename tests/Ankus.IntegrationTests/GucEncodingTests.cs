using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies native configuration metadata and hook conversions in a non-UTF8 database.
/// </summary>
/// <param name="context">The test cancellation and diagnostic context.</param>
[TestClass]
[DoNotParallelize]
public sealed class GucEncodingTests(TestContext context)
{
    /// <summary>
    /// Conversion failure while reconstructing a terminal hook diagnostic preserves FATAL after managed finally.
    /// </summary>
    /// <param name="library">The library exposing the selected hook phase.</param>
    /// <param name="setup">The setting changes which precede the terminal callback.</param>
    /// <param name="trigger">The command which invokes the terminal hook.</param>
    /// <param name="marker">The managed finally notice received before termination.</param>
    [TestMethod]
    [DataRow("Ankus.GucAssignExtension", "SELECT 1", "SET ankus_guc_assign.count = '669'", "Assignment finally 669.")]
    [DataRow("Ankus.GucShowExtension", "SET ankus_guc_show.count = '669'", "SHOW ankus_guc_show.count", "Display finally 669.")]
    [DataRow("Ankus.TestExtension", "SET ankus_guc.control = 'check-log-fatal-unrepresentable'", "SET ankus_guc.hook_int = '20'", "Check finally.")]
    public async Task Latin1TerminalDiagnosticConversionKeepsFatalSeverity(string library, string setup, string trigger, string marker)
    {
        CancellationToken token = context.CancellationToken;
        string database = "guc_terminal_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection administrator = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using (var create = new NpgsqlCommand(
            $"CREATE DATABASE {database} TEMPLATE template0 ENCODING 'LATIN1' LC_COLLATE 'C' LC_CTYPE 'C'", administrator))
        {
            await create.ExecuteNonQueryAsync(token);
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(administrator.ConnectionString) { Database = database, Pooling = false };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(token);
            await using var command = new NpgsqlCommand($"LOAD '{library}'; {setup}", connection);
            await command.ExecuteNonQueryAsync(token);
            var notices = new List<PostgresNotice>();
            connection.Notice += (_, args) => notices.Add(args.Notice);
            command.CommandText = trigger;
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
            Assert.AreEqual("FATAL", error.InvariantSeverity);
            Assert.AreEqual("22P05", error.SqlState);
            Assert.AreEqual(marker, Assert.ContainsSingle(notices).MessageText);
            await using var healthy = new NpgsqlConnection(builder.ConnectionString);
            await healthy.OpenAsync(token);
            await using var probe = new NpgsqlCommand("SELECT 42", healthy);
            Assert.AreEqual(42, await probe.ExecuteScalarAsync(token));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", administrator);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Restricted-phase logging converts owned messages without a transaction and recovers after conversion errors.
    /// </summary>
    [TestMethod]
    public async Task Latin1LoggingPreservesAbortAndReportMessagesAndConversionRecovery()
    {
        CancellationToken token = context.CancellationToken;
        string database = "guc_logging_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection administrator = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using (var create = new NpgsqlCommand(
            $"CREATE DATABASE {database} TEMPLATE template0 ENCODING 'LATIN1' LC_COLLATE 'C' LC_CTYPE 'C'", administrator))
        {
            await create.ExecuteNonQueryAsync(token);
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(administrator.ConnectionString) { Database = database, Pooling = false };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(token);
            var notices = new List<PostgresNotice>();
            connection.Notice += (_, args) => notices.Add(args.Notice);
            await using var command = new NpgsqlCommand("LOAD 'Ankus.GucAssignExtension'", connection);
            await command.ExecuteNonQueryAsync(token);
            Assert.AreEqual("assign=7;old=7;sql=unavailable;café 100%", Assert.ContainsSingle(notices).MessageText);
            notices.Clear();
            command.CommandText = "BEGIN; SET LOCAL ankus_guc_assign.count = '9'; ROLLBACK";
            await command.ExecuteNonQueryAsync(token);
            Assert.AreSequenceEqual<string>(
            [
                "assign=9;old=7;sql=unavailable;café 100%",
                "assign=7;old=9;sql=unavailable;café 100%",
            ], notices.Select(notice => notice.MessageText));
            Assert.IsTrue(notices.All(notice => notice.Detail == "Owned détail"));
            notices.Clear();
            command.CommandText = "LOAD 'Ankus.TestExtension'; SET ankus_guc.control = 'logging'; SET ankus_guc.reported = '2'";
            await command.ExecuteNonQueryAsync(token);
            Assert.AreEqual("report=2;sql=unavailable;café 100%", Assert.ContainsSingle(notices).MessageText);
            Assert.AreEqual("report=2;sql=unavailable", connection.PostgresParameters["ankus_guc.reported"]);
            command.CommandText = "SET ankus_guc.control = 'log-unrepresentable'";
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SET client_min_messages = 'warning'; SET log_min_messages = 'warning'; SHOW ankus_guc.hook_int";
            Assert.AreEqual("integer=10;extra=0A000000", await command.ExecuteScalarAsync(token));
            command.CommandText = "SET client_min_messages = 'notice'";
            await command.ExecuteNonQueryAsync(token);
            for (int iteration = 0; iteration < 20; iteration++)
            {
                command.CommandText = "SHOW ankus_guc.hook_int";
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
                Assert.AreEqual("22P05", error.SqlState);
                Assert.AreEqual("ERROR", error.InvariantSeverity);
                command.CommandText = "SELECT 42";
                Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            }

            command.CommandText = "RESET ankus_guc.control; SHOW ankus_guc.hook_int";
            Assert.AreEqual("integer=10;extra=0A000000", await command.ExecuteScalarAsync(token));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", administrator);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Native prefix conversion removes and reserves non-ASCII names in the database encoding.
    /// </summary>
    [TestMethod]
    public async Task Latin1PrefixReservationUsesDatabaseEncoding()
    {
        CancellationToken token = context.CancellationToken;
        string database = "guc_prefix_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection administrator = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using (var create = new NpgsqlCommand(
            $"CREATE DATABASE {database} TEMPLATE template0 ENCODING 'LATIN1' LC_COLLATE 'C' LC_CTYPE 'C'", administrator))
        {
            await create.ExecuteNonQueryAsync(token);
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(administrator.ConnectionString) { Database = database, Pooling = false };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(token);
            await using var command = new NpgsqlCommand("SELECT set_config('café.before', '1', false)", connection);
            Assert.AreEqual("1", await command.ExecuteScalarAsync(token));
            var notices = new List<PostgresNotice>();
            connection.Notice += (_, args) => notices.Add(args.Notice);
            command.CommandText = "LOAD 'Ankus.GucOnlyExtension'";
            await command.ExecuteNonQueryAsync(token);
            PostgresNotice warning = Assert.ContainsSingle(notices);
            Assert.AreEqual("42602", warning.SqlState);
            Assert.Contains("café.before", warning.MessageText);
            command.CommandText = "SELECT current_setting('café.before', true)";
            Assert.AreEqual(DBNull.Value, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT set_config('café.after', '2', false)";
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual("42602", error.SqlState);
            Assert.AreEqual("\"café\" is a reserved prefix.", error.Detail);
            command.CommandText = "SHOW ankus_guc_only.enabled";
            Assert.AreEqual("on", await command.ExecuteScalarAsync(token));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", administrator);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// LATIN1 values survive check normalization, assign restoration, show, and reporting outside transactions.
    /// </summary>
    [TestMethod]
    public async Task Latin1HooksPreserveValuesDuringRollbackAndReporting()
    {
        CancellationToken token = context.CancellationToken;
        string database = "guc_latin1_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection administrator = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using (var create = new NpgsqlCommand(
            $"CREATE DATABASE {database} TEMPLATE template0 ENCODING 'LATIN1' LC_COLLATE 'C' LC_CTYPE 'C'", administrator))
        {
            await create.ExecuteNonQueryAsync(token);
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(administrator.ConnectionString) { Database = database, Pooling = false };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(token);
            await using var command = new NpgsqlCommand("CREATE EXTENSION ankus_test; LOAD 'Ankus.TestExtension'", connection);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT extra_desc FROM pg_settings WHERE name = 'ankus_guc.enabled'";
            Assert.AreEqual("A retained description with café.", await command.ExecuteScalarAsync(token));
            command.CommandText = "SET ankus_guc.text = 'café'; SELECT guc_retained_text(true)";
            Assert.AreEqual("café", await command.ExecuteScalarAsync(token));
            command.CommandText = "SET ankus_guc.hook_text = '  café  '; SHOW ankus_guc.hook_text";
            Assert.AreEqual("text=café;extra=2020636166C3A92020", await command.ExecuteScalarAsync(token));

            // Use only ASCII protocol data after switching the client encoding: PostgreSQL constructs
            // non-ASCII values, and assertions return Boolean results rather than encoded text.
            command.CommandText = "SET client_encoding = 'LATIN1'; BEGIN; SELECT set_config('ankus_guc.hook_text', chr(233), true) IS NOT NULL";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "ROLLBACK; SET ankus_guc.reported = '2'";
            await command.ExecuteNonQueryAsync(token);
            Assert.AreEqual("report=2;sql=unavailable", connection.PostgresParameters["ankus_guc.reported"]);
            command.CommandText = "SELECT guc_hook_values() = ('False|10|1.25|caf' || chr(233) || '|18446744073709551615')";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT current_setting('ankus_guc.hook_text') = ('text=caf' || chr(233) || ';extra=2020636166C3A92020')";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "RESET ankus_guc.text; SELECT guc_retained_text(false) = ('caf' || chr(233))";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SET ankus_guc.hook_int = '13'";
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
            Assert.AreEqual("22P05", error.SqlState);
            command.CommandText = "SET ankus_guc.hook_int = '20'; SHOW ankus_guc.hook_int";
            Assert.AreEqual("integer=20;extra=14000000", await command.ExecuteScalarAsync(token));
            command.CommandText = "SET ankus_guc.control = 'unrepresentable'";
            await command.ExecuteNonQueryAsync(token);
            for (int iteration = 0; iteration < 25; iteration++)
            {
                command.CommandText = "SET ankus_guc.hook_text = 'ascii'";
                PostgresException conversion = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
                Assert.AreEqual("22P05", conversion.SqlState);
                command.CommandText = "SELECT current_setting('ankus_guc.hook_text') = ('text=caf' || chr(233) || ';extra=2020636166C3A92020')";
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            }

            command.CommandText = "RESET ankus_guc.control; SET ankus_guc.hook_text = 'recovered'; SHOW ankus_guc.hook_text";
            Assert.AreEqual("text=recovered;extra=7265636F7665726564", await command.ExecuteScalarAsync(token));
            command.CommandText = "SET client_encoding = 'UTF8'; SELECT 42";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", administrator);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
