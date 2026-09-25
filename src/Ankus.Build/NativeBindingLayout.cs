namespace Ankus.Build;

/// <summary>
/// Records physical representations measured by the selected PostgreSQL C compiler and headers.
/// </summary>
/// <param name="PostgresVersion">The complete native PG_VERSION_NUM value.</param>
/// <param name="PointerSize">The native pointer width in bytes.</param>
/// <param name="LongSize">The native C long width in bytes.</param>
/// <param name="CharIsSigned">Whether plain native char is signed.</param>
/// <param name="IsLittleEndian">Whether the compiler target stores the least significant byte first.</param>
/// <param name="RuntimeIdentifier">The compiler's measured operating-system and processor ABI.</param>
/// <param name="Types">Exact sizes, alignments and field positions for the selected declarations.</param>
/// <param name="Enums">Measured integer representations of embedded named enums.</param>
internal sealed record NativeBindingLayout(int PostgresVersion, int PointerSize, int LongSize,
    bool CharIsSigned, bool IsLittleEndian, string RuntimeIdentifier, IReadOnlyDictionary<string, NativeBindingTypeLayout> Types,
    IReadOnlyDictionary<string, NativeBindingEnumLayout> Enums);

/// <summary>
/// Describes a named enum's native integer storage on the selected compiler target.
/// </summary>
/// <param name="Size">The native width in bytes.</param>
/// <param name="IsSigned">Whether negative values are represented as signed integers.</param>
internal sealed record NativeBindingEnumLayout(int Size, bool IsSigned);

/// <summary>
/// Describes one complete native value representation, including tail padding.
/// </summary>
/// <param name="Size">The native sizeof value.</param>
/// <param name="Alignment">The native alignment requirement.</param>
/// <param name="Fields">Physical field representations keyed by bindgen name.</param>
internal sealed record NativeBindingTypeLayout(int Size, int Alignment,
    IReadOnlyDictionary<string, NativeBindingFieldLayout> Fields);

/// <summary>
/// Describes a native field without assuming that bindgen's reference platform has the same layout.
/// </summary>
/// <param name="Offset">The field's byte offset from its enclosing value.</param>
/// <param name="Size">The field's byte size, or zero for a flexible array.</param>
/// <param name="Alignment">The field's native alignment.</param>
/// <param name="ElementSize">The element size for arrays, or the scalar field size otherwise.</param>
internal sealed record NativeBindingFieldLayout(int Offset, int Size, int Alignment, int ElementSize);
