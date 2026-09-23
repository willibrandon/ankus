using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies native prefix registration, placeholder handling, and literal name matching.
/// </summary>
/// <param name="context">The test cancellation and diagnostic context.</param>
[TestClass]
public sealed class GucPrefixTests(TestContext context)
{
    /// <summary>
    /// Definitions adopt their placeholders before prefix cleanup removes only unknown settings.
    /// </summary>
    [TestMethod]
    public async Task DeclaredSettingsAreAdoptedBeforeUnknownPlaceholdersAreRemoved()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        var notices = new List<PostgresNotice>();
        connection.Notice += (_, args) => notices.Add(args.Notice);
        await ExecuteAsync(connection, "SET ankus_guc_only.enabled = 'off'; SET ankus_guc_only.misspelled = 'retained until load'");
        await ExecuteAsync(connection, "LOAD 'Ankus.GucOnlyExtension'");
        PostgresNotice warning = Assert.ContainsSingle(notices);
        Assert.AreEqual("WARNING", warning.InvariantSeverity);
        Assert.AreEqual("42602", warning.SqlState);
        Assert.Contains("ankus_guc_only.misspelled", warning.MessageText);
        Assert.AreEqual("off", await ScalarAsync(connection, "SHOW ankus_guc_only.enabled"));
        Assert.AreEqual(DBNull.Value, await ScalarAsync(connection, "SELECT current_setting('ankus_guc_only.misspelled', true)"));
        PostgresException reserved = await FailureAsync(connection, "SET ankus_guc_only.other = '1'");
        Assert.AreEqual("42602", reserved.SqlState);
        Assert.Contains("reserved prefix", reserved.Detail ?? string.Empty);
        await ExecuteAsync(connection, "SET ankus_guc_only.enabled = 'on'; LOAD 'Ankus.GucOnlyExtension'");
        Assert.AreEqual("on", await ScalarAsync(connection, "SHOW ankus_guc_only.enabled"));
        Assert.HasCount(1, notices);
    }

    /// <summary>
    /// Prefix-only libraries preserve native case sensitivity and dotted-prefix cleanup semantics.
    /// </summary>
    [TestMethod]
    public async Task PrefixOnlyLibraryPreservesLiteralCaseAndFirstComponentReservation()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        var notices = new List<PostgresNotice>();
        connection.Notice += (_, args) => notices.Add(args.Notice);
        await ExecuteAsync(connection,
            "SELECT set_config('ankus_reserved.before', '1', false), set_config('Ankus_Case.before', '2', false), " +
            "set_config('ankus_case.lowercase', '3', false), set_config('ankus_nested.scope.before', '4', false), " +
            "set_config('ankus_reserved_suffix.retained', '5', false)");
        await ExecuteAsync(connection, "LOAD 'Ankus.GucPrefixExtension'");
        Assert.HasCount(3, notices);
        Assert.AreSequenceEqual<string>(
            ["Ankus_Case.before", "ankus_nested.scope.before", "ankus_reserved.before"],
            notices.Select(notice => notice.MessageText.Split('"')[1]).Order(StringComparer.Ordinal));
        Assert.IsTrue(notices.All(notice => notice.SqlState == "42602" && notice.InvariantSeverity == "WARNING"));
        Assert.AreEqual(DBNull.Value, await ScalarAsync(connection, "SELECT current_setting('ankus_reserved.before', true)"));
        Assert.AreEqual(DBNull.Value, await ScalarAsync(connection, "SELECT current_setting('Ankus_Case.before', true)"));
        Assert.AreEqual(DBNull.Value, await ScalarAsync(connection, "SELECT current_setting('ankus_nested.scope.before', true)"));
        Assert.AreEqual("3", await ScalarAsync(connection, "SHOW ankus_case.lowercase"));
        Assert.AreEqual("5", await ScalarAsync(connection, "SHOW ankus_reserved_suffix.retained"));
        Assert.AreEqual("42602", (await FailureAsync(connection, "SELECT set_config('Ankus_Case.after', '6', false)")).SqlState);
        Assert.AreEqual("42602", (await FailureAsync(connection, "SET ankus_reserved.after = '6'")).SqlState);
        Assert.AreEqual("6", await ScalarAsync(connection, "SELECT set_config('ankus_case.after', '6', false)"));
        Assert.AreEqual("7", await ScalarAsync(connection, "SELECT set_config('ANKUS_RESERVED.after', '7', false)"));
        Assert.AreEqual("8", await ScalarAsync(connection, "SELECT set_config('ankus_nested.scope.after', '8', false)"));
        Assert.AreEqual(42, await ScalarAsync(connection, "SELECT 42"));
    }

    /// <summary>
    /// Reservation and placeholder removal survive rollback and repeated library loading.
    /// </summary>
    [TestMethod]
    public async Task ReservationSurvivesRollbackWithPlaceholderHistory()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await ExecuteAsync(connection, "SET ankus_reserved.stacked = '1'");
        await ExecuteAsync(connection, "BEGIN; SET ankus_reserved.stacked = '2'; SAVEPOINT nested; SET LOCAL ankus_reserved.stacked = '3'");
        await ExecuteAsync(connection, "LOAD 'Ankus.GucPrefixExtension'; ROLLBACK TO nested; ROLLBACK");
        Assert.AreEqual(DBNull.Value, await ScalarAsync(connection, "SELECT current_setting('ankus_reserved.stacked', true)"));
        await ExecuteAsync(connection, "LOAD 'Ankus.GucPrefixExtension'");
        Assert.AreEqual("42602", (await FailureAsync(connection, "SET ankus_reserved.stacked = '4'")).SqlState);
        Assert.AreEqual(42, await ScalarAsync(connection, "SELECT 42"));
    }

    /// <summary>
    /// A library with only prefix declarations can preload without entering the managed runtime.
    /// </summary>
    [TestMethod]
    public async Task PrefixOnlySharedPreloadReservesNamesInEveryBackend()
    {
        PostgresTestClusterOptions defaults = await IntegrationEnvironment.CreateOptionsAsync(context.CancellationToken);
        var options = new PostgresTestClusterOptions
        {
            Installation = defaults.Installation,
            DataDirectoryBase = defaults.DataDirectoryBase,
            LogDirectory = defaults.LogDirectory,
            PostgreSqlConfiguration = [.. defaults.PostgreSqlConfiguration, "shared_preload_libraries = 'Ankus.GucPrefixExtension'"],
        };
        await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(options, context.CancellationToken);
        var processes = new HashSet<int>();
        for (int iteration = 0; iteration < 3; iteration++)
        {
            await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(context.CancellationToken);
            Assert.IsTrue(processes.Add(connection.ProcessID));
            Assert.AreEqual("42602", (await FailureAsync(connection, "SET ankus_reserved.new_setting = '1'")).SqlState);
            Assert.AreEqual("2", await ScalarAsync(connection, "SELECT set_config('other_extension.allowed', '2', false)"));
        }
    }

    /// <summary>
    /// A Unicode prefix alone rejects shared preload before reservation while remaining supported in a database backend.
    /// </summary>
    [TestMethod]
    public async Task PrefixOnlySharedPreloadRejectsUnicodeWithoutMetadataMasking()
    {
        PostgresTestClusterOptions defaults = await IntegrationEnvironment.CreateOptionsAsync(context.CancellationToken);
        var options = new PostgresTestClusterOptions
        {
            Installation = defaults.Installation,
            DataDirectoryBase = defaults.DataDirectoryBase,
            LogDirectory = defaults.LogDirectory,
            PostgreSqlConfiguration = [.. defaults.PostgreSqlConfiguration, "shared_preload_libraries = 'Ankus.GucUnicodePrefixExtension'"],
        };
        InvalidOperationException startup = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => PostgresTestCluster.StartAsync(options, context.CancellationToken));
        Assert.Contains("Ankus shared-preload configuration prefixes must be ASCII", startup.Message);
        Assert.Contains("Load the library in a backend process to use a non-ASCII prefix.", startup.Message);

        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await ExecuteAsync(connection, "LOAD 'Ankus.GucUnicodePrefixExtension'");
        PostgresException reserved = await FailureAsync(connection, "SELECT set_config('café.after', '1', false)");
        Assert.AreEqual("42602", reserved.SqlState);
        Assert.AreEqual("\"café\" is a reserved prefix.", reserved.Detail);
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
}
