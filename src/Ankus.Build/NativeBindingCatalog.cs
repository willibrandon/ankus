namespace Ankus.Build;

/// <summary>
/// Retains the complete declarations needed to derive selected-header node layouts from pinned bindgen input.
/// </summary>
/// <param name="PostgresMajor">The PostgreSQL major represented by this catalog.</param>
/// <param name="Tags">Exact native node tag names and unsigned values.</param>
/// <param name="Types">Native struct and union declarations, including value dependencies of nodes.</param>
/// <param name="Aliases">Native typedef names and their complete Rust representations.</param>
/// <param name="Enums">Native enum declarations emitted as bindgen modules.</param>
internal sealed record NativeBindingCatalog(int PostgresMajor, IReadOnlyDictionary<string, uint> Tags,
    IReadOnlyDictionary<string, NativeBindingType> Types, IReadOnlyDictionary<string, string> Aliases,
    IReadOnlyDictionary<string, NativeBindingEnum> Enums);

/// <summary>
/// Describes a native struct or union without assuming its physical layout on any platform.
/// </summary>
/// <param name="Name">The bindgen type name.</param>
/// <param name="IsUnion">Whether fields overlap as a native union.</param>
/// <param name="Fields">Complete field representations in declaration order.</param>
/// <param name="IsNode">Whether pgrx's type graph identifies this declaration as a node.</param>
/// <param name="CastTags">Known tags accepted by this target; Node itself accepts every source node.</param>
internal sealed record NativeBindingType(string Name, bool IsUnion, IReadOnlyList<NativeBindingField> Fields,
    bool IsNode, IReadOnlyList<string> CastTags);

/// <summary>
/// Retains a field's name and full representation, including nested callback signatures and flexible arrays.
/// </summary>
/// <param name="Name">The escaped bindgen identifier.</param>
/// <param name="NativeName">The original C identifier.</param>
/// <param name="Representation">The complete Rust type expression.</param>
internal sealed record NativeBindingField(string Name, string NativeName, string Representation);

/// <summary>
/// Retains a named native enum's storage type and exact constant representations.
/// </summary>
/// <param name="Storage">The bindgen integer storage representation.</param>
/// <param name="Values">Native member names and complete constant expressions.</param>
internal sealed record NativeBindingEnum(string Storage, IReadOnlyDictionary<string, string> Values);
