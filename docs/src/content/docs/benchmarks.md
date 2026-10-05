---
title: Benchmarks
description: Measure extension code inside PostgreSQL and compare retained benchmark runs.
---

Ankus benchmarks run in the PostgreSQL backend that owns the extension. This
keeps SPI, datum conversion, memory contexts and PostgreSQL functions inside
their real execution environment.

Declare an accessible synchronous static `void` method with one `PgBencher`
parameter:

```csharp
public static class ExtensionBenchmarks
{
    [PgBenchmark]
    public static void AddNumeric(PgBencher bencher)
    {
        PgNumeric left = PgNumeric.Parse("123.45");
        PgNumeric right = PgNumeric.Parse("67.89");

        bencher.Iterate(() => left + right);
    }
}
```

The method must register exactly one timing loop. `Iterate` reuses captured
inputs. `IterateBatched` creates inputs outside the measured interval and accepts
`PgBenchmarkBatchSize` to control batching. Use `PgBenchmark.BlackBox` when you
need to keep a value observable to Native AOT optimization.

Benchmark entry points and the `benches` schema exist only in benchmark
publications. Normal build, install and package output excludes them.

## Run benchmarks

Run every benchmark in the selected extension:

```console
ankus bench --pg 18
```

Supply an ordinal substring to select benchmark names:

```console
ankus bench AddNumeric --pg 18
```

`--list` discovers benchmarks without measuring them. Each run gets a timestamp
and Git commit group name unless `--group-name` supplies one. By default, results
compare with the latest retained group from the same build configuration.
`--compare-group` selects a named group instead. `--report` reads recent retained
results without rebuilding. Add `--json` to a report for machine-readable output.

The default database is `<extension>_benches`. Use `--database` to select another
name and `--resetdb` to recreate it. The runner owns an `ankus_bench` schema in
that database for history. Extension setup and measured mutations execute inside
an enclosing transaction that is always rolled back; retained results are written
after that rollback.

## Configure measurement

`PgBenchmarkAttribute` uses the same defaults as pgrx: 100 samples, five seconds
of measurement, three seconds of warmup, 100,000 statistical resamples, a one
percent noise threshold and a five percent significance level.

```csharp
[PgBenchmark(
    Setup = nameof(Prepare),
    Transaction = PgBenchmarkTransactionMode.SubtransactionPerBatch,
    SampleSize = 50,
    MeasurementTimeMilliseconds = 2_000,
    WarmupTimeMilliseconds = 1_000)]
public static void InsertRows(PgBencher bencher)
    => bencher.IterateBatched(
        static () => 42L,
        static value => Spi.Execute(
            "INSERT INTO measurements(value) VALUES ($1)",
            SpiParameter.Create(value)),
        PgBenchmarkBatchSize.SmallInput);

internal static void Prepare()
    => Spi.Execute("CREATE TABLE measurements(value bigint NOT NULL)");
```

`Setup` identifies one accessible synchronous static parameterless `void` method.
It runs once before warmup and is outside measured intervals.

The default `Shared` transaction mode measures work in the benchmark's enclosing
transaction. `SubtransactionPerBatch` adds one internal subtransaction around
each batch. `SubtransactionPerIteration` isolates every iteration and includes
that cost in the measurement.

Results include raw iteration counts and elapsed nanoseconds, bootstrap confidence
intervals for mean and median time per iteration, and comparison significance.
The runner increases a sample's iteration count if its measured duration falls
below the time target, including when the timer cannot resolve a single iteration.
Results retain the actual iteration count and duration. A routine exception ends
the benchmark; it is not retried.
A comparison reports an improvement or regression only when the measured change
is statistically significant and its confidence interval lies beyond the configured
noise threshold.

The [benchmark sample](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Benchmarks)
shows value operations, batched setup and SPI writes.
