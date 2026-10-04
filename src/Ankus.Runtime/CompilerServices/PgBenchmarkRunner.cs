using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Ankus.CompilerServices;

/// <summary>
/// Carries validated measurement settings from generated benchmark wrappers.
/// </summary>
/// <param name="sampleSize">The number of measurement samples.</param>
/// <param name="measurementTimeMilliseconds">The target total measurement time.</param>
/// <param name="warmupTimeMilliseconds">The warmup time.</param>
/// <param name="resampleCount">The statistical resample count.</param>
/// <param name="noiseThreshold">The relative noise threshold.</param>
/// <param name="significanceLevel">The comparison significance level.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class PgBenchmarkConfiguration(
    int sampleSize,
    int measurementTimeMilliseconds,
    int warmupTimeMilliseconds,
    int resampleCount,
    double noiseThreshold,
    double significanceLevel)
{
    /// <summary>
    /// Gets the number of measurement samples.
    /// </summary>
    public int SampleSize { get; } = sampleSize;

    /// <summary>
    /// Gets the target total measurement time in milliseconds.
    /// </summary>
    public int MeasurementTimeMilliseconds { get; } = measurementTimeMilliseconds;

    /// <summary>
    /// Gets the warmup time in milliseconds.
    /// </summary>
    public int WarmupTimeMilliseconds { get; } = warmupTimeMilliseconds;

    /// <summary>
    /// Gets the statistical resample count.
    /// </summary>
    public int ResampleCount { get; } = resampleCount;

    /// <summary>
    /// Gets the relative noise threshold.
    /// </summary>
    public double NoiseThreshold { get; } = noiseThreshold;

    /// <summary>
    /// Gets the comparison significance level.
    /// </summary>
    public double SignificanceLevel { get; } = significanceLevel;
}

/// <summary>
/// Carries one generated benchmark identity without runtime reflection.
/// </summary>
/// <param name="schemaName">The benchmark wrapper schema.</param>
/// <param name="benchmarkName">The managed benchmark name.</param>
/// <param name="functionName">The generated SQL function name.</param>
/// <param name="setupFunction">The managed setup name, or null.</param>
/// <param name="transactionMode">The measured transaction boundary.</param>
/// <param name="sourceFile">The project-relative source path.</param>
/// <param name="sourceLine">The one-based declaration line.</param>
/// <param name="configuration">The validated measurement settings.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class PgBenchmarkDefinition(
    string schemaName,
    string benchmarkName,
    string functionName,
    string? setupFunction,
    PgBenchmarkTransactionMode transactionMode,
    string sourceFile,
    int sourceLine,
    PgBenchmarkConfiguration configuration)
{
    /// <summary>
    /// Gets the benchmark wrapper schema.
    /// </summary>
    public string SchemaName { get; } = schemaName;

    /// <summary>
    /// Gets the managed benchmark name.
    /// </summary>
    public string BenchmarkName { get; } = benchmarkName;

    /// <summary>
    /// Gets the generated SQL function name.
    /// </summary>
    public string FunctionName { get; } = functionName;

    /// <summary>
    /// Gets the managed setup name, or null.
    /// </summary>
    public string? SetupFunction { get; } = setupFunction;

    /// <summary>
    /// Gets the measured transaction boundary.
    /// </summary>
    public PgBenchmarkTransactionMode TransactionMode { get; } = transactionMode;

    /// <summary>
    /// Gets the project-relative source path.
    /// </summary>
    public string SourceFile { get; } = sourceFile;

    /// <summary>
    /// Gets the one-based declaration line.
    /// </summary>
    public int SourceLine { get; } = sourceLine;

    /// <summary>
    /// Gets the validated measurement settings.
    /// </summary>
    public PgBenchmarkConfiguration Configuration { get; } = configuration;
}

/// <summary>
/// Executes generated PostgreSQL benchmarks and writes explicit Native AOT-safe JSON payloads.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class PgBenchmarkRunner
{
    /// <summary>
    /// Describes one generated benchmark without running author code.
    /// </summary>
    /// <param name="definition">The generated benchmark definition.</param>
    /// <returns>The descriptor consumed by the Ankus tool.</returns>
    public static PgJsonb Describe(PgBenchmarkDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return WriteJson(writer => WriteDefinition(writer, definition));
    }

    /// <summary>
    /// Runs one generated benchmark inside the active PostgreSQL backend.
    /// </summary>
    /// <param name="definition">The generated benchmark definition.</param>
    /// <param name="setup">The optional once-per-run setup callback.</param>
    /// <param name="benchmark">The benchmark method that registers one timing loop.</param>
    /// <param name="baseline">The optional prior result used for comparison.</param>
    /// <returns>The complete measurement or failure payload.</returns>
    public static PgJsonb Run(
        PgBenchmarkDefinition definition,
        Action? setup,
        Action<PgBencher> benchmark,
        PgJsonb? baseline)
        => Run(definition, setup, benchmark, baseline,
            static action => PgTransaction.RunInSubtransaction(action),
            static action => PgTransaction.RunInSubtransaction(action));

    /// <summary>
    /// Runs one benchmark through an explicit transaction boundary for direct verification.
    /// </summary>
    internal static PgJsonb Run(
        PgBenchmarkDefinition definition,
        Action? setup,
        Action<PgBencher> benchmark,
        PgJsonb? baseline,
        Action<Action> subtransaction)
        => Run(definition, setup, benchmark, baseline, static action => action(), subtransaction);

    private static PgJsonb Run(
        PgBenchmarkDefinition definition,
        Action? setup,
        Action<PgBencher> benchmark,
        PgJsonb? baseline,
        Action<Action> recovery,
        Action<Action> subtransaction)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(benchmark);
        ArgumentNullException.ThrowIfNull(recovery);
        ArgumentNullException.ThrowIfNull(subtransaction);
        try
        {
            Sample[] samples = [];
            Estimate[] estimates = [];
            Comparison? comparison = null;
            recovery(() =>
            {
                Validate(definition.Configuration);
                setup?.Invoke();
                var bencher = new PgBencher();
                benchmark(bencher);
                PgBenchmarkRoutine routine = bencher.TakeRoutine();
                WarmUp(routine, definition, subtransaction);
                samples = Measure(routine, definition, subtransaction);
                estimates = EstimateSamples(samples, definition.Configuration);
                comparison = baseline is null ? null : Compare(samples, baseline.Value, definition.Configuration);
            });
            return WriteResult(definition, samples, estimates, comparison, null);
        }
        catch (Exception exception)
        {
            return WriteResult(definition, [], [], null, exception.Message);
        }
    }

    private static void Validate(PgBenchmarkConfiguration configuration)
    {
        if (configuration.SampleSize < 2 || configuration.MeasurementTimeMilliseconds <= 0 ||
            configuration.WarmupTimeMilliseconds < 0 || configuration.ResampleCount <= 0 ||
            !double.IsFinite(configuration.NoiseThreshold) || configuration.NoiseThreshold is < 0 or >= 1 ||
            !double.IsFinite(configuration.SignificanceLevel) || configuration.SignificanceLevel is <= 0 or >= 1)
        {
            throw new InvalidOperationException("The generated benchmark configuration is invalid.");
        }
    }

    private static void WarmUp(PgBenchmarkRoutine routine, PgBenchmarkDefinition definition, Action<Action> subtransaction)
    {
        long target = MillisecondsToTicks(definition.Configuration.WarmupTimeMilliseconds);
        long elapsed = 0;
        long iterations = 1;
        while (elapsed < target)
        {
            elapsed += routine.Measure(iterations, definition.TransactionMode, subtransaction);
            iterations = Math.Min(checked(iterations * 2), int.MaxValue);
        }
    }

    private static Sample[] Measure(PgBenchmarkRoutine routine, PgBenchmarkDefinition definition, Action<Action> subtransaction)
    {
        PgBenchmarkConfiguration configuration = definition.Configuration;
        long targetPerSample = Math.Max(1, MillisecondsToTicks(configuration.MeasurementTimeMilliseconds) / configuration.SampleSize);
        long iterations = 1;
        long elapsed;
        do
        {
            elapsed = routine.Measure(iterations, definition.TransactionMode, subtransaction);
            if (elapsed < targetPerSample)
            {
                iterations = Math.Min(checked(iterations * 2), int.MaxValue);
            }
        }
        while (elapsed < targetPerSample && iterations < int.MaxValue);

        var samples = new Sample[configuration.SampleSize];
        for (int index = 0; index < samples.Length; index++)
        {
            elapsed = routine.Measure(iterations, definition.TransactionMode, subtransaction);
            samples[index] = new(index, iterations, TicksToNanoseconds(elapsed));
        }

        return samples;
    }

    private static Estimate[] EstimateSamples(Sample[] samples, PgBenchmarkConfiguration configuration)
    {
        double[] values = SampleValues(samples);
        var random = new Random(1_262_774_131);
        (double[] means, double[] medians) = Bootstrap(values, configuration.ResampleCount, random);
        return
        [
            CreateEstimate("mean", Mean(values), means, 0.95),
            CreateEstimate("median", Median(values), medians, 0.95),
        ];
    }

    private static Comparison Compare(Sample[] samples, PgJsonb baseline, PgBenchmarkConfiguration configuration)
    {
        double[] current = SampleValues(samples);
        double[] previous = BaselineValues(baseline);
        var random = new Random(1_262_774_131);
        (double[] meanChanges, double[] medianChanges) = BootstrapChanges(
            current, previous, configuration.ResampleCount, random);
        double confidence = 1 - configuration.SignificanceLevel;
        ComparisonEstimate mean = CreateComparisonEstimate("mean", Relative(Mean(current), Mean(previous)),
            meanChanges, confidence);
        ComparisonEstimate median = CreateComparisonEstimate("median", Relative(Median(current), Median(previous)),
            medianChanges, confidence);
        double pValue = PValue(current, previous, configuration.ResampleCount, random);
        string summary;
        if (pValue >= configuration.SignificanceLevel)
        {
            summary = "No change in performance detected.";
        }
        else if (mean.LowerBound < -configuration.NoiseThreshold && mean.UpperBound < -configuration.NoiseThreshold)
        {
            summary = "Performance has improved.";
        }
        else if (mean.LowerBound > configuration.NoiseThreshold && mean.UpperBound > configuration.NoiseThreshold)
        {
            summary = "Performance has regressed.";
        }
        else
        {
            summary = "Change within noise threshold.";
        }

        return new(mean, median, pValue, configuration.SignificanceLevel, configuration.NoiseThreshold, summary);
    }

    private static double[] SampleValues(Sample[] samples)
        => [.. samples.Select(static sample => sample.ElapsedNanoseconds / sample.Iterations)];

    private static double[] BaselineValues(PgJsonb baseline)
    {
        using JsonDocument document = baseline.Parse();
        JsonElement root = document.RootElement;
        if (root.GetProperty("status").GetString() != "ok")
        {
            throw new InvalidOperationException("The benchmark baseline did not complete successfully.");
        }

        double[] values = [.. root.GetProperty("samples").EnumerateArray().Select(static sample =>
        {
            long iterations = sample.GetProperty("iteration_count").GetInt64();
            double elapsed = sample.GetProperty("elapsed_ns").GetDouble();
            if (iterations <= 0 || !double.IsFinite(elapsed) || elapsed < 0)
            {
                throw new InvalidOperationException("The benchmark baseline contains an invalid sample.");
            }

            return elapsed / iterations;
        })];
        if (values.Length < 2)
        {
            throw new InvalidOperationException("A benchmark comparison requires at least two baseline samples.");
        }

        return values;
    }

    private static (double[] Means, double[] Medians) Bootstrap(double[] values, int count, Random random)
    {
        double[] means = new double[count];
        double[] medians = new double[count];
        double[] sample = new double[values.Length];
        for (int index = 0; index < count; index++)
        {
            Resample(values, sample, random);
            means[index] = Mean(sample);
            Array.Sort(sample);
            medians[index] = MedianOrdered(sample);
        }

        return (means, medians);
    }

    private static (double[] Means, double[] Medians) BootstrapChanges(
        double[] current,
        double[] previous,
        int count,
        Random random)
    {
        double[] means = new double[count];
        double[] medians = new double[count];
        double[] currentSample = new double[current.Length];
        double[] previousSample = new double[previous.Length];
        for (int index = 0; index < count; index++)
        {
            Resample(current, currentSample, random);
            Resample(previous, previousSample, random);
            means[index] = Relative(Mean(currentSample), Mean(previousSample));
            Array.Sort(currentSample);
            Array.Sort(previousSample);
            medians[index] = Relative(MedianOrdered(currentSample), MedianOrdered(previousSample));
        }

        return (means, medians);
    }

    private static void Resample(double[] source, double[] destination, Random random)
    {
        for (int index = 0; index < destination.Length; index++)
        {
            destination[index] = source[random.Next(source.Length)];
        }
    }

    private static Estimate CreateEstimate(string kind, double point, double[] distribution, double confidence)
    {
        (double lower, double upper) = ConfidenceInterval(distribution, confidence);
        return new(kind, point, StandardDeviation(distribution), confidence, lower, upper);
    }

    private static ComparisonEstimate CreateComparisonEstimate(
        string kind,
        double point,
        double[] distribution,
        double confidence)
    {
        (double lower, double upper) = ConfidenceInterval(distribution, confidence);
        return new(kind, point, StandardDeviation(distribution), confidence, lower, upper);
    }

    private static (double Lower, double Upper) ConfidenceInterval(double[] distribution, double confidence)
    {
        Array.Sort(distribution);
        double tail = (1 - confidence) / 2;
        return (Quantile(distribution, tail), Quantile(distribution, 1 - tail));
    }

    private static double Quantile(double[] ordered, double probability)
    {
        double position = (ordered.Length - 1) * probability;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        return lower == upper ? ordered[lower] : ordered[lower] + (ordered[upper] - ordered[lower]) * (position - lower);
    }

    private static double PValue(double[] current, double[] previous, int count, Random random)
    {
        double observed = SampleT(current, previous);
        if (double.IsNaN(observed))
        {
            return Mean(current) == Mean(previous) ? 1 : 0;
        }

        double[] combined = [.. current, .. previous];
        double[] resampled = new double[combined.Length];
        int finite = 0;
        int below = 0;
        for (int index = 0; index < count; index++)
        {
            Resample(combined, resampled, random);
            double statistic = SampleT(resampled.AsSpan(0, current.Length), resampled.AsSpan(current.Length));
            if (!double.IsFinite(statistic))
            {
                continue;
            }

            finite++;
            if (statistic < observed)
            {
                below++;
            }
        }

        if (finite == 0)
        {
            return Mean(current) == Mean(previous) ? 1 : 0;
        }

        return Math.Min(1, 2d * Math.Min(below, finite - below) / finite);
    }

    private static double SampleT(ReadOnlySpan<double> current, ReadOnlySpan<double> previous)
    {
        double currentMean = Mean(current);
        double previousMean = Mean(previous);
        double denominator = Math.Sqrt(
            Variance(current, currentMean) / current.Length + Variance(previous, previousMean) / previous.Length);
        return denominator == 0 ? double.NaN : (currentMean - previousMean) / denominator;
    }

    private static double Mean(ReadOnlySpan<double> values)
    {
        double sum = 0;
        foreach (double value in values)
        {
            sum += value;
        }

        return sum / values.Length;
    }

    private static double Median(double[] values)
    {
        double[] ordered = (double[])values.Clone();
        Array.Sort(ordered);
        return MedianOrdered(ordered);
    }

    private static double MedianOrdered(double[] ordered)
    {
        int middle = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2 : ordered[middle];
    }

    private static double Variance(ReadOnlySpan<double> values, double mean)
    {
        if (values.Length < 2)
        {
            return 0;
        }

        double sum = 0;
        foreach (double value in values)
        {
            double difference = value - mean;
            sum += difference * difference;
        }

        return sum / (values.Length - 1);
    }

    private static double StandardDeviation(double[] values) => Math.Sqrt(Variance(values, Mean(values)));

    private static double Relative(double current, double previous)
    {
        if (previous == 0)
        {
            throw new InvalidOperationException("The benchmark baseline contains a zero-duration sample distribution.");
        }

        return current / previous - 1;
    }

    private static PgJsonb WriteResult(
        PgBenchmarkDefinition definition,
        Sample[] samples,
        Estimate[] estimates,
        Comparison? comparison,
        string? error)
        => WriteJson(writer =>
        {
            writer.WriteStartObject();
            WriteDefinitionProperties(writer, definition);
            writer.WriteString("status", error is null ? "ok" : "failed");
            if (error is null)
            {
                writer.WriteNull("error_text");
            }
            else
            {
                writer.WriteString("error_text", error);
            }

            writer.WriteStartArray("estimates");
            foreach (Estimate estimate in estimates)
            {
                writer.WriteStartObject();
                writer.WriteString("estimate_kind", estimate.Kind);
                writer.WriteNumber("point_estimate_ns", estimate.Point);
                WriteOptionalNumber(writer, "standard_error_ns", estimate.StandardError);
                WriteOptionalNumber(writer, "confidence_level", estimate.ConfidenceLevel);
                WriteOptionalNumber(writer, "ci_lower_bound_ns", estimate.LowerBound);
                WriteOptionalNumber(writer, "ci_upper_bound_ns", estimate.UpperBound);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("samples");
            foreach (Sample sample in samples)
            {
                writer.WriteStartObject();
                writer.WriteNumber("sample_index", sample.Index);
                writer.WriteNumber("iteration_count", sample.Iterations);
                writer.WriteNumber("elapsed_ns", sample.ElapsedNanoseconds);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            if (comparison is null)
            {
                writer.WriteNull("comparison");
            }
            else
            {
                writer.WriteStartObject("comparison");
                WriteComparisonEstimate(writer, "mean", comparison.Mean);
                WriteComparisonEstimate(writer, "median", comparison.Median);
                writer.WriteNumber("p_value", comparison.PValue);
                writer.WriteNumber("significance_level", comparison.SignificanceLevel);
                writer.WriteNumber("noise_threshold", comparison.NoiseThreshold);
                writer.WriteString("summary", comparison.Summary);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        });

    private static void WriteDefinition(Utf8JsonWriter writer, PgBenchmarkDefinition definition)
    {
        writer.WriteStartObject();
        WriteDefinitionProperties(writer, definition);
        writer.WriteEndObject();
    }

    private static void WriteDefinitionProperties(Utf8JsonWriter writer, PgBenchmarkDefinition definition)
    {
        writer.WriteString("schema_name", definition.SchemaName);
        writer.WriteString("bench_name", definition.BenchmarkName);
        writer.WriteString("function_name", definition.FunctionName);
        if (definition.SetupFunction is null)
        {
            writer.WriteNull("setup_function");
        }
        else
        {
            writer.WriteString("setup_function", definition.SetupFunction);
        }

        writer.WriteString("transaction_mode", TransactionName(definition.TransactionMode));
        writer.WriteString("source_file", definition.SourceFile);
        writer.WriteNumber("source_line", definition.SourceLine);
        PgBenchmarkConfiguration configuration = definition.Configuration;
        writer.WriteStartObject("config");
        writer.WriteNumber("sample_size", configuration.SampleSize);
        writer.WriteNumber("measurement_time_ms", configuration.MeasurementTimeMilliseconds);
        writer.WriteNumber("warm_up_time_ms", configuration.WarmupTimeMilliseconds);
        writer.WriteNumber("nresamples", configuration.ResampleCount);
        writer.WriteNumber("noise_threshold", configuration.NoiseThreshold);
        writer.WriteNumber("significance_level", configuration.SignificanceLevel);
        writer.WriteEndObject();
    }

    private static string TransactionName(PgBenchmarkTransactionMode mode)
        => mode switch
        {
            PgBenchmarkTransactionMode.Shared => "shared",
            PgBenchmarkTransactionMode.SubtransactionPerBatch => "subtransaction_per_batch",
            PgBenchmarkTransactionMode.SubtransactionPerIteration => "subtransaction_per_iteration",
            _ => throw new InvalidOperationException("The generated benchmark transaction mode is invalid."),
        };

    private static PgJsonb WriteJson(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }

        return new(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private static void WriteOptionalNumber(Utf8JsonWriter writer, string name, double? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteNumber(name, value.Value);
        }
    }

    private static void WriteComparisonEstimate(Utf8JsonWriter writer, string name, ComparisonEstimate estimate)
    {
        writer.WriteStartObject(name);
        writer.WriteString("estimate_kind", estimate.Kind);
        writer.WriteNumber("point_estimate", estimate.Point);
        writer.WriteNumber("standard_error", estimate.StandardError);
        writer.WriteNumber("confidence_level", estimate.ConfidenceLevel);
        writer.WriteNumber("ci_lower_bound", estimate.LowerBound);
        writer.WriteNumber("ci_upper_bound", estimate.UpperBound);
        writer.WriteEndObject();
    }

    private static long MillisecondsToTicks(int milliseconds)
        => checked((long)Math.Ceiling(milliseconds * (double)Stopwatch.Frequency / 1_000));

    private static double TicksToNanoseconds(long ticks) => ticks * 1_000_000_000d / Stopwatch.Frequency;

    private sealed record Sample(int Index, long Iterations, double ElapsedNanoseconds);

    private sealed record Estimate(string Kind, double Point, double? StandardError, double? ConfidenceLevel, double? LowerBound, double? UpperBound);

    private sealed record ComparisonEstimate(
        string Kind,
        double Point,
        double StandardError,
        double ConfidenceLevel,
        double LowerBound,
        double UpperBound);

    private sealed record Comparison(
        ComparisonEstimate Mean,
        ComparisonEstimate Median,
        double PValue,
        double SignificanceLevel,
        double NoiseThreshold,
        string Summary);
}
