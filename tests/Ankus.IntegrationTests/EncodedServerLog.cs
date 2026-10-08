using System.Text;
using Ankus.Testing;

namespace Ankus.IntegrationTests;

/// <summary>
/// Observes a report in a non-UTF-8 database through whichever sink PostgreSQL wrote it to.
/// </summary>
internal static class EncodedServerLog
{
    /// <summary>
    /// Captures the current ends of the native stderr file and the merged server log.
    /// </summary>
    /// <param name="cluster">The server whose log is observed.</param>
    /// <returns>The offsets after which a later report must appear.</returns>
    internal static (int Native, int Server) Mark(PostgresTestCluster cluster)
        => (ReadNative(cluster).Length, cluster.ReadServerLog().Length);

    /// <summary>
    /// Waits for a report, then requires its database-encoding bytes on native stderr or its exact text in the event log.
    /// </summary>
    /// <param name="cluster">The server whose log is observed.</param>
    /// <param name="mark">The offsets captured before the report.</param>
    /// <param name="message">The reported text, which must contain a non-ASCII character.</param>
    /// <param name="encoding">The database encoding of the reporting backend.</param>
    /// <param name="cancellationToken">Cancels waiting for the report.</param>
    /// <returns>A task that completes once the report has been verified.</returns>
    internal static async Task AssertReportedAsync(PostgresTestCluster cluster, (int Native, int Server) mark, string message,
        Encoding encoding, CancellationToken cancellationToken)
    {
        int split = message.AsSpan().IndexOfAnyExceptInRange('\0', '\x7f');
        Assert.IsGreaterThan(0, split, "The report must start with ASCII text and contain a non-ASCII character.");
        string prefix = message[..split];
        byte[] nativePrefix = Encoding.ASCII.GetBytes(prefix);
        byte[] expected = encoding.GetBytes(message);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            // Read the merged log first: a report absent from the later native read came from the event log.
            string server = cluster.ReadServerLog()[mark.Server..];
            byte[] native = ReadNative(cluster)[mark.Native..];
            if (native.AsSpan().IndexOf(nativePrefix) >= 0)
            {
                Assert.IsGreaterThanOrEqualTo(0, native.AsSpan().IndexOf(expected),
                    "Native stderr must carry the report in the database encoding.");
                return;
            }

            if (server.Contains(prefix, StringComparison.Ordinal))
            {
                // PostgreSQL 13 and 14 send a Windows service's reports to the event log, converting them from the
                // database encoding to UTF-16; an unconverted UTF-8 report would read as mojibake.
                Assert.Contains(message, server);
                return;
            }

            await Task.Delay(10, timeout.Token);
        }
    }

    /// <summary>
    /// Reads raw server log bytes, which use the database encoding of each reporting backend.
    /// </summary>
    /// <remarks>
    /// On Windows, LogFilePath is a decoded UTF-8 snapshot; pg_ctl writes the raw stream beside it, as
    /// PostgresServerLog.NativeFilePath describes.
    /// </remarks>
    private static byte[] ReadNative(PostgresTestCluster cluster)
    {
        string path = cluster.LogFilePath + (OperatingSystem.IsWindows() ? ".stderr.log" : string.Empty);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
