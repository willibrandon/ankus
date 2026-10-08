using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies configuration parameters defined at run time through PostgreSQL's own configuration machinery.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class RuntimeGucTests(TestContext context)
{
    /// <summary>
    /// Load-time definitions carry their metadata into pg_settings, and managed readers follow SET, SET LOCAL and RESET.
    /// </summary>
    [TestMethod]
    public async Task LoadTimeDefinitionsBehaveAsPostgresSettings()
    {
        await using NpgsqlConnection connection = await OpenAsync();
        Assert.AreEqual("True|10|0.25|<null>|1", await ScalarAsync(connection, "SELECT datatype.runtime_guc_values()"));
        await using (var metadata = new NpgsqlCommand("""
            SELECT string_agg(concat_ws(',', name, vartype, context, coalesce(unit, '-'), coalesce(min_val, '-'), coalesce(max_val, '-'),
                short_desc, coalesce(extra_desc, '-'), coalesce(array_to_string(enumvals, '/'), '-'), coalesce(boot_val, '<null>')), ';' ORDER BY name)
            FROM pg_catalog.pg_settings WHERE name LIKE 'ankus_runtime.%'
            """, connection))
        {
            Assert.AreEqual(
                "ankus_runtime.enabled,bool,user,-,-,-,Run-time Boolean,Defined after the name is computed.,-,on;" +
                "ankus_runtime.label,string,superuser,-,-,-,Run-time string,-,-,<null>;" +
                "ankus_runtime.limit,integer,user,kB,1,4096,Run-time integer,-,-,10;" +
                "ankus_runtime.mode,enum,user,-,-,-,Run-time enumeration,-,off/on/auto,on;" +
                "ankus_runtime.ratio,real,user,-,0,1,Run-time real,-,-,0.25",
                await metadata.ExecuteScalarAsync(context.CancellationToken));
        }

        await ExecuteAsync(connection, "SET \"ankus_runtime.enabled\" = off; SET \"ankus_runtime.limit\" = '2MB'; SET \"ankus_runtime.ratio\" = 0.5; " +
            "SET \"ankus_runtime.label\" = 'café'; SET \"ankus_runtime.mode\" = 'TRUE'");
        Assert.AreEqual("False|2048|0.5|café|1", await ScalarAsync(connection, "SELECT datatype.runtime_guc_values()"));
        Assert.AreEqual("on", await ScalarAsync(connection, "SHOW \"ankus_runtime.mode\""));
        await ExecuteAsync(connection, "BEGIN; SET LOCAL \"ankus_runtime.mode\" = auto; SET LOCAL \"ankus_runtime.limit\" = 3");
        Assert.AreEqual("False|3|0.5|café|2", await ScalarAsync(connection, "SELECT datatype.runtime_guc_values()"));
        await ExecuteAsync(connection, "ROLLBACK");
        Assert.AreEqual("False|2048|0.5|café|1", await ScalarAsync(connection, "SELECT datatype.runtime_guc_values()"));
        await ExecuteAsync(connection, "RESET ALL");
        Assert.AreEqual("True|10|0.25|<null>|1", await ScalarAsync(connection, "SELECT datatype.runtime_guc_values()"));

        PostgresException range = await Assert.ThrowsExactlyAsync<PostgresException>(() => ExecuteAsync(connection, "SET \"ankus_runtime.limit\" = 5000"));
        Assert.AreEqual(PostgresErrorCodes.InvalidParameterValue, range.SqlState);
        PostgresException label = await Assert.ThrowsExactlyAsync<PostgresException>(() => ExecuteAsync(connection, "SET \"ankus_runtime.mode\" = maybe"));
        Assert.AreEqual(PostgresErrorCodes.InvalidParameterValue, label.SqlState);
    }

    /// <summary>
    /// Definitions made after load adopt earlier placeholders, repeat idempotently, and reject conflicting redefinitions.
    /// </summary>
    [TestMethod]
    public async Task LaterDefinitionsAdoptPlaceholdersAndRejectConflicts()
    {
        await using NpgsqlConnection connection = await OpenAsync();
        string name = "ankus_runtime.late_" + Guid.NewGuid().ToString("N");
        await ExecuteAsync(connection, $"SET \"{name}\" = '7'");
        Assert.AreEqual(7, await ScalarAsync(connection, $"SELECT datatype.runtime_guc_define_int('{name}', 3, 9, 6)"));
        Assert.AreEqual(7, await ScalarAsync(connection, $"SELECT datatype.runtime_guc_define_int('{name}', 3, 9, 6)"));
        await ExecuteAsync(connection, $"RESET \"{name}\"");
        Assert.AreEqual("3", await ScalarAsync(connection, $"SHOW \"{name}\""));

        PostgresException conflict = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
            ScalarAsync(connection, $"SELECT datatype.runtime_guc_define_int('{name}', 3, 8, 6)"));
        Assert.AreEqual(PostgresErrorCodes.DuplicateObject, conflict.SqlState);
        Assert.Contains("already defined with different properties", conflict.MessageText);
        PostgresException attribute = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
            ScalarAsync(connection, "SELECT datatype.runtime_guc_redefine_attribute()"));
        Assert.AreEqual(PostgresErrorCodes.DuplicateObject, attribute.SqlState);
        Assert.Contains("already declared by a PgGuc attribute", attribute.MessageText);
        Assert.AreEqual(42, await ScalarAsync(connection, "SELECT 42"));
    }

    /// <summary>
    /// A postmaster parameter outside shared_preload_libraries reports PostgreSQL's unsupported-feature error, and the
    /// session continues.
    /// </summary>
    [TestMethod]
    public async Task PostmasterDefinitionsRequireSharedPreload()
    {
        await using NpgsqlConnection connection = await OpenAsync();
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
            ScalarAsync(connection, "SELECT datatype.runtime_guc_define_int('ankus_runtime.postmaster', 1, 2, 1)"));
        Assert.AreEqual(PostgresErrorCodes.FeatureNotSupported, error.SqlState);
        Assert.Contains("shared_preload_libraries", error.MessageText);
        Assert.AreEqual("True|10|0.25|<null>|1", await ScalarAsync(connection, "SELECT datatype.runtime_guc_values()"));
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await ExecuteAsync(connection, "LOAD 'Ankus.TestExtension'");
        return connection;
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
}
