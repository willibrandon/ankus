namespace Ankus.TestExtension;

/// <summary>
/// Uses deliberately unordered numeric values to distinguish label order from .NET numeric order.
/// </summary>
[PgEnum(Id = "enum.mood")]
public enum EnumMood : long
{
    /// <summary>
    /// The first PostgreSQL label.
    /// </summary>
    Low = 99,

    /// <summary>
    /// The second label has a smaller numeric value.
    /// </summary>
    Medium = -7,

    /// <summary>
    /// A high numeric value retains its exact managed identity.
    /// </summary>
    High = long.MaxValue,

    /// <summary>
    /// A Unicode label also works with a LATIN1 server encoding.
    /// </summary>
    [PgEnumLabel("café")]
    Cafe = 12,

    /// <summary>
    /// Empty labels remain distinct from SQL NULL.
    /// </summary>
    [PgEnumLabel("")]
    Empty = 13,

    /// <summary>
    /// Quotes and backslashes are retained verbatim.
    /// </summary>
    [PgEnumLabel("a'b\\c")]
    Escaped = 14,
}
