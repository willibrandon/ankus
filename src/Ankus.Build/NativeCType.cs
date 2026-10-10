namespace Ankus.Build;

/// <summary>
/// A C type read from a header, with its qualifiers.
/// </summary>
internal abstract record NativeCType
{
    /// <summary>
    /// Gets whether this level of the type is <c>const</c>.
    /// </summary>
    internal bool IsConst { get; init; }

    /// <summary>
    /// Gets whether this level of the type is <c>volatile</c>.
    /// </summary>
    internal bool IsVolatile { get; init; }
}

/// <summary>
/// A built-in arithmetic type or <c>void</c>, with its canonical spelling such as <c>unsigned long long</c>.
/// </summary>
/// <param name="Name">The canonical spelling.</param>
internal sealed record NativeCBuiltin(string Name) : NativeCType;

/// <summary>
/// A typedef name.
/// </summary>
/// <param name="Name">The typedef.</param>
internal sealed record NativeCTypedef(string Name) : NativeCType;

/// <summary>
/// A struct, union or enum, named or anonymous.
/// </summary>
/// <param name="Kind">The tag kind.</param>
/// <param name="Name">The tag, or an empty string for an anonymous type.</param>
/// <param name="Declaration">The identity of the declaration the type names.</param>
internal sealed record NativeCTag(NativeCTagKind Kind, string Name, string Declaration) : NativeCType;

/// <summary>
/// A pointer to an element type.
/// </summary>
/// <param name="Element">The pointed-to type.</param>
internal sealed record NativeCPointer(NativeCType Element) : NativeCType;

/// <summary>
/// An array, with no count for an incomplete array.
/// </summary>
/// <param name="Element">The element type.</param>
/// <param name="Count">The element count, or null when incomplete.</param>
internal sealed record NativeCArray(NativeCType Element, ulong? Count) : NativeCType;

/// <summary>
/// A function type.
/// </summary>
/// <param name="Result">The result type.</param>
/// <param name="Parameters">The fixed parameter types.</param>
/// <param name="IsVariadic">Whether further arguments may follow.</param>
internal sealed record NativeCFunction(NativeCType Result, IReadOnlyList<NativeCType> Parameters, bool IsVariadic) : NativeCType
{
    /// <summary>
    /// Gets the parameter names bindgen reads from the declaration the type appears in, with null for an unnamed one.
    /// Names are not part of the type's identity.
    /// </summary>
    internal IReadOnlyList<string?> ParameterNames { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the type is declared <c>noreturn</c>, which bindgen renders as <c>-&gt; !</c>.
    /// </summary>
    internal bool IsDivergent { get; init; }

    /// <inheritdoc />
    public bool Equals(NativeCFunction? other)
        => other is not null && Result == other.Result && Parameters.SequenceEqual(other.Parameters) && IsVariadic == other.IsVariadic &&
            IsDivergent == other.IsDivergent && IsConst == other.IsConst && IsVolatile == other.IsVolatile;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Result, Parameters.Count, IsVariadic, IsDivergent);
}

/// <summary>
/// A complex floating type.
/// </summary>
/// <param name="Element">The real and imaginary parts' type.</param>
internal sealed record NativeCComplex(NativeCType Element) : NativeCType;

/// <summary>
/// A type bindgen's configuration does not represent, which fails only if an allowlisted declaration needs it.
/// </summary>
/// <param name="Spelling">The compiler's spelling of the type.</param>
internal sealed record NativeCUnsupported(string Spelling) : NativeCType;

/// <summary>
/// The kinds of C tag.
/// </summary>
internal enum NativeCTagKind
{
    /// <summary>
    /// A struct.
    /// </summary>
    Struct,

    /// <summary>
    /// A union.
    /// </summary>
    Union,

    /// <summary>
    /// An enum.
    /// </summary>
    Enum,
}
