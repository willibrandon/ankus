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

        PgJsonb result = PgBenchmarkRunner.Run(definition, null,
            static bencher => bencher.Iterate(static () => PgBenchmark.BlackBox(42)), baseline, static action => action());

        using JsonDocument document = result.Parse();
        JsonElement comparison = document.RootElement.GetProperty("comparison");
        Assert.IsTrue(double.IsFinite(comparison.GetProperty("p_value").GetDouble()));
        Assert.AreEqual(0.95, comparison.GetProperty("mean").GetProperty("confidence_level").GetDouble(), 0.000_001);
        Assert.IsTrue(double.IsFinite(comparison.GetProperty("median").GetProperty("point_estimate").GetDouble()));
        Assert.IsFalse(string.IsNullOrWhiteSpace(comparison.GetProperty("summary").GetString()));
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
