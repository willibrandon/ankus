using System.Globalization;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies native failures require rollback despite ordinary managed exception handling.
/// </summary>
/// <param name="context">The per-test cancellation context.</param>
[TestClass]
public sealed class UnrecoveredErrorTests(TestContext context)
{
    /// <summary>
    /// Raw ERROR retains the original diagnostics, blocks later SQL and permits same-session use after PostgreSQL aborts.
    /// </summary>
    /// <param name="mode">The number of enclosing managed SPI sessions, or three for explicit recovery.</param>
    /// <param name="replace">Whether user code throws a different error after catching the native failure.</param>
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(0, true)]
    [DataRow(1, false)]
    [DataRow(1, true)]
    [DataRow(2, false)]
    [DataRow(2, true)]
    [DataRow(3, false)]
    [DataRow(3, true)]
    public async Task RawErrorsRequireRollback(int mode, bool replace)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand(
            "SELECT datatype.raw_call_caught(tests.raw_call_address(2), 'tests.raw_call_error(integer)'::regprocedure::oid, $1, $2)", connection);
        command.Parameters.AddWithValue(mode);
        command.Parameters.AddWithValue(replace);
        if (mode == 3)
        {
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        }
        else
        {
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual(PostgresErrorCodes.InvalidParameterValue, error.SqlState);
            Assert.AreEqual("raw call failure", error.MessageText);
            Assert.AreEqual("owned native detail", error.Detail);
            Assert.AreEqual("retry with a valid value", error.Hint);
        }

        command.Parameters.Clear();
        command.CommandText = "SELECT tests.raw_call_lock_held()";
        Assert.IsFalse(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
        command.CommandText = "SELECT datatype.unrecovered_error_observations()";
        Assert.AreEqual(mode == 3 ? 15 : 7, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT 42";
        Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Native memory ERROR follows the same rollback contract as raw calls and retains managed cleanup.
    /// </summary>
    /// <param name="recover">Whether the failing operation runs inside explicit recovery.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MemoryErrorsRequireRollback(bool recover)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        var settings = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        // Npgsql deliberately closes connections for class XX errors. PostgreSQL's
        // own client retains this connection and proves recovery in the same backend.
        ProcessResult result = await ProcessRunner.RunCheckedAsync(PostgresFixture.Cluster.Installation.PsqlPath,
            ["-X", "-A", "-t", "-w", "-v", "ON_ERROR_STOP=0", "-v", "VERBOSITY=verbose",
                "-c", "SELECT pg_backend_pid()",
                "-c", $"SELECT datatype.memory_error_caught({(recover ? "true" : "false")})",
                "-c", "SELECT datatype.unrecovered_error_observations()",
                "-c", "SELECT 42", "-c", "SELECT pg_backend_pid()"],
            new Dictionary<string, string?>
            {
                ["PGHOST"] = settings.Host,
                ["PGPORT"] = settings.Port.ToString(CultureInfo.InvariantCulture),
                ["PGUSER"] = settings.Username,
                ["PGDATABASE"] = settings.Database,
                ["PGPASSWORD"] = settings.Password,
                ["PGAPPNAME"] = nameof(MemoryErrorsRequireRollback),
            }, token);
        string[] lines = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        Assert.HasCount(recover ? 5 : 4, lines);
        Assert.IsGreaterThan(0, int.Parse(lines[0], CultureInfo.InvariantCulture));
        Assert.AreEqual(lines[0], lines[^1]);
        if (recover)
        {
            Assert.AreSequenceEqual<string>(["42", "15", "42"], lines[1..^1]);
            Assert.IsEmpty(result.StandardError);
        }
        else
        {
            Assert.AreSequenceEqual<string>(["7", "42"], lines[1..^1]);
            Assert.Contains("ERROR:  XX000: invalid memory alloc request size 1073741824", result.StandardError);
            Assert.DoesNotContain("FATAL:", result.StandardError);
            Assert.DoesNotContain("PANIC:", result.StandardError);
        }
    }
}
