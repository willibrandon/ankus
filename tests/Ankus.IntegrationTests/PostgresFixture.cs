using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Publishes the extension and owns one local PostgreSQL cluster for the assembly's parallel test sessions.
/// </summary>
internal static class PostgresFixture
{
    private static PostgresTestCluster? s_cluster;

    /// <summary>
    /// Gets the initialized assembly-scoped test cluster.
    /// </summary>
    internal static PostgresTestCluster Cluster => s_cluster ?? throw new InvalidOperationException("Cluster is not initialized.");

    /// <summary>
    /// Builds the extension, starts PostgreSQL, and registers the SQL functions used by the tests.
    /// </summary>
    /// <param name="context">The assembly test context.</param>
    internal static async Task InitializeAsync(TestContext context)
    {
        PostgresTestClusterOptions options = await IntegrationEnvironment.CreateOptionsAsync(context.CancellationToken);
        if (options.Installation.Version.Major != 18)
        {
            throw new InvalidOperationException("This integration suite publishes the sample for PostgreSQL 18.");
        }

        string library = await IntegrationEnvironment.PublishSampleAsync(context.CancellationToken);
        s_cluster = await PostgresTestCluster.StartAsync(options, context.CancellationToken);
        try
        {
            await using NpgsqlConnection connection = await Cluster.OpenConnectionAsync(context.CancellationToken);
            string libraryLiteral = library.Replace("\\", "/", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal);
            string generatedSql = await File.ReadAllTextAsync(Path.ChangeExtension(library, ".sql"), context.CancellationToken);
            generatedSql = generatedSql.Replace("MODULE_PATHNAME", libraryLiteral, StringComparison.Ordinal);
            string sql = """
                CREATE SCHEMA tests;
                CREATE TABLE tests.rollback_probe (value integer NOT NULL);
                CREATE FUNCTION tests.insert_probe() RETURNS void LANGUAGE sql AS
                    'INSERT INTO tests.rollback_probe VALUES (42)';
                CREATE FUNCTION tests.fail_probe() RETURNS integer LANGUAGE sql AS 'SELECT 1 / 0';
                """ + generatedSql;
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(context.CancellationToken);
        }
        catch
        {
            await Cluster.DisposeAsync();
            s_cluster = null;
            throw;
        }
    }

    /// <summary>
    /// Stops PostgreSQL even if tests failed or their cancellation token was canceled.
    /// </summary>
    internal static async Task CleanupAsync()
    {
        if (s_cluster is not null)
        {
            await s_cluster.DisposeAsync();
            s_cluster = null;
        }
    }
}
