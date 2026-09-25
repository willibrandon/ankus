namespace Ankus;

/// <summary>
/// Maps a closed managed type to an existing PostgreSQL representation through an explicit datum converter.
/// </summary>
/// <remarks>
/// This declaration generates conversion registration, not type definitions or input/output functions.
/// The converter implements IPgDatumReader&lt;T&gt;, IPgDatumWriter&lt;T&gt;, or both for this exact managed type.
/// Supported paths are scalar and array generated callbacks, set/TABLE and aggregate slots, declared SPI/function
/// parameters, typed SPI scalar and catalog/native-address function results, and explicit PgDatum.Read&lt;T&gt; calls.
/// One array layer uses the scalar converter with exact element and array identity, preserving NULL and shape.
/// Native-address calls retain the caller's responsibility for the actual result type and representation.
/// Ordinary SPI/tuple row conversions are not supported.
/// A reader must return independent managed data; retaining a checked PgDatum does not detach its storage.
/// </remarks>
/// <param name="name">The exact unquoted PostgreSQL type identifier.</param>
/// <param name="converter">The closed converter type with an accessible parameterless constructor.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum, Inherited = false)]
public sealed class PgDatumTypeAttribute(string name, Type converter) : Attribute
{
    /// <summary>
    /// Gets the exact catalog type identifier.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets the statically instantiated reader or writer type.
    /// </summary>
    public Type Converter { get; } = converter;

    /// <summary>
    /// Gets or sets the fixed schema. External mappings require an explicit schema.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>
    /// Gets or sets whether the SQL type is supplied by this extension or an existing external schema.
    /// </summary>
    public PgTypeOrigin Origin { get; set; }
}
