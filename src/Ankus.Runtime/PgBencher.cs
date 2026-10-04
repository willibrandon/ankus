using System.Diagnostics;

namespace Ankus;

/// <summary>
/// Registers exactly one timing loop for a PostgreSQL benchmark.
/// </summary>
public sealed class PgBencher
{
    private PgBenchmarkRoutine? _routine;

    /// <summary>
    /// Registers a timing loop that reuses its captured inputs.
    /// </summary>
    /// <param name="routine">The synchronous work to measure.</param>
    public void Iterate(Action routine)
    {
        ArgumentNullException.ThrowIfNull(routine);
        SetRoutine(new(null, _ => routine(), PgBenchmarkBatchSize.NumIterations(long.MaxValue)));
    }

    /// <summary>
    /// Registers a timing loop that reuses its captured inputs and consumes its result.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="routine">The synchronous work to measure.</param>
    public void Iterate<TResult>(Func<TResult> routine)
    {
        ArgumentNullException.ThrowIfNull(routine);
        SetRoutine(new(null, _ => PgBenchmark.BlackBox(routine()), PgBenchmarkBatchSize.NumIterations(long.MaxValue)));
    }

    /// <summary>
    /// Registers a loop whose input setup is excluded from measured work.
    /// </summary>
    /// <typeparam name="TInput">The prepared input type.</typeparam>
    /// <param name="setup">The input factory.</param>
    /// <param name="routine">The synchronous work to measure.</param>
    /// <param name="batchSize">The input batching strategy.</param>
    public void IterateBatched<TInput>(Func<TInput> setup, Action<TInput> routine, PgBenchmarkBatchSize batchSize)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(routine);
        SetRoutine(new(() => setup(), value => routine((TInput)value!), batchSize));
    }

    /// <summary>
    /// Registers a result-producing loop whose input setup is excluded from measured work.
    /// </summary>
    /// <typeparam name="TInput">The prepared input type.</typeparam>
    /// <typeparam name="TResult">The measured result type.</typeparam>
    /// <param name="setup">The input factory.</param>
    /// <param name="routine">The synchronous work to measure.</param>
    /// <param name="batchSize">The input batching strategy.</param>
    public void IterateBatched<TInput, TResult>(Func<TInput> setup, Func<TInput, TResult> routine, PgBenchmarkBatchSize batchSize)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(routine);
        SetRoutine(new(() => setup(), value => PgBenchmark.BlackBox(routine((TInput)value!)), batchSize));
    }

    /// <summary>
    /// Takes the single registered routine for generated execution.
    /// </summary>
    internal PgBenchmarkRoutine TakeRoutine()
        => _routine ?? throw new InvalidOperationException(
            "The benchmark method did not register a timing loop. Call Iterate or IterateBatched exactly once.");

    private void SetRoutine(PgBenchmarkRoutine routine)
    {
        if (_routine is not null)
        {
            throw new InvalidOperationException("Only one timing loop may be registered by a benchmark method.");
        }

        _routine = routine;
    }
}

/// <summary>
/// Executes one detached timing routine with explicit transaction boundaries.
/// </summary>
internal sealed class PgBenchmarkRoutine(Func<object?>? setup, Action<object?> routine, PgBenchmarkBatchSize batchSize)
{
    /// <summary>
    /// Runs an exact iteration count without collecting timing data.
    /// </summary>
    internal void Run(long iterations, PgBenchmarkTransactionMode mode, Action<Action> subtransaction)
        => Measure(iterations, mode, subtransaction);

    /// <summary>
    /// Measures an exact iteration count while excluding batched input creation.
    /// </summary>
    internal long Measure(long iterations, PgBenchmarkTransactionMode mode, Action<Action> subtransaction)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);
        ArgumentNullException.ThrowIfNull(subtransaction);
        return setup is null ? MeasureSimple(iterations, mode, subtransaction) : MeasureBatched(iterations, mode, subtransaction);
    }

    private long MeasureSimple(long iterations, PgBenchmarkTransactionMode mode, Action<Action> subtransaction)
    {
        long started = Stopwatch.GetTimestamp();
        if (mode == PgBenchmarkTransactionMode.Shared)
        {
            for (long index = 0; index < iterations; index++)
            {
                routine(null);
            }
        }
        else
        {
            for (long index = 0; index < iterations; index++)
            {
                subtransaction(() => routine(null));
            }
        }

        return Stopwatch.GetTimestamp() - started;
    }

    private long MeasureBatched(long iterations, PgBenchmarkTransactionMode mode, Action<Action> subtransaction)
    {
        long elapsed = 0;
        long remaining = iterations;
        long perBatch = Math.Max(1, batchSize.GetIterationsPerBatch(iterations));
        while (remaining > 0)
        {
            int current = checked((int)Math.Min(remaining, perBatch));
            if (mode == PgBenchmarkTransactionMode.Shared)
            {
                object?[] inputs = PrepareBatch(current, out _);
                long started = Stopwatch.GetTimestamp();
                InvokeBatch(inputs);
                elapsed += Stopwatch.GetTimestamp() - started;
            }
            else if (mode == PgBenchmarkTransactionMode.SubtransactionPerBatch)
            {
                long setupElapsed = 0;
                long started = Stopwatch.GetTimestamp();
                subtransaction(() =>
                {
                    object?[] inputs = PrepareBatch(current, out setupElapsed);
                    InvokeBatch(inputs);
                });
                elapsed += Stopwatch.GetTimestamp() - started - setupElapsed;
            }
            else
            {
                for (int index = 0; index < current; index++)
                {
                    long setupElapsed = 0;
                    long started = Stopwatch.GetTimestamp();
                    subtransaction(() =>
                    {
                        long setupStarted = Stopwatch.GetTimestamp();
                        object? input = setup!();
                        setupElapsed = Stopwatch.GetTimestamp() - setupStarted;
                        routine(input);
                    });
                    elapsed += Stopwatch.GetTimestamp() - started - setupElapsed;
                }
            }

            remaining -= current;
        }

        return elapsed;
    }

    private object?[] PrepareBatch(int count, out long elapsed)
    {
        long started = Stopwatch.GetTimestamp();
        object?[] inputs = new object?[count];
        for (int index = 0; index < inputs.Length; index++)
        {
            inputs[index] = setup!();
        }

        elapsed = Stopwatch.GetTimestamp() - started;
        return inputs;
    }

    private void InvokeBatch(object?[] inputs)
    {
        foreach (object? input in inputs)
        {
            routine(input);
        }
    }
}
