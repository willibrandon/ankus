namespace Ankus;

/// <summary>
/// Generates PostgreSQL relational operators and a default B-tree operator class from IComparable of the declared type.
/// Enum declarations use their underlying numeric order.
/// </summary>
/// <remarks>
/// Requires PgType or PgEnum and a same-type equality operator, generated with PgEquality or declared with PgOperator.
/// Non-enum types must also implement IEquatable of the declared type. Comparison must define an immutable,
/// parallel-safe total order whose zero results agree with equality. Generated functions are strict.
/// On PgEnum this explicit opt-in selects numeric ordering instead of PostgreSQL label declaration order.
/// Changing ordering after data is indexed requires rebuilding dependent indexes.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum, Inherited = false)]
public sealed class PgOrderingAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the installation dependency identifier for the completed B-tree operator family and class.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets installation entities that must precede the generated ordering declarations.
    /// </summary>
    public string[] Requires { get; set; } = [];
}
