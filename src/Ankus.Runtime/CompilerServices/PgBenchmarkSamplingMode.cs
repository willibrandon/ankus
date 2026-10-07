namespace Ankus.CompilerServices;

/// <summary>
/// Identifies the concrete sampling plan selected from Criterion's automatic mode.
/// </summary>
internal enum PgBenchmarkSamplingMode
{
    /// <summary>
    /// Increases the iteration count linearly between samples.
    /// </summary>
    Linear,

    /// <summary>
    /// Uses the same iteration count for every sample.
    /// </summary>
    Flat,
}
