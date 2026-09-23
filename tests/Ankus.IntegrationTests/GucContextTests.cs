using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies configuration source permissions against PostgreSQL's native context checks.
/// </summary>
/// <param name="context">The test cancellation and diagnostic context.</param>
[TestClass]
public sealed class GucContextTests(TestContext context)
{
    /// <summary>
    /// Ordinary startup options can set Backend values while SuperuserBackend values require a parameter grant.
    /// </summary>
    [TestMethod]
    public async Task ClientOptionsRespectBackendPrivilegeAndParameterGrant()
    {
        PostgresTestClusterOptions options = await OptionsAsync();
        await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(options, context.CancellationToken);
        await using NpgsqlConnection administrator = await cluster.OpenConnectionAsync(context.CancellationToken);
        string role = "guc_client_" + Guid.NewGuid().ToString("N");
        await ExecuteAsync(administrator, $"CREATE ROLE {role} LOGIN");
        var builder = new NpgsqlConnectionStringBuilder(administrator.ConnectionString)
        {
            Username = role,
            Pooling = false,
            Options = "-c ankus_configuration.connection=33",
        };
        await using var ordinary = new NpgsqlConnection(builder.ConnectionString);
        await ordinary.OpenAsync(context.CancellationToken);
        Assert.AreEqual(role, await ScalarAsync(ordinary, "SELECT current_user"));
        Assert.AreEqual("33|backend|client|33", await ScalarAsync(ordinary,
            "SELECT concat_ws('|', setting, context, source, reset_val) FROM pg_settings WHERE name = 'ankus_configuration.connection'"));
        Assert.AreEqual("40", await ScalarAsync(ordinary, "SHOW ankus_configuration.privileged_connection"));

        builder.Options += " -c ankus_configuration.privileged_connection=43";
        await using (var denied = new NpgsqlConnection(builder.ConnectionString))
        {
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => denied.OpenAsync(context.CancellationToken));
            Assert.AreEqual("42501", error.SqlState);
            Assert.AreEqual("FATAL", error.InvariantSeverity);
            Assert.AreEqual("permission denied to set parameter \"ankus_configuration.privileged_connection\"", error.MessageText);
        }

        Assert.AreEqual(42, await ScalarAsync(ordinary, "SELECT 42"));
        Assert.AreEqual("40", await ScalarAsync(ordinary, "SHOW ankus_configuration.privileged_connection"));
        await ExecuteAsync(administrator, $"GRANT SET ON PARAMETER ankus_configuration.privileged_connection TO {role}");
        await using var granted = new NpgsqlConnection(builder.ConnectionString);
        await granted.OpenAsync(context.CancellationToken);
        Assert.AreEqual(role, await ScalarAsync(granted, "SELECT current_user"));
        Assert.AreEqual("33", await ScalarAsync(granted, "SHOW ankus_configuration.connection"));
        Assert.AreEqual("43|superuser-backend|client|43", await ScalarAsync(granted,
            "SELECT concat_ws('|', setting, context, source, reset_val) FROM pg_settings WHERE name = 'ankus_configuration.privileged_connection'"));
        Assert.AreEqual("40", await ScalarAsync(administrator, "SHOW ankus_configuration.privileged_connection"));
    }

    /// <summary>
    /// PostgreSQL accepts manually supplied file values for this flag while rejecting ALTER SYSTEM writes.
    /// </summary>
    [TestMethod]
    public async Task DisallowInFilePreservesNativeFileAndAlterSystemBehavior()
    {
        PostgresTestClusterOptions options = await OptionsAsync("ankus_configuration.no_file = 8");
        await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(options, context.CancellationToken);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("8", await ScalarAsync(connection, "SHOW ankus_configuration.no_file"));
        Assert.AreEqual("8|1|8|configuration file", await ScalarAsync(connection,
            "SELECT concat_ws('|', setting, boot_val, reset_val, source) FROM pg_settings WHERE name = 'ankus_configuration.no_file'"));
        Assert.AreEqual("8|true|", await ScalarAsync(connection,
            "SELECT setting || '|' || applied::text || '|' || coalesce(error, '') FROM pg_file_settings WHERE name = 'ankus_configuration.no_file'"));

        await using var command = new NpgsqlCommand("ALTER SYSTEM SET ankus_configuration.no_file = '9'", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(context.CancellationToken));
        Assert.AreEqual("55P02", error.SqlState);
        Assert.AreEqual("ERROR", error.InvariantSeverity);
        Assert.AreEqual("parameter \"ankus_configuration.no_file\" cannot be changed", error.MessageText);
        Assert.AreEqual("8", await ScalarAsync(connection, "SHOW ankus_configuration.no_file"));
        Assert.AreEqual("8", await ScalarAsync(connection, "SELECT setting FROM pg_file_settings WHERE name = 'ankus_configuration.no_file'"));
        await ExecuteAsync(connection, "SET ankus_configuration.no_file = '9'");
        Assert.AreEqual("9|8|session", await ScalarAsync(connection,
            "SELECT concat_ws('|', setting, reset_val, source) FROM pg_settings WHERE name = 'ankus_configuration.no_file'"));
        await ExecuteAsync(connection, "RESET ankus_configuration.no_file");
        Assert.AreEqual("8|8|configuration file", await ScalarAsync(connection,
            "SELECT concat_ws('|', setting, reset_val, source) FROM pg_settings WHERE name = 'ankus_configuration.no_file'"));
        Assert.AreEqual(42, await ScalarAsync(connection, "SELECT 42"));
    }

    /// <summary>
    /// Builds an isolated cluster configuration with native-only shared preload and optional file values.
    /// </summary>
    /// <param name="settings">Additional native configuration-file entries.</param>
    /// <returns>The isolated cluster options.</returns>
    private async Task<PostgresTestClusterOptions> OptionsAsync(params string[] settings)
    {
        PostgresTestClusterOptions defaults = await IntegrationEnvironment.CreateOptionsAsync(context.CancellationToken);
        return new PostgresTestClusterOptions
        {
            Installation = defaults.Installation,
            DataDirectoryBase = defaults.DataDirectoryBase,
            LogDirectory = defaults.LogDirectory,
            PostgreSqlConfiguration =
            [
                .. defaults.PostgreSqlConfiguration,
                "shared_preload_libraries = 'Ankus.Examples.Configuration'",
                .. settings,
            ],
        };
    }

    /// <summary>
    /// Executes a scalar SQL assertion input on the specified backend.
    /// </summary>
    /// <param name="connection">The backend connection.</param>
    /// <param name="sql">The SQL text.</param>
    /// <returns>The first scalar result.</returns>
    private async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(context.CancellationToken);
    }

    /// <summary>
    /// Executes a native configuration or role change on the specified backend.
    /// </summary>
    /// <param name="connection">The backend connection.</param>
    /// <param name="sql">The SQL text.</param>
    private async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(context.CancellationToken);
    }
}
