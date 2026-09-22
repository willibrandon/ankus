namespace Ankus;

/// <summary>
/// Exposes owned range bounds to statically supported transport and SPI adapters.
/// </summary>
internal interface IPgRange
{
    /// <summary>
    /// Gets the built-in range OID.
    /// </summary>
    uint TypeOid { get; }

    /// <summary>
    /// Gets whether the range is empty.
    /// </summary>
    bool IsEmpty { get; }

    /// <summary>
    /// Gets the boxed lower bound, or null when absent.
    /// </summary>
    object? LowerValue { get; }

    /// <summary>
    /// Gets the boxed upper bound, or null when absent.
    /// </summary>
    object? UpperValue { get; }

    /// <summary>
    /// Gets whether the present lower bound is included.
    /// </summary>
    bool LowerInclusive { get; }

    /// <summary>
    /// Gets whether the present upper bound is included.
    /// </summary>
    bool UpperInclusive { get; }
}
