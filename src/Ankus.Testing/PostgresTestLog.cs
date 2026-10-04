using Ankus.PgConfig;

namespace Ankus.Testing;

/// <summary>
/// Supplies a distinct server identity to the shared PostgreSQL diagnostic collector.
/// </summary>
/// <param name="filePath">The retained log outside this invocation's data directory.</param>
internal sealed class PostgresTestLog(string filePath)
{
    /// <summary>
    /// Owns this invocation's diagnostics through the configuration package's deliberate public boundary.
    /// </summary>
    private readonly PostgresServerLog _log = new(filePath, Guid.NewGuid());

    /// <summary>
    /// Gets the stderr target held open by PostgreSQL.
    /// </summary>
    internal string NativeFilePath => _log.NativeFilePath;

    /// <summary>
    /// Gets this invocation's isolated event provider.
    /// </summary>
    internal string EventSource => _log.EventSource;

    /// <summary>
    /// Reads and retains this invocation's diagnostic snapshot.
    /// </summary>
    internal string Read() => _log.Read();
}
