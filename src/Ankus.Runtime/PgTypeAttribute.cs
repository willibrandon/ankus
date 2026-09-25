namespace Ankus;

/// <summary>
/// Generates a PostgreSQL base type with CBOR, packed native storage, or an explicit storage codec.
/// </summary>
/// <remarks>
/// Generated CBOR contracts support inherited members and explicitly tagged class variants declared with
/// <see cref="System.Text.Json.Serialization.JsonDerivedTypeAttribute"/> and
/// <see cref="System.Text.Json.Serialization.JsonPolymorphicAttribute"/>. Abstract classes require concrete variants.
/// Unknown runtime subtypes are rejected to preserve stored type identity.
/// </remarks>
/// <param name="codec">An explicit PgTypeCodec with an accessible parameterless constructor; omit to use generated storage and the selected text options.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum, Inherited = false)]
public sealed class PgTypeAttribute(Type? codec = null) : Attribute
{
    /// <summary>
    /// Gets the statically constructed codec type, or null for generated serialization.
    /// </summary>
    public Type? Codec { get; } = codec;

    /// <summary>
    /// Gets or sets a PgTypeTextCodec for custom SQL text with generated CBOR or packed native storage.
    /// Cannot be combined with an explicit storage codec.
    /// </summary>
    public Type? TextCodec { get; set; }

    /// <summary>
    /// Gets or sets whether storage uses the exact native representation of a densely packed unmanaged struct.
    /// Requires TextCodec and explicit sequential layout with Pack = 1 on the root and nested structs.
    /// Only fixed-width numeric fields, enums, nested packed structs and fixed buffers of numeric fields are supported.
    /// </summary>
    /// <remarks>
    /// Native storage depends on field order and host byte order. BinaryProtocol exposes this same representation.
    /// Changing the layout requires a data migration. Booleans, characters, pointers, platform-sized integers,
    /// explicit layouts, padding, empty structs and framework value types are rejected by the generator.
    /// Values of the declared struct use copied managed transport. PgVarlena wrappers provide checked native
    /// borrowing, copy-on-write mutation and explicit ownership for the same SQL type.
    /// </remarks>
    public bool NativeLayout { get; set; }

    /// <summary>
    /// Gets or sets the SQLSTATE 22004 message raised when the generated text input function receives SQL NULL.
    /// This includes untyped NULL literals coerced to this type, text array elements and text COPY fields.
    /// Null leaves the input function strict; already-typed SQL NULL values bypass the codec.
    /// </summary>
    public string? NullInputErrorMessage { get; set; }

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
