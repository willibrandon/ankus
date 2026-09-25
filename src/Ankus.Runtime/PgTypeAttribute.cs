namespace Ankus;

/// <summary>
/// Generates a PostgreSQL base type with CBOR storage, JSON or custom text, or an explicit storage codec.
/// </summary>
/// <remarks>
/// Generated contracts support inherited members and explicitly tagged class variants declared with
/// <see cref="System.Text.Json.Serialization.JsonDerivedTypeAttribute"/> and
/// <see cref="System.Text.Json.Serialization.JsonPolymorphicAttribute"/>. Abstract classes require concrete variants.
/// Unknown runtime subtypes are rejected to preserve stored type identity.
/// </remarks>
/// <param name="codec">An explicit PgTypeCodec with an accessible parameterless constructor; omit for generated CBOR storage and JSON text.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum, Inherited = false)]
public sealed class PgTypeAttribute(Type? codec = null) : Attribute
{
    /// <summary>
    /// Gets the statically constructed codec type, or null for generated serialization.
    /// </summary>
    public Type? Codec { get; } = codec;

    /// <summary>
    /// Gets or sets a PgTypeTextCodec for custom SQL text with generated CBOR storage.
    /// Cannot be combined with an explicit storage codec.
    /// </summary>
    public Type? TextCodec { get; set; }

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
