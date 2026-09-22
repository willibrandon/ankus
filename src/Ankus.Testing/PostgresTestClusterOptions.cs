using Ankus.PgConfig;

namespace Ankus.Testing;

/// <summary>
/// Configures one isolated PostgreSQL test-cluster invocation.
/// </summary>
public sealed class PostgresTestClusterOptions
{
    /// <summary>
    /// Gets or sets the configured PostgreSQL installation used by the cluster.
    /// </summary>
    public required PostgresInstallation Installation { get; init; }

    /// <summary>
    /// Gets or sets the base directory under which per-invocation PGDATA directories
    /// are created.
    /// </summary>
    public string DataDirectoryBase { get; init; }
        = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "test-pgdata");

    /// <summary>
    /// Gets or sets the architecture-independent PostgreSQL data directory supplied
    /// to <c>initdb -L</c>.
    /// </summary>
    public string? SharedDirectory { get; init; }

    /// <summary>
    /// Gets or sets the database created for extension tests.
    /// </summary>
    public string DatabaseName { get; init; } = "ankus_tests";

    /// <summary>
    /// Gets the bootstrap database superuser name, independent of the operating system account name.
    /// </summary>
    public string UserName { get; init; } = "ankus";

    /// <summary>
    /// Gets the directory where server logs remain available after cluster shutdown.
    /// </summary>
    public string LogDirectory { get; init; }
        = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "test-logs");

    /// <summary>
    /// Gets additional settings appended to <c>postgresql.auto.conf</c> after the
    /// harness defaults, allowing extension settings to override them.
    /// </summary>
    public IReadOnlyList<string> PostgreSqlConfiguration { get; init; } = [];

    /// <summary>
    /// Gets environment variables added to every PostgreSQL process invocation.
    /// </summary>
    public IReadOnlyDictionary<string, string?> ProcessEnvironment { get; init; }
        = new Dictionary<string, string?>();

    /// <summary>
    /// Gets the maximum time allowed for initialization and server readiness.
    /// </summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the independent timeout used for shutdown, even when a test's cancellation token has been canceled.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
