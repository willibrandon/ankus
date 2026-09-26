namespace Ankus.Build;

/// <summary>
/// Couples exact symbol signatures with an indexed, transitive selected-header layout graph.
/// </summary>
/// <param name="Headers">The existing AST-based symbol contracts.</param>
/// <param name="Graph">The independently validated Clang record and type observations.</param>
internal sealed record NativeHeaderRecords(NativeHeaderCatalog Headers, NativeRecordGraph Graph);

/// <summary>
/// Retains recursive native types without serializing native addresses or recursive managed objects.
/// </summary>
/// <param name="Target">The constants evaluated by the inspecting compiler library.</param>
/// <param name="Roots">The type index for each selected symbol, in ordinal order.</param>
/// <param name="Types">Declared and canonical types, indexed by their position.</param>
/// <param name="Declarations">Distinct struct, union and enum identities, indexed by their position.</param>
internal sealed record NativeRecordGraph(NativeHeaderTarget Target, IReadOnlyDictionary<string, int> Roots,
    IReadOnlyList<NativeRecordType> Types, IReadOnlyList<NativeRecordDeclaration> Declarations);

/// <summary>
/// Retains a type's declared shape separately from its canonical ABI shape.
/// </summary>
/// <param name="Kind">The normalized type form.</param>
/// <param name="Canonical">The compiler's canonical type index.</param>
/// <param name="Spelling">The compiler-printed complete type, retaining annotations with anonymous locations omitted.</param>
/// <param name="Qualifiers">Qualifiers observed at this type level.</param>
/// <param name="Size">Object size in bytes, or null for unavailable or non-object storage.</param>
/// <param name="Alignment">Native type alignment, or null when unavailable or inapplicable.</param>
/// <param name="Name">A builtin or typedef name; tag names belong to their declarations.</param>
/// <param name="Element">The referenced, element or modified type index, when applicable.</param>
/// <param name="Declaration">The referenced struct, union or enum declaration index.</param>
/// <param name="Count">An exact array/vector extent, or null for an incomplete array.</param>
/// <param name="Function">The function's ABI shape, when applicable.</param>
/// <param name="SourceDeclaration">A compiler-printed typedef preserving attributes, with anonymous locations omitted.</param>
internal sealed record NativeRecordType(string Kind, int Canonical, string Spelling, NativeHeaderQualifiers Qualifiers,
    long? Size, long? Alignment, string Name, int? Element, int? Declaration, long? Count,
    NativeRecordFunction? Function, string? SourceDeclaration);

/// <summary>
/// Retains a function type's shape; the containing type's canonical entry provides its adjusted ABI shape.
/// </summary>
/// <param name="Result">The return type index.</param>
/// <param name="Parameters">Ordered fixed parameters, with array adjustment available in the canonical function entry.</param>
/// <param name="IsVariadic">Whether additional promoted arguments are accepted.</param>
/// <param name="HasPrototype">Whether a C prototype supplies the fixed parameter list.</param>
/// <param name="CallingConvention">The explicit clang-c calling convention identifier.</param>
internal sealed record NativeRecordFunction(int Result, IReadOnlyList<int> Parameters, bool IsVariadic,
    bool HasPrototype, int CallingConvention);

/// <summary>
/// Identifies one native tag independently of typedef aliases and qualified uses.
/// </summary>
/// <param name="Kind">Struct, union or enum.</param>
/// <param name="Name">The actual tag name, or an empty string for an anonymous declaration.</param>
/// <param name="IsComplete">Whether native storage is defined in this translation unit.</param>
/// <param name="Size">The complete native byte size, or null for an opaque declaration.</param>
/// <param name="Alignment">The complete native alignment, or null for an opaque declaration.</param>
/// <param name="Fields">Physical fields in native order, including anonymous containers and zero-width bitfields.</param>
/// <param name="EnumUnderlying">The enum's actual integer representation, or null for records or unavailable enum storage.</param>
/// <param name="EnumValues">Exact enum values without narrowing unsigned 64-bit constants.</param>
internal sealed record NativeRecordDeclaration(string Kind, string Name, bool IsComplete, long? Size,
    long? Alignment, IReadOnlyList<NativeRecordField> Fields, int? EnumUnderlying,
    IReadOnlyList<NativeRecordConstant> EnumValues);

/// <summary>
/// Retains a physical native field, without confusing a promoted anonymous member with a named field.
/// </summary>
/// <param name="Name">The field identifier, or empty for an unnamed bitfield or anonymous container.</param>
/// <param name="Type">The exact declared type index.</param>
/// <param name="OffsetBits">The compiler's bit offset from the containing record.</param>
/// <param name="BitWidth">The exact bitfield width, including zero, or null for ordinary fields.</param>
/// <param name="IsAnonymous">Whether the field is a C anonymous struct/union container.</param>
/// <param name="SourceDeclaration">The compiler-printed field, retaining native attributes without source locations.</param>
internal sealed record NativeRecordField(string Name, int Type, long OffsetBits, int? BitWidth,
    bool IsAnonymous, string SourceDeclaration);

/// <summary>
/// Retains an enum constant as an exact invariant decimal integer.
/// </summary>
/// <param name="Name">The native constant's identifier.</param>
/// <param name="Value">Its complete signed or unsigned decimal value.</param>
internal sealed record NativeRecordConstant(string Name, string Value);
