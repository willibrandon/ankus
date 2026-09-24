namespace Ankus;

/// <summary>
/// Generates a PostgreSQL base type whose variable-length storage and text format are defined by a codec.
/// </summary>
/// <param name="codec">A concrete PgTypeCodec for this managed type, with an accessible parameterless constructor.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum, Inherited = false)]
public sealed class PgTypeAttribute(Type codec) : Attribute
{
    /// <summary>
    /// Gets the statically constructed codec type.
    /// </summary>
    public Type Codec { get; } = codec;

    /// <summary>
    /// Gets or sets the SQL type name; the default is the managed type name in snake_case.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets a fixed schema; otherwise the enclosing PgSchema or extension installation schema applies.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>
    /// Gets or sets the installation dependency identifier for the completed type.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets installation entities that must precede the type declaration.
    /// </summary>
    public string[] Requires { get; set; } = [];

    /// <summary>
    /// Gets or sets whether binary send and receive functions expose the codec's storage representation.
    /// </summary>
    public bool BinaryProtocol { get; set; }
}
