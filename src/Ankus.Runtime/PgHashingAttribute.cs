namespace Ankus;

/// <summary>
/// Generates a PostgreSQL hash support function and default hash operator class from IPgHashable.
/// Enum declarations hash their numeric value as eight little-endian bytes with PgHash.
/// </summary>
/// <remarks>
/// Requires PgType or PgEnum and a same-type equality operator, generated with PgEquality or declared with PgOperator.
/// Non-enum types must implement IEquatable of the declared type and IPgHashable. Hashes must agree with equality
/// and remain stable across processes, platforms and extension versions. Generated functions are strict,
/// immutable and parallel safe. Changing the hashing contract requires rebuilding dependent indexes.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum, Inherited = false)]
public sealed class PgHashingAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the installation dependency identifier for the completed hash operator family and class.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets installation entities that must precede the generated hashing declarations.
    /// </summary>
    public string[] Requires { get; set; } = [];
}
