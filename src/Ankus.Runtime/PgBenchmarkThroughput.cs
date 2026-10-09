namespace Ankus;

/// <summary>
/// Selects how a benchmark reports throughput from the work one iteration performs, as Criterion's
/// <c>Throughput</c> does.
/// </summary>
public enum PgBenchmarkThroughput
{
    /// <summary>
    /// Reports no throughput.
    /// </summary>
    None,

    /// <summary>
    /// Counts bytes per iteration and reports binary units such as MiB/s.
    /// </summary>
    Bytes,

    /// <summary>
    /// Counts bytes per iteration and reports decimal units such as MB/s.
    /// </summary>
    BytesDecimal,

    /// <summary>
    /// Counts elements per iteration and reports elem/s with decimal prefixes.
    /// </summary>
    Elements,
}
