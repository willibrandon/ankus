namespace Ankus;

/// <summary>
/// Generates a PostgreSQL enum type and Native AOT conversions for a C# enum.
/// Labels retain member names by default; PostgreSQL ordering follows declaration order, not numeric values.
/// </summary>
[AttributeUsage(AttributeTargets.Enum, Inherited = false)]
public sealed class PgEnumAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the SQL type identifier; the default is the enum name in snake_case.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets a fixed schema. Otherwise the nearest PgSchema or extension installation schema applies.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>
    /// Gets or sets this type's identifier in the installation dependency graph.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets installation entities that must precede this type.
    /// </summary>
    public string[] Requires { get; set; } = [];
}
