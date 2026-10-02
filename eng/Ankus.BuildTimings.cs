#:package Microsoft.Build

using System.Globalization;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

if (args.Length == 0)
{
    Console.Error.WriteLine("Specify one or more retained MSBuild binary logs.");
    return 2;
}

foreach (string file in args)
{
    Console.WriteLine("Binding tasks: " + Path.GetFileName(file));
    Dictionary<(int Node, int Submission, int Project, int Target, int Task), BindingTask> running = [];
    string[] phases = ["binding-sources", "binding-compile", "binding-link"];
    var replay = new BinaryLogReplayEventSource();
    int completed = 0;
    replay.TaskStarted += (_, entry) =>
    {
        if (entry.TaskName == "Exec" && entry.BuildEventContext is not null)
        {
            running.Add(Key(entry), new(entry.Timestamp));
        }
    };

    replay.MessageRaised += (_, entry) =>
    {
        if (!running.TryGetValue(Key(entry), out BindingTask? task))
        {
            return;
        }

        if (entry is TaskCommandLineEventArgs command)
        {
            task.Phase = phases.FirstOrDefault(phase =>
                command.CommandLine.Contains(" " + phase + " ", StringComparison.Ordinal));
        }

        if (entry.Message?.StartsWith("Native binding sources: reused", StringComparison.Ordinal) == true ||
            entry.Message?.StartsWith("Managed binding compilation: reused", StringComparison.Ordinal) == true)
        {
            task.Cache = "reused";
        }
        else if (entry.Message?.StartsWith("Native binding sources: collected", StringComparison.Ordinal) == true ||
            entry.Message?.StartsWith("Managed binding compilation: built", StringComparison.Ordinal) == true)
        {
            task.Cache = "produced";
        }
    };

    replay.TaskFinished += (_, entry) =>
    {
        if (entry.TaskName == "Exec" && running.Remove(Key(entry), out BindingTask? task) && task.Phase is not null)
        {
            double seconds = (entry.Timestamp - task.Started).TotalSeconds;
            Console.WriteLine($"{task.Phase}: {seconds.ToString("F3", CultureInfo.InvariantCulture)} seconds; success={entry.Succeeded}; cache={task.Cache}");
            completed++;
        }
    };

    try
    {
        replay.Replay(Path.GetFullPath(file));
    }
    catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or ArgumentException or InvalidOperationException)
    {
        Console.WriteLine("Binding timing replay incomplete; only observed completed tasks have proven durations.");
    }

    foreach (BindingTask task in running.Values.Where(static task => task.Phase is not null))
    {
        Console.WriteLine(task.Phase + ": no completed task event; duration and outcome unproven.");
    }

    if (completed == 0)
    {
        Console.WriteLine("No completed binding tasks recorded.");
    }
}

return 0;

static (int Node, int Submission, int Project, int Target, int Task) Key(BuildEventArgs entry)
{
    BuildEventContext? context = entry.BuildEventContext;
    return context is null ? (-1, -1, -1, -1, -1) :
        (context.NodeId, context.SubmissionId, context.ProjectContextId, context.TargetId, context.TaskId);
}

/// <summary>
/// Retains task identity and safe phase metadata without exposing compiler arguments.
/// </summary>
/// <param name="started">The original build-event timestamp.</param>
internal sealed class BindingTask(DateTime started)
{
    /// <summary>
    /// Gets the original task start time.
    /// </summary>
    internal DateTime Started { get; } = started;

    /// <summary>
    /// Gets or sets the known helper command name.
    /// </summary>
    internal string? Phase
    {
        get;
        set;
    }

    /// <summary>
    /// Gets or sets whether the task reported artifact reuse or production.
    /// </summary>
    internal string Cache
    {
        get;
        set;
    } = "unreported";
}
