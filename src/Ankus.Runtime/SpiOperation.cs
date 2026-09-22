namespace Ankus;

/// <summary>
/// Selects an operation inside the native SPI subtransaction guard.
/// </summary>
internal enum SpiOperation : byte
{
    /// <summary>
    /// Executes SQL text with optional positional parameters.
    /// </summary>
    Execute,

    /// <summary>
    /// Prepares and retains a PostgreSQL plan beyond the current SPI connection.
    /// </summary>
    Prepare,

    /// <summary>
    /// Executes a retained plan.
    /// </summary>
    ExecutePlan,

    /// <summary>
    /// Releases a retained plan on its owning backend thread.
    /// </summary>
    FreePlan,
}
