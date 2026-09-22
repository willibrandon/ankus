namespace Ankus;

/// <summary>
/// Specifies where PostgreSQL may insert a generated cast.
/// </summary>
public enum PgCastContext
{
    /// <summary>
    /// Requires an explicit CAST or :: expression.
    /// </summary>
    Explicit,

    /// <summary>
    /// Also permits conversion when assigning a value to a column.
    /// </summary>
    Assignment,

    /// <summary>
    /// Also permits conversion during expression and function resolution.
    /// </summary>
    Implicit,
}
