namespace Ankus;

/// <summary>
/// Identifies the direction or variadic role of a catalog argument.
/// </summary>
public enum PgArgumentMode
{
    /// <summary>
    /// An input argument.
    /// </summary>
    In,

    /// <summary>
    /// An output argument.
    /// </summary>
    Out,

    /// <summary>
    /// An input and output argument.
    /// </summary>
    InOut,

    /// <summary>
    /// A variadic input argument.
    /// </summary>
    Variadic,

    /// <summary>
    /// A named table result argument.
    /// </summary>
    Table,
}
