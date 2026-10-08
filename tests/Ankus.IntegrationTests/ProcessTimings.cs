using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Ankus.IntegrationTests;

/// <summary>
/// Aggregates child-process and cluster durations by command, so each platform's report shows where suite time goes.
/// </summary>
/// <remarks>
/// Only executable names and subcommand words are recorded; paths, arguments and machine identifiers are not.
/// </remarks>
internal static partial class ProcessTimings
{
    private static readonly ConcurrentDictionary<string, Accumulator> s_commands = new(StringComparer.Ordinal);

    /// <summary>
    /// Records one completed child process.
    /// </summary>
    /// <param name="fileName">The executable that ran.</param>
    /// <param name="arguments">Its arguments; only a leading subcommand word is kept.</param>
    /// <param name="waited">Time spent waiting for a compiler slot before starting.</param>
    /// <param name="ran">Time from process start until exit.</param>
    internal static void Record(string fileName, IReadOnlyList<string> arguments, TimeSpan waited, TimeSpan ran)
    {
        string executable = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        string command = arguments.Count != 0 && Subcommand().IsMatch(arguments[0]) ? executable + " " + arguments[0] : executable;
        Record(command, waited, ran);
    }

    /// <summary>
    /// Records one completed operation under a fixed name.
    /// </summary>
    /// <param name="name">The operation's report name.</param>
    /// <param name="waited">Time spent waiting before the operation began.</param>
    /// <param name="ran">The operation's duration.</param>
    internal static void Record(string name, TimeSpan waited, TimeSpan ran)
        => s_commands.GetOrAdd(name, static _ => new Accumulator()).Add(waited, ran);

    /// <summary>
    /// Writes the summary, longest total first, replacing any earlier summary at the same path.
    /// </summary>
    /// <param name="path">The report file.</param>
    internal static void Write(string path)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"# {RuntimeInformation.OSDescription.Trim()}; {RuntimeInformation.ProcessArchitecture}; {Environment.ProcessorCount} processors; package concurrency {IntegrationEnvironment.PackageTestConcurrency}");
        text.AppendLine("command\tcount\ttotal_s\tmean_s\tmax_s\twait_s");
        foreach ((string name, Accumulator accumulator) in s_commands.OrderByDescending(static pair => pair.Value.Total))
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"{name}\t{accumulator.Count}\t{Seconds(accumulator.Total):F1}\t{Seconds(accumulator.Total / Math.Max(1, accumulator.Count)):F2}\t{Seconds(accumulator.Maximum):F1}\t{Seconds(accumulator.Waited):F1}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ToString());
    }

    private static double Seconds(long ticks) => (double)ticks / Stopwatch.Frequency;

    [GeneratedRegex("^[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Subcommand();

    /// <summary>
    /// Accumulates durations for one command without locking.
    /// </summary>
    private sealed class Accumulator
    {
        private long _count;
        private long _total;
        private long _maximum;
        private long _waited;

        /// <summary>
        /// Gets the number of recorded operations.
        /// </summary>
        public long Count => Interlocked.Read(ref _count);

        /// <summary>
        /// Gets the summed duration in stopwatch ticks.
        /// </summary>
        public long Total => Interlocked.Read(ref _total);

        /// <summary>
        /// Gets the longest duration in stopwatch ticks.
        /// </summary>
        public long Maximum => Interlocked.Read(ref _maximum);

        /// <summary>
        /// Gets the summed waiting time in stopwatch ticks.
        /// </summary>
        public long Waited => Interlocked.Read(ref _waited);

        /// <summary>
        /// Adds one operation.
        /// </summary>
        /// <param name="waited">Time spent waiting before the operation.</param>
        /// <param name="ran">The operation's duration.</param>
        public void Add(TimeSpan waited, TimeSpan ran)
        {
            long ticks = (long)(ran.TotalSeconds * Stopwatch.Frequency);
            Interlocked.Increment(ref _count);
            Interlocked.Add(ref _total, ticks);
            Interlocked.Add(ref _waited, (long)(waited.TotalSeconds * Stopwatch.Frequency));
            long current = Interlocked.Read(ref _maximum);
            while (ticks > current)
            {
                long observed = Interlocked.CompareExchange(ref _maximum, ticks, current);
                if (observed == current)
                {
                    break;
                }

                current = observed;
            }
        }
    }
}
