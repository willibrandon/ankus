using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;

namespace Ankus.Testing;

/// <summary>
/// Retains only this cluster's native file and Windows event diagnostics in one readable log.
/// </summary>
/// <param name="filePath">The retained diagnostic file outside the cluster's data directory.</param>
internal sealed class PostgresTestLog(string filePath)
{
    /// <summary>
    /// Serializes event cursors and retained snapshots across parallel backend tests.
    /// </summary>
    private readonly Lock _readLock = new();

    /// <summary>
    /// Retains original event insertion strings without depending on a registered message resource.
    /// </summary>
    private readonly StringBuilder _events = new();

    /// <summary>
    /// Identifies the last consumed Application record without rereading earlier diagnostics.
    /// </summary>
    private long _lastRecord;

    /// <summary>
    /// Gets the file held open by PostgreSQL, separately from the combined Windows snapshot.
    /// </summary>
    internal string NativeFilePath { get; } = OperatingSystem.IsWindows() ? filePath + ".stderr.log" : filePath;

    /// <summary>
    /// Gets this invocation's distinct native provider name, without registry changes or machine identifiers.
    /// </summary>
    internal string EventSource { get; } = "Ankus-" + Guid.NewGuid().ToString("N");

    /// <summary>
    /// Reads current native diagnostics and retains a combined Windows snapshot for later inspection.
    /// </summary>
    internal string Read()
    {
        if (!OperatingSystem.IsWindows())
        {
            return ReadFile(NativeFilePath);
        }

        lock (_readLock)
        {
            ReadEvents();
            string text = ReadFile(NativeFilePath) + _events;
            if (text.Length != 0 || File.Exists(NativeFilePath))
            {
                File.WriteAllText(filePath, text);
            }

            return text;
        }
    }

    /// <summary>
    /// Reads only this cluster's provider and new record identities from the native Application log.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private void ReadEvents()
    {
        string query = string.Create(CultureInfo.InvariantCulture,
            $"*[System[Provider[@Name='{EventSource}'] and EventRecordID > {_lastRecord}]]");
        using var reader = new EventLogReader(new EventLogQuery("Application", PathType.LogName, query));
        for (EventRecord? entry = reader.ReadEvent(); entry is not null; entry = reader.ReadEvent())
        {
            using (entry)
            {
                foreach (EventProperty property in entry.Properties)
                {
                    if (property.Value is string message)
                    {
                        _events.Append(message);
                    }
                }

                _lastRecord = entry.RecordId ?? throw new InvalidOperationException("A PostgreSQL event has no record identity.");
            }
        }
    }

    /// <summary>
    /// Reads a live native log while preserving the server's write and cleanup handles.
    /// </summary>
    private static string ReadFile(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
