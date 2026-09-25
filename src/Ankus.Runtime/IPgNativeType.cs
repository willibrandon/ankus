namespace Ankus;

/// <summary>
/// Describes a generated unmanaged representation measured from one PostgreSQL header and compiler ABI.
/// </summary>
/// <remarks>
/// Generated bindings implement this contract without runtime reflection. Implementations must preserve
/// the complete native size and field layout; an unmanaged constraint alone does not establish ABI compatibility.
/// </remarks>
public interface IPgNativeType
{
    /// <summary>
    /// Gets the PostgreSQL major whose native declarations define this representation.
    /// </summary>
    static abstract int PostgresMajor { get; }

    /// <summary>
    /// Gets the identity of the complete measured binding ABI shared by these generated types.
    /// </summary>
    static abstract string AbiIdentity { get; }

    /// <summary>
    /// Gets the operating-system and processor ABI measured by the selected native compiler.
    /// </summary>
    static abstract string RuntimeIdentifier { get; }

    /// <summary>
    /// Gets the complete native value size, including padding but excluding flexible array storage.
    /// </summary>
    static abstract int NativeSize { get; }

    /// <summary>
    /// Gets the alignment required when allocating or borrowing this value in native memory.
    /// </summary>
    static abstract int NativeAlignment { get; }
}
