namespace Ankus;

/// <summary>
/// Assigns an exact, case-sensitive PostgreSQL label to a PgEnum member.
/// </summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class PgEnumLabelAttribute : Attribute
{
    /// <summary>
    /// Specifies a label of at most 63 UTF-8 bytes, including an empty label.
    /// </summary>
    /// <param name="label">The label, without zero characters.</param>
    public PgEnumLabelAttribute(string label) => Label = label;

    /// <summary>
    /// Gets the exact PostgreSQL label.
    /// </summary>
    public string Label { get; }
}
