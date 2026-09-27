namespace Ankus;

/// <summary>
/// Holds an owned snapshot of a dynamic worker's state and its PID when running.
/// </summary>
/// <param name="Status">The observed state.</param>
/// <param name="ProcessId">The running process identifier, or null when no running process was observed.</param>
public readonly record struct PgBackgroundWorkerState(PgBackgroundWorkerStatus Status, int? ProcessId);
