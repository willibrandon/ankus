using System.Globalization;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class PgLogTests
{
    /// <summary>
    /// Literal severity helpers retain PostgreSQL's wire severity, default SQLSTATE and client/server routing.
    /// </summary>
    /// <param name="level">The selected helper.</param>
    /// <param name="severity">The independently expected server severity.</param>
    /// <param name="state">The independently expected default SQLSTATE.</param>
    [TestMethod]
    [DataRow(0, "DEBUG", "00000")]
    [DataRow(1, "DEBUG", "00000")]
    [DataRow(2, "DEBUG", "00000")]
    [DataRow(3, "DEBUG", "00000")]
    [DataRow(4, "DEBUG", "00000")]
    [DataRow(5, "LOG", "00000")]
    [DataRow(6, "LOG", "00000")]
    [DataRow(7, "INFO", "00000")]
    [DataRow(8, "NOTICE", "00000")]
    [DataRow(9, "WARNING", "01000")]
    public Task SeverityHelpersUsePostgresRouting(int level, string severity, string state)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SeverityHelpersUsePostgresRouting),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand("SET LOCAL client_min_messages = debug5; SET LOCAL log_min_messages = debug5",
                    connection, transaction);
                await command.ExecuteNonQueryAsync(token);
                var notices = new List<PostgresNotice>();
                connection.Notice += (_, args) => notices.Add(args.Notice);
                string marker = "helper literal %s %n café 🐘 " + Guid.NewGuid().ToString("N");
                command.CommandText = "SELECT datatype.log_helper($1, $2)";
                command.Parameters.AddWithValue(level);
                command.Parameters.AddWithValue(marker);
                Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
                PostgresNotice[] matching = [.. notices.Where(notice => notice.MessageText == marker)];
                Assert.HasCount(level == 6 ? 0 : 1, matching);
                if (level != 6)
                {
                    Assert.AreEqual(severity, matching[0].InvariantSeverity);
                    Assert.AreEqual(state, matching[0].SqlState);
                }

                Assert.Contains(severity + ":  " + marker, PostgresFixture.Cluster.ReadServerLog());
                Assert.AreEqual(backend, connection.ProcessID);
            }, context.CancellationToken);

    /// <summary>
    /// Catchable helper errors preserve default and optional fields, while unhandled errors unwind through the native boundary.
    /// </summary>
    /// <param name="structured">Whether the catchable error contains detail and hint fields.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ErrorHelpersPreserveDiagnosticsAndRecovery(bool structured)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        var settings = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        // Npgsql closes class XX connections before ReadyForQuery. PostgreSQL's
        // own client proves the default-code ERROR and recovery in one backend.
        ProcessResult result = await ProcessRunner.RunCheckedAsync(PostgresFixture.Cluster.Installation.PsqlPath,
            ["-X", "-q", "-A", "-t", "-w", "-v", "ON_ERROR_STOP=0", "-v", "VERBOSITY=verbose",
                "-c", "SELECT pg_backend_pid()",
                "-c", $"SELECT datatype.log_catch_helper_error({(structured ? "true" : "false")})",
                "-c", "CREATE TEMP TABLE helper_rollback(value int)",
                "-c", "BEGIN; INSERT INTO helper_rollback VALUES (99)",
                "-c", "SELECT datatype.log_helper(10, 'unhandled helper error')",
                "-c", "ROLLBACK",
                "-c", "SELECT count(*) FROM helper_rollback",
                "-c", "SELECT 42", "-c", "SELECT pg_backend_pid()"],
            new Dictionary<string, string?>
            {
                ["PGHOST"] = settings.Host,
                ["PGPORT"] = settings.Port.ToString(CultureInfo.InvariantCulture),
                ["PGUSER"] = settings.Username,
                ["PGDATABASE"] = settings.Database,
                ["PGPASSWORD"] = settings.Password,
                ["PGAPPNAME"] = nameof(ErrorHelpersPreserveDiagnosticsAndRecovery),
            }, token);
        string[] lines = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        Assert.HasCount(5, lines);
        Assert.IsGreaterThan(0, int.Parse(lines[0], CultureInfo.InvariantCulture));
        Assert.AreEqual(lines[0], lines[^1]);
        Assert.AreSequenceEqual<string>([structured ? "XX000|helper error|detail|hint|42" : "XX000|helper error|||42", "0", "42"], lines[1..^1]);
        Assert.Contains("ERROR:  XX000: unhandled helper error", result.StandardError);
    }
}
