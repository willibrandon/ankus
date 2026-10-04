namespace Ankus;

/// <summary>
/// Declares a synchronous benchmark that executes inside PostgreSQL.
/// Native entry points are emitted only when AnkusIncludeBenchmarks is enabled.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PgBenchmarkAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the name of a synchronous static setup method invoked once before measurement.
    /// </summary>
    public string? Setup
    {
        get;
        set;
    }

    /// <summary>
    /// Gets or sets the transaction boundary used by measured work.
    /// </summary>
    public PgBenchmarkTransactionMode Transaction
    {
        get;
        set;
    }

    /// <summary>
    /// Gets or sets the number of measurement samples.
    /// </summary>
    public int SampleSize
    {
        get;
        set;
    } = 100;

    /// <summary>
    /// Gets or sets the target total measurement time in milliseconds.
    /// </summary>
    public int MeasurementTimeMilliseconds
    {
        get;
        set;
    } = 5_000;

    /// <summary>
    /// Gets or sets the warmup time in milliseconds.
    /// </summary>
    public int WarmupTimeMilliseconds
    {
        get;
        set;
    } = 3_000;

    /// <summary>
    /// Gets or sets the statistical resample count.
    /// </summary>
    public int ResampleCount
    {
        get;
        set;
    } = 100_000;

    /// <summary>
    /// Gets or sets the relative change below which a comparison is treated as noise.
    /// </summary>
    public double NoiseThreshold
    {
        get;
        set;
    } = 0.01;

    /// <summary>
    /// Gets or sets the significance level used for comparisons.
    /// </summary>
    public double SignificanceLevel
    {
        get;
        set;
    } = 0.05;
}
