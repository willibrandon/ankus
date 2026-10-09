namespace Ankus;

/// <summary>
/// Configures a <see cref="PgPropertyRunner"/>, with proptest's defaults.
/// </summary>
public sealed class PgPropertyOptions
{
    /// <summary>
    /// Gets the number of passing cases a run requires; the default is 256.
    /// </summary>
    public int Cases
    {
        get;
        init;
    } = 256;

    /// <summary>
    /// Gets the seed that selects the inputs, or null for a new random seed on each run.
    /// </summary>
    /// <remarks>
    /// A failure reports its seed. Set it here to replay the same inputs while investigating.
    /// </remarks>
    public ulong? Seed
    {
        get;
        init;
    }

    /// <summary>
    /// Gets the most test runs spent shrinking a failing input; the default is 4,096 and zero reports the first
    /// failing input unchanged.
    /// </summary>
    public int MaxShrinkRuns
    {
        get;
        init;
    } = 4_096;

    /// <summary>
    /// Gets the most cases <see cref="PgGenerator{T}.Where"/> may reject before the run fails; the default is 1,024.
    /// </summary>
    public int MaxRejects
    {
        get;
        init;
    } = 1_024;
}
