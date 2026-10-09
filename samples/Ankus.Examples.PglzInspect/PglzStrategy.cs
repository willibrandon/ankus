namespace Ankus.Examples.PglzInspect;

/// <summary>
/// Selects one of PostgreSQL's built-in PGLZ compression strategies.
/// </summary>
public enum PglzStrategy
{
    /// <summary>
    /// <c>PGLZ_strategy_default</c>: inputs of at least 32 bytes that save at least 25%, as TOAST uses.
    /// </summary>
    Default,

    /// <summary>
    /// <c>PGLZ_strategy_always</c>: attempts every input and accepts any output that is not larger.
    /// </summary>
    Always,
}
