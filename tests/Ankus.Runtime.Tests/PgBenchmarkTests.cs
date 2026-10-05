using System.Text.Json;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies benchmark authoring, execution, batching and explicit result contracts.
/// </summary>
[TestClass]
public sealed class PgBenchmarkTests
{
    /// <summary>
    /// Keeps Ankus defaults aligned with the pgrx benchmark defaults.
    /// </summary>
    [TestMethod]
    public void AttributeUsesPgrxDefaults()
    {
        var attribute = new PgBenchmarkAttribute();

        Assert.AreEqual(PgBenchmarkTransactionMode.Shared, attribute.Transaction);
        Assert.AreEqual(100, attribute.SampleSize);
        Assert.AreEqual(5_000, attribute.MeasurementTimeMilliseconds);
        Assert.AreEqual(3_000, attribute.WarmupTimeMilliseconds);
        Assert.AreEqual(100_000, attribute.ResampleCount);
        Assert.AreEqual(0.01, attribute.NoiseThreshold);
        Assert.AreEqual(0.05, attribute.SignificanceLevel);
        Assert.IsNull(attribute.Setup);
    }

    /// <summary>
    /// Resolves every public batch strategy exactly.
    /// </summary>
    [TestMethod]
    public void BatchSizesPreserveStrategiesAndValues()
    {
        Assert.AreEqual(10L, PgBenchmarkBatchSize.SmallInput.GetIterationsPerBatch(100));
        Assert.AreEqual(1L, PgBenchmarkBatchSize.LargeInput.GetIterationsPerBatch(100));
        Assert.AreEqual(1L, PgBenchmarkBatchSize.PerIteration.GetIterationsPerBatch(100));
        Assert.AreEqual(25L, PgBenchmarkBatchSize.NumBatches(4).GetIterationsPerBatch(100));
        Assert.AreEqual(1L, PgBenchmarkBatchSize.NumBatches(long.MaxValue).GetIterationsPerBatch(100));
        Assert.AreEqual(7L, PgBenchmarkBatchSize.NumIterations(7).GetIterationsPerBatch(100));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PgBenchmarkBatchSize.NumBatches(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PgBenchmarkBatchSize.NumIterations(0));
    }

    /// <summary>
    /// Rejects missing and duplicate timing loops before measurement.
    /// </summary>
    [TestMethod]
    public void BencherRequiresExactlyOneRoutine()
    {
        var missing = new PgBencher();
        Assert.ThrowsExactly<InvalidOperationException>(() => missing.TakeRoutine());

        var duplicate = new PgBencher();
        duplicate.Iterate(static () => 1);
        Assert.ThrowsExactly<InvalidOperationException>(() => duplicate.Iterate(static () => 2));
    }

    /// <summary>
    /// Prepares each input once and invokes the measured body with that input.
    /// </summary>
    [TestMethod]
    public void BatchedSetupRunsOutsideMeasuredRoutine()
    {
        int setup = 0;
        int invoked = 0;
        var bencher = new PgBencher();
        bencher.IterateBatched(() => ++setup, value => invoked += value, PgBenchmarkBatchSize.NumIterations(2));

        PgBenchmarkRoutine routine = bencher.TakeRoutine();
        routine.Run(5, PgBenchmarkTransactionMode.Shared, static action => action());

        Assert.AreEqual(5, setup);
        Assert.AreEqual(15, invoked);
    }

    /// <summary>
    /// Applies shared, per-batch and per-iteration transaction boundaries exactly.
    /// </summary>
    [TestMethod]
    public void TransactionModesUseTheirExactBoundaries()
    {
        Assert.AreEqual(0, CountSubtransactions(PgBenchmarkTransactionMode.Shared));
        Assert.AreEqual(3, CountSubtransactions(PgBenchmarkTransactionMode.SubtransactionPerBatch));
        Assert.AreEqual(5, CountSubtransactions(PgBenchmarkTransactionMode.SubtransactionPerIteration));
    }

    /// <summary>
    /// Keeps prepared inputs inside the same subtransaction as their measured use.
    /// </summary>
    /// <param name="mode">The transaction mode under test.</param>
    [TestMethod]
    [DataRow(PgBenchmarkTransactionMode.SubtransactionPerBatch)]
    [DataRow(PgBenchmarkTransactionMode.SubtransactionPerIteration)]
    public void BatchedSetupUsesSelectedTransactionBoundary(PgBenchmarkTransactionMode mode)
    {
        bool inside = false;
        var bencher = new PgBencher();
        bencher.IterateBatched(() =>
        {
            Assert.IsTrue(inside);
            return 1;
        }, value =>
        {
            Assert.AreEqual(1, value);
            Assert.IsTrue(inside);
        }, PgBenchmarkBatchSize.NumIterations(2));

        bencher.TakeRoutine().Run(5, mode, action =>
        {
            Assert.IsFalse(inside);
            inside = true;
            try
            {
                action();
            }
            finally
            {
                inside = false;
            }
        });

        Assert.IsFalse(inside);
    }

    /// <summary>
    /// Writes descriptors and successful measurement payloads without reflection.
    /// </summary>
    [TestMethod]
    public void RunnerReturnsNativeAotSafeDescriptorAndSamples()
    {
        var definition = new PgBenchmarkDefinition("benches", "Extension.Benchmarks.Add()", "ankus_bench_test", null,
            PgBenchmarkTransactionMode.Shared, "Benchmarks.cs", 17,
            new(3, 1, 0, 100, 0.01, 0.05));

        PgJsonb descriptor = PgBenchmarkRunner.Describe(definition);
        using (JsonDocument document = descriptor.Parse())
        {
            Assert.AreEqual("Extension.Benchmarks.Add()", document.RootElement.GetProperty("bench_name").GetString());
            Assert.AreEqual("shared", document.RootElement.GetProperty("transaction_mode").GetString());
            Assert.AreEqual(3, document.RootElement.GetProperty("config").GetProperty("sample_size").GetInt32());
        }

        PgJsonb result = PgBenchmarkRunner.Run(definition, null, static bencher => bencher.Iterate(static () => 42), null,
            static action => action());
        using JsonDocument resultDocument = result.Parse();
        Assert.AreEqual("ok", resultDocument.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(3, resultDocument.RootElement.GetProperty("samples").GetArrayLength());
        Assert.IsGreaterThanOrEqualTo(2, resultDocument.RootElement.GetProperty("estimates").GetArrayLength());
    }

    /// <summary>
    /// Uses persisted raw samples for bootstrap confidence bounds, significance, and the Criterion-style summary.
    /// </summary>
    [TestMethod]
    public void RunnerComparesPersistedSamplesWithConfiguredResampling()
    {
        var definition = new PgBenchmarkDefinition("benches", "Extension.Benchmarks.Compare()", "ankus_bench_compare", null,
            PgBenchmarkTransactionMode.Shared, "Benchmarks.cs", 23,
            new(3, 1, 0, 100, 0.01, 0.05));
        PgJsonb baseline = PgBenchmarkRunner.Run(definition, null,
            static bencher => bencher.Iterate(static () => PgBenchmark.BlackBox(42)), null, static action => action());
        using (JsonDocument baselineDocument = baseline.Parse())
        {
            Assert.AreEqual("ok", baselineDocument.RootElement.GetProperty("status").GetString(), baseline.Text);
        }

        PgJsonb result = PgBenchmarkRunner.Run(definition, null,
            static bencher => bencher.Iterate(static () => PgBenchmark.BlackBox(42)), baseline, static action => action());

        using JsonDocument document = result.Parse();
        Assert.AreEqual("ok", document.RootElement.GetProperty("status").GetString(), result.Text);
        JsonElement comparison = document.RootElement.GetProperty("comparison");
        Assert.IsTrue(double.IsFinite(comparison.GetProperty("p_value").GetDouble()));
        Assert.AreEqual(0.95, comparison.GetProperty("mean").GetProperty("confidence_level").GetDouble(), 0.000_001);
        Assert.IsTrue(double.IsFinite(comparison.GetProperty("median").GetProperty("point_estimate").GetDouble()));
        Assert.IsFalse(string.IsNullOrWhiteSpace(comparison.GetProperty("summary").GetString()));
    }

    /// <summary>
    /// Recalibrates short samples after a delayed pilot without substituting invented elapsed values.
    /// </summary>
    [TestMethod]
    public void MeasurementRecoversFromDelayedCalibrationAndZeroDurationSamples()
    {
        long[] durations = [1_000, 0, 40, 120, 124];
        int next = 0;
        List<long> measured = [];
        long Measure(long iterations)
        {
            measured.Add(iterations);
            return durations[next++];
        }

        (long pilotIterations, long pilotElapsed) = PgBenchmarkRunner.MeasureSample(Measure, 1, 100);
        Assert.AreEqual(1L, pilotIterations);
        Assert.AreEqual(1_000L, pilotElapsed);
        (long iterations, long elapsed) = PgBenchmarkRunner.MeasureSample(Measure, pilotIterations, 100);
        Assert.AreEqual(4L, iterations);
        Assert.AreEqual(120L, elapsed);
        (long nextIterations, long nextElapsed) = PgBenchmarkRunner.MeasureSample(Measure, iterations, 100);
        Assert.AreEqual(4L, nextIterations);
        Assert.AreEqual(124L, nextElapsed);
        Assert.AreSequenceEqual([1, 1, 2, 4, 4], measured);
        Assert.AreEqual(durations.Length, next);
    }

    /// <summary>
    /// Preserves a resolved measurement at and above its target without running the routine again.
    /// </summary>
    /// <param name="duration">The exact measured duration.</param>
    [TestMethod]
    [DataRow(100L)]
    [DataRow(101L)]
    public void MeasurementPreservesResolvedSamples(long duration)
    {
        int calls = 0;
        (long iterations, long elapsed) = PgBenchmarkRunner.MeasureSample(count =>
        {
            calls++;
            Assert.AreEqual(7L, count);
            return duration;
        }, 7, 100);
        Assert.AreEqual(7L, iterations);
        Assert.AreEqual(duration, elapsed);
        Assert.AreEqual(1, calls);
    }

    /// <summary>
    /// Saturates the iteration bound while retaining a real positive duration below the target.
    /// </summary>
    [TestMethod]
    public void MeasurementSaturatesIterationCountWithoutOverflow()
    {
        List<long> measured = [];
        (long iterations, long elapsed) = PgBenchmarkRunner.MeasureSample(count =>
        {
            measured.Add(count);
            return 1;
        }, (int.MaxValue / 2) + 1, long.MaxValue);
        Assert.AreEqual(int.MaxValue, iterations);
        Assert.AreEqual(1L, elapsed);
        Assert.AreSequenceEqual([(int.MaxValue / 2) + 1, int.MaxValue], measured);
    }

    /// <summary>
    /// Rejects an unresolved clock at the iteration limit instead of looping or fabricating a sample.
    /// </summary>
    [TestMethod]
    public void MeasurementRejectsZeroDurationAtIterationLimit()
    {
        int calls = 0;
        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            PgBenchmarkRunner.MeasureSample(_ =>
            {
                calls++;
                return 0;
            }, int.MaxValue, 1));
        Assert.AreEqual("The benchmark measurement remained below timer resolution at the iteration limit.", error.Message);
        Assert.AreEqual(1, calls);
    }

    /// <summary>
    /// Rejects invalid timing values and preserves routine failures without retrying author code.
    /// </summary>
    [TestMethod]
    public void MeasurementPreservesFailuresWithoutRetrying()
    {
        InvalidOperationException negative = Assert.ThrowsExactly<InvalidOperationException>(() =>
            PgBenchmarkRunner.MeasureSample(static _ => -1, 1, 1));
        Assert.AreEqual("The benchmark measurement returned a negative duration.", negative.Message);
        var expected = new InvalidOperationException("benchmark routine failed");
        int calls = 0;
        InvalidOperationException actual = Assert.ThrowsExactly<InvalidOperationException>(() =>
            PgBenchmarkRunner.MeasureSample(_ =>
            {
                calls++;
                throw expected;
            }, 1, 1));
        Assert.AreSame(expected, actual);
        Assert.AreEqual(1, calls);
    }

    /// <summary>
    /// Returns author failures as explicit result payloads.
    /// </summary>
    [TestMethod]
    public void RunnerReturnsExplicitFailurePayload()
    {
        var definition = new PgBenchmarkDefinition("benches", "Extension.Benchmarks.Fails()", "ankus_bench_failure", null,
            PgBenchmarkTransactionMode.Shared, "Benchmarks.cs", 29,
            new(3, 1, 0, 100, 0.01, 0.05));

        PgJsonb result = PgBenchmarkRunner.Run(definition, null,
            static _ => throw new InvalidOperationException("expected benchmark failure"), null, static action => action());

        using JsonDocument document = result.Parse();
        Assert.AreEqual("failed", document.RootElement.GetProperty("status").GetString());
        Assert.AreEqual("expected benchmark failure", document.RootElement.GetProperty("error_text").GetString());
        Assert.AreEqual(0, document.RootElement.GetProperty("samples").GetArrayLength());
    }

    private static int CountSubtransactions(PgBenchmarkTransactionMode mode)
    {
        int boundaries = 0;
        var bencher = new PgBencher();
        bencher.IterateBatched(static () => 1, static _ => { }, PgBenchmarkBatchSize.NumIterations(2));
        bencher.TakeRoutine().Run(5, mode, action =>
        {
            boundaries++;
            action();
        });
        return boundaries;
    }
}
