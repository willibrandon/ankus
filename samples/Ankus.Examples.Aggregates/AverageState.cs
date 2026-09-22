namespace Ankus.Examples.Aggregates;

/// <summary>
/// Holds the exact integer sum and nonnull count for one average state.
/// </summary>
public sealed class AverageState
{
    /// <summary>
    /// Gets or sets the checked sum of the input integers.
    /// </summary>
    public long Sum { get; set; }

    /// <summary>
    /// Gets or sets the number of nonnull input integers.
    /// </summary>
    public long Count { get; set; }
}
