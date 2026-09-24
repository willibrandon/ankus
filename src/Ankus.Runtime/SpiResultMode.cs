namespace Ankus;

/// <summary>
/// Selects which SPI values are copied after PostgreSQL executes the full command.
/// </summary>
internal enum SpiResultMode : byte
{
    /// <summary>
    /// Copies only the processed-row count.
    /// </summary>
    None,

    /// <summary>
    /// Copies the complete tuple table.
    /// </summary>
    All,

    /// <summary>
    /// Copies the first column of the first row without limiting command execution.
    /// </summary>
    Scalar,

    /// <summary>
    /// Copies the first two columns of the first row without limiting command execution.
    /// </summary>
    Pair,

    /// <summary>
    /// Copies the first three columns of the first row without limiting command execution.
    /// </summary>
    Triple,
}
